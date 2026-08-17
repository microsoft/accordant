// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Connects to a target system under investigation, turning an adapter-owned, opaque
/// JSON settings value into a live <see cref="ITargetSession"/>.
///
/// An adapter identifies itself with a stable <see cref="AdapterType"/> string - the same
/// string a workspace's <see cref="TargetAdapterDeclaration.AdapterType"/> selects it by -
/// and owns all interpretation and validation of its own settings shape. The SDK places no
/// constraints on what a particular adapter type's settings look like: an OpenAPI adapter,
/// a command adapter, or an in-memory test adapter each define their own shape.
/// </summary>
public interface ITargetAdapter
{
    /// <summary>The adapter implementation's stable, non-blank type name.</summary>
    string AdapterType { get; }

    /// <summary>
    /// Interprets <paramref name="settings"/> and connects to the target it describes,
    /// producing a live session. The adapter owns validation of the settings shape and
    /// should throw a specific exception if <paramref name="settings"/> is invalid for
    /// this adapter type.
    /// </summary>
    /// <param name="settings">The adapter's own, opaque configuration.</param>
    /// <param name="cancellationToken">A token to cancel connecting.</param>
    /// <returns>A live session, owned by the caller from this point on.</returns>
    Task<ITargetSession> ConnectAsync(JsonElement settings, CancellationToken cancellationToken = default);
}
