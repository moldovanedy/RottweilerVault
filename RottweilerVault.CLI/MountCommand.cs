using System;
using System.IO;
using System.Text;
using System.Threading;
using RottweilerVault.DummyFs;
using RottweilerVault.Ext2;
using RottweilerVault.FsBase;
using Tmds.Fuse;

namespace RottweilerVault.CLI;

public static class MountCommand
{
    private static string _volumeName = string.Empty;
    private static string _mountPoint = string.Empty;
    private static string _password = string.Empty;
    private static string _fsHint = string.Empty;

    //TODO: use the cancellation token inside the actual FS implementation, so as to wait for any pending operations
    public static void Run(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1)
        {
            throw new ArgumentException("Assertion failed: No arguments provided for mount (not even itself)");
        }

        if (args.Length < 2)
        {
            Console.WriteLine("ERROR: Too few arguments provided");
            PrintHelp();
            Environment.Exit(1);
        }

        if (args[1] == "-h" || args[1] == "--help")
        {
            PrintHelp();
            return;
        }

        ParseArguments(args);

        (byte[] key1, byte[] key2) = KeyDerivationUtils.DeriveFromPlainPassword(_password);
        string[] fileSystems = ["dummy", "ext2"];

        if (!string.IsNullOrEmpty(_fsHint))
        {
            int idx = fileSystems.IndexOf(_fsHint);
            if (idx >= 0)
            {
                //swap the hint with the first value so it has a higher priority
                (fileSystems[idx], fileSystems[0]) = (fileSystems[0], fileSystems[idx]);
            }
        }

        IEncryptedVolumeHandler? volumeHandler = null;
        bool hasFoundSupportedFs = false;
        foreach (string fileSystem in fileSystems)
        {
            switch (fileSystem)
            {
                case "dummy":
                    volumeHandler = new DummyVolumeHandler(_volumeName);
                    break;
                case "ext2":
                    volumeHandler = new Ext2VolumeHandler(_volumeName, key1, key2);
                    break;
                default:
                    continue;
            }

            hasFoundSupportedFs = volumeHandler.Probe();
            if (hasFoundSupportedFs)
            {
                break;
            }
        }

        if (!hasFoundSupportedFs || volumeHandler == null)
        {
            Console.WriteLine("ERROR: The volume could not be opened with any file system. It might be corrupt.");
            Environment.Exit(1);
        }

        Fuse.LazyUnmount(_mountPoint);
        Directory.CreateDirectory(_mountPoint);

        try
        {
            IFuseFileSystem fsImplementation = volumeHandler.GetFsImplementation();
            MountOptions options = new()
            {
                SingleThread = fsImplementation.SupportsMultiThreading
            };

            using IFuseMount mountConnection = Fuse.Mount(_mountPoint, fsImplementation, options);

            Console.WriteLine($"Mounted volume \"{_volumeName}\" at path \"{_mountPoint}\"");
            //we explicitly don't pass the cancellation token because the internal Tmds.Fuse implementation
            //doesn't play nice with cancellation, Task.Run etc. (the folder is not unmounted properly)
            mountConnection.WaitForUnmountAsync().Wait(CancellationToken.None);

            //this shouldn't generally happen, as the previous call never returns (at least from testing)
            Console.WriteLine($"Unexpected unmount of volume \"{_volumeName}\"");
        }
        catch (FuseException ex)
        {
            Console.WriteLine(
                $"ERROR: FUSE threw an exception (you might need to force the unmounting manually): {ex.Message}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: rottweiler-vault mount <volume_name> [mount_point] [options]\n");
        Console.WriteLine(
            "Mounts the given volume_name at the given mount_point using FUSE (Linux-only).\n" +
            "Program termination means automatically unmounting the volume.\n");

        Console.WriteLine("Parameters:");
        Console.WriteLine("  volume_name:        Mandatory. Specifies the encrypted volume name.");
        Console.WriteLine("  mount_point:        Optional. Specifies the mount path. By default, it is\n" +
                          "                      the path of the volume file with the \"_data\" suffix.\n" +
                          "                      The full path will be output anyways.");
        Console.WriteLine();

        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help:         Display this help screen.");
        Console.WriteLine("  -p, --password <password>:\n" +
                          "                      Directly specifies the password, so it will no longer be\n" +
                          "                      requested with stdin. This should be avoided if possible,\n" +
                          "                      as command-line arguments might be visible in different\n" +
                          "                      places around the system, affecting security.");
        Console.WriteLine("  --fs-hint <fs>:     Specifies a hint for the used file system so the\n" +
                          "                      initialization is faster. For now, only \"ext2\" and\n" +
                          "                      \"dummy\" are supported.");
    }

    private static void ParseArguments(string[] args)
    {
        _volumeName = args[1];
        if (string.IsNullOrEmpty(_volumeName) || _volumeName.StartsWith('-'))
        {
            Console.WriteLine("ERROR: Invalid volume name specified. Volume name can not start with \"-\"");
            Environment.Exit(1);
        }

        if (args.Length > 2)
        {
            _mountPoint = args[2];
            if (string.IsNullOrEmpty(_mountPoint) || _mountPoint.StartsWith('-'))
            {
                Console.WriteLine("ERROR: Invalid mounting point specified");
                Environment.Exit(1);
            }
        }
        else
        {
            _mountPoint = Path.Combine(VolumeManagementUtils.GetAppDataDirectoryPath(), _volumeName + "_data");
        }

        bool wasSpecifyingPassword = false;
        bool wasSpecifyingFsHint = false;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p":
                case "--password":
                    wasSpecifyingPassword = true;
                    continue;
                case "--fs-hint":
                    wasSpecifyingFsHint = true;
                    continue;
            }

            if (wasSpecifyingPassword)
            {
                _password = args[i];
                if (string.IsNullOrWhiteSpace(_password))
                {
                    Console.WriteLine("ERROR: Password cannot be empty or whitespace");
                    Environment.Exit(1);
                }

                wasSpecifyingPassword = false;
            }

            if (wasSpecifyingFsHint)
            {
                _fsHint = args[i];
                wasSpecifyingFsHint = false;
            }
        }


        //request password from stdin
        if (string.IsNullOrEmpty(_password))
        {
            StringBuilder sb = new();
            Console.Write("Password:");

            ConsoleKeyInfo keyInfo;
            while ((keyInfo = Console.ReadKey(true)).Key != ConsoleKey.Enter)
            {
                if (keyInfo.Key == ConsoleKey.Backspace && sb.Length > 0)
                {
                    sb.Remove(sb.Length - 1, 1);
                    continue;
                }

                if (keyInfo.KeyChar != 0)
                {
                    sb.Append(keyInfo.KeyChar);
                }
            }

            _password = sb.ToString();
            Console.WriteLine();
            if (string.IsNullOrWhiteSpace(_password))
            {
                Console.WriteLine("ERROR: Password cannot be empty or whitespace");
                Environment.Exit(1);
            }
        }
    }
}