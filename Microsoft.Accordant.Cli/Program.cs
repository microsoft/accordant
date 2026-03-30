// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using Microsoft.Accordant.Cli;

namespace Microsoft.Accordant.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Accordant CLI - scaffold model-based testing projects");

        rootCommand.AddCommand(NewCommand.Create());
        rootCommand.AddCommand(FromSwaggerCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
