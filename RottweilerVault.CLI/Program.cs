using System;
using System.Diagnostics;
using System.Threading;

namespace RottweilerVault.CLI;

internal static class Program
{
    private static readonly CancellationTokenSource CancelTokenSource = new();

    private static void Main(string[] args)
    {
        // AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnProcessExit;

        try
        {
            Trace.Listeners.Add(new ConsoleTraceListener());
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                PrintHelp();
                return;
            }

            switch (args[0])
            {
                case "mount":
                    MountCommand.Run(args, CancelTokenSource.Token);
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

    //TODO: in the future, this should wait for crypto completions so as to not corrupt the sector/entire volume
    private static void OnProcessExit(object? o, ConsoleCancelEventArgs e)
    {
        CancelTokenSource.Cancel();
        // e.Cancel = true;
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