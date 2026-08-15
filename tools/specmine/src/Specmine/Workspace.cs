// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// A minimal, on-disk investigation workspace: a small, fixed set of artifacts rooted at
/// one directory.
///
/// <code>
/// &lt;root&gt;/
///   workspace.json   - schema version and the target adapter declaration
///   target/          - reserved for adapter-owned target artifacts
///   traces/          - recorded traces (see <see cref="TraceRecorder"/> / <see cref="TraceStore"/>)
///   frontier.md      - free-form notes on open questions and next steps
///   journal.md       - free-form running log of what was tried and observed
/// </code>
///
/// A workspace performs no orchestration: it only creates, validates, and resolves the
/// paths of this fixed artifact set. It knows nothing about how an adapter executes
/// operations or how a trace gets produced - it only exposes <see cref="TracesDirectory"/>
/// as the directory a <see cref="TraceRecorder"/> should be pointed at.
/// </summary>
public sealed class Workspace
{
    /// <summary>The file name of the workspace declaration.</summary>
    public const string WorkspaceJsonFileName = "workspace.json";

    /// <summary>The name of the directory reserved for adapter-owned target artifacts.</summary>
    public const string TargetDirectoryName = "target";

    /// <summary>The name of the directory recorded traces are written into.</summary>
    public const string TracesDirectoryName = "traces";

    /// <summary>The file name of the free-form investigation frontier notes.</summary>
    public const string FrontierFileName = "frontier.md";

    /// <summary>The file name of the free-form investigation journal notes.</summary>
    public const string JournalFileName = "journal.md";

    private const string FrontierTemplate = "# Frontier\n\nOpen questions and next steps for this investigation.\n";
    private const string JournalTemplate = "# Journal\n\nA running log of what was tried and observed.\n";

    // The artifacts a workspace requires besides workspace.json itself, used both to
    // reject conflicting pre-existing files on initialization and to validate presence
    // on load.
    private static readonly (string Name, bool IsDirectory)[] RequiredArtifactSpecs =
    {
        (TargetDirectoryName, true),
        (TracesDirectoryName, true),
        (FrontierFileName, false),
        (JournalFileName, false),
    };

    /// <summary>
    /// The workspace root directory, as a fully-qualified path resolved on this machine.
    /// This path is never persisted into <c>workspace.json</c>.
    /// </summary>
    public string RootPath { get; }

    /// <summary>The parsed contents of <c>workspace.json</c>.</summary>
    public WorkspaceDocument Document { get; }

    /// <summary>The resolved path of <c>workspace.json</c>.</summary>
    public string WorkspaceJsonPath => Path.Combine(RootPath, WorkspaceJsonFileName);

    /// <summary>The resolved path of the <c>target/</c> directory.</summary>
    public string TargetDirectory => Path.Combine(RootPath, TargetDirectoryName);

    /// <summary>
    /// The resolved path of the <c>traces/</c> directory. Pass this directly as the
    /// <c>tracesDirectory</c> argument to <see cref="TraceRecorder.RunAsync"/> to record a
    /// trace into this workspace.
    /// </summary>
    public string TracesDirectory => Path.Combine(RootPath, TracesDirectoryName);

    /// <summary>The resolved path of <c>frontier.md</c>.</summary>
    public string FrontierPath => Path.Combine(RootPath, FrontierFileName);

    /// <summary>The resolved path of <c>journal.md</c>.</summary>
    public string JournalPath => Path.Combine(RootPath, JournalFileName);

    private Workspace(string rootPath, WorkspaceDocument document)
    {
        RootPath = rootPath;
        Document = document;
    }

    /// <summary>
    /// Creates a new workspace at <paramref name="rootPath"/>: writes <c>workspace.json</c>
    /// declaring <paramref name="targetAdapter"/>, and creates the <c>target/</c> and
    /// <c>traces/</c> directories and the <c>frontier.md</c> and <c>journal.md</c> notes.
    ///
    /// Where possible (when <paramref name="rootPath"/> does not already exist) the whole
    /// artifact set is built in a private staging directory first and then moved into
    /// place with a single directory rename, so a reader can never observe a partially
    /// initialized workspace at <paramref name="rootPath"/>. If <paramref name="rootPath"/>
    /// already exists (necessarily empty of any conflicting artifact - see below), the
    /// staged artifacts are moved in individually instead, since an existing directory
    /// cannot itself be replaced by a rename.
    /// </summary>
    /// <param name="rootPath">The workspace root directory. Created if it does not exist.</param>
    /// <param name="targetAdapter">The target adapter declaration to write into <c>workspace.json</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// A workspace is already initialized at <paramref name="rootPath"/>, or an existing
    /// file or directory there would conflict with one of the workspace's artifacts.
    /// </exception>
    public static async Task<Workspace> InitializeAsync(string rootPath, TargetAdapterDeclaration targetAdapter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(targetAdapter);

        var fullRootPath = Path.GetFullPath(rootPath);

        if (Directory.Exists(fullRootPath))
        {
            if (File.Exists(Path.Combine(fullRootPath, WorkspaceJsonFileName)))
            {
                throw new InvalidOperationException($"A workspace is already initialized at '{fullRootPath}'.");
            }

            foreach (var (name, isDirectory) in RequiredArtifactSpecs)
            {
                var existingPath = Path.Combine(fullRootPath, name);
                if (isDirectory ? Directory.Exists(existingPath) : File.Exists(existingPath))
                {
                    throw new InvalidOperationException(
                        $"Cannot initialize a workspace at '{fullRootPath}': '{name}' already exists there.");
                }
            }
        }

        var document = new WorkspaceDocument(WorkspaceDocument.CurrentSchemaVersion, targetAdapter);

        var parentDirectory = Path.GetDirectoryName(fullRootPath);
        if (string.IsNullOrEmpty(parentDirectory))
        {
            throw new ArgumentException($"'{fullRootPath}' is not a valid workspace root path.", nameof(rootPath));
        }

        Directory.CreateDirectory(parentDirectory);

        var stagingPath = Path.Combine(
            parentDirectory, $".{Path.GetFileName(fullRootPath)}.{Guid.NewGuid():N}.staging");

        try
        {
            Directory.CreateDirectory(stagingPath);
            Directory.CreateDirectory(Path.Combine(stagingPath, TargetDirectoryName));
            Directory.CreateDirectory(Path.Combine(stagingPath, TracesDirectoryName));

            await File.WriteAllTextAsync(
                Path.Combine(stagingPath, WorkspaceJsonFileName),
                JsonSerializer.Serialize(document, WorkspaceJson.FileOptions)).ConfigureAwait(false);

            await File.WriteAllTextAsync(Path.Combine(stagingPath, FrontierFileName), FrontierTemplate)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(stagingPath, JournalFileName), JournalTemplate)
                .ConfigureAwait(false);

            if (Directory.Exists(fullRootPath))
            {
                // Already confirmed above to hold nothing that would conflict, but it may
                // hold unrelated content, so it cannot itself be replaced by a directory
                // rename: move each staged artifact into it individually instead.
                foreach (var entry in Directory.EnumerateFileSystemEntries(stagingPath))
                {
                    var destination = Path.Combine(fullRootPath, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                    {
                        Directory.Move(entry, destination);
                    }
                    else
                    {
                        File.Move(entry, destination);
                    }
                }

                Directory.Delete(stagingPath, recursive: true);
            }
            else
            {
                Directory.Move(stagingPath, fullRootPath);
            }
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            throw;
        }

        return new Workspace(fullRootPath, document);
    }

