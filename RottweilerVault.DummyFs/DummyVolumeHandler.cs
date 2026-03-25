using System.IO;
using System.Linq;
using System.Threading;
using RottweilerVault.FsBase;
using RottweilerVault.FsBase.Utils;
using Tmds.Fuse;

namespace RottweilerVault.DummyFs;

public class DummyVolumeHandler : IEncryptedVolumeHandler
{
    private readonly string _volumeName;

    private static readonly byte[] FileSignature =
    [
        0x8a,
        (byte)'E', (byte)'n', (byte)'c',
        (byte)'D', (byte)'u', (byte)'m', (byte)'m', (byte)'y',
        (byte)'F', (byte)'S'
    ];

    public DummyVolumeHandler(string volumeName)
    {
        _volumeName = volumeName;
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

            byte[] fileSigCheck = new byte[FileSignature.Length];
            int read = fs.Read(fileSigCheck);

            if (read != FileSignature.Length)
            {
                return false;
            }

            return !fileSigCheck.Where((t, i) => t != FileSignature[i]).Any();
        }
        catch
        {
            return false;
        }
    }

    public void Create()
    {
        string appDataDir = VolumeManagementUtils.GetAppDataDirectoryPath();
        using FileStream fs = File.Create(Path.Combine(appDataDir, _volumeName));

        fs.Write(FileSignature);
        fs.Write(new byte[4096 - FileSignature.Length]);
    }

    public IFuseFileSystem GetFsImplementation(CancellationToken cancellationToken)
    {
        return new DummyFileSystem(_volumeName);
    }
}