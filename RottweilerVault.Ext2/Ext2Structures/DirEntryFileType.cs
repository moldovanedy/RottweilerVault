namespace RottweilerVault.Ext2.Ext2Structures;

public enum DirEntryFileType : byte
{
    Unknown = 0,
    Regular = 1,
    Directory = 2,
    CharDev = 3,
    BlockDev = 4,
    Fifo = 5,
    Socket = 6,
    SymLink = 7
}