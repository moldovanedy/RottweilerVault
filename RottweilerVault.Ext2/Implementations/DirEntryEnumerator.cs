using System;
using System.Collections;
using System.Collections.Generic;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.FsBase;

namespace RottweilerVault.Ext2.Implementations;

internal class DirEntryEnumerator : IEnumerator<InodeImpl>
{
    public InodeImpl Current { get; private set; } = null!;

    object IEnumerator.Current => Current;

    private readonly DirectoryImpl _directory;
    private readonly SuperStructure _superStructure;

    private uint _dataBlockOffset;
    private byte[] _dataBlockRaw = [];
    private int _offsetInBlock;

    public DirEntryEnumerator(SuperStructure superStructure, DirectoryImpl directory)
    {
        _superStructure = superStructure;
        _directory = directory;
    }

    public bool MoveNext()
    {
        if (_dataBlockRaw.Length == 0)
        {
            uint blockId = _superStructure.GetBlockIdOfInodeDataOffset(_directory.Inode, _dataBlockOffset);
            if (blockId == 0)
            {
                Current = null!;
                return false;
            }

            _dataBlockRaw = _superStructure.CryptoWriter.DecryptLba(_superStructure.SharedFs, blockId);
        }

        int offset = _offsetInBlock;
        DirectoryEntry ext2Entry = new(_dataBlockRaw, ref offset);
        _offsetInBlock = offset;

        if (offset == AesXtsWriter.BLOCK_SIZE)
        {
            _dataBlockOffset++;
            _dataBlockRaw = [];
            _offsetInBlock = 0;

            // ReSharper disable once TailRecursiveCall
            return MoveNext();
        }

        switch (ext2Entry.FileType)
        {
            case DirEntryFileType.Directory:
                Current = DirectoryImpl.GetDir(_superStructure, ext2Entry.Inode, ext2Entry.Name)
                          ?? throw new Exception("Could not get sub-directory");
                break;
            case DirEntryFileType.Regular:
                Current = FileImpl.GetFile(_superStructure, ext2Entry.Inode, ext2Entry.Name)
                          ?? throw new Exception("Could not get file");
                break;
            default:
                throw new Exception("Unknown file type");
        }

        return true;
    }

    public void Reset()
    {
        _dataBlockOffset = 0;
        _offsetInBlock = 0;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}