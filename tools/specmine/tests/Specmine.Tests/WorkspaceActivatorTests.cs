// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;
using NUnit.Framework;

/// <summary>
/// Exercises <see cref="WorkspaceActivator.ConnectAsync"/> end to end: resolving a
/// workspace's declared adapter type through a <see cref="TargetAdapterRegistry"/>,
/// handing the declaration's own settings to that adapter untouched, returning a live,
/// caller-owned session usable against the rest of the SDK (operation enumeration,
/// execution, <see cref="TraceRecorder"/>), and propagating unknown-adapter, settings-
/// validation, connection, cancellation, and invalid-adapter (null session) failures
/// rather than swallowing any of them.
/// </summary>
[TestFixture]
public sealed class WorkspaceActivatorTests
{
    [Test]
    public async Task ConnectAsync_ResolvesTheDeclaredAdapterAndReturnsAUsableSession()
    {
        using var root = new TestWorkspaceRoot();
        var settings = JsonSerializer.SerializeToElement(new { idPrefix = "widget" });
        var workspace = await Workspace.InitializeAsync(
            root.Path, new TargetAdapterDeclaration("in-memory-task", settings));

        var registry = new TargetAdapterRegistry();
        registry.Register(new InMemoryTaskAdapter());

        await using var session = await WorkspaceActivator.ConnectAsync(workspace, registry);

        var response = await session.ExecuteAsync(
            "CreateTask", JsonSerializer.SerializeToElement(new { Title = "from workspace" }));

        Assert.That(response.GetProperty("Id").GetString(), Does.StartWith("widget-"));
    }

