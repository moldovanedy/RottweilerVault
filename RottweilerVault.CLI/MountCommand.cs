using System;
using RottweilerVault.DummyFs;

namespace RottweilerVault.CLI;

public static class MountCommand
{
    public static void Run(string[] args)
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

        string volumeName = args[1];
        if (string.IsNullOrEmpty(volumeName) || volumeName.StartsWith('-'))
        {
            Console.WriteLine("ERROR: Invalid volume name specified. Volume name can not start with \"-\"");
            Environment.Exit(1);
        }

        string mountPoint;
        if (args.Length > 2)
        {
            mountPoint = args[2];
            if (string.IsNullOrEmpty(mountPoint) || mountPoint.StartsWith('-'))
            {
                Console.WriteLine("ERROR: Invalid mounting point specified");
                Environment.Exit(1);
            }
        }
        else
        {
            mountPoint = volumeName + "_data";
        }

        //TODO: probe all FS-es if no hint
        DummyVolumeHandler dummyVolume = new(volumeName, mountPoint);
        dummyVolume.Probe();
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

        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help:         Display this help screen.");
        Console.WriteLine("  -p, --password <password>:\n" +
                          "                      Directly specifies the password, so it will no longer be\n" +
                          "                      requested with stdin.");
        Console.WriteLine("  --fs-hint <fs>:     Specifies a hint for the used file system so the\n" +
                          "                      initialization is faster. For now, only \"ext2\" and\n" +
                          "                      \"dummy\" are supported.");
    }
}