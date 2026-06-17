// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Cli.Commands;

using System.CommandLine;
using System.Reflection;

public static class NewCommand
{
    private static readonly Assembly Assembly = typeof(NewCommand).Assembly;

    // Supported AI editors and their skill paths
    private static readonly Dictionary<string, string> EditorSkillPaths = new()
    {
        ["copilot"] = ".github/skills/accordant",
        ["cursor"] = ".cursor/skills/accordant",
        ["claude"] = ".claude/skills/accordant",
        ["windsurf"] = ".windsurf/skills/accordant",
        ["universal"] = ".agents/skills/accordant",
    };

    // Template file mappings: output filename -> embedded resource name
    private static readonly Dictionary<string, List<(string OutputName, string ResourceName)>> TemplateFiles = new()
    {
        ["api"] = new()
        {
            ("{{Name}}State.cs", "State.txt"),
            ("{{Name}}Spec.cs", "Spec.txt"),
            ("{{Name}}Tests.cs", "Tests.txt"),
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
            aliases: ["--template", "-t"],
            getDefaultValue: () => "api",
            description: "Template to use (default: api)");

        var outputOption = new Option<string>(
            aliases: ["--output", "-o"],
            getDefaultValue: () => ".",
            description: "Output directory");

        var editorOption = new Option<string?>(
            aliases: ["--editor", "-e"],
            description: $"AI editor for skills installation ({string.Join(", ", EditorSkillPaths.Keys)})");

        var noSkillsOption = new Option<bool>(
            "--no-skills",
            getDefaultValue: () => false,
            description: "Skip AI skills installation");

        var command = new Command("new", "Create a new Accordant project")
        {
            nameArgument,
            templateOption,
            outputOption,
            editorOption,
            noSkillsOption
        };

        command.SetHandler(HandleNewCommand, nameArgument, templateOption, outputOption, editorOption, noSkillsOption);

        return command;
    }

    private static void HandleNewCommand(string name, string template, string output, string? editor, bool noSkills)
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

        // Determine editor for skills installation
        string? selectedEditor = null;
        if (!noSkills)
        {
            if (!string.IsNullOrEmpty(editor))
            {
                var editorKey = editor.ToLowerInvariant();
                if (!EditorSkillPaths.ContainsKey(editorKey))
                {
                    Console.WriteLine($"Error: Unknown editor '{editor}'. Available: {string.Join(", ", EditorSkillPaths.Keys)}");
                    return;
                }
                selectedEditor = editorKey;
            }
            else
            {
                selectedEditor = PromptForEditor();
            }
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
            var content = ReadEmbeddedResource("Templates", templateKey, resourceName);
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

        // Install skills if editor selected
        if (selectedEditor != null)
        {
            Console.WriteLine();
            InstallSkills(targetDir, selectedEditor, name);
        }

        Console.WriteLine();
        Console.WriteLine($"Project '{name}' created successfully!");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  cd {name}");
        Console.WriteLine("  dotnet restore");
        Console.WriteLine("  dotnet test");
    }

    private static string? PromptForEditor()
    {
        Console.WriteLine();
        Console.WriteLine("Which AI editor do you use?");
        var editors = EditorSkillPaths.Keys.ToList();
        for (int i = 0; i < editors.Count; i++)
        {
            var desc = editors[i] switch
            {
                "copilot" => "GitHub Copilot (VS Code)",
                "cursor" => "Cursor",
                "claude" => "Claude Code",
                "windsurf" => "Windsurf",
                "universal" => "Universal (.agents/ - works with Cursor & Windsurf)",
                _ => editors[i]
            };
            Console.WriteLine($"  [{i + 1}] {desc}");
        }
        Console.WriteLine($"  [0] Skip - don't install skills");
        Console.WriteLine();
        Console.Write("Enter choice [1-5, or 0 to skip]: ");

        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input) || input == "0")
        {
            return null;
        }

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= editors.Count)
        {
            return editors[choice - 1];
        }

        // Try parsing as editor name
        var editorKey = input.ToLowerInvariant();
        if (EditorSkillPaths.ContainsKey(editorKey))
        {
            return editorKey;
        }

        Console.WriteLine("Invalid choice, skipping skills installation.");
        return null;
    }

    private static void InstallSkills(string targetDir, string editor, string projectName)
    {
        var skillsPath = EditorSkillPaths[editor];
        var skillsDir = Path.Combine(targetDir, skillsPath);

        Directory.CreateDirectory(skillsDir);

        // Find and extract all skill resources
        var allResources = Assembly.GetManifestResourceNames();
        var skillResources = allResources
            .Where(r => r.Contains(".Skills.") && r.EndsWith(".SKILL.md"))
            .ToList();

        foreach (var resource in skillResources)
        {
            // Extract skill folder name from resource path
            // e.g., "Microsoft.Accordant.Cli.Skills._00_overview.SKILL.md" -> "00-overview"
            // Resource names convert hyphens to underscores and prefix numbers with underscore
            var parts = resource.Split('.');
            var skillFolderIndex = Array.IndexOf(parts, "Skills") + 1;
            if (skillFolderIndex > 0 && skillFolderIndex < parts.Length - 2)
            {
                var skillFolder = parts[skillFolderIndex];
                // Convert back from resource name format: _00_overview -> 00-overview
                // _02_design_state -> 02-design-state
                skillFolder = skillFolder.TrimStart('_').Replace('_', '-');

                var skillDir = Path.Combine(skillsDir, skillFolder);
                Directory.CreateDirectory(skillDir);

                using var stream = Assembly.GetManifestResourceStream(resource);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();
                    var filePath = Path.Combine(skillDir, "SKILL.md");
                    File.WriteAllText(filePath, content);
                }
            }
        }

        Console.WriteLine($"  Installed: AI skills to {skillsPath}/");

        // Create AGENTS.md at project root
        var agentsContent = ReadEmbeddedResource("Templates", "", "AGENTS.md");
        if (agentsContent != null)
        {
            agentsContent = agentsContent
                .Replace("{{ProjectName}}", projectName)
                .Replace("{{SkillsPath}}", skillsPath);

            var agentsPath = Path.Combine(targetDir, "AGENTS.md");
            File.WriteAllText(agentsPath, agentsContent);
            Console.WriteLine("  Created: AGENTS.md");
        }
    }

    private static string? ReadEmbeddedResource(string folder, string subfolder, string resourceName)
    {
        var allResources = Assembly.GetManifestResourceNames();

        // Build resource path based on folder structure
        string fullResourceName;
        if (string.IsNullOrEmpty(subfolder))
        {
            fullResourceName = $"Microsoft.Accordant.Cli.{folder}.{resourceName}";
        }
        else
        {
            fullResourceName = $"Microsoft.Accordant.Cli.{folder}.{subfolder}.{resourceName}";
        }

        using var stream = Assembly.GetManifestResourceStream(fullResourceName);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // Fallback: find resource by partial match
        var searchPattern = string.IsNullOrEmpty(subfolder)
            ? $".{folder}."
            : $".{folder}.{subfolder}.";

        var match = allResources.FirstOrDefault(r =>
            r.Contains(searchPattern) && r.EndsWith(resourceName));

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
