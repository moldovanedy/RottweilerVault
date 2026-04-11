using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.Ext2.Implementations;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Linux;

namespace RottweilerVault.Ext2;

internal partial class Ext2FsHandler : IFsHandler
{
    public bool SupportsMultiThreading => false;

    private readonly SuperStructure _superStructure;

    private readonly uint _userUid;
    private readonly uint _userGid;

    public Ext2FsHandler(SuperStructure superStructure)
    {
        _superStructure = superStructure;
        _userUid = LibC.geteuid();
        _userGid = LibC.getegid();
    }

    public bool IsAccessAllowed(FsInode inode, int accessMode)
    {
        if (inode.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(inode)} has an inode of 0 in IsAccessAllowed");
            return false;
        }

        Inode? rawInode = _superStructure.GetBlockGroupOfInode(inode.InodeId)?.GetLocalInode(inode.InodeId);
        if (rawInode == null)
        {
            return false;
        }

        //checking if file exists, so return true
        if (accessMode == LibC.F_OK)
        {
            return true;
        }

        uint fileGid = ((uint)rawInode.GidHigh << 16) | rawInode.GidLow;
        uint fileUid = ((uint)rawInode.UidHigh << 16) | rawInode.UidLow;

        UnixFileMode readMask;
        UnixFileMode writeMask;
        UnixFileMode executeMask;
        var actualFileMode = (UnixFileMode)rawInode.Mode;

        if (fileUid == _userUid)
        {
            readMask = UnixFileMode.UserRead;
            writeMask = UnixFileMode.UserWrite;
            executeMask = UnixFileMode.UserExecute;
        }
        else if (fileGid == _userGid)
        {
            readMask = UnixFileMode.GroupRead;
            writeMask = UnixFileMode.GroupWrite;
            executeMask = UnixFileMode.GroupExecute;
        }
        else
        {
            readMask = UnixFileMode.OtherRead;
            writeMask = UnixFileMode.OtherWrite;
            executeMask = UnixFileMode.OtherExecute;
        }

        if (accessMode == LibC.R_OK)
        {
            return (actualFileMode & readMask) != UnixFileMode.None;
        }

        if (accessMode == LibC.W_OK)
        {
            return (actualFileMode & writeMask) != UnixFileMode.None;
        }

        if (accessMode == LibC.X_OK)
        {
            return (actualFileMode & executeMask) != UnixFileMode.None;
        }

        return false;
    }

    public FuseError GetAttributes(FsInode inode, ref stat statAttributes)
    {
        if (inode.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(inode)} has an inode of 0 in GetAttributes");
            return FuseError.IoError;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(inode.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        Inode rawInode = blockGroup.GetLocalInode(inode.InodeId);
        statAttributes.st_blksize = AesXtsWriter.BLOCK_SIZE;
        statAttributes.st_blocks = rawInode.SmallLbaBlocksReserved / 8;
        statAttributes.st_size = ((long)rawInode.DataSizeHigh << 32) | rawInode.DataSizeLow;

        statAttributes.st_ino = inode.InodeId;
        statAttributes.st_uid = ((uint)rawInode.UidHigh << 16) | rawInode.UidLow;
        statAttributes.st_gid = ((uint)rawInode.GidHigh << 16) | rawInode.GidLow;
        statAttributes.st_mode =
            (inode is FsDirectory ? LibC.S_IFDIR : LibC.S_IFREG) | (ushort)inode.InodeMode;
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

    public FuseError GetFsStats(ref statvfs stats)
    {
        stats.f_bsize = AesXtsWriter.BLOCK_SIZE;
        stats.f_frsize = AesXtsWriter.BLOCK_SIZE;
        stats.f_blocks = Superblock.NumBlocks;
        stats.f_bfree = _superStructure.ExtSuperblock.NumUnallocatedBlocks;
        stats.f_bavail = _superStructure.ExtSuperblock.NumUnallocatedBlocks;
        stats.f_files = Superblock.NumInodes;
        stats.f_ffree = _superStructure.ExtSuperblock.NumUnallocatedInodes;
        stats.f_favail = _superStructure.ExtSuperblock.NumUnallocatedInodes;
        stats.f_namemax = 255;
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
            dirImplEnumerator.Reset);

        error = FuseError.Success;
        return enumerator;
    }


    private bool IsAccessGranted(uint fileUid, uint fileGid, UnixFileMode fileMode, int accessMode)
    {
        UnixFileMode readMask;
        UnixFileMode writeMask;

        if (fileUid == _userUid)
        {
            readMask = UnixFileMode.UserRead;
            writeMask = UnixFileMode.UserWrite;
        }
        else if (fileGid == _userGid)
        {
            readMask = UnixFileMode.GroupRead;
            writeMask = UnixFileMode.GroupWrite;
        }
        else
        {
            readMask = UnixFileMode.OtherRead;
            writeMask = UnixFileMode.OtherWrite;
        }

        if (accessMode == LibC.O_RDONLY)
        {
            return (fileMode & readMask) != UnixFileMode.None;
        }

        if (accessMode == LibC.O_WRONLY)
        {
            return (fileMode & writeMask) != UnixFileMode.None;
        }

        if (accessMode == LibC.O_RDWR)
        {
            return (fileMode & writeMask) != UnixFileMode.None && (fileMode & readMask) != UnixFileMode.None;
        }

        return false;
    }
}