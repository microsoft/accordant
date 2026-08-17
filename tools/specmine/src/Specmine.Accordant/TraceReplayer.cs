// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

using System.Text.Json;
using Microsoft.Accordant;

/// <summary>
/// Replays an immutable <see cref="RecordedTrace"/> sequentially through an Accordant
/// <c>Spec&lt;TState&gt;</c>, using the spec as a pass/fail-plus-explanation oracle over what
/// the trace recorded - never executing anything itself. This mirrors the sequential
/// conformance-checking pattern documented for cross-language trace validation (see
/// <c>agent/skills/cross-language/SKILL.md</c> and
/// <c>docs/how-to/testing-any-system.md</c>), but working directly against Specmine's
/// <see cref="RecordedTrace"/>/<see cref="RecordedCall"/> shape instead of a hand-rolled
/// trace format.
///
/// <para>
/// <b>Sequential only.</b> <see cref="RecordedTrace"/> has no concurrency segments today, so
/// this replayer only ever calls <c>Spec&lt;TState&gt;.Allows</c>, never
/// <c>Spec&lt;TState&gt;.AllowsConcurrent</c>. Supporting concurrent replay waits on a future
/// trace representation for concurrent segments; nothing in this slice invents one.
/// </para>
///
/// <para>
/// <b>Partial models are legitimate.</b> A trace call whose <c>OperationName</c> does not
/// match any operation registered in the spec is reported as
/// <see cref="ReplayStepOutcome.OperationNotModeled"/> - never silently accepted and never
/// treated as a <see cref="ReplayStepOutcome.ModelViolation"/>.
/// </para>
///
/// <para>
/// <b>Conservative stop.</b> Once a call's outcome is anything other than
/// <see cref="ReplayStepOutcome.Conforming"/> - a model violation, an unmodeled operation, a
/// recorded execution error, or a request/response deserialization failure - replay stops
/// without attempting later calls. For a model violation this is forced: Accordant's own
/// <c>Verify</c> has no successor <see cref="StateProfile"/> to offer once a response is
/// rejected. For the other cases a later call could, in principle, still be checked against
/// the same state profile (the unmodeled/erroring call never advanced the model's state),
/// but this slice stops there anyway: once one call in a trace could not be explained, later
/// "conforming" results would rest on an unverified assumption that the skipped call had no
/// effect on real system state the model tracks. Stopping is the conservative, honest choice
/// the task deliberately allows; a caller who wants best-effort continuation past an
/// unmodeled/erroring call can slice <see cref="RecordedTrace.Calls"/> itself and call
/// <see cref="Replay{TState}"/> again from the resulting <see cref="TraceReplayResult.FinalStateProfile"/>.
/// </para>
/// </summary>
public static class TraceReplayer
{
    /// <summary>
    /// Replays <paramref name="trace"/> sequentially against <paramref name="spec"/>, starting
    /// from <paramref name="initialState"/>.
    /// </summary>
    /// <typeparam name="TState">The spec's state type.</typeparam>
    /// <param name="spec">The spec to use as an oracle. Never mutated.</param>
    /// <param name="initialState">
    /// The state the system was in before the trace's first call. Never mutated: Accordant's
    /// own state model only ever hands modifiers a clone (see <c>IState.Clone</c>), so this
    /// value is safe to reuse across repeated replays of the same or different traces.
    /// </param>
    /// <param name="trace">The trace to replay. Never mutated.</param>
    /// <param name="serializerOptions">
    /// Options used to deserialize each call's recorded request/response JSON into the
    /// matched operation's declared request/response type. Defaults to
    /// <see cref="ReplayJsonOptions.CreateDefault"/> when omitted.
    /// </param>
    /// <returns>A structured report of the whole replay; see <see cref="TraceReplayResult"/>.</returns>
    public static TraceReplayResult Replay<TState>(
        Spec<TState> spec,
        TState initialState,
        RecordedTrace trace,
        JsonSerializerOptions? serializerOptions = null)
        where TState : class, IState
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(trace);

        var options = serializerOptions ?? ReplayJsonOptions.CreateDefault();

        if (trace.SchemaVersion != RecordedTrace.CurrentSchemaVersion)
        {
            return new TraceReplayResult(
                trace.TraceId,
                TraceReplayStatus.UnsupportedSchemaVersion,
                $"Trace schema version {trace.SchemaVersion} is not supported; this library " +
                    $"only replays schema version {RecordedTrace.CurrentSchemaVersion}.",
                steps: Array.Empty<ReplayStepResult>(),
                finalStateProfile: null);
        }

        if (!TryValidateCallOrder(trace.Calls, out var orderErrorMessage))
        {
            return new TraceReplayResult(
                trace.TraceId,
                TraceReplayStatus.InvalidTraceStructure,
                orderErrorMessage,
                steps: Array.Empty<ReplayStepResult>(),
                finalStateProfile: null);
        }

