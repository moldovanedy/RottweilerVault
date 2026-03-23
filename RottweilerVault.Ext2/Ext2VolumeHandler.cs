using RottweilerVault.Ext2.Structures;
using RottweilerVault.FsBase;
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
        // 256 for the inode descriptors
        byte[] dataToWrite = new byte[
            AesXtsWriter.BLOCK_SIZE
            + 4 * AesXtsWriter.BLOCK_SIZE
            + AesXtsWriter.BLOCK_SIZE
            + AesXtsWriter.BLOCK_SIZE
            + 256 * AesXtsWriter.BLOCK_SIZE];

        Superblock superblock = new();
        int startIdx = 1024;
        superblock.WriteToBuffer(dataToWrite, ref startIdx);

        //block group descriptor table
        startIdx = 4096;
        BlockGroupDescriptor blockGroupDescriptor = new();
        for (int i = 0; i < BlockGroupDescriptor.NUM_DESCRIPTORS_IN_TABLE; i++)
        {
            blockGroupDescriptor.WriteToBuffer(dataToWrite, ref startIdx);
        }

        //block and inode bitmaps
        byte[] bitmapsBytes = new byte[AesXtsWriter.BLOCK_SIZE * 2];
        bitmapsBytes.CopyTo(dataToWrite, startIdx);

        //inode table
        Inode inode = new();
        for (int i = 0; i < Inode.NUM_INODES_IN_TABLE; i++)
        {
            inode.WriteToBuffer(dataToWrite, ref startIdx);
        }

        AesXtsWriter cryptoWriter = new(_key1, _key2);
        int numLbaUsed = dataToWrite.Length / AesXtsWriter.BLOCK_SIZE;

        for (int i = 0; i < numLbaUsed; i++)
        {
            cryptoWriter.EncryptLba(fs, i,
                dataToWrite[(i * AesXtsWriter.BLOCK_SIZE)..((i + 1) * AesXtsWriter.BLOCK_SIZE)]);
        }

        Console.WriteLine($"Volume \"{_volumeName}\" created successfully!");
    }

    public IFuseFileSystem GetFsImplementation()
    {
        throw new NotImplementedException();
    }
}