    [Test]
    public async Task ConnectAsync_ReturnedSessionWorksWithTraceRecorder()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path,
            new TargetAdapterDeclaration("in-memory-task", JsonSerializer.SerializeToElement(new { })));

        var registry = new TargetAdapterRegistry();
        registry.Register(new InMemoryTaskAdapter());

        await using var session = await WorkspaceActivator.ConnectAsync(workspace, registry);

        var (trace, path) = await TraceRecorder.RunAsync(workspace.TracesDirectory, session, async recordingTarget =>
        {
            await recordingTarget.ExecuteAsync(
                "CreateTask", JsonSerializer.SerializeToElement(new { Title = "traced" }));
        });

        Assert.Multiple(() =>
        {
            Assert.That(trace.Status, Is.EqualTo(TraceStatus.Completed));
            Assert.That(Path.GetDirectoryName(path), Is.EqualTo(workspace.TracesDirectory));
        });
    }

    [Test]
    public async Task ConnectAsync_PassesTheDeclarationsExactSettingsElementToTheAdapter()
    {
        using var root = new TestWorkspaceRoot();
        var settings = JsonSerializer.SerializeToElement(new { anything = "goes", nested = new { a = 1 } });
        var workspace = await Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("stub", settings));

        var adapter = new StubTargetAdapter("stub");
        var registry = new TargetAdapterRegistry();
        registry.Register(adapter);

        await using var session = await WorkspaceActivator.ConnectAsync(workspace, registry);

        Assert.That(
            JsonElement.DeepEquals(adapter.LastSettings!.Value, workspace.Document.TargetAdapter.Settings), Is.True);
    }

    [Test]
    public async Task ConnectAsync_NeverAddsWorkspacePathsIntoTheAdapterSettings()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path, new TargetAdapterDeclaration("stub", JsonSerializer.SerializeToElement(new { })));

        var adapter = new StubTargetAdapter("stub");
        var registry = new TargetAdapterRegistry();
        registry.Register(adapter);

        await using var session = await WorkspaceActivator.ConnectAsync(workspace, registry);

        // Workspace paths (RootPath, TargetDirectory, TracesDirectory, ...) are
        // machine-resolved runtime properties of Workspace itself, not settings: the
        // adapter must see exactly the (here, empty) declared settings and nothing else.
        Assert.Multiple(() =>
        {
            Assert.That(adapter.LastSettings!.Value.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(adapter.LastSettings!.Value.EnumerateObject().Any(), Is.False);
        });
    }

    [Test]
    public async Task ConnectAsync_UnknownAdapterType_ThrowsUnknownAdapterTypeExceptionNamingTheDeclaredType()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path, new TargetAdapterDeclaration("openapi", JsonSerializer.SerializeToElement(new { })));

        var registry = new TargetAdapterRegistry();
        registry.Register(new InMemoryTaskAdapter());

        var thrown = Assert.ThrowsAsync<UnknownAdapterTypeException>(() =>
            WorkspaceActivator.ConnectAsync(workspace, registry));

        Assert.That(thrown!.AdapterType, Is.EqualTo("openapi"));
    }

    [Test]
    public void ConnectAsync_AdapterSettingsValidationFailure_PropagatesUnchanged()
    {
        var settings = JsonSerializer.SerializeToElement(new { idPrefix = 42 });

        using var root = new TestWorkspaceRoot();
        var workspaceTask = Workspace.InitializeAsync(root.Path, new TargetAdapterDeclaration("in-memory-task", settings));
        var workspace = workspaceTask.GetAwaiter().GetResult();

        var registry = new TargetAdapterRegistry();
        registry.Register(new InMemoryTaskAdapter());

        // InMemoryTaskAdapter rejects a non-string idPrefix while interpreting its own
        // settings; that ArgumentException must reach the caller of ConnectAsync
        // unchanged, not be swallowed or replaced by activation.
        Assert.ThrowsAsync<ArgumentException>(() => WorkspaceActivator.ConnectAsync(workspace, registry));
    }

    [Test]
    public async Task ConnectAsync_AdapterConnectionFailure_PropagatesUnchanged()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path, new TargetAdapterDeclaration("stub", JsonSerializer.SerializeToElement(new { })));

        var adapter = new StubTargetAdapter(
            "stub", connect: (_, _) => throw new InvalidOperationException("simulated connection failure"));
        var registry = new TargetAdapterRegistry();
        registry.Register(adapter);

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkspaceActivator.ConnectAsync(workspace, registry));

        Assert.That(thrown!.Message, Does.Contain("simulated connection failure"));
    }

    [Test]
    public async Task ConnectAsync_AlreadyCanceledToken_ThrowsWithoutInvokingTheAdapter()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path, new TargetAdapterDeclaration("stub", JsonSerializer.SerializeToElement(new { })));

        var connectWasCalled = false;
        var adapter = new StubTargetAdapter("stub", connect: (_, _) =>
        {
            connectWasCalled = true;
            return Task.FromResult<ITargetSession>(new StubTargetSession());
        });
        var registry = new TargetAdapterRegistry();
        registry.Register(adapter);

        using var alreadyCanceled = new CancellationTokenSource();
        alreadyCanceled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() =>
            WorkspaceActivator.ConnectAsync(workspace, registry, alreadyCanceled.Token));

        Assert.That(connectWasCalled, Is.False);
    }

    [Test]
    public async Task ConnectAsync_CancellationDuringConnect_PropagatesFromTheAdapterUnchanged()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path, new TargetAdapterDeclaration("stub", JsonSerializer.SerializeToElement(new { })));

        using var cts = new CancellationTokenSource();

        // Simulates real connecting work (e.g. a network handshake) that keeps observing
        // the token rather than exiting immediately, proving the same token the caller
        // passed to ConnectAsync is the one that reaches the adapter.
        var adapter = new StubTargetAdapter("stub", connect: async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new StubTargetSession();
        });
        var registry = new TargetAdapterRegistry();
        registry.Register(adapter);

        var connectTask = WorkspaceActivator.ConnectAsync(workspace, registry, cts.Token);
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => connectTask);
    }

    [Test]
    public async Task ConnectAsync_AdapterReturnsNullSession_ThrowsInvalidOperationException()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path, new TargetAdapterDeclaration("stub", JsonSerializer.SerializeToElement(new { })));

        var adapter = new StubTargetAdapter("stub", connect: (_, _) => Task.FromResult<ITargetSession>(null!));
        var registry = new TargetAdapterRegistry();
        registry.Register(adapter);

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkspaceActivator.ConnectAsync(workspace, registry));

        Assert.That(thrown!.Message, Does.Contain("stub"));
    }

    [Test]
    public async Task ConnectAsync_ReturnedSessionIsCallerOwned_ActivationNeverDisposesIt()
    {
        using var root = new TestWorkspaceRoot();
        var workspace = await Workspace.InitializeAsync(
            root.Path,
            new TargetAdapterDeclaration("in-memory-task", JsonSerializer.SerializeToElement(new { })));

        var registry = new TargetAdapterRegistry();
        registry.Register(new InMemoryTaskAdapter());

        var session = (InMemoryTaskSession)await WorkspaceActivator.ConnectAsync(workspace, registry);

        Assert.That(session.Disposed, Is.False);

        await session.DisposeAsync();

        Assert.That(session.Disposed, Is.True);
    }
}
