// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.Reflection;

namespace Microsoft.Accordant.Cli.Commands;

public static class NewCommand
{
    private static readonly Assembly Assembly = typeof(NewCommand).Assembly;
    
    // Template file mappings: output filename -> embedded resource name
    private static readonly Dictionary<string, List<(string OutputName, string ResourceName)>> TemplateFiles = new()
    {
        ["api"] = new()
        {
            ("{{Name}}State.cs", "State.txt"),
            ("{{Name}}Spec.cs", "Spec.txt"),
            ("{{Name}}Tests.cs", "Tests.txt"),
            ("ApiResult.cs", "ApiResult.txt"),
            ("{{Name}}.csproj", "Project.txt"),
            ("README.md", "README.txt"),
            ("NuGet.config", "NuGet.txt"),
        }
    };

    public static Command Create()
    {
        var nameArgument = new Argument<string>(
            "name",
            "Name of the project (e.g., MyApi, StackTests)");

        var templateOption = new Option<string>(
            aliases: new[] { "--template", "-t" },
            getDefaultValue: () => "api",
            description: "Template to use (default: api)");

        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            getDefaultValue: () => ".",
            description: "Output directory");

        var command = new Command("new", "Create a new Accordant project")
        {
            nameArgument,
            templateOption,
            outputOption
        };

        command.SetHandler(HandleNewCommand, nameArgument, templateOption, outputOption);

        return command;
    }

    private static void HandleNewCommand(string name, string template, string output)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || name.Contains("..") || Path.IsPathRooted(name))
        {
            Console.WriteLine($"Error: Invalid project name '{name}'. Project names cannot contain path separators, '..', or be rooted paths.");
            return;
        }

        var targetDir = Path.Combine(output, name);

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            Console.WriteLine($"Error: Directory '{targetDir}' already exists and is not empty.");
            return;
        }

        var templateKey = template.ToLowerInvariant();
        if (!TemplateFiles.ContainsKey(templateKey))
        {
            Console.WriteLine($"Error: Unknown template '{template}'. Available: {string.Join(", ", TemplateFiles.Keys)}");
            return;
        }

        Directory.CreateDirectory(targetDir);

        foreach (var (outputName, resourceName) in TemplateFiles[templateKey])
        {
            var content = ReadEmbeddedResource(templateKey, resourceName);
            if (content == null)
            {
                Console.WriteLine($"Error: Could not find template resource '{resourceName}'.");
                return;
            }

            var fileName = outputName.Replace("{{Name}}", name);
            var fileContent = content.Replace("{{Name}}", name);
            var filePath = Path.Combine(targetDir, fileName);

            if (!filePath.StartsWith(targetDir))
            {
                Console.WriteLine($"Error: File path '{filePath}' is outside the target directory.");
                return;
            }
            
            File.WriteAllText(filePath, fileContent);
            Console.WriteLine($"  Created: {fileName}");
        }

        Console.WriteLine();
        Console.WriteLine($"Project '{name}' created successfully!");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  cd {name}");
        Console.WriteLine("  dotnet restore");
        Console.WriteLine("  dotnet test");
    }

    private static string? ReadEmbeddedResource(string template, string resourceName)
    {
        var allResources = Assembly.GetManifestResourceNames();
        
        // Try exact resource name
        var fullResourceName = $"Microsoft.Accordant.Cli.Templates.{template}.{resourceName}";
        
        using var stream = Assembly.GetManifestResourceStream(fullResourceName);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // Fallback: find resource by partial match
        var match = allResources.FirstOrDefault(r => 
            r.Contains($".{template}.") && r.EndsWith(resourceName));
        
        if (match != null)
        {
            using var matchStream = Assembly.GetManifestResourceStream(match);
            if (matchStream != null)
            {
                using var matchReader = new StreamReader(matchStream);
                return matchReader.ReadToEnd();
            }
        }
        
        return null;
    }
}
