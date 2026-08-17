// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;

/// <summary>
/// A minimal, fully-configurable <see cref="ITargetAdapter"/> test double used to exercise
/// <see cref="TargetAdapterRegistry"/> and <see cref="WorkspaceActivator"/> without any real
/// transport. Each instance's connect behavior - what it returns, throws, or observes on
/// the connect callback's cancellation token - is supplied by whichever test constructs
/// it; by default it just hands back a fresh <see cref="StubTargetSession"/>.
/// </summary>
internal sealed class StubTargetAdapter : ITargetAdapter
{
    private readonly Func<JsonElement, CancellationToken, Task<ITargetSession>> _connect;

    /// <summary>
    /// Creates a stub adapter reporting <paramref name="adapterType"/> as its
    /// <see cref="AdapterType"/>.
    /// </summary>
    /// <param name="adapterType">The adapter type this stub reports.</param>
    /// <param name="connect">
    /// The behavior <see cref="ConnectAsync"/> delegates to. Defaults to returning a fresh
    /// <see cref="StubTargetSession"/> when omitted.
    /// </param>
    public StubTargetAdapter(
        string adapterType, Func<JsonElement, CancellationToken, Task<ITargetSession>>? connect = null)
    {
        AdapterType = adapterType;
        _connect = connect ?? ((_, _) => Task.FromResult<ITargetSession>(new StubTargetSession()));
    }

    /// <inheritdoc/>
    public string AdapterType { get; }

    /// <summary>The settings most recently passed to <see cref="ConnectAsync"/>, if any.</summary>
    public JsonElement? LastSettings { get; private set; }

    /// <inheritdoc/>
    public Task<ITargetSession> ConnectAsync(JsonElement settings, CancellationToken cancellationToken = default)
    {
        LastSettings = settings;
        return _connect(settings, cancellationToken);
    }
}

/// <summary>
/// A minimal <see cref="ITargetSession"/> test double: exposes no operations, rejects any
/// execution attempt as unknown, and tracks whether it has been disposed.
/// </summary>
internal sealed class StubTargetSession : ITargetSession
{
    /// <inheritdoc/>
    public IReadOnlyList<OperationDefinition> Operations { get; } = Array.Empty<OperationDefinition>();

    /// <summary>Whether <see cref="DisposeAsync"/> has been called on this session.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc/>
    public Task<JsonElement> ExecuteAsync(
        string operationName, JsonElement request, CancellationToken cancellationToken = default) =>
        throw new UnknownOperationException(operationName);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
