using System;
using Tmds.Fuse;

namespace RottweilerVault.CLI;

internal static class Program
{
    private static IFuseMount? _mountConnection;
    private static string _mountedVolumeName = string.Empty;

    private static void Main(string[] args)
    {
        //TODO: in the future, this should wait for crypto completions so as to not corrupt the sector/entire volume

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (_mountConnection == null)
            {
                return;
            }

            Console.WriteLine($"Unmounting volume \"{_mountedVolumeName}\"");
            _mountConnection.LazyUnmount();
            _mountConnection.Dispose();
        };

        try
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                PrintHelp();
                return;
            }

            switch (args[0])
            {
                case "mount":
                    MountCommand.Run(args,
                        (mountConn, volumeName) =>
                        {
                            _mountConnection = mountConn;
                            _mountedVolumeName = volumeName;
                        });
                    break;
                case "create":
                    CreateCommand.Run(args);
                    break;
                default:
                    Console.WriteLine($"ERROR: unknown option {args[0]}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unhandled exception: {ex}");
            Environment.Exit(1);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            "rottweiler-vault - Rottweiler vault: store files securely in an encrypted file system\n" +
            "volume file\n");
        Console.WriteLine("Usage: rott-vault <command> [command-specific options]\n");
        Console.WriteLine(
            "There should be one (and only one) command. Each command has specific options,\n" +
            " some of which might be mandatory, some may not. All commands also have a\n" +
            "\"--help\" option for showing the help screen for that command.\n");

        Console.WriteLine("Commands:");
        Console.WriteLine("  -h, --help:         Display this help screen.");
        Console.WriteLine("  mount:              Mounts the given volume file using FUSE (Linux-only) and\n" +
                          "                      then allows using it as any normal directory.");
        Console.WriteLine("  create:             Creates a new volume file.");
    }
}