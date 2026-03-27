using System;
using System.IO;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;
using Tmds.Linux;

namespace RottweilerVault.FsBase;

public interface IFsHandler
{
    public bool SupportsMultiThreading { get; }

    public FsInode? CreateInode(
        FsDirectory parent,
        string name,
        InodeType inodeType,
        UnixFileMode fileMode,
        ref FuseFileInfo fileInfo,
        out FuseError error);

    public FuseError RemoveFile(FsFile fileToDelete);

    public FuseError RemoveDir(FsDirectory dirToDelete);

    public FuseError GetAttributes(FsInode inodeToQuery, ref stat statAttributes);

    public FuseError OpenFile(FsFile fileToOpen, ref FuseFileInfo fileInfo);

    public FuseError CloseFile(FsFile fileToClose, ref FuseFileInfo fileInfo);

    public int Read(FsFile fileToRead, ulong offset, Span<byte> buffer, ref FuseFileInfo fileInfo, out FuseError error);

    public int Write(
        FsFile fileToWriteInto,
        ulong offset,
        ReadOnlySpan<byte> buffer,
        ref FuseFileInfo fileInfo,
        out FuseError error);

    public FuseError RenameFile(string oldName, string newName, int flags);


    public FsInode? GetInodeIfExists(FsDirectory parentDir, string name, out FuseError error);

    public FsDirectoryEnumerator? GetInodeEnumerator(FsDirectory parentDir, out FuseError error);
}