using System.IO;

namespace RottweilerVault.FsBase.FsStructures;

public abstract class FsInode
{
    public FsDirectory? Parent { get; set; }

    public uint InodeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public UnixFileMode InodeMode { get; set; }
}