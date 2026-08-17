// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// A live connection to one target system under investigation: a stable catalog of the
/// operations it exposes, and the ability to execute them by name with concrete JSON
/// request/response values.
///
/// JSON Schema is the portable operation contract (see <see cref="OperationDefinition"/>);
/// everything about how a session actually talks to its target - the client library it
/// uses, an in-memory data structure, process/network lifetime, internal (de)serialization
/// - is the session's own business. A session owns the lifetime of whatever underlying
/// client or in-memory target it holds and must release it on <see cref="IAsyncDisposable.DisposeAsync"/>;
/// nothing in this SDK disposes a session on the caller's behalf (see
/// <see cref="TraceRecorder.RunAsync"/>).
/// </summary>
public interface ITargetSession : IAsyncDisposable
{
    /// <summary>
    /// The stable list of operations this session supports, each with its request and
    /// response JSON Schema. Does not change over the session's lifetime.
    /// </summary>
    IReadOnlyList<OperationDefinition> Operations { get; }

    /// <summary>
    /// Executes the named operation with a concrete JSON request, returning its concrete
    /// JSON response.
    ///
    /// Implementations must validate tightly: an unrecognized <paramref name="operationName"/>
    /// or an undefined/invalid <paramref name="request"/> must throw rather than silently
    /// proceed. See <see cref="TargetSessionBase"/> for a base class that centralizes this.
    /// </summary>
    /// <param name="operationName">The name of a previously advertised operation.</param>
    /// <param name="request">The concrete JSON request to execute.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <returns>The concrete JSON response.</returns>
    Task<JsonElement> ExecuteAsync(
        string operationName, JsonElement request, CancellationToken cancellationToken = default);
}
