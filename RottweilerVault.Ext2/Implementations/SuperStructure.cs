using System;
using System.Collections.Generic;
using System.IO;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.Utils;

namespace RottweilerVault.Ext2.Implementations;

/// <summary>
/// Handles the general bookkeeping in Ext2, handling both the superblock and all the block groups (which is an
/// independent class). 
/// </summary>
internal class SuperStructure : IDisposable
{
    /// <summary>
    /// The total number of blocks that are for bookkeeping, not data blocks. Never write arbitrary data in the first
    /// this number of blocks. This is per block group.
    /// </summary>
    /// <remarks>
    /// These blocks are: 1 for the superblock, 4 for the block group descriptor, 1 for the block bitmap,
    /// 1 for the inode bitmap, 256 for the inode descriptors
    /// </remarks>
    public const int NUM_NON_DATA_BLOCKS_PER_GROUP = 1 + 4 + 1 + 1 + 256;

    private const uint NUM_BLOCKS_IN_INDIRECTION_BLOCK = 1024;

    public AesXtsWriter CryptoWriter { get; }
    public FileStream SharedFs { get; }

    public Superblock ExtSuperblock { get; private set; } = null!;
    public List<BlockGroup> BlockGroups { get; } = [];

    /// <summary>
    /// Loads all relevant data structures. Propagates all errors, so there might be an exception thrown by this
    /// constructor.
    /// </summary>
    /// <param name="sharedFs">Takes ownership of this stream, so don't manually dispose of it.</param>
    /// <param name="key1"></param>
    /// <param name="key2"></param>
    public SuperStructure(FileStream sharedFs, byte[] key1, byte[] key2)
    {
        SharedFs = sharedFs;
        CryptoWriter = new AesXtsWriter(key1, key2);

        SharedFs.Seek(0, SeekOrigin.Begin);
        LoadSuperblock();
        LoadBlockGroupDescriptors();
    }

    public void Dispose()
    {
        WriteAllBlockDescriptorTables();

        SharedFs.Close();
    }

    public BlockGroup? GetBlockGroupOfInode(uint inodeId)
    {
        uint blockIndex = (inodeId - 1) / Inode.NUM_INODES_IN_TABLE;
        return BlockGroups.Count <= blockIndex ? null : BlockGroups[(int)blockIndex];
    }

    public Inode? CreateInode(out uint? inodeId)
    {
        BlockGroup? group = null;
        foreach (BlockGroup blockGroup in BlockGroups)
        {
            if (blockGroup.Ext2BlockDescriptor.NumFreeInodes > 0)
            {
                group = blockGroup;
                break;
            }
        }

        if (group == null)
        {
            CreateNewBlockGroup();
            group = BlockGroups[^1];
        }

        return !group.TryCreateInode(out Inode? inode, out inodeId) ? null : inode;
    }

    public void AddDataBlockToInode(Inode inode, uint inodeId, uint? inodeDataOffset, out uint blockId)
    {
        // uint? newBlockId = ReserveDataBlock();
        // if (newBlockId == null)
        // {
        //     throw new Exception("Could not add data block to inode");
        // }

        uint? newBlockId = ReserveDataBlock();
        if (newBlockId == null)
        {
            throw new Exception("Could not reserve data block");
        }

        if (inodeDataOffset != null)
        {
            SetBlockIdToInodeDataOffset(inode, inodeId, newBlockId.Value, inodeDataOffset.Value);
        }
        else
        {
            uint offset = inode.SmallLbaBlocksReserved / 8;
            SetBlockIdToInodeDataOffset(inode, inodeId, newBlockId.Value, offset);
        }

        blockId = newBlockId.Value;
    }

    public uint? ReserveDataBlock()
    {
        BlockGroup? group = null;
        foreach (BlockGroup blockGroup in BlockGroups)
        {
            if (blockGroup.Ext2BlockDescriptor.NumFreeBlocks > 0)
            {
                group = blockGroup;
                break;
            }
        }

        if (group == null)
        {
            CreateNewBlockGroup();
            group = BlockGroups[^1];
        }

        return group.TryReserveDataBlock(out uint? blockId) ? blockId : null;
    }

