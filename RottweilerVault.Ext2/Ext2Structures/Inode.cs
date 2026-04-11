using System;
using System.IO;
using RottweilerVault.FsBase.FsStructures;
using RottweilerVault.FsBase.Utils;

namespace RottweilerVault.Ext2.Ext2Structures;

public class Inode
{
    public const int NUM_INODES_IN_TABLE = 4096 * 2;
    public const int NUM_INODES_IN_BLOCK = 4096 / STRUCTURE_SIZE;

    public const int STRUCTURE_SIZE = 128;

    /// <summary>
    /// Use <see cref="InodeType"/> and <see cref="UnixFileMode"/> for this.
    /// </summary>
    public ushort Mode { get; set; }

    public ushort UidLow { get; set; }

    /// <summary>
    /// For once, we use version 1, not 0, as we want 64-bit file sizes.
    /// </summary>
    public uint DataSizeLow { get; set; }

    public uint LastAccessTime { get; set; }
    public uint CreateTime { get; set; }
    public uint LastWriteTime { get; set; }
    public uint DeleteTime { get; set; }
    public ushort GidLow { get; set; }
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
    private static uint Reserved4 => 0;
    public ushort UidHigh { get; set; }
    public ushort GidHigh { get; set; }
    private static uint Reserved5 => 0;

    public Inode()
    {
    }

    public Inode(byte[] buffer, ref int readPosition)
    {
        if (buffer.Length < readPosition + STRUCTURE_SIZE)
        {
            throw new ArgumentException("Buffer is too small");
        }

        Mode = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        UidLow = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
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
        GidLow = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
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
        _ = BinaryUtils.ConvertBytesToInt(buffer, readPosition);
        readPosition += 4;
        UidHigh = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        GidHigh = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
    }

    public void WriteToBuffer(byte[] buffer, ref int writePosition)
    {
        if (buffer.Length < writePosition + STRUCTURE_SIZE)
        {
            throw new ArgumentException("Buffer is too small");
        }

        BinaryUtils.ConvertUshortToBytes(Mode, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(UidLow, buffer, writePosition);
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
        BinaryUtils.ConvertUshortToBytes(GidLow, buffer, writePosition);
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
        BinaryUtils.ConvertUintToBytes(Reserved4, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUshortToBytes(UidHigh, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(GidHigh, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUintToBytes(Reserved5, buffer, writePosition);
        writePosition += 4;
    }
}