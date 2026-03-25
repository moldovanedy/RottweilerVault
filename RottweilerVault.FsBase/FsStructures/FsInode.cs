using System;
using System.IO;

namespace RottweilerVault.FsBase.FsStructures;

public abstract class FsInode
{
    public FsInode? Parent { get; set; }

    public uint Inode { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset LastModified { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastAccessed { get; set; }

    public UnixFileMode InodeMode { get; set; }
}