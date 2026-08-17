// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;
using NUnit.Framework;

[TestFixture]
public sealed class WorkspaceTests
{
    // A minimal test-only ITargetSession that echoes back whatever JSON request it is
    // given under a single "Echo" operation.
    private sealed class EchoTargetSession : ITargetSession
    {
        public IReadOnlyList<OperationDefinition> Operations { get; } = new[]
        {
            new OperationDefinition("Echo", JsonDocument.Parse("true").RootElement, JsonDocument.Parse("true").RootElement),
        };

        public Task<JsonElement> ExecuteAsync(
            string operationName, JsonElement request, CancellationToken cancellationToken = default) =>
            operationName == "Echo"
                ? Task.FromResult(request)
                : throw new UnknownOperationException(operationName);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static JsonElement OpenApiStyleSettings() => JsonSerializer.SerializeToElement(new
    {
        baseUrl = "https://example.test",
        openApiDocumentPath = "openapi.json",
        timeoutSeconds = 30,
        retryPolicy = new { maxAttempts = 3, backoffMilliseconds = new[] { 100, 200, 400 } },
    });

    // Writes the four artifacts a workspace requires besides workspace.json, so a test
    // that wants to isolate one specific workspace.json validation failure does not also
    // trip the "required artifact is missing" check.
    private static void CreateSiblingArtifacts(string rootPath)
    {
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, Workspace.TargetDirectoryName));
        Directory.CreateDirectory(Path.Combine(rootPath, Workspace.TracesDirectoryName));
        File.WriteAllText(Path.Combine(rootPath, Workspace.FrontierFileName), "# Frontier\n");
        File.WriteAllText(Path.Combine(rootPath, Workspace.JournalFileName), "# Journal\n");
    }

    [Test]
    public async Task InitializeAsync_CreatesExpectedLayout()
    {
        using var root = new TestWorkspaceRoot();
        var declaration = new TargetAdapterDeclaration("openapi", OpenApiStyleSettings());

        var workspace = await Workspace.InitializeAsync(root.Path, declaration);

        Assert.Multiple(() =>
        {
            Assert.That(workspace.RootPath, Is.EqualTo(Path.GetFullPath(root.Path)));
            Assert.That(File.Exists(workspace.WorkspaceJsonPath), Is.True);
            Assert.That(Directory.Exists(workspace.TargetDirectory), Is.True);
            Assert.That(Directory.Exists(workspace.TracesDirectory), Is.True);
            Assert.That(File.Exists(workspace.FrontierPath), Is.True);
            Assert.That(File.Exists(workspace.JournalPath), Is.True);

            // Deliberately small: nothing else should have been created.
            Assert.That(Directory.GetFileSystemEntries(root.Path), Has.Length.EqualTo(5));
        });
    }

    [Test]
    public async Task InitializeAsync_WritesMinimalJsonShapeWithNoExtraTopLevelFields()
    {
        using var root = new TestWorkspaceRoot();
        var settings = OpenApiStyleSettings();
        var declaration = new TargetAdapterDeclaration("openapi", settings);

        var workspace = await Workspace.InitializeAsync(root.Path, declaration);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(workspace.WorkspaceJsonPath));
        var documentRoot = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(
                documentRoot.EnumerateObject().Select(p => p.Name),
                Is.EquivalentTo(new[] { "SchemaVersion", "TargetAdapter" }));
            Assert.That(documentRoot.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(1));

            var targetAdapter = documentRoot.GetProperty("TargetAdapter");
            Assert.That(
                targetAdapter.EnumerateObject().Select(p => p.Name),
                Is.EquivalentTo(new[] { "AdapterType", "Settings" }));
            Assert.That(targetAdapter.GetProperty("AdapterType").GetString(), Is.EqualTo("openapi"));
            Assert.That(JsonElement.DeepEquals(targetAdapter.GetProperty("Settings"), settings), Is.True);

            // No hardcoded taxonomy, credentials, reset model, or operation catalog.
            Assert.That(documentRoot.TryGetProperty("BaseUrl", out _), Is.False);
            Assert.That(documentRoot.TryGetProperty("Capabilities", out _), Is.False);
            Assert.That(documentRoot.TryGetProperty("Credentials", out _), Is.False);
            Assert.That(documentRoot.TryGetProperty("Operations", out _), Is.False);
            Assert.That(documentRoot.TryGetProperty("Reset", out _), Is.False);
        });
    }

    [Test]
    public void TargetAdapterDeclaration_RequiresNonBlankAdapterType()
    {
        var settings = JsonSerializer.SerializeToElement(new { });

        Assert.Throws<ArgumentException>(() => new TargetAdapterDeclaration(string.Empty, settings));
        Assert.Throws<ArgumentException>(() => new TargetAdapterDeclaration("   ", settings));
    }

    [Test]
    public void TargetAdapterDeclaration_SnapshotsSettings_SurvivingDisposalOfSourceDocument()
    {
        TargetAdapterDeclaration declaration;
        using (var sourceDocument = JsonDocument.Parse("{\"key\":\"value\"}"))
        {
            // Construct while the source document is still alive - a correct
            // implementation clones the element here rather than keeping it alive by
            // reference into the source document's buffer.
            declaration = new TargetAdapterDeclaration("command", sourceDocument.RootElement);
        }

        // The source document is now disposed. If the declaration had not snapshotted
        // its own independent copy, reading Settings here would throw.
        Assert.That(declaration.Settings.GetProperty("key").GetString(), Is.EqualTo("value"));
    }

    [Test]
    public async Task RoundTrip_OpaqueAdapterSettingsSurviveInitializeThenLoad()
    {
        using var root = new TestWorkspaceRoot();
        var settings = OpenApiStyleSettings();

        await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings));
        var loaded = await Workspace.LoadAsync(root.Path);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Document.TargetAdapter.AdapterType, Is.EqualTo("openapi"));
            Assert.That(JsonElement.DeepEquals(loaded.Document.TargetAdapter.Settings, settings), Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_FreshCall_ReloadsAnEquivalentWorkspace()
    {
        using var root = new TestWorkspaceRoot();
        var settings = JsonSerializer.SerializeToElement(new { command = "investigator", args = new[] { "--flag" } });
        var initialized = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("command", settings));

        // Load as an entirely separate call, as a fresh process reopening the workspace
        // would: no state from `initialized` is reused here.
        var reloaded = await Workspace.LoadAsync(root.Path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.RootPath, Is.EqualTo(initialized.RootPath));
            Assert.That(reloaded.WorkspaceJsonPath, Is.EqualTo(initialized.WorkspaceJsonPath));
            Assert.That(reloaded.TargetDirectory, Is.EqualTo(initialized.TargetDirectory));
            Assert.That(reloaded.TracesDirectory, Is.EqualTo(initialized.TracesDirectory));
            Assert.That(reloaded.FrontierPath, Is.EqualTo(initialized.FrontierPath));
            Assert.That(reloaded.JournalPath, Is.EqualTo(initialized.JournalPath));
            Assert.That(reloaded.Document.SchemaVersion, Is.EqualTo(initialized.Document.SchemaVersion));
            Assert.That(reloaded.Document.TargetAdapter.AdapterType, Is.EqualTo(initialized.Document.TargetAdapter.AdapterType));
            Assert.That(
                JsonElement.DeepEquals(reloaded.Document.TargetAdapter.Settings, initialized.Document.TargetAdapter.Settings),
                Is.True);
        });
    }

    [Test]
    public async Task InitializeAsync_RefusesToOverwriteAnExistingWorkspace()
    {
        using var root = new TestWorkspaceRoot();
        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://example.test" });

        var workspace = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings));
        var originalContent = await File.ReadAllTextAsync(workspace.WorkspaceJsonPath);

        var conflictingSettings = JsonSerializer.SerializeToElement(new { command = "should-not-be-written" });
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("command", conflictingSettings)));

        var contentAfterAttempt = await File.ReadAllTextAsync(workspace.WorkspaceJsonPath);
        Assert.That(contentAfterAttempt, Is.EqualTo(originalContent));
    }

    [Test]
    public void InitializeAsync_RefusesWhenAConflictingArtifactAlreadyExistsAndLeavesNoPartialWorkspace()
    {
        using var root = new TestWorkspaceRoot();
        Directory.CreateDirectory(Path.Combine(root.Path, Workspace.TargetDirectoryName));

        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://example.test" });
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings)));

        Assert.Multiple(() =>
        {
            // Only the pre-existing conflicting directory should be there - no other
            // artifact was partially written before the conflict was detected.
            Assert.That(Directory.GetFileSystemEntries(root.Path), Has.Length.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(root.Path, Workspace.WorkspaceJsonFileName)), Is.False);
            Assert.That(Directory.Exists(Path.Combine(root.Path, Workspace.TracesDirectoryName)), Is.False);
            Assert.That(File.Exists(Path.Combine(root.Path, Workspace.FrontierFileName)), Is.False);
            Assert.That(File.Exists(Path.Combine(root.Path, Workspace.JournalFileName)), Is.False);
        });
    }

    [Test]
    public async Task InitializeAsync_IntoAPreExistingEmptyDirectory_Succeeds()
    {
        using var root = new TestWorkspaceRoot();
        Directory.CreateDirectory(root.Path);

        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://example.test" });
        var workspace = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings));

        Assert.That(Directory.GetFileSystemEntries(root.Path), Has.Length.EqualTo(5));
        Assert.That(File.Exists(workspace.WorkspaceJsonPath), Is.True);
    }

    [Test]
    public async Task InitializeAsync_IntoAPreExistingDirectoryWithUnrelatedContent_PreservesThatContent()
    {
        using var root = new TestWorkspaceRoot();
        Directory.CreateDirectory(root.Path);
        var unrelatedPath = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(unrelatedPath, "keep me");

        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://example.test" });
        var workspace = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(unrelatedPath), Is.True);
            Assert.That(File.ReadAllText(unrelatedPath), Is.EqualTo("keep me"));
            Assert.That(File.Exists(workspace.WorkspaceJsonPath), Is.True);
            Assert.That(Directory.GetFileSystemEntries(root.Path), Has.Length.EqualTo(6));
        });
    }

    [Test]
    public void LoadAsync_MissingRootDirectory_Throws()
    {
        using var root = new TestWorkspaceRoot();

        Assert.ThrowsAsync<DirectoryNotFoundException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public void LoadAsync_MissingWorkspaceJson_Throws()
    {
        using var root = new TestWorkspaceRoot();
        Directory.CreateDirectory(root.Path);

        Assert.ThrowsAsync<FileNotFoundException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public void LoadAsync_MalformedWorkspaceJson_Throws()
    {
        using var root = new TestWorkspaceRoot();
        CreateSiblingArtifacts(root.Path);
        File.WriteAllText(Path.Combine(root.Path, Workspace.WorkspaceJsonFileName), "{ not valid json");

        // JsonDocument.Parse throws the JsonException subtype JsonReaderException for
        // malformed input, so CatchAsync (which matches subtypes) is used rather than
        // ThrowsAsync (which requires an exact type match).
        Assert.CatchAsync<JsonException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public void LoadAsync_UnsupportedSchemaVersion_Throws()
    {
        using var root = new TestWorkspaceRoot();
        CreateSiblingArtifacts(root.Path);
        File.WriteAllText(
            Path.Combine(root.Path, Workspace.WorkspaceJsonFileName),
            """{ "SchemaVersion": 2, "TargetAdapter": { "AdapterType": "openapi", "Settings": {} } }""");

        Assert.ThrowsAsync<NotSupportedException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public void LoadAsync_BlankAdapterType_Throws()
    {
        using var root = new TestWorkspaceRoot();
        CreateSiblingArtifacts(root.Path);
        File.WriteAllText(
            Path.Combine(root.Path, Workspace.WorkspaceJsonFileName),
            """{ "SchemaVersion": 1, "TargetAdapter": { "AdapterType": "  ", "Settings": {} } }""");

        Assert.ThrowsAsync<InvalidDataException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public void LoadAsync_MissingTargetAdapter_Throws()
    {
        using var root = new TestWorkspaceRoot();
        CreateSiblingArtifacts(root.Path);
        File.WriteAllText(
            Path.Combine(root.Path, Workspace.WorkspaceJsonFileName), """{ "SchemaVersion": 1 }""");

        Assert.ThrowsAsync<InvalidDataException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public async Task LoadAsync_MissingRequiredDirectory_Throws()
    {
        using var root = new TestWorkspaceRoot();
        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://example.test" });
        var workspace = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings));

        Directory.Delete(workspace.TracesDirectory, recursive: true);

        Assert.ThrowsAsync<DirectoryNotFoundException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public async Task LoadAsync_MissingRequiredFile_Throws()
    {
        using var root = new TestWorkspaceRoot();
        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://example.test" });
        var workspace = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings));

        File.Delete(workspace.JournalPath);

        Assert.ThrowsAsync<FileNotFoundException>(() => Workspace.LoadAsync(root.Path));
    }

    [Test]
    public async Task TracesDirectory_SupportsRecordingAndReloadingATraceThroughTheWorkspace()
    {
        using var root = new TestWorkspaceRoot();
        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://example.test" });
        var workspace = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("openapi", settings));
        var session = new EchoTargetSession();

        var (trace, path) = await TraceRecorder.RunAsync(workspace.TracesDirectory, session, async recordingTarget =>
        {
            await recordingTarget.ExecuteAsync("Echo", JsonSerializer.SerializeToElement(new { Text = "hello" }));
        });

        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetDirectoryName(path), Is.EqualTo(workspace.TracesDirectory));
            Assert.That(trace.Status, Is.EqualTo(TraceStatus.Completed));
            Assert.That(reloaded.TraceId, Is.EqualTo(trace.TraceId));
            Assert.That(reloaded.Calls, Has.Count.EqualTo(1));
            Assert.That(reloaded.Calls[0].Response!.Value.GetProperty("Text").GetString(), Is.EqualTo("hello"));
        });
    }
}