// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Thrown when <see cref="ITargetSession.ExecuteAsync"/> is called with an operation name
/// the session does not recognize.
/// </summary>
public sealed class UnknownOperationException : ArgumentException
{
    /// <summary>The unrecognized operation name.</summary>
    public string OperationName { get; }

    /// <summary>
    /// Creates an exception describing an unrecognized operation name.
    /// </summary>
    /// <param name="operationName">The unrecognized operation name.</param>
    public UnknownOperationException(string operationName)
        : base($"Operation '{operationName}' is not defined by this session.", nameof(operationName))
    {
        OperationName = operationName;
    }
}

/// <summary>
/// A convenience base for <see cref="ITargetSession"/> implementations that centralizes
/// the tight validation every session must perform - rejecting an unrecognized operation
/// name or an undefined JSON request before any target-specific execution runs - so each
/// adapter's session only has to implement its own operation dispatch.
/// </summary>
public abstract class TargetSessionBase : ITargetSession
{
    private readonly Dictionary<string, OperationDefinition> _operationsByName;

    /// <summary>
    /// Creates a session exposing exactly <paramref name="operations"/>.
    /// </summary>
    /// <param name="operations">The session's stable operation catalog.</param>
    protected TargetSessionBase(IReadOnlyList<OperationDefinition> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        Operations = operations;
        _operationsByName = operations.ToDictionary(operation => operation.Name, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public IReadOnlyList<OperationDefinition> Operations { get; }

    /// <inheritdoc/>
    public async Task<JsonElement> ExecuteAsync(
        string operationName, JsonElement request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (!_operationsByName.TryGetValue(operationName, out var operation))
        {
            throw new UnknownOperationException(operationName);
        }

        if (request.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Request must be a valid JSON value.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await ExecuteOperationAsync(operation, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes one already-validated operation call: <paramref name="operation"/> is
    /// guaranteed to be one of <see cref="Operations"/> and <paramref name="request"/> is
    /// guaranteed to be a defined JSON value.
    /// </summary>
    /// <param name="operation">The resolved operation being called.</param>
    /// <param name="request">The concrete JSON request.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    protected abstract Task<JsonElement> ExecuteOperationAsync(
        OperationDefinition operation, JsonElement request, CancellationToken cancellationToken);

    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();
}
