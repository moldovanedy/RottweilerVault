using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.FsBase;

namespace RottweilerVault.Ext2.Implementations;

internal class BlockGroup
{
    public int BlockGroupIndex { get; }
    public SuperStructure ParentSuperStructure { get; }
    public BlockGroupDescriptor Ext2BlockDescriptor { get; }

    private byte[] _blockBitmap;
    private readonly byte[] _inodeBitmap;

    private readonly Dictionary<uint, Inode> _inodeTableCache = [];

    /// <summary>
    /// The total size of elements in all the caches.
    /// </summary>
    private static uint _inodeCacheSize;

    private const uint MAX_TOTAL_CACHE_SIZE = 4096 * 8;

    public BlockGroup(
        int blockGroupIndex,
        BlockGroupDescriptor ext2BlockDescriptor,
        SuperStructure parentSuperStructure)
    {
        BlockGroupIndex = blockGroupIndex;
        Ext2BlockDescriptor = ext2BlockDescriptor;
        ParentSuperStructure = parentSuperStructure;

        uint lbaIndex = Ext2BlockDescriptor.BlockBitmapBlockId;
        _blockBitmap = ParentSuperStructure.CryptoWriter.DecryptLba(ParentSuperStructure.SharedFs, lbaIndex);
        lbaIndex = Ext2BlockDescriptor.InodeBitmapBlockId;
        _inodeBitmap = ParentSuperStructure.CryptoWriter.DecryptLba(ParentSuperStructure.SharedFs, lbaIndex);
    }

    #region CRUD

    /// <summary>
    /// Creates a new inode (with no data in it, being your responsibility to fill it and then call
    /// <see cref="UpdateInodeOnDisk"/>) if possible.
    /// </summary>
    /// <param name="inode"></param>
    /// <param name="inodeId"></param>
    /// <returns>Returns true if the inode was successfully created, false otherwise.</returns>
    public bool TryCreateInode(
        [NotNullWhen(true)] out Inode? inode,
        [NotNullWhen(true)] out uint? inodeId)
    {
        inode = null;
        inodeId = null;
        if (Ext2BlockDescriptor.NumFreeInodes == 0)
        {
            return false;
        }

        try
        {
            uint newId = (uint)(Inode.NUM_INODES_IN_TABLE * BlockGroupIndex) + 1;
            bool foundId = false;

            for (int i = 0; i < Inode.NUM_INODES_IN_TABLE / 8; i++)
            {
                if (foundId)
                {
                    break;
                }

                if (_inodeBitmap[i] == 0xff)
                {
                    newId += 8;
                    continue;
                }

                for (int j = 0; j < 8; j++)
                {
                    if ((_inodeBitmap[i] & (0b10000000 >> j)) == 0)
                    {
                        foundId = true;
                        _inodeBitmap[i] |= (byte)(0b10000000 >> j);

                        //this should never happen, but it's better to be prepared 
                        if (_inodeTableCache.TryGetValue(newId, out Inode? value) && value.DataBlocksIds[0] != 0)
                        {
                            foundId = false;
                        }

                        break;
                    }

                    newId++;
                }
            }

            if (!foundId)
            {
                return false;
            }

            inode = new Inode();
            inodeId = newId;
            Ext2BlockDescriptor.NumFreeInodes--;

            //transactions at the end
            _inodeTableCache[newId] = inode;
            _inodeCacheSize++;
            WriteInodeBitmap();
            UpdateInodeOnDisk(newId, inode);
            WriteBackupBlockGroupDescriptorTable();
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return false;
        }
    }

    public Inode GetLocalInode(uint inodeId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(inodeId);
        if (_inodeTableCache.TryGetValue(inodeId, out Inode? inode))
        {
            return inode;
        }

        if (_inodeCacheSize > MAX_TOTAL_CACHE_SIZE)
        {
            //TODO (later): a more reasonable solution is needed, like keeping track of the oldest entries
            _inodeTableCache.Clear();
            _inodeCacheSize = 0;
        }

        uint localInodeIndex = (inodeId - 1) % Inode.NUM_INODES_IN_TABLE;
        uint relativeBlockId = localInodeIndex / Inode.NUM_INODES_IN_BLOCK;
        uint lbaIndex = Ext2BlockDescriptor.InodeTableStartBlockId + relativeBlockId;

        byte[] rawData = ParentSuperStructure.CryptoWriter.DecryptLba(ParentSuperStructure.SharedFs, lbaIndex);
        int position = 0;
        //get the inode ID at the start of the block
        uint blockInodeId = inodeId - (inodeId - 1) % Inode.NUM_INODES_IN_BLOCK;

        for (int i = 0; i < Inode.NUM_INODES_IN_BLOCK; i++)
        {
            Inode blockInode = new(rawData, ref position);
            _inodeTableCache[blockInodeId] = blockInode;
            if (blockInodeId == inodeId)
            {
                inode = blockInode;
            }

            blockInodeId++;
        }

        _inodeCacheSize += Inode.NUM_INODES_IN_BLOCK;
        return inode ??
               throw new Exception(
                   "Assertion failed: did not find the inode in the corresponding block of the inode table");
    }

