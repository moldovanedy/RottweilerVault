using RottweilerVault.FsBase;

namespace RottweilerVault.Ext2.Structures;

public class Superblock
{
    private const int STRUCTURE_SIZE = 84;

    private static uint NumInodes => 0x400_000;
    private static uint NumBlocks => 0x1_000_000;
    private static uint Unused3 => 0;
    public uint NumUnallocatedBlocks { get; set; }
    public uint NumUnallocatedInodes { get; set; }
    public static uint FirstDataBlockId => 0;
    public static uint BlockSizeMultiplier => 2;
    public static uint FragmentSizeMultiplier => 2;
    public static uint NumBlocksPerGroup => 0x8000;
    public static uint NumFragmentsPerGroup => 0x8000;
    public static uint NumInodesPerGroup => 4096 * 2;
    public uint LastMountTimestamp { get; set; }
    public uint LastWriteTimestamp { get; set; }
    private static ushort Unused4 => 0;
    private static ushort Unused5 => 1;
    public static ushort Ext2Signature => 0xef53;
    private static ushort Unused6 => 1;
    private static ushort Unused7 => 3;
    public static ushort MinorVersion => 0;
    private static uint Unused8 => 0;
    private static uint Unused9 => int.MaxValue;
    public static uint OsId => 0;
    public static uint MajorVersion => 0;
    private static ushort Unused10 => 0;
    private static ushort Unused11 => 0;

    public Superblock()
    {
    }

    public Superblock(byte[] buffer, ref int readPosition)
    {
        if (buffer.Length <= readPosition + STRUCTURE_SIZE)
        {
            throw new ArgumentException("Buffer is too small");
        }

        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        NumUnallocatedBlocks = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        NumUnallocatedInodes = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        LastMountTimestamp = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        LastWriteTimestamp = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        ushort signature = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        _ = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;

        if (signature != Ext2Signature)
        {
            throw new FormatException("Invalid EXT2 signature");
        }
    }


    /// <summary>
    /// Writes the current superblock in the given buffer at the given position. Does not take into account regular
    /// ext2 specifications like leaving 1024 bytes free before itself etc. so you are responsible for the given
    /// position
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

        BinaryUtils.ConvertUintToBytes(NumInodes, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(NumBlocks, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(Unused3, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(NumUnallocatedBlocks, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(NumUnallocatedInodes, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(FirstDataBlockId, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(BlockSizeMultiplier, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(FragmentSizeMultiplier, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(NumBlocksPerGroup, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(NumFragmentsPerGroup, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(NumInodesPerGroup, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(LastMountTimestamp, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(LastWriteTimestamp, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUshortToBytes(Unused4, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(Unused5, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(Ext2Signature, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(Unused6, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(Unused7, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(MinorVersion, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUintToBytes(Unused8, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(Unused9, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(OsId, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUintToBytes(MajorVersion, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUshortToBytes(Unused10, buffer, writePosition);
        writePosition += 2;
        BinaryUtils.ConvertUshortToBytes(Unused11, buffer, writePosition);
        writePosition += 2;
    }
}