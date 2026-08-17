// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

/// <summary>
/// Runs one bounded recording experiment against a target session and persists its trace.
///
/// <see cref="RunAsync"/> is the sole, primary entry point: it wraps a caller-owned
/// <see cref="ITargetSession"/> in a <see cref="RecordingTargetSession"/> and hands that
/// wrapper to the experiment body, so every <see cref="ITargetSession.ExecuteAsync"/> call
/// the body makes is automatically recorded, in order, with response-dependent chaining
/// working naturally (e.g. reading a server-generated ID from a Create call's response
/// before issuing a Complete call):
///
/// <code>
/// await TraceRecorder.RunAsync(tracesDirectory, target, async recordingTarget =>
/// {
///     var created = await recordingTarget.ExecuteAsync("Create", requestJson);
///     await recordingTarget.ExecuteAsync("Complete", ToRequestJson(created));
/// });
/// </code>
///
/// The target session passed to <see cref="RunAsync"/> is never disposed by this method:
/// the caller or adapter that produced it owns its lifetime and is responsible for
/// disposing it, typically once it is done running experiments against it - not once any
/// single <see cref="RunAsync"/> call returns.
/// </summary>
public static class TraceRecorder
{
    /// <summary>
    /// Runs one bounded recording experiment against <paramref name="target"/> and persists
    /// its trace.
    ///
    /// <paramref name="target"/> is wrapped in a fresh <see cref="RecordingTargetSession"/>
    /// and passed to <paramref name="body"/>. If the body runs to completion, a
    /// <see cref="TraceStatus.Completed"/> trace of every recorded call is saved. If the
    /// body throws, a <see cref="TraceStatus.Interrupted"/> trace of the calls recorded so
    /// far is saved and the original exception is rethrown - this method never swallows a
    /// failure or turns it into a success.
    /// </summary>
    /// <param name="tracesDirectory">
    /// The directory the trace file is written to. Created if it does not already exist.
    /// </param>
    /// <param name="target">
    /// The target session to record against. Owned by the caller: not disposed by this
    /// method, whether the experiment completes or is interrupted.
    /// </param>
    /// <param name="body">The experiment body, given a recording session to call operations with.</param>
    /// <returns>The persisted trace and the path of the file it was written to.</returns>
    public static async Task<(RecordedTrace Trace, string Path)> RunAsync(
        string tracesDirectory,
        ITargetSession target,
        Func<RecordingTargetSession, Task> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracesDirectory);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);

        var recordingSession = new RecordingTargetSession(target);
        var traceId = Guid.NewGuid();
        var startedAt = DateTime.UtcNow;

        try
        {
            await body(recordingSession).ConfigureAwait(false);

            var trace = new RecordedTrace(
                RecordedTrace.CurrentSchemaVersion,
                traceId,
                startedAt,
                DateTime.UtcNow,
                TraceStatus.Completed,
                recordingSession.Calls);

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
                recordingSession.Calls);

            await TraceStore.SaveAsync(tracesDirectory, trace).ConfigureAwait(false);
            throw;
        }
    }
}
