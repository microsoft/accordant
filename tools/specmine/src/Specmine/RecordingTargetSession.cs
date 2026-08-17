// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Decorates any <see cref="ITargetSession"/>, delegating operation discovery and
/// execution to it while automatically recording every execution attempt - its request,
/// and either its response or its failure - as an ordered list of <see cref="RecordedCall"/>.
///
/// This is a transparent decorator: it does not change which operations are advertised or
/// how a call behaves, it only observes and records. See <see cref="TraceRecorder.RunAsync"/>
/// for the usual way one is created and handed to an experiment body; a caller may also
/// construct one directly (for example to inspect <see cref="Calls"/> without persisting a
/// trace file).
/// </summary>
public sealed class RecordingTargetSession : ITargetSession
{
    private readonly ITargetSession _inner;
    private readonly List<RecordedCall> _calls = new();
    private int _nextCallId = 1;

    /// <summary>
    /// Decorates <paramref name="inner"/> with recording. <paramref name="inner"/> remains
    /// owned by whoever created it: disposing this decorator disposes <paramref name="inner"/>
    /// in turn (as any transparent decorator would), but this type never disposes it on its
    /// own initiative.
    /// </summary>
    /// <param name="inner">The target session to decorate.</param>
    public RecordingTargetSession(ITargetSession inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    /// <inheritdoc/>
    public IReadOnlyList<OperationDefinition> Operations => _inner.Operations;

    /// <summary>
    /// The ordered list of calls recorded so far, each snapshotted at the moment it was
    /// attempted. Grows as <see cref="ExecuteAsync"/> is called; never shrinks or reorders.
    /// </summary>
    public IReadOnlyList<RecordedCall> Calls => _calls;

    /// <summary>
    /// Executes <paramref name="operationName"/> against the wrapped session and records
    /// the attempt.
    ///
    /// If the wrapped session throws - for an unrecognized operation, an invalid request,
    /// or a genuine execution failure - that failure is recorded as an execution error and
    /// the exception is rethrown unchanged. This method never swallows a failure or turns
    /// it into a success.
    /// </summary>
    public async Task<JsonElement> ExecuteAsync(
        string operationName, JsonElement request, CancellationToken cancellationToken = default)
    {
        // Guard clauses for calls that cannot be recorded at all (no well-formed name, no
        // well-formed request) run before a call ID is assigned or anything is recorded -
        // mirroring how the previous ExecutableOperation-based recorder validated its
        // arguments before creating a call entry. An unrecognized operation name, by
        // contrast, is a well-formed attempted call that the wrapped session is left to
        // reject on its own terms, so it is recorded like any other execution failure below.
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (request.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Request must be a valid JSON value.", nameof(request));
        }

        var callId = _nextCallId++;

        // Clone defensively: the caller's JsonElement may come from a JsonDocument the
        // caller mutates or disposes after this call returns.
        var requestSnapshot = request.Clone();

        try
        {
            var response = await _inner.ExecuteAsync(operationName, request, cancellationToken)
                .ConfigureAwait(false);
            var responseSnapshot = response.Clone();

            _calls.Add(new RecordedCall(callId, operationName, requestSnapshot, responseSnapshot, error: null));

            return response;
        }
        catch (Exception ex)
        {
            _calls.Add(new RecordedCall(
                callId, operationName, requestSnapshot, response: null, RecordedError.FromException(ex)));

            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
