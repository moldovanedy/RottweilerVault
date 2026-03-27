using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using RottweilerVault.FsBase.FsStructures;
using Tmds.Fuse;
using Tmds.Linux;

namespace RottweilerVault.FsBase;

public class FuseHandler : FuseFileSystemBase
{
    public override bool SupportsMultiThreading => _fsHandler.SupportsMultiThreading;

    private readonly IFsHandler _fsHandler;
    private readonly FsDirectory _rootDir;

    private const UnixFileMode DIRECTORY_DEFAULT_MODE =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode REGULAR_FILE_DEFAULT_MODE =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead
        | UnixFileMode.OtherRead;

    public FuseHandler(IFsHandler fsHandler, FsDirectory rootDir)
    {
        _fsHandler = fsHandler;
        _rootDir = rootDir;
    }

    public override int Create(ReadOnlySpan<byte> path, mode_t mode, ref FuseFileInfo fi)
    {
        try
        {
            if (!path.StartsWith("/"u8))
            {
                throw new ArgumentException("Path is not absolute");
            }

            int lastPathSeparator = path.LastIndexOf("/"u8);
            FuseError error = TraverseFs(path[..lastPathSeparator], true, out FsInode? inode);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (inode is not FsDirectory parentDir)
            {
                return (int)FuseError.NotADirectory;
            }

            string fileName = Encoding.UTF8.GetString(path[(lastPathSeparator + 1)..]);
            FsInode? existingInode = parentDir.GetEntryOrNull(fileName);
            if (existingInode != null)
            {
                return (int)FuseError.AlreadyExists;
            }

            //this causes a StackOverflow, so use the default mode:
            // (UnixFileMode)(ushort)mode
            existingInode = _fsHandler.CreateInode(
                parentDir, fileName, InodeType.Regular, REGULAR_FILE_DEFAULT_MODE, ref fi, out error);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (existingInode is not FsFile file)
            {
                return (int)FuseError.IoError;
            }

            file.Name = fileName;
            parentDir[fileName] = file;
            return 0;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return (int)FuseError.IoError;
        }
    }

    public override int GetAttr(ReadOnlySpan<byte> path, ref stat stat, FuseFileInfoRef fiRef)
    {
        try
        {
            if (!path.StartsWith("/"u8))
            {
                throw new ArgumentException("Path is not absolute");
            }

            if (path.SequenceEqual("/"u8))
            {
                return (int)_fsHandler.GetAttributes(_rootDir, ref stat);
            }

            FuseError error = TraverseFs(path, false, out FsInode? inode);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (inode != null)
            {
                return (int)_fsHandler.GetAttributes(inode, ref stat);
            }

            return (int)FuseError.NoEntry;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return (int)FuseError.IoError;
        }
    }

