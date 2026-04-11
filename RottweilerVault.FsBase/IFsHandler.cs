using System;
using System.IO;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;
using Tmds.Linux;

namespace RottweilerVault.FsBase;

public interface IFsHandler
{
    public bool SupportsMultiThreading { get; }

    public bool IsAccessAllowed(FsInode inode, int accessMode);

    public FsInode? CreateInode(
        FsDirectory parentDir,
        string name,
        InodeType inodeType,
        UnixFileMode fileMode,
        ref FuseFileInfo fileInfo,
        out FuseError error);

    public FuseError RemoveFile(FsFile file);

    public FuseError RemoveDir(FsDirectory dir);


    public FuseError GetAttributes(FsInode inode, ref stat statAttributes);

    public FuseError OpenFile(FsFile file, ref FuseFileInfo fileInfo);

    public FuseError OpenDir(FsDirectory dir, ref FuseFileInfo fileInfo);

    public FuseError CloseFile(FsFile file, ref FuseFileInfo fileInfo);

    public FuseError UpdateTimestamps(FsFile file, TimestampData timestamp, ref FuseFileInfo fileInfo);


    public int Read(FsFile file, ulong offset, Span<byte> buffer, ref FuseFileInfo fileInfo, out FuseError error);

    public int Write(
        FsFile file,
        ulong offset,
        ReadOnlySpan<byte> buffer,
        ref FuseFileInfo fileInfo,
        out FuseError error);

    public FuseError Truncate(FsFile file, ulong newSize, ref FuseFileInfo fileInfo);

    public FuseError PreAllocate(FsFile file, ulong offset, long length, ref FuseFileInfo fileInfo);


    public FuseError RenameFile(FsDirectory parentDir, string oldName, string newName, int flags);

    public FuseError Chmod(FsInode inode, UnixFileMode newMode, ref FuseFileInfo fileInfo);

    public FuseError Chown(FsInode inode, uint uid, uint gid, ref FuseFileInfo fileInfo);

    public FuseError GetFsStats(ref statvfs stats);


    public FsInode? GetInodeIfExists(FsDirectory parentDir, string name, out FuseError error);

    public FsDirectoryEnumerator? GetInodeEnumerator(FsDirectory parentDir, out FuseError error);
}