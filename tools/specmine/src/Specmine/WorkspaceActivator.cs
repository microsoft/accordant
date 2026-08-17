// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

/// <summary>
/// Activates a workspace's declared target adapter into a live, caller-owned
/// <see cref="ITargetSession"/>.
///
/// A <see cref="Workspace"/> performs no orchestration of its own - it only creates,
/// validates, and resolves the paths of its fixed on-disk artifact set (see
/// <see cref="Workspace"/>). Turning its declared <see cref="TargetAdapterDeclaration"/>
/// into a live connection needs a host-specific set of adapter implementations, which is
/// exactly what a <see cref="TargetAdapterRegistry"/> holds; <see cref="ConnectAsync"/> is
/// the small bridge between the two, and stops there - it does not run experiments,
/// record traces, or manage the resulting session's lifetime beyond handing it back.
/// </summary>
public static class WorkspaceActivator
{
    /// <summary>
    /// Resolves <paramref name="workspace"/>'s declared
    /// <c>Document.TargetAdapter.AdapterType</c> in <paramref name="registry"/> and connects
    /// it with the declaration's own opaque <c>Settings</c>, returning the live session.
    ///
    /// <paramref name="workspace"/>'s machine-resolved runtime paths (<see cref="Workspace.RootPath"/>
    /// and everything derived from it, such as <see cref="Workspace.TargetDirectory"/>) are
    /// never added to, or implied into, the settings passed to the adapter: the adapter
    /// receives exactly the opaque settings its own declaration carries, nothing more. An
    /// adapter that needs a workspace-relative path (for example, a future OpenAPI
    /// adapter's document path) must have that path included explicitly, by whoever
    /// authored the declaration, inside its own settings.
    ///
    /// Any exception the resolved adapter throws while validating its settings or
    /// connecting - and any cancellation requested through <paramref name="cancellationToken"/> -
    /// propagates unchanged to the caller; this method never swallows or replaces it.
    /// </summary>
    /// <param name="workspace">The workspace whose declared adapter should be activated.</param>
    /// <param name="registry">The registry to resolve the declared adapter type from.</param>
    /// <param name="cancellationToken">A token to cancel resolving or connecting.</param>
    /// <returns>
    /// The live session the resolved adapter connected. It is owned by the caller from this
    /// point on: nothing in this method disposes it, and the caller is responsible for
    /// eventually calling <see cref="IAsyncDisposable.DisposeAsync"/> on it.
    /// </returns>
    /// <exception cref="UnknownAdapterTypeException">
    /// No adapter is registered in <paramref name="registry"/> for the workspace's declared
    /// adapter type.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The resolved adapter's <see cref="ITargetAdapter.ConnectAsync"/> returned a null
    /// session, which violates the adapter contract.
    /// </exception>
    public static async Task<ITargetSession> ConnectAsync(
        Workspace workspace, TargetAdapterRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(registry);

        cancellationToken.ThrowIfCancellationRequested();

        var declaration = workspace.Document.TargetAdapter;
        var adapter = registry.Resolve(declaration.AdapterType);

        var session = await adapter.ConnectAsync(declaration.Settings, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            throw new InvalidOperationException(
                $"Adapter type '{declaration.AdapterType}' returned a null session from " +
                $"'{nameof(ITargetAdapter.ConnectAsync)}', which violates the adapter contract.");
        }

        return session;
    }
}