    public override int Open(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
    {
        try
        {
            if (!path.StartsWith("/"u8))
            {
                throw new ArgumentException("Path is not absolute");
            }

            FuseError error = TraverseFs(path, false, out FsInode? inode);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (inode is not FsFile file)
            {
                return (int)FuseError.IsADirectory;
            }

            int accessMode = fi.flags & LibC.O_ACCMODE;
            if (accessMode != LibC.O_RDONLY && accessMode != LibC.O_RDWR)
            {
                return (int)FuseError.AccessDenied;
            }

            return (int)_fsHandler.OpenFile(file, ref fi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return (int)FuseError.IoError;
        }
    }

    public override int Read(ReadOnlySpan<byte> path, ulong offset, Span<byte> buffer, ref FuseFileInfo fi)
    {
        try
        {
            if (!path.StartsWith("/"u8))
            {
                throw new ArgumentException("Path is not absolute");
            }

            FuseError error = TraverseFs(path, false, out FsInode? inode);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (inode is not FsFile file)
            {
                return (int)FuseError.IsADirectory;
            }

            int accessMode = fi.flags & LibC.O_ACCMODE;
            if (accessMode != LibC.O_RDONLY && accessMode != LibC.O_RDWR)
            {
                return (int)FuseError.AccessDenied;
            }

            int bytesRead = _fsHandler.Read(file, offset, buffer, ref fi, out error);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            return bytesRead;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return (int)FuseError.IoError;
        }
    }

    public override int ReadDir(
        ReadOnlySpan<byte> path, ulong offset, ReadDirFlags flags, DirectoryContent content, ref FuseFileInfo fi)
    {
        try
        {
            if (!path.StartsWith("/"u8))
            {
                throw new ArgumentException("Path is not absolute");
            }

            FuseError error = TraverseFs(path, false, out FsInode? inode);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (inode is not FsDirectory directory)
            {
                return (int)FuseError.NotADirectory;
            }

            int accessMode = fi.flags & LibC.O_ACCMODE;
            if (accessMode != LibC.O_RDONLY && accessMode != LibC.O_RDWR)
            {
                return (int)FuseError.AccessDenied;
            }

            FsDirectoryEnumerator? enumerator = _fsHandler.GetInodeEnumerator(directory, out error);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (enumerator == null)
            {
                return (int)FuseError.IoError;
            }

            while (enumerator.MoveNext())
            {
                FsInode? currentEntry = enumerator.Current;
                if (currentEntry == null)
                {
                    return (int)FuseError.IoError;
                }

                content.AddEntry(currentEntry.Name);
                directory[currentEntry.Name] = currentEntry;
            }

            return (int)FuseError.Success;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return (int)FuseError.IoError;
        }
    }

    public override int Rename(ReadOnlySpan<byte> path, ReadOnlySpan<byte> newPath, int flags)
    {
        try
        {
            if (!path.StartsWith("/"u8) || !newPath.StartsWith("/"u8))
            {
                throw new ArgumentException("Path is not absolute");
            }

            int lastPathSeparatorFirstPath = path.LastIndexOf("/"u8);
            int lastPathSeparatorSecondPath = newPath.LastIndexOf("/"u8);
            if (lastPathSeparatorFirstPath != lastPathSeparatorSecondPath
                || path[..lastPathSeparatorFirstPath] == newPath[..lastPathSeparatorSecondPath])
            {
                throw new Exception("Unexpected condition: Rename ascendent paths are not the same");
            }

            FuseError error = TraverseFs(path, false, out FsInode? inode);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (inode == null)
            {
                return (int)FuseError.IoError;
            }

            inode.Name = Encoding.UTF8.GetString(path[(lastPathSeparatorFirstPath + 1)..]);
            return (int)_fsHandler.RenameFile(
                Encoding.UTF8.GetString(path[(lastPathSeparatorFirstPath + 1)..]),
                Encoding.UTF8.GetString(newPath[(lastPathSeparatorSecondPath + 1)..]),
                flags);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return (int)FuseError.IoError;
        }
    }

    public override int Write(ReadOnlySpan<byte> path, ulong offset, ReadOnlySpan<byte> buffer, ref FuseFileInfo fi)
    {
        try
        {
            if (!path.StartsWith("/"u8))
            {
                throw new ArgumentException("Path is not absolute");
            }

            FuseError error = TraverseFs(path, false, out FsInode? inode);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            if (inode is not FsFile file)
            {
                return (int)FuseError.IsADirectory;
            }

            int accessMode = fi.flags & LibC.O_ACCMODE;
            if (accessMode != LibC.O_WRONLY && accessMode != LibC.O_RDWR)
            {
                return (int)FuseError.AccessDenied;
            }

            int bytesWritten = _fsHandler.Write(file, offset, buffer, ref fi, out error);
            if (error != FuseError.Success)
            {
                return (int)error;
            }

            return bytesWritten;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return (int)FuseError.IoError;
        }
    }


    private FuseError TraverseFs(ReadOnlySpan<byte> path, bool createNonExistentDirs, out FsInode? inode)
    {
        inode = null;
        MemoryExtensions.SpanSplitEnumerator<byte> pathParts = path.Split("/"u8);
        FsDirectory currentDir = _rootDir;

        foreach (Range pathPart in pathParts)
        {
            //this is the root separator (what is before the first "/")
            if (pathPart.Start.Value == pathPart.End.Value)
            {
                continue;
            }

            ReadOnlySpan<byte> inodeName =
                path.Slice(pathPart.Start.Value, pathPart.End.Value - pathPart.Start.Value);

            string inodeNameStr = Encoding.UTF8.GetString(inodeName);
            FsInode? entry = currentDir.GetEntryOrNull(inodeNameStr);
            if (entry == null)
            {
                //we now check if there actually is a record in the FS, but was not in the cache
                FsInode? existingInode = _fsHandler.GetInodeIfExists(currentDir, inodeNameStr, out FuseError error);
                if (error != FuseError.Success)
                {
                    return error;
                }

                if (existingInode == null)
                {
                    if (!createNonExistentDirs)
                    {
                        return FuseError.IoError;
                    }

                    FuseFileInfo dirInfo = new();
                    FsInode? newInode = _fsHandler.CreateInode(
                        currentDir, inodeNameStr, InodeType.Directory, DIRECTORY_DEFAULT_MODE, ref dirInfo, out error);
                    if (error != FuseError.Success)
                    {
                        return error;
                    }

                    if (newInode == null)
                    {
                        return FuseError.IoError;
                    }

                    currentDir[inodeNameStr] = newInode;
                }
                else
                {
                    currentDir[inodeNameStr] = existingInode;
                }
            }

            //what we really want
            if (pathPart.End.Value == path.Length)
            {
                inode = entry;
                return FuseError.Success;
            }

            if (entry is FsDirectory dir)
            {
                currentDir = dir;
            }
            else
            {
                return FuseError.IoError;
            }
        }

        return FuseError.IoError;
    }
}