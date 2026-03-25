using System;
using System.Text;
using RottweilerVault.FsBase.Utils;

namespace RottweilerVault.Ext2.Ext2Structures;

public class DirectoryEntry
{
    public const int MIN_SIZE = 4 + 2 + 1 + 1;

    public uint Inode { get; set; }
    public ushort RecordLength { get; set; }
    public byte NameLength => (byte)Name.Length;
    public DirEntryFileType FileType { get; set; }
    public string Name { get; set; } = string.Empty;

    public DirectoryEntry()
    {
    }

    public DirectoryEntry(byte[] buffer, ref int readPosition)
    {
        if (buffer.Length <= readPosition)
        {
            throw new ArgumentException("Buffer is too small");
        }

        Inode = BinaryUtils.ConvertBytesToUint(buffer, readPosition);
        readPosition += 4;
        RecordLength = BinaryUtils.ConvertBytesToUshort(buffer, readPosition);
        readPosition += 2;
        int nameLength = buffer[readPosition];
        readPosition += 1;
        FileType =
            buffer[readPosition] > (byte)DirEntryFileType.SymLink
                ? DirEntryFileType.Unknown
                : (DirEntryFileType)buffer[readPosition];
        readPosition += 1;
        Name = Encoding.UTF8.GetString(buffer.AsSpan(readPosition, nameLength));
        readPosition += nameLength;
    }

    public void MarkAsLastBlock(ushort remainingSizeForPadding)
    {
        Inode = 0;
        RecordLength = remainingSizeForPadding;
        FileType = 0;
        Name = string.Empty;
    }

    public void WriteToBuffer(byte[] buffer, ref int writePosition)
    {
        if (buffer.Length <= writePosition)
        {
            throw new ArgumentException("Buffer is too small");
        }

        int startPosition = writePosition;

        BinaryUtils.ConvertUintToBytes(Inode, buffer, writePosition);
        writePosition += 4;
        BinaryUtils.ConvertUshortToBytes(RecordLength, buffer, writePosition);
        writePosition += 2;
        buffer[writePosition] = NameLength;
        writePosition += 1;
        buffer[writePosition] = (byte)FileType;
        writePosition += 1;
        Encoding.UTF8.GetBytes(Name, 0, Name.Length, buffer, writePosition);
        writePosition += NameLength;

        byte[] filler = new byte[RecordLength - (writePosition - startPosition)];
        Array.Copy(filler, 0, buffer, writePosition, filler.Length);
        writePosition += filler.Length;
    }
}