using System;
using System.IO;
using System.Threading;
using RottweilerVault.Ext2.Ext2Structures;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.Utils;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;

namespace RottweilerVault.Ext2;

public class Ext2VolumeHandler : IEncryptedVolumeHandler
{
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

            byte[] superblockBytes = new byte[AesXtsWriter.BLOCK_SIZE];
            int read = fs.Read(superblockBytes);

            if (read != AesXtsWriter.BLOCK_SIZE)
            {
                return false;
            }

            int readPos = 1024;
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
            AesXtsWriter.BLOCK_SIZE
            + 4 * AesXtsWriter.BLOCK_SIZE
            + AesXtsWriter.BLOCK_SIZE
            + AesXtsWriter.BLOCK_SIZE
            + 256 * AesXtsWriter.BLOCK_SIZE
            + AesXtsWriter.BLOCK_SIZE];

        WriteInitialDataToBuffer(dataToWrite);

        AesXtsWriter cryptoWriter = new(_key1, _key2);
        int numLbaUsed = dataToWrite.Length / AesXtsWriter.BLOCK_SIZE;

        for (int i = 0; i < numLbaUsed; i++)
        {
            cryptoWriter.EncryptLba(fs, i,
                dataToWrite[(i * AesXtsWriter.BLOCK_SIZE)..((i + 1) * AesXtsWriter.BLOCK_SIZE)]);
        }

        Console.WriteLine($"Volume \"{_volumeName}\" created successfully!");
    }

    public IFuseFileSystem GetFsImplementation(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }


    private static void WriteInitialDataToBuffer(byte[] dataToWrite)
    {
        uint numBlocksUsed = (uint)dataToWrite.Length / AesXtsWriter.BLOCK_SIZE;
        Superblock superblock = new()
        {
            NumUnallocatedInodes = Superblock.NumInodes - 11,
            NumUnallocatedBlocks = Superblock.NumBlocks - numBlocksUsed
        };

        int startIdx = 1024;
        superblock.WriteToBuffer(dataToWrite, ref startIdx);

        //block group descriptor table
        startIdx = 4096;
        BlockGroupDescriptor firstBlockGroupDescriptor = new()
        {
            BlockBitmapBlockId = 5,
            InodeBitmapBlockId = 6,
            InodeTableStartBlockId = 7,
            NumFreeInodes = (ushort)(Superblock.NumInodesPerGroup - 11),
            NumFreeBlocks = (ushort)(Superblock.NumBlocksPerGroup - numBlocksUsed)
        };
        firstBlockGroupDescriptor.WriteToBuffer(dataToWrite, ref startIdx);

        BlockGroupDescriptor unusedBlockGroupDescriptor = new();
        for (int i = 0; i < BlockGroupDescriptor.NUM_DESCRIPTORS_IN_TABLE; i++)
        {
            unusedBlockGroupDescriptor.WriteToBuffer(dataToWrite, ref startIdx);
        }

        //block and inode bitmaps
        byte[] bitmapsBytes = new byte[AesXtsWriter.BLOCK_SIZE * 2];
        bitmapsBytes.CopyTo(dataToWrite, startIdx);

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
                        | (ushort)(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead),
                    CreateTime = unixTimestamp,
                    LastAccessTime = unixTimestamp,
                    LastWriteTime = unixTimestamp,
                    HardLinksCount = 1,
                    SmallLbaBlocksReserved = 1
                };

                //the directory data will always start from the last block
                rootDir.DataBlocksIds[0] = numBlocksUsed;

                rootDir.WriteToBuffer(dataToWrite, ref startIdx);
                continue;
            }

            unusedInode.WriteToBuffer(dataToWrite, ref startIdx);
        }

        //root directory entries start
        DirectoryEntry directoryEntry =
            new()
            {
                Inode = 0,
                FileType = DirEntryFileType.Unknown,
                RecordLength = 4096
            };
        directoryEntry.WriteToBuffer(dataToWrite, ref startIdx);
    }
}