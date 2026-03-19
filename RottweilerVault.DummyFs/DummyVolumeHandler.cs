using System.IO;
using System.Linq;
using RottweilerVault.FsBase;

namespace RottweilerVault.DummyFs;

public class DummyVolumeHandler : IEncryptedVolumeHandler
{
    private readonly string _volumeName;
    private readonly string _mountPath;

    private static readonly byte[] FileSignature =
    [
        0x8a,
        (byte)'E', (byte)'n', (byte)'c',
        (byte)'D', (byte)'u', (byte)'m', (byte)'m', (byte)'y',
        (byte)'F', (byte)'S'
    ];

    public DummyVolumeHandler(string volumeName, string mountPath)
    {
        _volumeName = volumeName;
        _mountPath = mountPath;
    }

    public bool Probe()
    {
        try
        {
            string appDataDir = VolumeManagementUtils.GetAppDataDirectoryPath();
            if (!File.Exists(appDataDir + _volumeName))
            {
                return false;
            }

            using FileStream fs = File.OpenRead(appDataDir + _volumeName);

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
}