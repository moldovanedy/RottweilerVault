using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using RottweilerVault.FsBase;

namespace RottweilerVault.CLI;

internal static class Program
{
    private static readonly CancellationTokenSource CancelTokenSource = new();

    internal static FuseHandler? FsHandler { get; set; }

    private static bool _isCancelled;

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

    internal static string GetStdinPassword()
    {
        StringBuilder sb = new();
        int position = 0;

        ConsoleKeyInfo keyInfo;
        while ((keyInfo = Console.ReadKey(true)).Key != ConsoleKey.Enter)
        {
            switch (keyInfo.Key)
            {
                case ConsoleKey.Backspace:
                {
                    if (sb.Length > 0)
                    {
                        sb.Remove(position - 1, 1);
                    }

                    continue;
                }
                case ConsoleKey.LeftArrow when position > 0:
                    position--;
                    continue;
                case ConsoleKey.RightArrow when position < sb.Length:
                    position++;
                    continue;
            }

            if (keyInfo.KeyChar != 0)
            {
                sb.Insert(position, keyInfo.KeyChar);
                position++;
            }
        }

        return sb.ToString();
    }

    private static void OnProcessExit(object? o, ConsoleCancelEventArgs e)
    {
        if (!_isCancelled)
        {
            CancelTokenSource.Cancel();
            _isCancelled = true;
        }

        if (FsHandler == null)
        {
            e.Cancel = false;
            return;
        }

        if (FsHandler.NumFuseThreadsRunning != 0)
        {
            e.Cancel = true;
            return;
        }

        Console.WriteLine("Unmounting volume...");
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
        Console.Out.Flush();

        e.Cancel = false;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            "rottweiler-vault - Rottweiler vault: store files securely in an encrypted file system\n" +
            "volume file\n");
        Console.WriteLine("Usage: rottweiler-vault <command> [command-specific options]\n");
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