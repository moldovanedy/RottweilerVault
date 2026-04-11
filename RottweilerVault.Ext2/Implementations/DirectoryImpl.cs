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

        //write the initial entry
        DirectoryEntry[] initialEntries =
        [
            new()
            {
                Inode = inodeId.Value,
                FileType = DirEntryFileType.Directory,
                Name = ".",
                RecordLength = DirectoryEntry.MIN_SIZE + 1
            },
            new()
            {
                Inode = parentDir.InodeId,
                FileType = DirEntryFileType.Directory,
                Name = "..",
                RecordLength = DirectoryEntry.MIN_SIZE + 2
            },
            new()
            {
                FileType = DirEntryFileType.Unknown,
                Inode = 0,
                Name = string.Empty,
                RecordLength = AesXtsWriter.BLOCK_SIZE - DirectoryEntry.MIN_SIZE * 2 - 3
            }
        ];

        byte[] buffer = new byte[AesXtsWriter.BLOCK_SIZE];
        int position = 0;
        foreach (DirectoryEntry directoryEntry in initialEntries)
        {
            directoryEntry.WriteToBuffer(buffer, ref position);
        }

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
            //add another data block for this directory
            _superStructure.AddDataBlockToInode(Inode, InodeId, lastFilledDataBlockIndex + 1, out blockId);
            lastEntries =
            [
                new DirectoryEntry
                {
                    Inode = 0,
                    FileType = DirEntryFileType.Unknown,
                    RecordLength = AesXtsWriter.BLOCK_SIZE
                }
            ];

            paddingEntry = lastEntries[^1];
            Inode.SmallLbaBlocksReserved += 8;
            _superStructure.GetBlockGroupOfInode(InodeId)?.UpdateInodeOnDisk(InodeId, Inode);
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
                if (entry.Name != descendantName)
                {
                    continue;
                }

                thisBlockEntries[j] = newData.ToDirectoryEntry();
                int sizeDelta = thisBlockEntries[j].RecordLength - entry.RecordLength;

                if (paddingEntry.RecordLength - sizeDelta < DirectoryEntry.MIN_SIZE)
                {
                    //add another data block for this directory
                    _superStructure.AddDataBlockToInode(Inode, InodeId, numBlocksOfEntries, out blockId);
                    lastEntries =
                    [
                        new DirectoryEntry
                        {
                            Inode = 0,
                            FileType = DirEntryFileType.Unknown,
                            RecordLength = AesXtsWriter.BLOCK_SIZE
                        }
                    ];

                    SetEntriesToDataBlock(blockId, lastEntries);
                    Inode.SmallLbaBlocksReserved += 8;
                    _superStructure.GetBlockGroupOfInode(InodeId)?.UpdateInodeOnDisk(InodeId, Inode);

                    //retry
                    UpdateDescendant(descendantName, newData);
                    return;
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

    public void RemoveDescendant(string descendantName)
    {
        uint numBlocksOfEntries = Inode.SmallLbaBlocksReserved / 8;
        bool done = false;
        for (uint i = 0; i < numBlocksOfEntries; i++)
        {
            if (done)
            {
                break;
            }

            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(Inode, i);
            List<DirectoryEntry> thisBlockEntries = GetEntriesFromDataBlock(blockId);

            for (int j = 0; j < thisBlockEntries.Count; j++)
            {
                DirectoryEntry entry = thisBlockEntries[j];
                if (entry.Name != descendantName)
                {
                    continue;
                }

                ushort sizeToRemove = entry.RecordLength;
                thisBlockEntries.RemoveAt(j);

                DirectoryEntry paddingEntry = thisBlockEntries[^1];
                if (paddingEntry.Name.Length > 0)
                {
                    throw new Exception("Assertion failed: padding entry has a name");
                }

                paddingEntry.RecordLength += sizeToRemove;

                //remove the block if no more entries are available
                if (paddingEntry.RecordLength == AesXtsWriter.BLOCK_SIZE)
                {
                    _superStructure.SetBlockIdToInodeDataOffset(Inode, InodeId, 0, numBlocksOfEntries - 1);
                    Inode.SmallLbaBlocksReserved -= 8;
                    _superStructure.GetBlockGroupOfInode(InodeId)?.UpdateInodeOnDisk(InodeId, Inode);
                }

                //actually write the changes
                SetEntriesToDataBlock(blockId, thisBlockEntries);

                done = true;
                break;
            }
        }
    }

    public InodeImpl? GetLastDescendant()
    {
        uint lastFilledDataBlockIndex = Inode.SmallLbaBlocksReserved / 8 - 1;
        uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(Inode, lastFilledDataBlockIndex);

        List<DirectoryEntry> thisBlockEntries = GetEntriesFromDataBlock(blockId);
        if (thisBlockEntries.Count < 2)
        {
            return null;
        }

        DirectoryEntry entry = thisBlockEntries[^2];
        switch (entry.FileType)
        {
            case DirEntryFileType.Regular:
                return FileImpl.GetFile(_superStructure, entry.Inode, entry.Name);
            case DirEntryFileType.Directory:
                return GetDir(_superStructure, entry.Inode, entry.Name);
            default:
                throw new Exception("Assertion failed: invalid file type");
        }
    }

    public void RemoveLastDescendant()
    {
        uint lastFilledDataBlockIndex = Inode.SmallLbaBlocksReserved / 8 - 1;
        uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(Inode, lastFilledDataBlockIndex);

        List<DirectoryEntry> thisBlockEntries = GetEntriesFromDataBlock(blockId);
        if (thisBlockEntries.Count < 2)
        {
            //retry
            Inode.SmallLbaBlocksReserved -= 8;
            _superStructure.GetBlockGroupOfInode(InodeId)?.UpdateInodeOnDisk(InodeId, Inode);
            if (Inode.SmallLbaBlocksReserved <= 0)
            {
                return;
            }

            // ReSharper disable once TailRecursiveCall
            RemoveLastDescendant();
            return;
        }

        DirectoryEntry entry = thisBlockEntries[^2];
        ushort sizeToRemove = entry.RecordLength;
        thisBlockEntries.RemoveAt(thisBlockEntries.Count - 2);

        DirectoryEntry paddingEntry = thisBlockEntries[^1];
        if (paddingEntry.Name.Length > 0)
        {
            throw new Exception("Assertion failed: padding entry has a name");
        }

        paddingEntry.RecordLength += sizeToRemove;

        //remove the block if no more entries are available
        if (paddingEntry.RecordLength == AesXtsWriter.BLOCK_SIZE)
        {
            _superStructure.SetBlockIdToInodeDataOffset(Inode, InodeId, 0, lastFilledDataBlockIndex);
            Inode.SmallLbaBlocksReserved -= 8;
            _superStructure.GetBlockGroupOfInode(InodeId)?.UpdateInodeOnDisk(InodeId, Inode);
        }

        //actually write the changes
        SetEntriesToDataBlock(blockId, thisBlockEntries);
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