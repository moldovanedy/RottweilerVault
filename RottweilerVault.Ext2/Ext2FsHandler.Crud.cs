using System;
using System.Diagnostics;
using System.IO;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.Ext2.Implementations;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;

namespace RottweilerVault.Ext2;

internal partial class Ext2FsHandler
{
    public FsInode? CreateInode(
        FsDirectory parentDir,
        string name,
        InodeType inodeType,
        UnixFileMode fileMode,
        ref FuseFileInfo fileInfo,
        out FuseError error)
    {
        if (parentDir.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(parentDir)} has an inode of 0 in CreateInode");
            error = FuseError.IoError;
            return null;
        }

        if (parentDir.GetEntryOrNull(name) != null)
        {
            error = FuseError.AlreadyExists;
            return null;
        }

        DirectoryImpl? directoryImpl = DirectoryImpl.GetDir(_superStructure, parentDir.InodeId, parentDir.Name);
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
                    Parent = parentDir
                };

                Chown(newInode, _userUid, _userGid, ref fileInfo);
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
                    Parent = parentDir
                };

                Chown(newInode, _userUid, _userGid, ref fileInfo);
                error = FuseError.Success;
                return newInode;
            }
            default:
                error = FuseError.IoError;
                return null;
        }
    }

    public FuseError RemoveFile(FsFile file)
    {
        if (file.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(file)} has an inode of 0 in RemoveFile");
            return FuseError.IoError;
        }

        if (file.Parent == null)
        {
            Trace.WriteLine($"Assertion failed: {nameof(file)} has a null parent in RemoveFile");
            return FuseError.IoError;
        }

        if (file.Name == "." || file.Name == "..")
        {
            Trace.WriteLine($"Assertion failed: {nameof(file)} was \".\" or \"..\" RemoveFile");
            return FuseError.IoError;
        }

        DirectoryImpl? parentDirImpl = DirectoryImpl.GetDir(_superStructure, file.Parent.InodeId, file.Parent.Name);
        if (parentDirImpl == null)
        {
            return FuseError.NoEntry;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(file.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        parentDirImpl.RemoveDescendant(file.Name);

        Inode rawInode = blockGroup.GetLocalInode(file.InodeId);
        uint sizeInBlocks = rawInode.SmallLbaBlocksReserved / 8;
        while (sizeInBlocks > 0)
        {
            uint existingBlockId = _superStructure.GetBlockIdOfInodeDataOffset(rawInode, sizeInBlocks);
            if (existingBlockId != 0)
            {
                _superStructure.SetBlockIdToInodeDataOffset(rawInode, file.InodeId, 0, sizeInBlocks);
                blockGroup.FreeDataBlockFast(existingBlockId);
            }

            sizeInBlocks--;
        }

        blockGroup.CommitDataBlockChanges();
        blockGroup.DeleteInode(file.InodeId);
        return FuseError.Success;
    }

    public FuseError RemoveDir(FsDirectory dir)
    {
        if (dir.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(dir)} has an inode of 0 in RemoveDir");
            return FuseError.IoError;
        }

        //we can't remove "/", only its contents
        if (dir.Parent == null)
        {
            Trace.WriteLine($"Assertion failed: {nameof(dir)} has a null parent in RemoveDir");
            return FuseError.IoError;
        }

        DirectoryImpl? dirImpl = DirectoryImpl.GetDir(_superStructure, dir.InodeId, dir.Name);
        if (dirImpl == null)
        {
            return FuseError.NoEntry;
        }

        //remove all subdirectories and files in the directory
        int loopGuard = 0;
        bool isFinished = false;
        while (dirImpl.Inode.SmallLbaBlocksReserved > 0 && loopGuard < 10_000_000 && !isFinished)
        {
            InodeImpl? lastDescendant = dirImpl.GetLastDescendant();
            switch (lastDescendant?.InodeType)
            {
                case DirEntryFileType.Directory:
                {
                    FsInode? subDir = dir.GetEntryOrNull(lastDescendant.Name);
                    if (subDir == null)
                    {
                        subDir = GetInodeIfExists(dir, lastDescendant.Name, out FuseError error);
                        if (error != FuseError.Success)
                        {
                            return error;
                        }
                    }

                    if (subDir is not FsDirectory fsSubDirectory)
                    {
                        return FuseError.IoError;
                    }

                    //if reaching the special directories, just break out of the loop and remove the directory itself
                    if (fsSubDirectory.Name == "." || fsSubDirectory.Name == "..")
                    {
                        isFinished = true;
                        break;
                    }

                    RemoveDir(fsSubDirectory);
                    break;
                }
                case DirEntryFileType.Regular:
                {
                    FsInode? subFile = dir.GetEntryOrNull(lastDescendant.Name);
                    if (subFile == null)
                    {
                        subFile = GetInodeIfExists(dir, lastDescendant.Name, out FuseError error);
                        if (error != FuseError.Success)
                        {
                            return error;
                        }
                    }

                    if (subFile is not FsFile fsSubFile)
                    {
                        return FuseError.IoError;
                    }

                    RemoveFile(fsSubFile);
                    break;
                }
            }

            dirImpl.RemoveLastDescendant();
            loopGuard++;
        }

        //remove the directory entry itself
        DirectoryImpl? parentDirImpl = DirectoryImpl.GetDir(_superStructure, dir.Parent.InodeId, dir.Parent.Name);
        if (parentDirImpl == null)
        {
            return FuseError.NoEntry;
        }

        parentDirImpl.RemoveDescendant(dir.Name);

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(dirImpl.InodeId);
        blockGroup?.FreeDataBlock(dirImpl.Inode.DataBlocksIds[0]);
        blockGroup?.DeleteInode(dirImpl.InodeId);

        return FuseError.Success;
    }

    public FuseError UpdateTimestamps(FsFile file, TimestampData timestamp, ref FuseFileInfo fileInfo)
    {
        FileImpl? fileImpl = FileImpl.GetFile(_superStructure, file.InodeId, file.Name);
        if (fileImpl == null)
        {
            return FuseError.IoError;
        }

        if (timestamp.ShouldUpdateAccessTime)
        {
            fileImpl.Inode.LastAccessTime = (uint)timestamp.AccessTime;
        }

        if (timestamp.ShouldUpdateModifyTime)
        {
            fileImpl.Inode.LastWriteTime = (uint)timestamp.ModifyTime;
        }

        if (timestamp.ShouldUpdateAccessTime || timestamp.ShouldUpdateModifyTime)
        {
            _superStructure.GetBlockGroupOfInode(file.InodeId)?.UpdateInodeOnDisk(file.InodeId, fileImpl.Inode);
        }

        return FuseError.Success;
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

        //sometimes the uid/gid is set to -1 or 0xFFFFFFFF, which is invalid, so we just keep the old value
        if (uid != 0xFFFFFFFF)
        {
            fileImpl.Inode.UidLow = (ushort)uid;
            fileImpl.Inode.UidHigh = (ushort)(uid >> 16);
        }

        if (gid != 0xFFFFFFFF)
        {
            fileImpl.Inode.GidLow = (ushort)gid;
            fileImpl.Inode.GidHigh = (ushort)(gid >> 16);
        }

        fileImpl.Inode.LastWriteTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        blockGroup.UpdateInodeOnDisk(fileImpl.InodeId, fileImpl.Inode);

        return FuseError.Success;
    }
}