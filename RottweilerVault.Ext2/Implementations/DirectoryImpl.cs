using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.FsBase;

namespace RottweilerVault.Ext2.Implementations;

internal class DirectoryImpl : InodeImpl, IEnumerable<InodeImpl>
{
    public override DirEntryFileType InodeType => DirEntryFileType.Directory;

    private readonly SuperStructure _superStructure;

    private DirectoryImpl(Inode inode, uint inodeId, string name, SuperStructure superStructure)
        : base(inode, inodeId, name)
    {
        _superStructure = superStructure;
    }

    private static DirectoryImpl? _root;

    public static DirectoryImpl GetRootDir(SuperStructure superStructure)
    {
        _root ??= new DirectoryImpl(superStructure.BlockGroups[0].GetLocalInode(2), 2, "/", superStructure);
        return _root;
    }

    public static DirectoryImpl? GetDir(SuperStructure superStructure, uint inodeId, string name)
    {
        BlockGroup? blockGroup = superStructure.GetBlockGroupOfInode(inodeId);
        if (blockGroup == null)
        {
            return null;
        }

        Inode dirInode = blockGroup.GetLocalInode(inodeId);
        return new DirectoryImpl(dirInode, inodeId, name, superStructure);
    }

    public static DirectoryImpl Create(
        SuperStructure superStructure,
        DirectoryImpl parentDir,
        string name,
        UnixFileMode fileMode)
    {
        Inode? inode = superStructure.CreateInode(out uint? inodeId);
        if (inode == null || inodeId == null)
        {
            throw new Exception("Could not create inode");
        }

        inode.CreateTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        inode.LastAccessTime = inode.CreateTime;
        inode.LastWriteTime = inode.CreateTime;
        inode.Mode = (ushort)fileMode;
        inode.HardLinksCount = 1;
        inode.SmallLbaBlocksReserved = 8;
        superStructure.GetBlockGroupOfInode(inodeId.Value)?.UpdateInodeOnDisk(inodeId.Value, inode);

        uint? blockId = superStructure.ReserveDataBlock();
        if (blockId == null)
        {
            throw new Exception("Could not create directory's first data block");
        }

        inode.DataBlocksIds[0] = blockId.Value;

        //write the padding entry
        DirectoryEntry ext2PaddingEntry = new()
        {
            FileType = DirEntryFileType.Directory,
            Inode = 0,
            Name = string.Empty,
            RecordLength = AesXtsWriter.BLOCK_SIZE
        };

        byte[] buffer = new byte[AesXtsWriter.BLOCK_SIZE];
        int position = 0;
        ext2PaddingEntry.WriteToBuffer(buffer, ref position);
        superStructure.CryptoWriter.EncryptLba(superStructure.SharedFs, blockId.Value, buffer);

        DirectoryImpl dir = new(inode, inodeId.Value, name, superStructure);
        parentDir.AddDescendant(dir);
        return dir;
    }


    public IEnumerator<InodeImpl> GetEnumerator()
    {
        return new DirEntryEnumerator(_superStructure, this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void AddDescendant(InodeImpl descInode)
    {
        //for directories, we can always get the last data block for addition, as that always contains the last
        // entries, including the padding entry
        uint lastFilledDataBlockIndex = Inode.SmallLbaBlocksReserved / 8 - 1;
        uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(Inode, lastFilledDataBlockIndex);

        List<DirectoryEntry> lastEntries = GetEntriesFromDataBlock(blockId);
        DirectoryEntry paddingEntry = lastEntries[^1];
        if (paddingEntry.Name.Length > 0)
        {
            throw new Exception("Assertion failed: padding entry has a name");
        }

        var descEntry = descInode.ToDirectoryEntry();
        if (paddingEntry.RecordLength - DirectoryEntry.MIN_SIZE < descEntry.RecordLength)
        {
            //TODO: add another data block for this directory
            throw new NotImplementedException();
        }

        lastEntries.Insert(lastEntries.Count - 1, descEntry);
        paddingEntry.RecordLength -= descEntry.RecordLength;
        SetEntriesToDataBlock(blockId, lastEntries);
    }

    public void UpdateDescendant(string descendantName, InodeImpl newData)
    {
        uint numBlocksOfEntries = Inode.SmallLbaBlocksReserved / 8;
        uint lastEntriesBlockId = _superStructure.GetBlockIdOfInodeDataOffset(
            Inode, numBlocksOfEntries - 1);
        List<DirectoryEntry> lastEntries = GetEntriesFromDataBlock(lastEntriesBlockId);

        DirectoryEntry paddingEntry = lastEntries[^1];
        if (paddingEntry.Name.Length > 0)
        {
            throw new Exception("Assertion failed: padding entry has a name");
        }

        bool done = false;
        for (uint i = 0; i < numBlocksOfEntries; i++)
        {
            if (done)
            {
                break;
            }

            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(Inode, i);
            //tread the case where this block IS the last block
            List<DirectoryEntry> thisBlockEntries =
                blockId == lastEntriesBlockId
                    ? lastEntries
                    : GetEntriesFromDataBlock(blockId);

            for (int j = 0; j < thisBlockEntries.Count; j++)
            {
                DirectoryEntry entry = thisBlockEntries[j];
                if (entry.Name == descendantName)
                {
                    thisBlockEntries[j] = newData.ToDirectoryEntry();
                    int sizeDelta = thisBlockEntries[j].RecordLength - entry.RecordLength;

                    if (paddingEntry.RecordLength - sizeDelta < DirectoryEntry.MIN_SIZE)
                    {
                        //TODO: add another data block for this directory
                        throw new NotImplementedException();
                    }

                    if (sizeDelta >= 0)
                    {
                        paddingEntry.RecordLength -= (ushort)sizeDelta;
                    }
                    else
                    {
                        paddingEntry.RecordLength += (ushort)-sizeDelta;
                    }

                    //actually write the changes, first the padding entry, then the actual entries where the
                    //descendant was changed
                    SetEntriesToDataBlock(blockId, thisBlockEntries);
                    SetEntriesToDataBlock(lastEntriesBlockId, lastEntries);

                    done = true;
                    break;
                }
            }
        }
    }


    private List<DirectoryEntry> GetEntriesFromDataBlock(uint blockId)
    {
        byte[] lastEntriesRaw = _superStructure.CryptoWriter.DecryptLba(_superStructure.SharedFs, blockId);
        List<DirectoryEntry> entries = [];

        int bytesRead = 0;
        while (bytesRead < AesXtsWriter.BLOCK_SIZE)
        {
            DirectoryEntry ext2DirEntry = new(lastEntriesRaw, ref bytesRead);
            entries.Add(ext2DirEntry);
        }

        return entries;
    }

    private void SetEntriesToDataBlock(uint blockId, List<DirectoryEntry> entries)
    {
        byte[] buffer = new byte[AesXtsWriter.BLOCK_SIZE];
        int position = 0;
        foreach (DirectoryEntry entry in entries)
        {
            entry.WriteToBuffer(buffer, ref position);
        }

        _superStructure.CryptoWriter.EncryptLba(_superStructure.SharedFs, blockId, buffer);
    }
}