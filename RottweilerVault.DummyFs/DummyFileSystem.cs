using System;
using System.IO;
using System.Text;
using RottweilerVault.FsBase;
using Tmds.Fuse;
using Tmds.Linux;

namespace RottweilerVault.DummyFs;

public class DummyFileSystem : FuseFileSystemBase
{
    public override bool SupportsMultiThreading => false;

    private readonly string _rootAbsolutePath;

    public DummyFileSystem(string volumeName)
    {
        _rootAbsolutePath = Path.Combine(
            VolumeManagementUtils.GetAppDataDirectoryPath(),
            volumeName + "_FS"
        );

        if (!Directory.Exists(_rootAbsolutePath))
        {
            Directory.CreateDirectory(_rootAbsolutePath);
        }
    }

    public override int Create(ReadOnlySpan<byte> path, mode_t mode, ref FuseFileInfo fi)
    {
        try
        {
            //only direct files are allowed, no subdirectories
            if (!path.StartsWith((byte)'/') || path.LastIndexOf((byte)'/') != 0)
            {
                return -LibC.EACCES;
            }

            string fileAbsPath = Path.Combine(_rootAbsolutePath, Encoding.UTF8.GetString(path[1..]));
            if (File.Exists(fileAbsPath))
            {
                return -LibC.EEXIST;
            }

            if ((mode & LibC.S_IFDIR) != 0)
            {
                return -LibC.ENOENT;
            }

            File.Create(fileAbsPath).Close();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    fileAbsPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            return 0;
        }
        catch
        {
            return -LibC.EIO;
        }
    }

    public override int GetAttr(ReadOnlySpan<byte> path, ref stat stat, FuseFileInfoRef fiRef)
    {
        try
        {
            if (path.SequenceEqual(RootPath))
            {
                stat.st_mode = LibC.S_IFDIR | 0b111_101_101; // rwx.r-x.r-x
                stat.st_nlink = 2; // 2 + nr of subdirectories
                return 0;
            }

            //only direct files are allowed, no subdirectories
            if (!path.StartsWith((byte)'/') || path.LastIndexOf((byte)'/') != 0)
            {
                return -LibC.EACCES;
            }

            string fileAbsPath = Path.Combine(_rootAbsolutePath, Encoding.UTF8.GetString(path[1..]));
            if (File.Exists(fileAbsPath))
            {
                stat.st_mode = LibC.S_IFREG | 0b110_110_110; // rw-.rw-.rw-
                stat.st_nlink = 1;

                try
                {
                    stat.st_size = new FileInfo(fileAbsPath).Length;
                }
                catch
                {
                    stat.st_size = 0;
                }

                return 0;
            }

            return -LibC.ENOENT;
        }
        catch
        {
            return -LibC.EIO;
        }
    }

    public override int Open(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
    {
        try
        {
            //only direct files are allowed, no subdirectories
            if (!path.StartsWith((byte)'/') || path.LastIndexOf((byte)'/') != 0)
            {
                return -LibC.EACCES;
            }

            string fileAbsPath = Path.Combine(_rootAbsolutePath, Encoding.UTF8.GetString(path[1..]));
            if (!File.Exists(fileAbsPath))
            {
                return -LibC.ENOENT;
            }

            if ((fi.flags & LibC.O_ACCMODE) != LibC.O_RDONLY)
            {
                return -LibC.EACCES;
            }

            return 0;
        }
        catch
        {
            return -LibC.EIO;
        }
    }

    public override int Read(ReadOnlySpan<byte> path, ulong offset, Span<byte> buffer, ref FuseFileInfo fi)
    {
        try
        {
            //only direct files are allowed, no subdirectories
            if (!path.StartsWith((byte)'/') || path.LastIndexOf((byte)'/') != 0)
            {
                return -LibC.EACCES;
            }

            string fileAbsPath = Path.Combine(_rootAbsolutePath, Encoding.UTF8.GetString(path[1..]));
            if (!File.Exists(fileAbsPath))
            {
                return -LibC.ENOENT;
            }

            using FileStream fs = File.OpenRead(fileAbsPath);
            if (offset > (ulong)fs.Length)
            {
                return 0;
            }

            ulong length = Math.Min((ulong)fs.Length - offset, (ulong)buffer.Length);
            try
            {
                fs.Seek((long)offset, SeekOrigin.Begin);
                fs.ReadExactly(buffer);
            }
            catch (EndOfStreamException)
            {
            }

            return (int)length;
        }
        catch
        {
            return -LibC.EIO;
        }
    }

    public override int ReadDir(
        ReadOnlySpan<byte> path,
        ulong offset,
        ReadDirFlags flags,
        DirectoryContent content,
        ref FuseFileInfo fi)
    {
        try
        {
            if (!path.SequenceEqual("/"u8))
            {
                return -LibC.ENOENT;
            }

            content.AddEntry(".");
            content.AddEntry("..");

            foreach (FileInfo fileInfo in new DirectoryInfo(_rootAbsolutePath).EnumerateFiles())
            {
                content.AddEntry(fileInfo.Name);
            }

            return 0;
        }
        catch
        {
            return -LibC.EIO;
        }
    }

    public override int Rename(ReadOnlySpan<byte> oldPath, ReadOnlySpan<byte> newPath, int flags)
    {
        try
        {
            //only direct files are allowed, no subdirectories
            if (
                !oldPath.StartsWith((byte)'/')
                || oldPath.LastIndexOf((byte)'/') != 0
                || !newPath.StartsWith((byte)'/')
                || newPath.LastIndexOf((byte)'/') != 0)
            {
                return -LibC.EACCES;
            }

            string oldFileAbsPath = Path.Combine(_rootAbsolutePath, Encoding.UTF8.GetString(oldPath[1..]));
            if (!File.Exists(oldFileAbsPath))
            {
                return -LibC.ENOENT;
            }

            const int RENAME_NOREPLACE = 1;
            // const int RENAME_EXCHANGE = 2;

            string newFileAbsPath = Path.Combine(_rootAbsolutePath, Encoding.UTF8.GetString(newPath[1..]));
            if (File.Exists(newFileAbsPath) && flags == RENAME_NOREPLACE)
            {
                return -LibC.EEXIST;
            }

            File.Move(oldFileAbsPath, newFileAbsPath);
            return 0;
        }
        catch
        {
            return -LibC.EIO;
        }
    }

    public override int Write(ReadOnlySpan<byte> path, ulong offset, ReadOnlySpan<byte> span, ref FuseFileInfo fi)
    {
        try
        {
            //only direct files are allowed, no subdirectories
            if (!path.StartsWith((byte)'/') || path.LastIndexOf((byte)'/') != 0)
            {
                return -LibC.EACCES;
            }

            string fileAbsPath = Path.Combine(_rootAbsolutePath, Encoding.UTF8.GetString(path[1..]));
            if (!File.Exists(fileAbsPath))
            {
                return -LibC.ENOENT;
            }

            using FileStream fs = File.OpenWrite(fileAbsPath);
            if (offset > (ulong)fs.Length)
            {
                return -LibC.EACCES;
            }

            fs.Seek((long)offset, SeekOrigin.Begin);
            fs.Write(span);
            return span.Length;
        }
        catch
        {
            return -LibC.EIO;
        }
    }
}