    public void CreateNewBlockGroup()
    {
        if (BlockGroups.Count == BlockGroupDescriptor.NUM_DESCRIPTORS_IN_TABLE)
        {
            throw new Exception("Out of space");
        }

        uint startBlock = (uint)BlockGroups.Count * Superblock.NumBlocksPerGroup;
        BlockGroup newGroup = new(
            BlockGroups.Count,
            new BlockGroupDescriptor
            {
                NumFreeInodes = Inode.NUM_INODES_IN_TABLE,
                NumFreeBlocks = (ushort)Superblock.NumBlocksPerGroup,
                BlockBitmapBlockId = startBlock + 5,
                InodeBitmapBlockId = startBlock + 6,
                InodeTableStartBlockId = startBlock + 7
            },
            this);

        BlockGroups.Add(newGroup);
        WriteAllBlockDescriptorTables();
    }

    /// <param name="inode"></param>
    /// <param name="inodeDataOffset"></param>
    /// <returns>Returns the block ID of the given offset of data (in blocks).</returns>
    public uint GetBlockIdOfInodeDataOffset(Inode inode, uint inodeDataOffset)
    {
        switch (inodeDataOffset)
        {
            case < 12:
                return inode.DataBlocksIds[inodeDataOffset];
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK when inode.DataBlocksIds[12] == 0:
                return 0;
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK:
            {
                byte[] singleIndirect = CryptoWriter.DecryptLba(SharedFs, inode.DataBlocksIds[12]);
                return BinaryUtils.ConvertBytesToUint(
                    singleIndirect,
                    (int)((inodeDataOffset - 12) * sizeof(uint)));
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK
                when inode.DataBlocksIds[13] == 0:
                return 0;
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK:
            {
                inodeDataOffset -= 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK;

                byte[] doubleIndirect = CryptoWriter.DecryptLba(SharedFs, inode.DataBlocksIds[13]);
                uint singleIndirectIdx = BinaryUtils.ConvertBytesToUint(
                    doubleIndirect,
                    (int)(inodeDataOffset / NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint)));

                byte[] singleIndirect = CryptoWriter.DecryptLba(SharedFs, doubleIndirect[singleIndirectIdx]);
                return BinaryUtils.ConvertBytesToUint(
                    singleIndirect,
                    (int)(inodeDataOffset % NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint)));
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK *
                NUM_BLOCKS_IN_INDIRECTION_BLOCK
                when inode.DataBlocksIds[14] == 0:
                return 0;
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK *
                NUM_BLOCKS_IN_INDIRECTION_BLOCK:
            {
                inodeDataOffset -= 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK;

                byte[] tripleIndirect = CryptoWriter.DecryptLba(SharedFs, inode.DataBlocksIds[14]);
                uint doubleIndirectIdx = BinaryUtils.ConvertBytesToUint(
                    tripleIndirect,
                    (int)(inodeDataOffset
                        / (NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK) * sizeof(uint)));

                byte[] doubleIndirect = CryptoWriter.DecryptLba(SharedFs, tripleIndirect[doubleIndirectIdx]);
                uint singleIndirectIdx = BinaryUtils.ConvertBytesToUint(
                    doubleIndirect,
                    (int)(inodeDataOffset / NUM_BLOCKS_IN_INDIRECTION_BLOCK
                        % NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint)));

                byte[] singleIndirect = CryptoWriter.DecryptLba(SharedFs, doubleIndirect[singleIndirectIdx]);
                return BinaryUtils.ConvertBytesToUint(
                    singleIndirect,
                    (int)(inodeDataOffset % NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint)));
            }
            default:
                return 0;
        }
    }

    public void SetBlockIdToInodeDataOffset(Inode inode, uint inodeId, uint blockId, uint inodeDataOffset)
    {
        byte[] blockIdRaw = BinaryUtils.ConvertUintToBytes(blockId);

        switch (inodeDataOffset)
        {
            case < 12:
            {
                uint previousBlockId = inode.DataBlocksIds[inodeDataOffset];
                inode.DataBlocksIds[inodeDataOffset] = blockId;

                if (previousBlockId == 0 && blockId != 0)
                {
                    inode.SmallLbaBlocksReserved += 8;
                }
                else if (previousBlockId != 0 && blockId == 0)
                {
                    inode.SmallLbaBlocksReserved -= 8;
                }

                GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                break;
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK when inode.DataBlocksIds[12] == 0:
            {
                uint? singleIndirectBlockId = ReserveDataBlock();
                if (singleIndirectBlockId == null)
                {
                    throw new Exception("Could not reserve single indirect block");
                }

                GetBlockGroupOfInode(inodeId)
                    ?.UpdateDataBlockOnDisk(singleIndirectBlockId.Value, new byte[AesXtsWriter.BLOCK_SIZE]);

                inode.DataBlocksIds[12] = singleIndirectBlockId.Value;
                inode.SmallLbaBlocksReserved += 8;
                GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                //retry
                // ReSharper disable once TailRecursiveCall
                SetBlockIdToInodeDataOffset(inode, inodeId, blockId, inodeDataOffset);
                break;
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK:
            {
                byte[] singleIndirect = CryptoWriter.DecryptLba(SharedFs, inode.DataBlocksIds[12]);
                int startIdx = (int)((inodeDataOffset - 12) * sizeof(uint));
                uint previousBlockId = BinaryUtils.ConvertBytesToUint(singleIndirect, startIdx);
                blockIdRaw.CopyTo(singleIndirect, startIdx);
                CryptoWriter.EncryptLba(SharedFs, inode.DataBlocksIds[12], singleIndirect);

                if (previousBlockId == 0 && blockId != 0)
                {
                    inode.SmallLbaBlocksReserved += 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }
                else if (previousBlockId != 0 && blockId == 0)
                {
                    inode.SmallLbaBlocksReserved -= 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }

                break;
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK
                when inode.DataBlocksIds[13] == 0:
            {
                uint? doubleIndirectBlockId = ReserveDataBlock();
                if (doubleIndirectBlockId == null)
                {
                    throw new Exception("Could not reserve double indirect block");
                }

                GetBlockGroupOfInode(inodeId)
                    ?.UpdateDataBlockOnDisk(doubleIndirectBlockId.Value, new byte[AesXtsWriter.BLOCK_SIZE]);

                inode.DataBlocksIds[13] = doubleIndirectBlockId.Value;
                inode.SmallLbaBlocksReserved += 8;
                GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                //retry
                // ReSharper disable once TailRecursiveCall
                SetBlockIdToInodeDataOffset(inode, inodeId, blockId, inodeDataOffset);
                break;
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK:
            {
                inodeDataOffset -= 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK;

                byte[] doubleIndirect = CryptoWriter.DecryptLba(SharedFs, inode.DataBlocksIds[13]);
                int startIdx = (int)(inodeDataOffset / NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint));
                uint previousBlockId = BinaryUtils.ConvertBytesToUint(doubleIndirect, startIdx);
                uint singleIndirectIdx = BinaryUtils.ConvertBytesToUint(doubleIndirect, startIdx);
                CryptoWriter.EncryptLba(SharedFs, inode.DataBlocksIds[13], doubleIndirect);

                if (previousBlockId == 0 && blockId != 0)
                {
                    inode.SmallLbaBlocksReserved += 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }
                else if (previousBlockId != 0 && blockId == 0)
                {
                    inode.SmallLbaBlocksReserved -= 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }


                uint? singleIndirectBlockId = BinaryUtils.ConvertBytesToUint(
                    doubleIndirect,
                    (int)singleIndirectIdx);
                if (singleIndirectBlockId == 0)
                {
                    singleIndirectBlockId = ReserveDataBlock();
                    if (singleIndirectBlockId == null)
                    {
                        throw new Exception("Could not reserve single indirect block");
                    }

                    GetBlockGroupOfInode(inodeId)
                        ?.UpdateDataBlockOnDisk(singleIndirectBlockId.Value, new byte[AesXtsWriter.BLOCK_SIZE]);

                    BinaryUtils.ConvertUintToBytes(singleIndirectBlockId.Value, doubleIndirect, (int)singleIndirectIdx);
                    //retry
                    // ReSharper disable once TailRecursiveCall
                    SetBlockIdToInodeDataOffset(inode, inodeId, blockId, inodeDataOffset);
                }

                byte[] singleIndirect = CryptoWriter.DecryptLba(SharedFs, singleIndirectBlockId.Value);
                startIdx = (int)(inodeDataOffset % NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint));
                previousBlockId = BinaryUtils.ConvertBytesToUint(singleIndirect, startIdx);
                blockIdRaw.CopyTo(singleIndirect, startIdx);
                CryptoWriter.EncryptLba(SharedFs, singleIndirectBlockId.Value, singleIndirect);

                if (previousBlockId == 0 && blockId != 0)
                {
                    inode.SmallLbaBlocksReserved += 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }
                else if (previousBlockId != 0 && blockId == 0)
                {
                    inode.SmallLbaBlocksReserved -= 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }

                break;
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK *
                NUM_BLOCKS_IN_INDIRECTION_BLOCK
                when inode.DataBlocksIds[14] == 0:
            {
                uint? tripleIndirectBlockId = ReserveDataBlock();
                if (tripleIndirectBlockId == null)
                {
                    throw new Exception("Could not reserve triple indirect block");
                }

                GetBlockGroupOfInode(inodeId)
                    ?.UpdateDataBlockOnDisk(tripleIndirectBlockId.Value, new byte[AesXtsWriter.BLOCK_SIZE]);

                inode.DataBlocksIds[14] = tripleIndirectBlockId.Value;
                inode.SmallLbaBlocksReserved += 8;
                GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                //retry
                // ReSharper disable once TailRecursiveCall
                SetBlockIdToInodeDataOffset(inode, inodeId, blockId, inodeDataOffset);
                break;
            }
            case < 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK *
                NUM_BLOCKS_IN_INDIRECTION_BLOCK:
            {
                inodeDataOffset -= 12 + NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK;

                byte[] tripleIndirect = CryptoWriter.DecryptLba(SharedFs, inode.DataBlocksIds[14]);
                int startIdx = (int)(inodeDataOffset /
                    (NUM_BLOCKS_IN_INDIRECTION_BLOCK * NUM_BLOCKS_IN_INDIRECTION_BLOCK) * sizeof(uint));
                uint previousBlockId = BinaryUtils.ConvertBytesToUint(tripleIndirect, startIdx);
                uint doubleIndirectIdx = BinaryUtils.ConvertBytesToUint(tripleIndirect, startIdx);
                CryptoWriter.EncryptLba(SharedFs, inode.DataBlocksIds[14], tripleIndirect);

                if (previousBlockId == 0 && blockId != 0)
                {
                    inode.SmallLbaBlocksReserved += 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }
                else if (previousBlockId != 0 && blockId == 0)
                {
                    inode.SmallLbaBlocksReserved -= 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }


                uint? doubleIndirectBlockId = BinaryUtils.ConvertBytesToUint(
                    tripleIndirect,
                    (int)doubleIndirectIdx);
                if (doubleIndirectBlockId == 0)
                {
                    doubleIndirectBlockId = ReserveDataBlock();
                    if (doubleIndirectBlockId == null)
                    {
                        throw new Exception("Could not reserve double indirect block");
                    }

                    GetBlockGroupOfInode(inodeId)
                        ?.UpdateDataBlockOnDisk(doubleIndirectBlockId.Value, new byte[AesXtsWriter.BLOCK_SIZE]);

                    BinaryUtils.ConvertUintToBytes(doubleIndirectBlockId.Value, tripleIndirect, (int)doubleIndirectIdx);
                    //retry
                    // ReSharper disable once TailRecursiveCall
                    SetBlockIdToInodeDataOffset(inode, inodeId, blockId, inodeDataOffset);
                }

                byte[] doubleIndirect = CryptoWriter.DecryptLba(SharedFs, doubleIndirectBlockId.Value);
                startIdx = (int)(inodeDataOffset / NUM_BLOCKS_IN_INDIRECTION_BLOCK
                    % NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint));
                previousBlockId = BinaryUtils.ConvertBytesToUint(doubleIndirect, startIdx);
                uint singleIndirectIdx = BinaryUtils.ConvertBytesToUint(doubleIndirect, startIdx);
                CryptoWriter.EncryptLba(SharedFs, doubleIndirectBlockId.Value, doubleIndirect);

                if (previousBlockId == 0 && blockId != 0)
                {
                    inode.SmallLbaBlocksReserved += 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }
                else if (previousBlockId != 0 && blockId == 0)
                {
                    inode.SmallLbaBlocksReserved -= 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }


                uint? singleIndirectBlockId = BinaryUtils.ConvertBytesToUint(
                    doubleIndirect,
                    (int)singleIndirectIdx);
                if (singleIndirectBlockId == 0)
                {
                    singleIndirectBlockId = ReserveDataBlock();
                    if (singleIndirectBlockId == null)
                    {
                        throw new Exception("Could not reserve single indirect block");
                    }

                    GetBlockGroupOfInode(inodeId)
                        ?.UpdateDataBlockOnDisk(singleIndirectBlockId.Value, new byte[AesXtsWriter.BLOCK_SIZE]);

                    BinaryUtils.ConvertUintToBytes(singleIndirectBlockId.Value, doubleIndirect, (int)singleIndirectIdx);
                    //retry
                    // ReSharper disable once TailRecursiveCall
                    SetBlockIdToInodeDataOffset(inode, inodeId, blockId, inodeDataOffset);
                }

                byte[] singleIndirect = CryptoWriter.DecryptLba(SharedFs, singleIndirectBlockId.Value);
                startIdx = (int)(inodeDataOffset % NUM_BLOCKS_IN_INDIRECTION_BLOCK * sizeof(uint));
                previousBlockId = BinaryUtils.ConvertBytesToUint(singleIndirect, startIdx);
                blockIdRaw.CopyTo(singleIndirect, startIdx);
                CryptoWriter.EncryptLba(SharedFs, singleIndirectBlockId.Value, singleIndirect);

                if (previousBlockId == 0 && blockId != 0)
                {
                    inode.SmallLbaBlocksReserved += 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }
                else if (previousBlockId != 0 && blockId == 0)
                {
                    inode.SmallLbaBlocksReserved -= 8;
                    GetBlockGroupOfInode(inodeId)?.UpdateInodeOnDisk(inodeId, inode);
                }

                break;
            }
            default:
                throw new Exception("Max file size reached");
        }
    }


    public void WriteAllBlockDescriptorTables()
    {
        foreach (BlockGroup group in BlockGroups)
        {
            group.WriteBackupBlockGroupDescriptorTable();
        }
    }


    private void LoadSuperblock()
    {
        byte[] superblockRawData = CryptoWriter.DecryptLba(SharedFs, 0);
        int position = Superblock.BLOCK_OFFSET_BYTES;
        ExtSuperblock = new Superblock(superblockRawData, ref position);
    }

    private void LoadBlockGroupDescriptors()
    {
        byte[] descriptorRawData = new byte[AesXtsWriter.BLOCK_SIZE * 4];
        for (int i = 0; i < 4; i++)
        {
            byte[] block = CryptoWriter.DecryptLba(SharedFs, 1 + i);
            block.CopyTo(descriptorRawData, AesXtsWriter.BLOCK_SIZE * i);
        }

        int position = 0;
        for (int i = 0; i < BlockGroupDescriptor.NUM_DESCRIPTORS_IN_TABLE; i++)
        {
            BlockGroupDescriptor ext2BlockDescriptor = new(descriptorRawData, ref position);
            if (
                ext2BlockDescriptor.BlockBitmapBlockId == 0
                && ext2BlockDescriptor.InodeBitmapBlockId == 0
                && ext2BlockDescriptor.InodeTableStartBlockId == 0)
            {
                break;
            }

            BlockGroups.Add(new BlockGroup(i, ext2BlockDescriptor, this));
        }
    }
}