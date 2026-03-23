using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RottweilerVault.DummyFs;
using RottweilerVault.Ext2;
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

        string password = string.Empty;
        if (args.Length > 3 && (args[3] == "-p" || args[3] == "--password"))
        {
            if (args.Length <= 4)
            {
                Console.WriteLine("ERROR: Password option did not have a value");
                Environment.Exit(1);
            }

            password = args[4];
        }

        //request password from stdin
        if (string.IsNullOrEmpty(password))
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

            password = sb.ToString();
            Console.WriteLine();
            if (string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("ERROR: Password cannot be empty or whitespace");
                Environment.Exit(1);
            }
        }

        (byte[] key1, byte[] key2) = KeyDerivationUtils.DeriveFromPlainPassword(password);
        switch (fileSystemType)
        {
            case "ext2":
                Ext2VolumeHandler ext2VolumeHandler = new(volumeName, key1, key2);
                ext2VolumeHandler.Create();
                break;
            case "dummy":
                DummyVolumeHandler dummyVolumeHandler = new(volumeName);
                dummyVolumeHandler.Create();
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
                          "                      are: \"ext2\" and \"dummy\".");
        Console.WriteLine();

        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --password <password>:\n" +
                          "                      Directly specifies the password, so it will no longer be\n" +
                          "                      requested with stdin. This should be avoided if possible,\n" +
                          "                      as command-line arguments might be visible in different\n" +
                          "                      places around the system, affecting security.");
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