    /// <summary>
    /// Updates the inode with the specified ID on the storage device. Note that it overrides the data in the cache,
    /// so you always need to pass the inode data to always use the latest version.
    /// </summary>
    /// <param name="inodeId"></param>
    /// <param name="inode"></param>
    public void UpdateInodeOnDisk(uint inodeId, Inode inode)
    {
        GetLocalInode(inodeId);
        _inodeTableCache[inodeId] = inode;

        //get the inode ID at the start of the block
        uint blockInodeId = inodeId - (inodeId - 1) % Inode.NUM_INODES_IN_BLOCK;
        WriteBlockFromInodeTable(blockInodeId);
    }

    public void DeleteInode(uint inodeId)
    {
        try
        {
            _inodeTableCache.Remove(inodeId);

            uint localInodeIndex = (inodeId - 1) % Inode.NUM_INODES_IN_TABLE;
            _inodeBitmap[localInodeIndex / 8] |= (byte)(0b10000000 >> (int)(localInodeIndex % 8));
            WriteInodeBitmap();
            UpdateInodeOnDisk(inodeId, new Inode());

            _inodeCacheSize--;
            Ext2BlockDescriptor.NumFreeInodes++;
            WriteBackupBlockGroupDescriptorTable();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }
    }


    /// <summary>
    /// Reserves a new data block if possible.
    /// </summary>
    /// <param name="blockId"></param>
    /// <returns>Returns true if the block was successfully reserved, false otherwise.</returns>
    public bool TryReserveDataBlock([NotNullWhen(true)] out uint? blockId)
    {
        blockId = null;
        if (Ext2BlockDescriptor.NumFreeBlocks == 0)
        {
            return false;
        }

        try
        {
            uint newId = (uint)(Superblock.NumBlocksPerGroup * BlockGroupIndex) + 1;
            bool foundBlock = false;

            for (int i = 0; i < Superblock.NumBlocksPerGroup / 8; i++)
            {
                if (foundBlock)
                {
                    break;
                }

                if (_blockBitmap[i] == 0xff)
                {
                    newId += 8;
                    continue;
                }

                for (int j = 0; j < 8; j++)
                {
                    if ((_blockBitmap[i] & (0b10000000 >> j)) == 0)
                    {
                        foundBlock = true;
                        _blockBitmap[i] |= (byte)(0b10000000 >> j);
                        break;
                    }

                    newId++;
                }
            }

            if (!foundBlock)
            {
                return false;
            }

            blockId = newId;
            Ext2BlockDescriptor.NumFreeBlocks--;

            //transactions at the end
            WriteBlockBitmap();
            WriteBackupBlockGroupDescriptorTable();
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return false;
        }
    }

    public bool TryGetDataBlock(uint blockId, out ReadOnlySpan<byte> buffer)
    {
        try
        {
            if (blockId % Superblock.NumBlocksPerGroup < SuperStructure.NUM_NON_DATA_BLOCKS_PER_GROUP)
            {
                throw new ArgumentOutOfRangeException(nameof(blockId), "Tried to reserve a non-data block");
            }

            buffer = ParentSuperStructure.CryptoWriter.DecryptLba(
                ParentSuperStructure.SharedFs, blockId);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            buffer = new ReadOnlySpan<byte>();
            return false;
        }
    }

    public void UpdateDataBlockOnDisk(uint blockId, byte[] data)
    {
        try
        {
            if (blockId % Superblock.NumBlocksPerGroup < SuperStructure.NUM_NON_DATA_BLOCKS_PER_GROUP)
            {
                throw new ArgumentOutOfRangeException(nameof(blockId), "Tried to reserve a non-data block");
            }

            ParentSuperStructure.CryptoWriter.EncryptLba(
                ParentSuperStructure.SharedFs, blockId, data);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }
    }

