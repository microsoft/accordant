// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// An explicit, in-process registry mapping a stable <see cref="ITargetAdapter.AdapterType"/>
/// string to the concrete <see cref="ITargetAdapter"/> instance that implements it.
///
/// A host - a test harness, a future CLI, an IDE extension, or any other process that
/// wants to activate workspaces - builds one of these by registering, by hand, exactly the
/// concrete adapter instances it supports. There is no reflection, assembly scanning,
/// NuGet/package loading, dependency injection container, or other implicit discovery
/// mechanism here: every adapter type a host can activate is named in code the host
/// controls, in a call to <see cref="Register"/>.
///
/// Adapter type strings are compared using ordinal (case-sensitive, culture-invariant)
/// semantics throughout - the same semantics a raw string equality check would give, and
/// the least surprising choice for an identifier that is also embedded verbatim into a
/// workspace's on-disk <c>workspace.json</c> (see <see cref="TargetAdapterDeclaration.AdapterType"/>).
/// </summary>
public sealed class TargetAdapterRegistry
{
    private readonly Dictionary<string, ITargetAdapter> _adaptersByType = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers <paramref name="adapter"/> under its own <see cref="ITargetAdapter.AdapterType"/>.
    /// </summary>
    /// <param name="adapter">The concrete adapter instance to register.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="adapter"/>'s <see cref="ITargetAdapter.AdapterType"/> is null or
    /// consists only of whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An adapter is already registered for the same (ordinal-equal) adapter type - whether
    /// that is the same instance registered twice or a different adapter that happens to
    /// declare the same type string. Adapter types must be unique within one registry.
    /// </exception>
    public void Register(ITargetAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter.AdapterType);

        if (!_adaptersByType.TryAdd(adapter.AdapterType, adapter))
        {
            throw new InvalidOperationException(
                $"An adapter is already registered for adapter type '{adapter.AdapterType}'.");
        }
    }

    /// <summary>
    /// Resolves the adapter registered for <paramref name="adapterType"/>.
    /// </summary>
    /// <param name="adapterType">The adapter type to resolve, compared ordinally.</param>
    /// <exception cref="UnknownAdapterTypeException">
    /// No adapter is registered for <paramref name="adapterType"/>.
    /// </exception>
    public ITargetAdapter Resolve(string adapterType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterType);

        if (!_adaptersByType.TryGetValue(adapterType, out var adapter))
        {
            throw new UnknownAdapterTypeException(adapterType);
        }

        return adapter;
    }

    /// <summary>
    /// Attempts to resolve the adapter registered for <paramref name="adapterType"/>,
    /// returning <see langword="false"/> instead of throwing when none is registered.
    /// </summary>
    /// <param name="adapterType">The adapter type to resolve, compared ordinally.</param>
    /// <param name="adapter">The resolved adapter, when this method returns <see langword="true"/>.</param>
    public bool TryResolve(string adapterType, [NotNullWhen(true)] out ITargetAdapter? adapter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterType);

        return _adaptersByType.TryGetValue(adapterType, out adapter);
    }
}

/// <summary>
/// Thrown when resolving an <see cref="ITargetAdapter.AdapterType"/> string that no adapter
/// has been registered for in a <see cref="TargetAdapterRegistry"/>.
/// </summary>
public sealed class UnknownAdapterTypeException : ArgumentException
{
    /// <summary>The unrecognized adapter type string.</summary>
    public string AdapterType { get; }

    /// <summary>
    /// Creates an exception describing an unrecognized adapter type.
    /// </summary>
    /// <param name="adapterType">The unrecognized adapter type string.</param>
    public UnknownAdapterTypeException(string adapterType)
        : base($"No adapter is registered for adapter type '{adapterType}'.", nameof(adapterType))
    {
        AdapterType = adapterType;
    }
}
