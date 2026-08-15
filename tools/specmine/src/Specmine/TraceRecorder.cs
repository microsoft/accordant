// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Records the calls made during one bounded recording experiment.
///
/// A <see cref="TraceRecorder"/> is scoped to a single experiment: create one via
/// <see cref="RunAsync"/>, call <see cref="ExecuteAsync{TRequest, TResponse}"/> for each
/// operation invocation (using the returned response to build subsequent requests, e.g.
/// reading a server-generated ID from a Create call before issuing a Complete call), and
/// let <see cref="RunAsync"/> persist the resulting completed or interrupted trace.
/// </summary>
public sealed class TraceRecorder
{
    private readonly List<RecordedCall> _calls = new();
    private int _nextCallId = 1;

    private TraceRecorder()
    {
    }

    /// <summary>
    /// Executes one operation call: snapshots <paramref name="request"/>, invokes
    /// <paramref name="execute"/> against the system under test, and records either the
    /// snapshotted response or the execution error.
    ///
    /// If <paramref name="execute"/> throws, that means the execution delegate failed to
    /// produce its declared response. The failure is recorded as an execution error and
    /// the exception is rethrown unchanged - this method never swallows a failure or
    /// turns it into a success.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request.</typeparam>
    /// <typeparam name="TResponse">The type of the declared response.</typeparam>
    /// <param name="operationName">The name of the operation being called.</param>
    /// <param name="request">The request to record and pass to <paramref name="execute"/>.</param>
    /// <param name="execute">
    /// The delegate that actually calls the system under test with <paramref name="request"/>
    /// and returns its response.
    /// </param>
    /// <returns>The response produced by <paramref name="execute"/>.</returns>
    public async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
        string operationName,
        TRequest request,
        Func<TRequest, Task<TResponse>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(execute);

        var callId = _nextCallId++;
        var requestSnapshot = JsonSerializer.SerializeToElement(request, TraceJson.SnapshotOptions);

        try
        {
            var response = await execute(request).ConfigureAwait(false);
            var responseSnapshot = JsonSerializer.SerializeToElement(response, TraceJson.SnapshotOptions);

            _calls.Add(new RecordedCall(callId, operationName, requestSnapshot, responseSnapshot, error: null));

            return response;
        }
        catch (Exception ex)
        {
            _calls.Add(new RecordedCall(
                callId,
                operationName,
                requestSnapshot,
                response: null,
                RecordedError.FromException(ex)));

            throw;
        }
    }

    /// <summary>
    /// Runs one bounded recording experiment and persists its trace.
    ///
    /// A fresh recorder is created and passed to <paramref name="body"/>. If the body runs
    /// to completion, a <see cref="TraceStatus.Completed"/> trace of every recorded call is
    /// saved. If the body throws, a <see cref="TraceStatus.Interrupted"/> trace of the calls
    /// recorded so far is saved and the original exception is rethrown - this method never
    /// swallows a failure or turns it into a success.
    /// </summary>
    /// <param name="tracesDirectory">
    /// The directory the trace file is written to. Created if it does not already exist.
    /// </param>
    /// <param name="body">The experiment body, given the recorder to call operations with.</param>
    /// <returns>The persisted trace and the path of the file it was written to.</returns>
    public static async Task<(RecordedTrace Trace, string Path)> RunAsync(
        string tracesDirectory,
        Func<TraceRecorder, Task> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracesDirectory);
        ArgumentNullException.ThrowIfNull(body);

        var recorder = new TraceRecorder();
        var traceId = Guid.NewGuid();
        var startedAt = DateTime.UtcNow;

        try
        {
            await body(recorder).ConfigureAwait(false);

            var trace = new RecordedTrace(
                RecordedTrace.CurrentSchemaVersion,
                traceId,
                startedAt,
                DateTime.UtcNow,
                TraceStatus.Completed,
                recorder._calls.AsReadOnly());

            var path = await TraceStore.SaveAsync(tracesDirectory, trace).ConfigureAwait(false);
            return (trace, path);
        }
        catch
        {
            var trace = new RecordedTrace(
                RecordedTrace.CurrentSchemaVersion,
                traceId,
                startedAt,
                DateTime.UtcNow,
                TraceStatus.Interrupted,
                recorder._calls.AsReadOnly());

            await TraceStore.SaveAsync(tracesDirectory, trace).ConfigureAwait(false);
            throw;
        }
    }
}