    public void FreeDataBlock(uint blockId)
    {
        try
        {
            uint localBlockId = blockId % Superblock.NumBlocksPerGroup;
            if (localBlockId < SuperStructure.NUM_NON_DATA_BLOCKS_PER_GROUP)
            {
                throw new ArgumentOutOfRangeException(nameof(blockId), "Tried to reserve a non-data block");
            }

            _blockBitmap[localBlockId / 8] |= (byte)(0b10000000 >> (int)(localBlockId % 8));
            WriteBlockBitmap();

            Ext2BlockDescriptor.NumFreeBlocks++;
            WriteBackupBlockGroupDescriptorTable();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }
    }

    #endregion

    #region Backup management

    public bool WriteBackupSuperblock()
    {
        try
        {
            byte[] buffer = new byte[AesXtsWriter.BLOCK_SIZE];
            int position = Superblock.BLOCK_OFFSET_BYTES;
            ParentSuperStructure.ExtSuperblock.WriteToBuffer(buffer, ref position);

            //the first block is the superblock, then 4 blocks for the descriptors, then the block bitmap, so
            // subtract 5
            uint lbaIndex = Ext2BlockDescriptor.BlockBitmapBlockId - 5;
            ParentSuperStructure.CryptoWriter.EncryptLba(ParentSuperStructure.SharedFs, lbaIndex, buffer);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return false;
        }
    }

    public bool WriteBackupBlockGroupDescriptorTable()
    {
        try
        {
            byte[] buffer = new byte[AesXtsWriter.BLOCK_SIZE];
            int blockGroupIndex = 0;
            bool finished = false;

            for (int i = 0; i < 4; i++)
            {
                if (finished)
                {
                    break;
                }

                const int BLOCK_DESC_PER_BUFFER = BlockGroupDescriptor.NUM_DESCRIPTORS_IN_TABLE / 4; //128
                //clear buffer
                Array.Clear(buffer, 0, buffer.Length);

                int position = 0;
                for (int j = 0; j < BLOCK_DESC_PER_BUFFER; j++)
                {
                    ParentSuperStructure.BlockGroups[blockGroupIndex].Ext2BlockDescriptor
                        .WriteToBuffer(buffer, ref position);
                    blockGroupIndex++;

                    if (blockGroupIndex >= ParentSuperStructure.BlockGroups.Count)
                    {
                        finished = true;
                        break;
                    }
                }

                //the first block is the superblock, then 4 blocks for the descriptors, so subtract 4
                uint lbaIndex = Ext2BlockDescriptor.BlockBitmapBlockId - 4 + (uint)i;
                ParentSuperStructure.CryptoWriter.EncryptLba(ParentSuperStructure.SharedFs, lbaIndex, buffer);
            }

            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return false;
        }
    }

    #endregion


    private void WriteBlockFromInodeTable(uint startInodeId)
    {
        byte[] rawData = new byte[AesXtsWriter.BLOCK_SIZE];
        int position = 0;
        uint currentInodeId = startInodeId;

        for (int i = 0; i < Inode.NUM_INODES_IN_BLOCK; i++)
        {
            if (_inodeTableCache.TryGetValue(currentInodeId, out Inode? validInode))
            {
                validInode.WriteToBuffer(rawData, ref position);
            }
            else
            {
                new Inode().WriteToBuffer(rawData, ref position);
            }

            currentInodeId++;
        }

        uint localInodeIndex = (startInodeId - 1) % Inode.NUM_INODES_IN_TABLE;
        uint relativeBlockId = localInodeIndex / Inode.NUM_INODES_IN_BLOCK;
        uint lbaIndex = Ext2BlockDescriptor.InodeTableStartBlockId + relativeBlockId;
        ParentSuperStructure.CryptoWriter.EncryptLba(ParentSuperStructure.SharedFs, lbaIndex, rawData);
    }

    private void WriteInodeBitmap()
    {
        ParentSuperStructure.CryptoWriter.EncryptLba(
            ParentSuperStructure.SharedFs,
            Ext2BlockDescriptor.InodeBitmapBlockId,
            _inodeBitmap);
    }

    private void WriteBlockBitmap()
    {
        ParentSuperStructure.CryptoWriter.EncryptLba(
            ParentSuperStructure.SharedFs,
            Ext2BlockDescriptor.BlockBitmapBlockId,
            _blockBitmap);
    }
}