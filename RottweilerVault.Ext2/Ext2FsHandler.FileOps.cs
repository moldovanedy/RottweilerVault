using System;
using System.Diagnostics;
using System.IO;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.Ext2.Implementations;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;
using Tmds.Linux;

namespace RottweilerVault.Ext2;

internal partial class Ext2FsHandler
{
    public FuseError OpenFile(FsFile file, ref FuseFileInfo fileInfo)
    {
        Inode? rawInode = _superStructure.GetBlockGroupOfInode(file.InodeId)?.GetLocalInode(file.InodeId);
        if (rawInode == null)
        {
            return FuseError.IoError;
        }

        uint fileGid = ((uint)rawInode.GidHigh << 16) | rawInode.GidLow;
        uint fileUid = ((uint)rawInode.UidHigh << 16) | rawInode.UidLow;
        int accessMode = fileInfo.flags & LibC.O_ACCMODE;

        bool isGranted = IsAccessGranted(fileUid, fileGid, (UnixFileMode)rawInode.Mode, accessMode);
        return !isGranted ? FuseError.AccessDenied : FuseError.Success;
    }

    public FuseError OpenDir(FsDirectory dir, ref FuseFileInfo fileInfo)
    {
        Inode? rawInode = _superStructure.GetBlockGroupOfInode(dir.InodeId)?.GetLocalInode(dir.InodeId);
        if (rawInode == null)
        {
            return FuseError.IoError;
        }

        uint fileGid = ((uint)rawInode.GidHigh << 16) | rawInode.GidLow;
        uint fileUid = ((uint)rawInode.UidHigh << 16) | rawInode.UidLow;
        int accessMode = fileInfo.flags & LibC.O_ACCMODE;

        bool isGranted = IsAccessGranted(fileUid, fileGid, (UnixFileMode)rawInode.Mode, accessMode);
        return !isGranted ? FuseError.AccessDenied : FuseError.Success;
    }

    public FuseError CloseFile(FsFile file, ref FuseFileInfo fileInfo)
    {
        return FuseError.Success;
    }

    public int Read(FsFile file, ulong offset, Span<byte> buffer, ref FuseFileInfo fileInfo, out FuseError error)
    {
        if (file.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(file)} has an inode of 0 in Read");
            error = FuseError.IoError;
            return 0;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(file.InodeId);
        if (blockGroup == null)
        {
            error = FuseError.IoError;
            return 0;
        }

        if (offset + (ulong)buffer.Length > (ulong)AesXtsWriter.BLOCK_SIZE * uint.MaxValue)
        {
            error = FuseError.FileTooLarge;
            return 0;
        }

        Inode rawInode = blockGroup.GetLocalInode(file.InodeId);

        int numBytesRead = 0;
        uint numBlocksRead = 0;
        while (numBytesRead < buffer.Length)
        {
            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(
                rawInode, (uint)(offset / AesXtsWriter.BLOCK_SIZE) + numBlocksRead);

            ReadOnlySpan<byte> blockData;
            if (blockId == 0)
            {
                blockData = new ReadOnlySpan<byte>(new byte[AesXtsWriter.BLOCK_SIZE]);
            }
            else
            {
                if (!blockGroup.TryGetDataBlock(blockId, out blockData))
                {
                    error = FuseError.IoError;
                    return numBytesRead;
                }
            }

            int numBytesToRead = buffer.Length - numBytesRead;
            if (numBytesToRead < AesXtsWriter.BLOCK_SIZE)
            {
                blockData[..numBytesToRead].CopyTo(buffer[numBytesRead..]);
                numBytesRead += numBytesToRead;
            }
            else
            {
                blockData.CopyTo(buffer[numBytesRead..]);
                numBytesRead += AesXtsWriter.BLOCK_SIZE;
            }

            numBlocksRead++;
        }

        error = FuseError.Success;
        return numBytesRead;
    }

