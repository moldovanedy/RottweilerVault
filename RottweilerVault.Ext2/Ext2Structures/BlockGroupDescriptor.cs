using System;
using RottweilerVault.FsBase.Utils;

namespace RottweilerVault.Ext2.Ext2Structures;

public class BlockGroupDescriptor
{
    public const int NUM_DESCRIPTORS_IN_TABLE = 512;

    private const int STRUCTURE_SIZE = 32;

    public uint BlockBitmapBlockId { get; set; }
    public uint InodeBitmapBlockId { get; set; }
    public uint InodeTableStartBlockId { get; set; }
    public ushort NumFreeBlocks { get; set; }
    public ushort NumFreeInodes { get; set; }
    public ushort NumInodesForDirs { get; set; }

    private static ushort Padding => 0;
    private static ulong Reserved1 => 0;
    private static uint Reserved2 => 0;


    public BlockGroupDescriptor()
    {
    }

    public BlockGroupDescriptor(byte[] buffer, ref int readPosition)
    {
        if (buffer.Length <= readPosition + STRUCTURE_SIZE)
        {
            throw new ArgumentException("Buffer is too small");
        }

        BlockBitmapBlockId = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        InodeBitmapBlockId = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        InodeTableStartBlockId = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        NumFreeBlocks = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        NumFreeInodes = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        NumInodesForDirs = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToLong(buffer, readPosition);
        readPosition += 8;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
    }

    /// <summary>
    /// Writes the current block group to the given buffer.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="writePosition">
    /// When in, acts as an offset in the buffer. When out, it is the position directly after the last byte written.
    /// </param>
    public void WriteToBuffer(byte[] buffer, ref int writePosition)
    {
        if (buffer.Length <= writePosition + STRUCTURE_SIZE)
        {
            throw new ArgumentException("Buffer is too small");
        }

        BinaryUtils.ConvertUintToBytes(BlockBitmapBlockId, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(InodeBitmapBlockId, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(InodeTableStartBlockId, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUshortToBytes(NumFreeBlocks, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(NumFreeInodes, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(NumInodesForDirs, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(Padding, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertLongToBytes(unchecked((long)Reserved1), buffer, writePosition);
        writePosition += 8;
        BinaryUtils.ConvertUintToBytes(Reserved2, buffer, writePosition);
        writePosition += 4;
    }
}