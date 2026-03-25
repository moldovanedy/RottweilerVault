using System.Threading;
using Tmds.Fuse;

namespace RottweilerVault.FsBase;

public interface IEncryptedVolumeHandler
{
    /// <returns>Returns true if the volume is valid (not corrupt and having this file system type).</returns>
    public bool Probe();

    /// <summary>
    /// Creates a new volume with the specified name but does not mount it.
    /// </summary>
    public void Create();

    /// <returns>The FS implementation</returns>
    public IFuseFileSystem GetFsImplementation(CancellationToken cancellationToken);
}