    public int Write(
        FsFile file,
        ulong offset,
        ReadOnlySpan<byte> buffer,
        ref FuseFileInfo fileInfo,
        out FuseError error)
    {
        if (file.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(file)} has an inode of 0 in Write");
            error = FuseError.IoError;
            return 0;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(file.InodeId);
        if (blockGroup == null)
        {
            error = FuseError.IoError;
            return 0;
        }

        if (offset + (ulong)buffer.Length > (ulong)AesXtsWriter.BLOCK_SIZE * uint.MaxValue)
        {
            error = FuseError.FileTooLarge;
            return 0;
        }

        Inode rawInode = blockGroup.GetLocalInode(file.InodeId);

        int numBytesWritten = 0;
        uint numBlocksWritten = 0;
        while (numBytesWritten < buffer.Length)
        {
            uint inodeDataOffset = (uint)(offset / AesXtsWriter.BLOCK_SIZE) + numBlocksWritten;
            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(rawInode, inodeDataOffset);
            //if 0, reserve another block
            if (blockId == 0)
            {
                try
                {
                    _superStructure.AddDataBlockToInode(
                        rawInode, file.InodeId, inodeDataOffset, out blockId);
                }
                catch
                {
                    error = FuseError.IoError;
                    return numBytesWritten;
                }
            }

            byte[] data = new byte[AesXtsWriter.BLOCK_SIZE];

            int numBytesToWrite = buffer.Length - numBytesWritten;
            if (numBytesToWrite < AesXtsWriter.BLOCK_SIZE)
            {
                buffer[numBytesWritten..].CopyTo(data);
                numBytesWritten += numBytesToWrite;
            }
            else
            {
                buffer[numBytesWritten..(numBytesWritten + AesXtsWriter.BLOCK_SIZE)].CopyTo(data);
                numBytesWritten += AesXtsWriter.BLOCK_SIZE;
            }

            blockGroup.UpdateDataBlockOnDisk(blockId, data);
            numBlocksWritten++;
        }

        ulong totalSize = offset + (ulong)buffer.Length;
        rawInode.DataSizeLow = (uint)totalSize;
        rawInode.DataSizeHigh = (uint)(totalSize >> 32);

        rawInode.LastAccessTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        rawInode.LastWriteTime = rawInode.LastAccessTime;
        blockGroup.UpdateInodeOnDisk(file.InodeId, rawInode);

        error = FuseError.Success;
        return numBytesWritten;
    }

    public FuseError PreAllocate(FsFile file, ulong offset, long length, ref FuseFileInfo fileInfo)
    {
        if (file.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(file)} has an inode of 0 in PreAllocate");
            return FuseError.IoError;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(file.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        Inode rawInode = blockGroup.GetLocalInode(file.InodeId);

        uint numBlocksNeeded = (uint)(length / AesXtsWriter.BLOCK_SIZE) + 1;
        uint numBlocksAllocated = 0;
        while (numBlocksAllocated < numBlocksNeeded)
        {
            uint inodeDataOffset = (uint)(offset / AesXtsWriter.BLOCK_SIZE) + numBlocksAllocated;
            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(rawInode, inodeDataOffset);
            //if 0, reserve another block
            if (blockId == 0)
            {
                try
                {
                    _superStructure.AddDataBlockToInode(
                        rawInode, file.InodeId, inodeDataOffset, out blockId);
                }
                catch
                {
                    return FuseError.IoError;
                }
            }

            numBlocksAllocated++;
        }

        ulong totalSize = offset + (ulong)length;
        rawInode.DataSizeLow = (uint)totalSize;
        rawInode.DataSizeHigh = (uint)(totalSize >> 32);

        rawInode.LastAccessTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        rawInode.LastWriteTime = rawInode.LastAccessTime;
        blockGroup.UpdateInodeOnDisk(file.InodeId, rawInode);

        return FuseError.Success;
    }

    public FuseError Truncate(FsFile file, ulong newSize, ref FuseFileInfo fileInfo)
    {
        if (file.InodeId == 0)
        {
            Trace.WriteLine($"Assertion failed: {nameof(file)} has an inode of 0 in Truncate");
            return FuseError.IoError;
        }

        BlockGroup? blockGroup = _superStructure.GetBlockGroupOfInode(file.InodeId);
        if (blockGroup == null)
        {
            return FuseError.IoError;
        }

        Inode rawInode = blockGroup.GetLocalInode(file.InodeId);
        uint offsetInBlocks = rawInode.SmallLbaBlocksReserved / 8;
        uint newOffsetValue = (uint)(newSize / AesXtsWriter.BLOCK_SIZE) + 1;

        while (offsetInBlocks > newOffsetValue)
        {
            uint existingBlockId = _superStructure.GetBlockIdOfInodeDataOffset(rawInode, offsetInBlocks);
            if (existingBlockId != 0)
            {
                _superStructure.SetBlockIdToInodeDataOffset(rawInode, file.InodeId, 0, offsetInBlocks);
                blockGroup.FreeDataBlockFast(existingBlockId);
            }

            offsetInBlocks--;
        }

        blockGroup.CommitDataBlockChanges();

        rawInode.DataSizeLow = (uint)newSize;
        rawInode.DataSizeHigh = (uint)(newSize >> 32);

        rawInode.LastAccessTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        rawInode.LastWriteTime = rawInode.LastAccessTime;
        blockGroup.UpdateInodeOnDisk(file.InodeId, rawInode);

        return FuseError.IoError;
    }
}