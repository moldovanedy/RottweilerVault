using System;
using System.IO;
using System.Threading;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.Ext2.Implementations;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.Utils;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;

namespace RottweilerVault.Ext2;

public class Ext2VolumeHandler : IEncryptedVolumeHandler
{
    public const UnixFileMode DEFAULT_DIRECTORY_MODE =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly string _volumeName;
    private readonly byte[] _key1;
    private readonly byte[] _key2;

    public Ext2VolumeHandler(string volumeName, byte[] key1, byte[] key2)
    {
        _volumeName = volumeName;
        _key1 = key1;
        _key2 = key2;
    }

    public bool Probe()
    {
        try
        {
            string appDataDir = VolumeManagementUtils.GetAppDataDirectoryPath();
            string volumePath = Path.Combine(appDataDir, _volumeName);

            if (!File.Exists(volumePath))
            {
                return false;
            }

            using FileStream fs = File.OpenRead(volumePath);
            AesXtsWriter cryptoWriter = new(_key1, _key2);
            byte[] superblockBytes = cryptoWriter.DecryptLba(fs, 0);

            if (superblockBytes.Length != AesXtsWriter.BLOCK_SIZE)
            {
                return false;
            }

            int readPos = Superblock.BLOCK_OFFSET_BYTES;
            _ = new Superblock(superblockBytes, ref readPos);

            //the constructor throws if the ext2 signature is invalid
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Create()
    {
        string appDataDir = VolumeManagementUtils.GetAppDataDirectoryPath();
        string volumePath = Path.Combine(appDataDir, _volumeName);

        if (File.Exists(volumePath))
        {
            File.Delete(volumePath);
        }

        using FileStream fs = File.Create(volumePath);
        //1 for the superblock, 4 for the block group descriptor, 1 for the block bitmap, 1 for the inode bitmap,
        // 256 for the inode descriptors, 1 for the root directory entries start
        byte[] dataToWrite = new byte[
            SuperStructure.NUM_NON_DATA_BLOCKS_PER_GROUP * AesXtsWriter.BLOCK_SIZE + AesXtsWriter.BLOCK_SIZE];

        WriteInitialDataToBuffer(dataToWrite);

        AesXtsWriter cryptoWriter = new(_key1, _key2);
        for (int i = 0; i < SuperStructure.NUM_NON_DATA_BLOCKS_PER_GROUP + 1; i++)
        {
            cryptoWriter.EncryptLba(fs, i,
                dataToWrite[(i * AesXtsWriter.BLOCK_SIZE)..((i + 1) * AesXtsWriter.BLOCK_SIZE)]);
        }

        Console.WriteLine($"Volume \"{_volumeName}\" created successfully!");
    }

    public IFuseFileSystem GetFsImplementation(CancellationToken cancellationToken)
    {
        string appDataDir = VolumeManagementUtils.GetAppDataDirectoryPath();
        string volumePath = Path.Combine(appDataDir, _volumeName);

        if (!File.Exists(volumePath))
        {
            throw new FileNotFoundException($"Volume \"{volumePath}\" does not exist");
        }

        FileStream fs = File.Open(volumePath, FileMode.Open, FileAccess.ReadWrite);
        fs.Seek(0, SeekOrigin.Begin);
        SuperStructure superStructure = new(fs, _key1, _key2);

        return new FuseHandler(new Ext2FsHandler(superStructure), new FsDirectory
        {
            Name = "/",
            InodeId = 2,
            InodeMode = DEFAULT_DIRECTORY_MODE
        });
    }


    private static void WriteInitialDataToBuffer(byte[] dataToWrite)
    {
        const uint NUM_BLOCKS_USED = SuperStructure.NUM_NON_DATA_BLOCKS_PER_GROUP + 1;
        Superblock superblock = new()
        {
            NumUnallocatedInodes = Superblock.NumInodes - Superblock.NUM_RESERVED_INODES,
            NumUnallocatedBlocks = Superblock.NumBlocks - NUM_BLOCKS_USED
        };

        int startIdx = Superblock.BLOCK_OFFSET_BYTES;
        superblock.WriteToBuffer(dataToWrite, ref startIdx);

        //block group descriptor table
        startIdx = 4096;
        BlockGroupDescriptor firstBlockGroupDescriptor = new()
        {
            BlockBitmapBlockId = 5,
            InodeBitmapBlockId = 6,
            InodeTableStartBlockId = 7,
            NumFreeInodes = (ushort)(Superblock.NumInodesPerGroup - Superblock.NUM_RESERVED_INODES),
            NumFreeBlocks = (ushort)(Superblock.NumBlocksPerGroup - NUM_BLOCKS_USED)
        };
        firstBlockGroupDescriptor.WriteToBuffer(dataToWrite, ref startIdx);

        BlockGroupDescriptor unusedBlockGroupDescriptor = new();
        for (int i = 0; i < BlockGroupDescriptor.NUM_DESCRIPTORS_IN_TABLE - 1; i++)
        {
            unusedBlockGroupDescriptor.WriteToBuffer(dataToWrite, ref startIdx);
        }

        //block bitmap
        byte[] blockBitmap = new byte[AesXtsWriter.BLOCK_SIZE];
        const uint FULLY_BITMAPPED = NUM_BLOCKS_USED / 8;
        for (int i = 0; i < FULLY_BITMAPPED; i++)
        {
            blockBitmap[i] = 0xff;
        }

        //there is no remainder, the required bitmap bytes are fully written to, no partial bitmap byte needed 
        blockBitmap.CopyTo(dataToWrite, startIdx);
        startIdx += AesXtsWriter.BLOCK_SIZE;

        //inode bitmap
        byte[] inodeBitmap = new byte[AesXtsWriter.BLOCK_SIZE];
        //first 10 reserved
        inodeBitmap[0] = 0xff;
        inodeBitmap[1] = 0b11000000;

        inodeBitmap.CopyTo(dataToWrite, startIdx);
        startIdx += AesXtsWriter.BLOCK_SIZE;

        //inode table
        Inode unusedInode = new();
        for (int i = 1; i <= Inode.NUM_INODES_IN_TABLE; i++)
        {
            //root directory
            if (i == 2)
            {
                uint unixTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Inode rootDir = new()
                {
                    Mode =
                        (ushort)InodeType.Directory
                        | (ushort)DEFAULT_DIRECTORY_MODE,
                    CreateTime = unixTimestamp,
                    LastAccessTime = unixTimestamp,
                    LastWriteTime = unixTimestamp,
                    HardLinksCount = 1,
                    SmallLbaBlocksReserved = 8
                };

                //the directory data will always start from the last block
                rootDir.DataBlocksIds[0] = NUM_BLOCKS_USED - 1;

                rootDir.WriteToBuffer(dataToWrite, ref startIdx);
                continue;
            }

            unusedInode.WriteToBuffer(dataToWrite, ref startIdx);
        }

        //root directory entries start
        DirectoryEntry[] directoryEntries =
        [
            new()
            {
                Inode = 2,
                FileType = DirEntryFileType.Directory,
                Name = ".",
                RecordLength = DirectoryEntry.MIN_SIZE + 1
            },
            new()
            {
                Inode = 2,
                FileType = DirEntryFileType.Directory,
                Name = "..",
                RecordLength = DirectoryEntry.MIN_SIZE + 2
            },
            new()
            {
                Inode = 0,
                FileType = DirEntryFileType.Unknown,
                RecordLength = 4096 - DirectoryEntry.MIN_SIZE * 2 - 3
            }
        ];

        foreach (DirectoryEntry entry in directoryEntries)
        {
            entry.WriteToBuffer(dataToWrite, ref startIdx);
        }
    }
}