    /// <summary>
    /// Loads and validates the workspace rooted at <paramref name="rootPath"/>.
    ///
    /// Validates that <c>workspace.json</c> exists and declares a supported schema
    /// version and a non-blank target adapter type, and that the <c>target/</c> and
    /// <c>traces/</c> directories and the <c>frontier.md</c> and <c>journal.md</c> files
    /// all exist. Every failure is reported as a specific exception rather than falling
    /// back to a partially usable workspace.
    /// </summary>
    /// <param name="rootPath">The workspace root directory to load.</param>
    public static async Task<Workspace> LoadAsync(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var fullRootPath = Path.GetFullPath(rootPath);

        if (!Directory.Exists(fullRootPath))
        {
            throw new DirectoryNotFoundException($"No workspace directory found at '{fullRootPath}'.");
        }

        var workspaceJsonPath = Path.Combine(fullRootPath, WorkspaceJsonFileName);
        if (!File.Exists(workspaceJsonPath))
        {
            throw new FileNotFoundException(
                $"'{WorkspaceJsonFileName}' was not found in workspace directory '{fullRootPath}'.",
                workspaceJsonPath);
        }

        JsonDocument parsed;
        await using (var stream = File.OpenRead(workspaceJsonPath))
        {
            parsed = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        }

        using (parsed)
        {
            var root = parsed.RootElement;

            if (!root.TryGetProperty(nameof(WorkspaceDocument.SchemaVersion), out var schemaVersionElement) ||
                schemaVersionElement.ValueKind != JsonValueKind.Number ||
                !schemaVersionElement.TryGetInt32(out var schemaVersion))
            {
                throw new InvalidDataException(
                    $"'{workspaceJsonPath}' does not declare an integer '{nameof(WorkspaceDocument.SchemaVersion)}'.");
            }

            if (schemaVersion != WorkspaceDocument.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Workspace schema version {schemaVersion} at '{workspaceJsonPath}' is not supported by " +
                    $"this library (supports schema version {WorkspaceDocument.CurrentSchemaVersion}).");
            }

            if (!root.TryGetProperty(nameof(WorkspaceDocument.TargetAdapter), out var targetAdapterElement) ||
                targetAdapterElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"'{workspaceJsonPath}' does not declare a '{nameof(WorkspaceDocument.TargetAdapter)}' object.");
            }

            if (!targetAdapterElement.TryGetProperty(
                    nameof(TargetAdapterDeclaration.AdapterType), out var adapterTypeElement) ||
                adapterTypeElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(adapterTypeElement.GetString()))
            {
                throw new InvalidDataException(
                    $"'{workspaceJsonPath}' target adapter does not declare a non-blank " +
                    $"'{nameof(TargetAdapterDeclaration.AdapterType)}'.");
            }

            if (!targetAdapterElement.TryGetProperty(
                    nameof(TargetAdapterDeclaration.Settings), out var settingsElement))
            {
                throw new InvalidDataException(
                    $"'{workspaceJsonPath}' target adapter does not declare " +
                    $"'{nameof(TargetAdapterDeclaration.Settings)}'.");
            }

            var targetAdapter = new TargetAdapterDeclaration(adapterTypeElement.GetString()!, settingsElement);
            var document = new WorkspaceDocument(schemaVersion, targetAdapter);

            foreach (var (name, isDirectory) in RequiredArtifactSpecs)
            {
                var artifactPath = Path.Combine(fullRootPath, name);

                if (isDirectory && !Directory.Exists(artifactPath))
                {
                    throw new DirectoryNotFoundException(
                        $"Required workspace directory '{name}' was not found at '{artifactPath}'.");
                }

                if (!isDirectory && !File.Exists(artifactPath))
                {
                    throw new FileNotFoundException(
                        $"Required workspace file '{name}' was not found at '{artifactPath}'.", artifactPath);
                }
            }

            return new Workspace(fullRootPath, document);
        }
    }
}