using RottweilerVault.Ext2.Ext2Structures;

namespace RottweilerVault.Ext2.Implementations;

internal abstract class InodeImpl
{
    public abstract DirEntryFileType InodeType { get; }

    public Inode Inode { get; protected set; }
    public uint InodeId { get; protected set; }
    public string Name { get; protected set; }

    protected InodeImpl(Inode inode, uint inodeId, string name)
    {
        Inode = inode;
        InodeId = inodeId;
        Name = name;
    }

    public DirectoryEntry ToDirectoryEntry()
    {
        return new DirectoryEntry
        {
            Inode = InodeId,
            Name = Name,
            FileType = InodeType,
            RecordLength = (ushort)(DirectoryEntry.MIN_SIZE + Name.Length)
        };
    }
}