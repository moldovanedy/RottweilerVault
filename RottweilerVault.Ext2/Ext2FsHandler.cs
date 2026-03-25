using System;
using System.IO;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;
using Tmds.Linux;

namespace RottweilerVault.Ext2;

public class Ext2FsHandler : IFsHandler
{
    public bool SupportsMultiThreading => false;

    public FsInode? CreateInode(
        FsDirectory parent, InodeType inodeType, UnixFileMode fileMode, ref FuseFileInfo fileInfo, out FuseError error)
    {
        throw new NotImplementedException();
    }

    public FuseError CreateDir(FsInode parent, UnixFileMode dirMode)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    public FuseError OpenFile(FsFile fileToOpen, ref FuseFileInfo fileInfo)
    {
        throw new NotImplementedException();
    }

    public FuseError CloseFile(FsFile fileToClose, ref FuseFileInfo fileInfo)
    {
        throw new NotImplementedException();
    }

    public int Read(FsFile fileToRead, ulong offset, Span<byte> buffer, ref FuseFileInfo fileInfo, out FuseError error)
    {
        throw new NotImplementedException();
    }

    public int Write(
        FsFile fileToWriteInto, ulong offset, ReadOnlySpan<byte> buffer, ref FuseFileInfo fileInfo, out FuseError error)
    {
        throw new NotImplementedException();
    }

    public FuseError RenameFile(string oldName, string newName, int flags)
    {
        throw new NotImplementedException();
    }


    public FsInode? GetInodeIfExists(FsDirectory parentDir, string name, out FuseError error)
    {
        throw new NotImplementedException();
    }

    public FsDirectoryEnumerator? GetInodeEnumerator(FsDirectory parentDir, out FuseError error)
    {
        throw new NotImplementedException();
    }
}