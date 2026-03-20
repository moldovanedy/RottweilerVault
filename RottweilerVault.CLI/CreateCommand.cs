using System;
using System.IO;
using System.Text.RegularExpressions;
using RottweilerVault.DummyFs;
using RottweilerVault.FsBase;

namespace RottweilerVault.CLI;

public static partial class CreateCommand
{
    public static void Run(string[] args)
    {
        if (args.Length < 1)
        {
            throw new ArgumentException("Assertion failed: No arguments provided for create (not even itself)");
        }

        if (args.Length < 3)
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

        CheckVolumeName(volumeName);

        string fileSystemType = args[2];
        if (string.IsNullOrEmpty(fileSystemType) || fileSystemType.StartsWith('-'))
        {
            Console.WriteLine("ERROR: Invalid file system type specified");
            Environment.Exit(1);
        }

        //TODO: get password and derive a crypto key from it

        switch (fileSystemType)
        {
            case "fat32":
                throw new NotImplementedException("Not yet implemented");
            case "dummy":
                DummyVolumeHandler dummyVolume = new(volumeName, [], []);
                dummyVolume.Create();
                break;
            default:
                Console.WriteLine($"ERROR: Unknown file system type specified (\"{fileSystemType}\")");
                Environment.Exit(1);
                break;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: rottweiler-vault create <volume_name> <file_system> [options]\n");
        Console.WriteLine("Creates a new volume file.");

        Console.WriteLine("Parameters:");
        Console.WriteLine("  volume_name:        Mandatory. Specifies the encrypted volume name.");
        Console.WriteLine("  file_system:        Mandatory. Specifies the file system type. Accepted values\n" +
                          "                      are: \"fat32\" and \"dummy\".");
        Console.WriteLine();

        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --password <password>:\n" +
                          "                      Directly specifies the password, so it will no longer be\n" +
                          "                      requested with stdin.");
    }

    private static void CheckVolumeName(string volumeName)
    {
        //check for duplicates
        string appDataPath = VolumeManagementUtils.GetAppDataDirectoryPath();
        if (File.Exists(Path.Combine(appDataPath, volumeName)))
        {
            Console.WriteLine("ERROR: A volume with the same name already exists");
            Environment.Exit(1);
        }

        Match regexMatch = VolumeNameRegex().Match(volumeName);
        if (
            regexMatch.Success
            && regexMatch.Groups.Count == 1
            && regexMatch.Groups[0].Success
            && regexMatch.Groups[0].Value == volumeName)
        {
            return;
        }

        Console.WriteLine(
            "ERROR: Invalid volume name specified. Volume name can only contain lowercase and uppercase " +
            "a-z letters, digits (0-9), underscores (_), and hyphens (-).");
        Environment.Exit(1);
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]*$")]
    private static partial Regex VolumeNameRegex();
}