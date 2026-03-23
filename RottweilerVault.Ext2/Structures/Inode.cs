using RottweilerVault.FsBase;

namespace RottweilerVault.Ext2.Structures;

public class Inode
{
    public const int NUM_INODES_IN_TABLE = 4096 * 2;

    private const int STRUCTURE_SIZE = 128;

    public ushort Mode { get; set; }
    public ushort Uid { get; set; }

    /// <summary>
    /// For once, we use version 1, not 0, as we want 64-bit file sizes.
    /// </summary>
    public uint DataSizeLow { get; set; }

    public uint LastAccessTime { get; set; }
    public uint CreateTime { get; set; }
    public uint LastWriteTime { get; set; }
    public uint DeleteTime { get; set; }
    public ushort Gid { get; set; }
    public ushort HardLinksCount { get; set; }

    /// <summary>
    /// The number of 512-byte data blocks reserved. This needs to be divided by 8 to actually use as an index in the
    /// blocks array.
    /// </summary>
    public uint SmallLbaBlocksReserved { get; set; }

    public uint Flags { get; set; }
    private static uint Reserved1 => 0;
    public uint[] DataBlocksIds { get; set; } = new uint[15];
    public uint FileVersion { get; set; }
    private static uint Reserved2 => 0;
    public uint DataSizeHigh { get; set; }
    private static uint Reserved3 => 0;
    private static ulong Reserved4 => 0;
    private static uint Reserved5 => 0;

    public Inode()
    {
    }

    public Inode(byte[] buffer, ref int readPosition)
    {
        if (buffer.Length <= readPosition + STRUCTURE_SIZE)
        {
            throw new ArgumentException("Buffer is too small");
        }

        Mode = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        Uid = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        DataSizeLow = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        LastAccessTime = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        CreateTime = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        LastWriteTime = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        DeleteTime = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        Gid = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        HardLinksCount = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        SmallLbaBlocksReserved = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        Flags = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;

        DataBlocksIds = new uint[15];
        for (int i = 0; i < DataBlocksIds.Length; i++)
        {
            DataBlocksIds[i] = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
            readPosition += 4;
        }

        FileVersion = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        DataSizeHigh = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToLong(buffer, readPosition);
        readPosition += 8;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
    }

    public void WriteToBuffer(byte[] buffer, ref int writePosition)
    {
        if (buffer.Length <= writePosition + STRUCTURE_SIZE)
        {
            throw new ArgumentException("Buffer is too small");
        }

        BinaryUtils.ConvertUshortToBytes(Mode, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(Uid, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUintToBytes(DataSizeLow, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(LastAccessTime, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(CreateTime, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(LastWriteTime, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(DeleteTime, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUshortToBytes(Gid, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(HardLinksCount, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUintToBytes(SmallLbaBlocksReserved, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(Flags, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(Reserved1, buffer, writePosition);
        writePosition += 4;

        foreach (uint blockId in DataBlocksIds)
        {
            BinaryUtils.ConvertUintToBytes(blockId, buffer, writePosition);
            writePosition += 4;
        }

        BinaryUtils.ConvertUintToBytes(FileVersion, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(Reserved2, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(DataSizeHigh, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(Reserved3, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertLongToBytes(unchecked((long)Reserved4), buffer, writePosition);
        writePosition += 8;
        BinaryUtils.ConvertUintToBytes(Reserved5, buffer, writePosition);
        writePosition += 4;
    }
}