        var steps = new List<ReplayStepResult>(trace.Calls.Count);
        var stateProfile = new StateProfile(initialState);
        var lastReliableStateProfile = stateProfile;

        foreach (var call in trace.Calls)
        {
            IOperation operation;

            try
            {
                operation = spec.GetOperation(call.OperationName);
            }
            catch (SpecException ex)
            {
                steps.Add(ReplayStepResult.OperationNotModeled(call.CallId, call.OperationName, ex.Message));
                break;
            }

            if (call.Error is not null)
            {
                steps.Add(ReplayStepResult.ExecutionError(call.CallId, call.OperationName, call.Error));
                break;
            }

            object? request;

            try
            {
                request = JsonSerializer.Deserialize(call.Request, operation.RequestType, options);
            }
            catch (JsonException ex)
            {
                steps.Add(ReplayStepResult.RequestDeserializationFailed(
                    call.CallId,
                    call.OperationName,
                    $"Request JSON did not deserialize into '{operation.RequestType}': {ex.Message}"));
                break;
            }

            object? response;

            try
            {
                // A recorded call has exactly one of Response/Error (enforced by
                // RecordedCall's constructor), and Error was already ruled out above.
                response = JsonSerializer.Deserialize(call.Response!.Value, operation.ResponseType, options);
            }
            catch (JsonException ex)
            {
                steps.Add(ReplayStepResult.ResponseDeserializationFailed(
                    call.CallId,
                    call.OperationName,
                    $"Response JSON did not deserialize into '{operation.ResponseType}': {ex.Message}"));
                break;
            }

            var (isValid, message, nextStateProfile) = spec.Allows(operation, request, response, stateProfile);

            if (!isValid)
            {
                steps.Add(ReplayStepResult.ModelViolation(call.CallId, call.OperationName, message));
                break;
            }

            steps.Add(ReplayStepResult.Conforming(call.CallId, call.OperationName));
            stateProfile = nextStateProfile;
            lastReliableStateProfile = nextStateProfile;
        }

        var fullyConforming =
            steps.Count == trace.Calls.Count &&
            steps.All(step => step.Outcome == ReplayStepOutcome.Conforming);

        return new TraceReplayResult(
            trace.TraceId,
            fullyConforming ? TraceReplayStatus.Conforming : TraceReplayStatus.Stopped,
            message: null,
            steps,
            lastReliableStateProfile);
    }

    /// <summary>
    /// Loads a trace from <paramref name="tracePath"/> (via <see cref="TraceStore.LoadAsync"/>)
    /// and replays it exactly as <see cref="Replay{TState}"/> would.
    /// </summary>
    /// <typeparam name="TState">The spec's state type.</typeparam>
    /// <param name="spec">The spec to use as an oracle. Never mutated.</param>
    /// <param name="initialState">The state the system was in before the trace's first call. Never mutated.</param>
    /// <param name="tracePath">The path of a trace file previously written by <see cref="TraceStore.SaveAsync"/>.</param>
    /// <param name="serializerOptions">
    /// Options used to deserialize each call's recorded request/response JSON. Defaults to
    /// <see cref="ReplayJsonOptions.CreateDefault"/> when omitted.
    /// </param>
    public static async Task<TraceReplayResult> ReplayAsync<TState>(
        Spec<TState> spec,
        TState initialState,
        string tracePath,
        JsonSerializerOptions? serializerOptions = null)
        where TState : class, IState
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracePath);

        var trace = await TraceStore.LoadAsync(tracePath).ConfigureAwait(false);
        return Replay(spec, initialState, trace, serializerOptions);
    }

    /// <summary>
    /// Validates that every call's <see cref="RecordedCall.CallId"/> is positive and that
    /// call IDs strictly increase in trace order. <see cref="RecordedTrace"/> and
    /// <see cref="RecordedCall"/> already guarantee schema-level shape (e.g. exactly one of
    /// response/error), but neither enforces call ID ordering - a hand-authored or
    /// non-Specmine-recorded trace file could carry any integers there, so this is checked
    /// here rather than assumed.
    /// </summary>
    private static bool TryValidateCallOrder(IReadOnlyList<RecordedCall> calls, out string? errorMessage)
    {
        var previousCallId = 0;

        foreach (var call in calls)
        {
            if (call.CallId <= 0)
            {
                errorMessage = $"Call ID {call.CallId} for operation '{call.OperationName}' is not positive.";
                return false;
            }

            if (call.CallId <= previousCallId)
            {
                errorMessage =
                    $"Call ID {call.CallId} for operation '{call.OperationName}' is not strictly " +
                    $"greater than the previous call ID {previousCallId}.";
                return false;
            }

            previousCallId = call.CallId;
        }

        errorMessage = null;
        return true;
    }
}
