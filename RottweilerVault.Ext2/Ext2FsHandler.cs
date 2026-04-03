using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.Ext2.Implementations;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;
using Tmds.Linux;

namespace RottweilerVault.Ext2;

internal class Ext2FsHandler : IFsHandler
{
    public bool SupportsMultiThreading => false;

    private readonly SuperStructure _superStructure;

    public Ext2FsHandler(SuperStructure superStructure)
    {
        _superStructure = superStructure;
    }

    public FsInode? CreateInode(
        FsDirectory parent,
        string name,
        InodeType inodeType,
        UnixFileMode fileMode,
        ref FuseFileInfo fileInfo,
        out FuseError error)
    {
        if (parent.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: parentDir has an inode of 0 in CreateInode");
            error = FuseError.IoError;
            return null;
        }

        if (parent.GetEntryOrNull(name) != null)
        {
            error = FuseError.AlreadyExists;
            return null;
        }

        DirectoryImpl? directoryImpl = DirectoryImpl.GetDir(_superStructure, parent.InodeId, parent.Name);
        if (directoryImpl == null)
        {
            error = FuseError.IoError;
            return null;
        }

        switch (inodeType)
        {
            case InodeType.Regular:
            {
                var fileImpl = FileImpl.Create(_superStructure, directoryImpl, name, fileMode);
                FsFile newInode = new()
                {
                    InodeId = fileImpl.InodeId,
                    InodeMode = fileMode,
                    Name = name,
                    Parent = parent
                };

                error = FuseError.Success;
                return newInode;
            }
            case InodeType.Directory:
            {
                var dirImpl = DirectoryImpl.Create(_superStructure, directoryImpl, name, fileMode);
                FsDirectory newInode = new()
                {
                    InodeId = dirImpl.InodeId,
                    InodeMode = fileMode,
                    Name = name,
                    Parent = parent
                };

                error = FuseError.Success;
                return newInode;
            }
            default:
                error = FuseError.IoError;
                return null;
        }
    }

    public FuseError RemoveFile(FsFile fileToDelete)
    {
        throw new NotImplementedException();
    }

    public FuseError RemoveDir(FsDirectory dirToDelete)
    {
        throw new NotImplementedException();
    }

