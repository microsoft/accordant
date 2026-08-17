// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Typed convenience wrappers over the JSON <see cref="ITargetSession"/> boundary, for C#
/// callers that would rather work with concrete request/response types than raw
/// <see cref="JsonElement"/> values.
///
/// These helpers are a thin, optional layer: they serialize a typed request to JSON, call
/// <see cref="ITargetSession.ExecuteAsync"/> exactly as any other caller would, and
/// deserialize the JSON response back to a typed value. They never change what is actually
/// exchanged or recorded - a trace sees the same portable JSON either way - and they never
/// substitute for the operation's real JSON Schema contract.
/// </summary>
public static class TargetSessionExtensions
{
    /// <summary>
    /// Serializes <paramref name="request"/>, executes <paramref name="operationName"/>
    /// against <paramref name="session"/>, and deserializes the JSON response as
    /// <typeparamref name="TResponse"/>.
    /// </summary>
    /// <typeparam name="TRequest">The C# type to serialize the request from.</typeparam>
    /// <typeparam name="TResponse">The C# type to deserialize the response into.</typeparam>
    /// <param name="session">The session (typically a <see cref="RecordingTargetSession"/>) to execute against.</param>
    /// <param name="operationName">The name of a previously advertised operation.</param>
    /// <param name="request">The typed request to serialize and execute.</param>
    /// <param name="serializerOptions">
    /// The serializer options used for both directions. Defaults to
    /// <see cref="JsonSerializerOptions.Default"/> when omitted.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    public static async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
        this ITargetSession session,
        string operationName,
        TRequest request,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var options = serializerOptions ?? JsonSerializerOptions.Default;
        var requestElement = JsonSerializer.SerializeToElement(request, options);

        var responseElement = await session.ExecuteAsync(operationName, requestElement, cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize<TResponse>(responseElement, options)!;
    }
}
