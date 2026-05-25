// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using Microsoft.Accordant.Cli.Commands;

namespace Microsoft.Accordant.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Show banner when no args or --help
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help") || args.Contains("-?"))
        {
            PrintBanner();
        }

        var rootCommand = new RootCommand("Accordant CLI - scaffold model-based testing projects");

        rootCommand.AddCommand(NewCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }

    static void PrintBanner()
    {
        Console.WriteLine();
        
        // ASCII art inspired by the Accordant logo's stylized A
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(@"    /\    ");
        Console.Write(@"   /");
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Write(@"--");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(@"\   ");
        Console.Write(@"  /    \ ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"ccordant");
        Console.ResetColor();
        
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(@"  Executable behavioral specifications for .NET");
        Console.ResetColor();
        Console.WriteLine();
    }
}