    public FuseError GetAttributes(FsInode inodeToQuery, ref stat statAttributes)
    {
        if (inodeToQuery.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: inodeToQuery has an inode of 0 in GetAttributes");
            return FuseError.IoError;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(inodeToQuery.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        Inode rawInode = blockGroup.GetLocalInode(inodeToQuery.InodeId);
        statAttributes.st_blksize = AesXtsWriter.BLOCK_SIZE;
        statAttributes.st_blocks = rawInode.SmallLbaBlocksReserved / 8;
        statAttributes.st_size = ((long)rawInode.DataSizeHigh << 32) | rawInode.DataSizeLow;

        statAttributes.st_ino = inodeToQuery.InodeId;
        statAttributes.st_uid = rawInode.Uid;
        statAttributes.st_gid = rawInode.Gid;
        statAttributes.st_mode =
            (inodeToQuery is FsDirectory ? LibC.S_IFDIR : LibC.S_IFREG) | (ushort)inodeToQuery.InodeMode;
        // statAttributes.st_nlink = rawInode.HardLinksCount;
        statAttributes.st_nlink = 2;

        statAttributes.st_atim = new timespec
        {
            tv_sec = rawInode.LastAccessTime,
            tv_nsec = (long_t)(rawInode.LastAccessTime * 1_000_000_000L)
        };
        statAttributes.st_mtim = new timespec
        {
            tv_sec = rawInode.LastWriteTime,
            tv_nsec = (long_t)(rawInode.LastWriteTime * 1_000_000_000L)
        };
        statAttributes.st_ctim = new timespec
        {
            tv_sec = rawInode.CreateTime,
            tv_nsec = (long_t)(rawInode.CreateTime * 1_000_000_000L)
        };

        return FuseError.Success;
    }

    public FuseError OpenFile(FsFile fileToOpen, ref FuseFileInfo fileInfo)
    {
        return FuseError.Success;
    }

    public FuseError CloseFile(FsFile fileToClose, ref FuseFileInfo fileInfo)
    {
        return FuseError.Success;
    }

    public int Read(FsFile fileToRead, ulong offset, Span<byte> buffer, ref FuseFileInfo fileInfo, out FuseError error)
    {
        if (fileToRead.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: fileToRead has an inode of 0 in Read");
            error = FuseError.IoError;
            return 0;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(fileToRead.InodeId);
        if (blockGroup == null)
        {
            error = FuseError.IoError;
            return 0;
        }

        if (offset + (ulong)buffer.Length > (ulong)AesXtsWriter.BLOCK_SIZE * uint.MaxValue)
        {
            error = FuseError.FileTooLarge;
            return 0;
        }

        Inode rawInode = blockGroup.GetLocalInode(fileToRead.InodeId);

        int numBytesRead = 0;
        uint numBlocksRead = 0;
        while (numBytesRead < buffer.Length)
        {
            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(
                rawInode, (uint)(offset / AesXtsWriter.BLOCK_SIZE) + numBlocksRead);

            ReadOnlySpan<byte> blockData;
            if (blockId == 0)
            {
                blockData = new ReadOnlySpan<byte>(new byte[AesXtsWriter.BLOCK_SIZE]);
            }
            else
            {
                if (!blockGroup.TryGetDataBlock(blockId, out blockData))
                {
                    error = FuseError.IoError;
                    return numBytesRead;
                }
            }

            int numBytesToRead = buffer.Length - numBytesRead;
            if (numBytesToRead < AesXtsWriter.BLOCK_SIZE)
            {
                blockData[..numBytesToRead].CopyTo(buffer[numBytesRead..]);
                numBytesRead += numBytesToRead;
            }
            else
            {
                blockData.CopyTo(buffer[numBytesRead..]);
                numBytesRead += AesXtsWriter.BLOCK_SIZE;
            }

            numBlocksRead++;
        }

        error = FuseError.Success;
        return numBytesRead;
    }

    public int Write(
        FsFile fileToWriteInto,
        ulong offset,
        ReadOnlySpan<byte> buffer,
        ref FuseFileInfo fileInfo,
        out FuseError error)
    {
        if (fileToWriteInto.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: fileToWriteInto has an inode of 0 in Write");
            error = FuseError.IoError;
            return 0;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(fileToWriteInto.InodeId);
        if (blockGroup == null)
        {
            error = FuseError.IoError;
            return 0;
        }

        if (offset + (ulong)buffer.Length > (ulong)AesXtsWriter.BLOCK_SIZE * uint.MaxValue)
        {
            error = FuseError.FileTooLarge;
            return 0;
        }

        Inode rawInode = blockGroup.GetLocalInode(fileToWriteInto.InodeId);

        int numBytesWritten = 0;
        uint numBlocksWritten = 0;
        while (numBytesWritten < buffer.Length)
        {
            uint inodeDataOffset = (uint)(offset / AesXtsWriter.BLOCK_SIZE) + numBlocksWritten;
            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(rawInode, inodeDataOffset);
            //if 0, reserve another block
            if (blockId == 0)
            {
                try
                {
                    _superStructure.AddDataBlockToInode(
                        rawInode, fileToWriteInto.InodeId, inodeDataOffset, out blockId);
                }
                catch
                {
                    error = FuseError.IoError;
                    return numBytesWritten;
                }
            }

            byte[] data = new byte[AesXtsWriter.BLOCK_SIZE];

            int numBytesToWrite = buffer.Length - numBytesWritten;
            if (numBytesToWrite < AesXtsWriter.BLOCK_SIZE)
            {
                buffer[numBytesWritten..].CopyTo(data);
                numBytesWritten += numBytesToWrite;
            }
            else
            {
                buffer[numBytesWritten..(numBytesWritten + AesXtsWriter.BLOCK_SIZE)].CopyTo(data);
                numBytesWritten += AesXtsWriter.BLOCK_SIZE;
            }

            blockGroup.UpdateDataBlockOnDisk(blockId, data);
            numBlocksWritten++;
        }

        ulong totalSize = offset + (ulong)buffer.Length;
        rawInode.DataSizeLow = (uint)totalSize;
        rawInode.DataSizeHigh = (uint)(totalSize >> 32);

        rawInode.LastAccessTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        rawInode.LastWriteTime = rawInode.LastAccessTime;
        blockGroup.UpdateInodeOnDisk(fileToWriteInto.InodeId, rawInode);

        error = FuseError.Success;
        return numBytesWritten;
    }

    public FuseError RenameFile(FsDirectory parentDir, string oldName, string newName, int flags)
    {
        if (parentDir.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: parentDir has an inode of 0 in RenameFile");
            return FuseError.IoError;
        }

        if (parentDir.GetEntryOrNull(newName) != null)
        {
            return FuseError.AlreadyExists;
        }

        FsInode? inode = GetInodeIfExists(parentDir, oldName, out FuseError error);
        if (error != FuseError.Success || inode == null)
        {
            return error;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(parentDir.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        FileImpl? fileImpl = FileImpl.GetFile(_superStructure, inode.InodeId, oldName);
        if (fileImpl == null)
        {
            return FuseError.IoError;
        }

        fileImpl.Name = newName;
        DirectoryImpl? directoryImpl = DirectoryImpl.GetDir(_superStructure, parentDir.InodeId, parentDir.Name);
        directoryImpl?.UpdateDescendant(oldName, fileImpl);

        Inode rawInode = blockGroup.GetLocalInode(parentDir.InodeId);
        //update the directory inode itself
        blockGroup.UpdateInodeOnDisk(parentDir.InodeId, rawInode);
        return FuseError.Success;
    }

    public FuseError Chmod(FsInode inode, UnixFileMode newMode, ref FuseFileInfo fileInfo)
    {
        if (inode.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: inode has an inode of 0 in Chmod");
            return FuseError.IoError;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(inode.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        FileImpl? fileImpl = FileImpl.GetFile(_superStructure, inode.InodeId, inode.Name);
        if (fileImpl == null)
        {
            return FuseError.IoError;
        }

        fileImpl.Inode.Mode =
            (ushort)((fileImpl.InodeType == DirEntryFileType.Directory
                ? (ushort)InodeType.Directory
                : (ushort)InodeType.Regular) | (ushort)newMode);
        blockGroup.UpdateInodeOnDisk(fileImpl.InodeId, fileImpl.Inode);

        return FuseError.Success;
    }

    public FuseError Chown(FsInode inode, uint uid, uint gid, ref FuseFileInfo fileInfo)
    {
        if (inode.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: inode has an inode of 0 in Chown");
            return FuseError.IoError;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(inode.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        FileImpl? fileImpl = FileImpl.GetFile(_superStructure, inode.InodeId, inode.Name);
        if (fileImpl == null)
        {
            return FuseError.IoError;
        }

        //TODO: add the l_i_uid_high and l_i_gid_high fields in the raw inode
        fileImpl.Inode.Uid = (ushort)uid;
        fileImpl.Inode.Gid = (ushort)gid;
        fileImpl.Inode.LastWriteTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        blockGroup.UpdateInodeOnDisk(fileImpl.InodeId, fileImpl.Inode);

        return FuseError.Success;
    }


    public FsInode? GetInodeIfExists(FsDirectory parentDir, string name, out FuseError error)
    {
        if (parentDir.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: parentDir has an inode of 0 in GetNodeIfExists");
            error = FuseError.IoError;
            return null;
        }

        FsDirectoryEnumerator? directoryEnumerator = GetInodeEnumerator(parentDir, out error);
        if (error != FuseError.Success || directoryEnumerator == null)
        {
            return null;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(parentDir.InodeId);
        if (blockGroup != null)
        {
            Inode rawInode = blockGroup.GetLocalInode(parentDir.InodeId);

            //update the directory inode itself
            rawInode.LastWriteTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            blockGroup.UpdateInodeOnDisk(parentDir.InodeId, rawInode);
        }

        while (directoryEnumerator.MoveNext())
        {
            if (directoryEnumerator.Current?.Name == name)
            {
                error = FuseError.Success;
                return directoryEnumerator.Current;
            }
        }

        error = FuseError.NoEntry;
        return null;
    }

    public FsDirectoryEnumerator? GetInodeEnumerator(FsDirectory parentDir, out FuseError error)
    {
        if (parentDir.InodeId == 0)
        {
            Trace.WriteLine("Assertion failed: parentDir has an inode of 0 in GetInodeEnumerator");
            error = FuseError.IoError;
            return null;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(parentDir.InodeId);
        if (blockGroup == null)
        {
            error = FuseError.IoError;
            return null;
        }

        DirectoryImpl? directoryImpl = DirectoryImpl.GetDir(_superStructure, parentDir.InodeId, parentDir.Name);
        if (directoryImpl == null)
        {
            error = FuseError.NotADirectory;
            return null;
        }

        IEnumerator<InodeImpl> dirImplEnumerator = directoryImpl.GetEnumerator();
        FsDirectoryEnumerator enumerator = new(
            () =>
            {
                if (!dirImplEnumerator.MoveNext())
                {
                    dirImplEnumerator.Dispose();
                    return null;
                }

                if (dirImplEnumerator.Current.InodeType == DirEntryFileType.Regular)
                {
                    FsFile file = new()
                    {
                        InodeId = dirImplEnumerator.Current.InodeId,
                        Name = dirImplEnumerator.Current.Name,
                        InodeMode = (UnixFileMode)dirImplEnumerator.Current.Inode.Mode,
                        Parent = parentDir
                    };

                    parentDir.TryAdd(file);
                    return file;
                }

                if (dirImplEnumerator.Current.InodeType == DirEntryFileType.Directory)
                {
                    FsDirectory dir = new()
                    {
                        InodeId = dirImplEnumerator.Current.InodeId,
                        Name = dirImplEnumerator.Current.Name,
                        InodeMode = (UnixFileMode)dirImplEnumerator.Current.Inode.Mode,
                        Parent = parentDir
                    };

                    parentDir.TryAdd(dir);
                    return dir;
                }

                return null;
            },
            () => { dirImplEnumerator.Reset(); });

        error = FuseError.Success;
        return enumerator;
    }
}