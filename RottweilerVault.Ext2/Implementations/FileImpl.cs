using System;
using System.IO;
using RottweilerVault.Ext2.Ext2Structures;

namespace RottweilerVault.Ext2.Implementations;

internal class FileImpl : InodeImpl
{
    public override DirEntryFileType InodeType => DirEntryFileType.Regular;

    private FileImpl(Inode inode, uint inodeId, string name) : base(inode, inodeId, name)
    {
    }

    public static FileImpl? GetFile(SuperStructure superStructure, uint inodeId, string name)
    {
        BlockGroup? blockGroup = superStructure.GetBlockGroupOfInode(inodeId);
        if (blockGroup == null)
        {
            return null;
        }

        Inode fileInode = blockGroup.GetLocalInode(inodeId);
        return new FileImpl(fileInode, inodeId, name);
    }

    public static FileImpl Create(
        SuperStructure superStructure,
        DirectoryImpl parentDir,
        string name,
        UnixFileMode fileMode)
    {
        Inode? inode = superStructure.CreateInode(out uint? inodeId);
        if (inode == null || inodeId == null)
        {
            throw new Exception("Could not create inode");
        }

        inode.CreateTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        inode.LastAccessTime = inode.CreateTime;
        inode.LastWriteTime = inode.CreateTime;
        inode.Mode = (ushort)fileMode;
        inode.HardLinksCount = 1;
        superStructure.GetBlockGroupOfInode(inodeId.Value)?.UpdateInodeOnDisk(inodeId.Value, inode);

        FileImpl file = new(inode, inodeId.Value, name);
        return file;
    }
}