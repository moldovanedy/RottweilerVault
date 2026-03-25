namespace RottweilerVault.FsBase.FsStructures;

public enum InodeType : ushort
{
    Socket = 0xc000,
    SymLink = 0xa000,
    Regular = 0x8000,
    BlockDev = 0x6000,
    Directory = 0x4000,
    CharDev = 0x2000,
    Fifo = 0x1000
}