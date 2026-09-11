// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

using System.Runtime.ExceptionServices;
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
/// <b>Conservative stop.</b> A conforming or provisional match advances state. Any other
/// outcome - an unknown region, an out-of-scope assumption violation, a model violation, an
/// unmodeled operation, a recorded execution error, or a request/response deserialization
/// failure - stops replay
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
///
/// <para>
/// <b>Understanding markers.</b> <see cref="Specmine.Accordant.Understanding.Unknown{TResponse}"/>/<see cref="Specmine.Accordant.Understanding.Provisional"/>
/// are detected the same way any other caller would: by calling
/// <c>Spec&lt;TState&gt;.Allows</c> as usual and then reading <see cref="Understanding.LastEncounter"/>
/// immediately afterward - there is
/// no separate inspection pass. This replayer runs at whatever <see cref="Understanding.CurrentStrictness"/>
/// the caller has ambiently set (defaulting to <see cref="UnderstandingStrictness.Reject"/> if unset). If
/// a caller wraps a <see cref="Replay{TState}"/> call in <see cref="Understanding.UseStrictness"/> with
/// <see cref="UnderstandingStrictness.Strict"/>, an <see cref="UnknownRegionEncounteredException"/> or
/// <see cref="ProvisionalMatchEncounteredException"/> propagates out of <see cref="Replay{TState}"/>
/// rather than being converted into a step result - that is the point of strict mode: an
/// immediate, hard failure instead of a structured report. <see cref="AssumptionViolatedException"/>
/// is different: <see cref="Understanding.Assume"/> has no permissive mode, so it always throws, and this
/// replayer always catches it and reports <see cref="ReplayStepOutcome.OutOfScope"/> instead.
///
/// <para>
/// <b>Unwrapping Accordant's own wrapping.</b> Every Understanding marker throws from inside the
/// validator that <c>Spec&lt;TState&gt;.Allows</c> invokes while exploring the state graph, so
/// Accordant's own <see cref="StateGraph"/>/<see cref="SystemChecker"/> machinery catches it
/// first and re-throws it as an <see cref="InvalidSpecException"/> wrapping a
/// <see cref="StepFunctionApplicationException"/> wrapping our original exception - the same
/// thing that happens to any exception a model's <c>Apply</c> function throws by accident.
/// This replayer catches that wrapper, unwraps down to the original
/// <see cref="AssumptionViolatedException"/>/<see cref="UnknownRegionEncounteredException"/>/
/// <see cref="ProvisionalMatchEncounteredException"/>, and re-throws (or reports) the unwrapped
/// exception so callers never have to know about the wrapping - any other
/// <see cref="InvalidSpecException"/> (a real bug in the model) is left alone and propagates as-is.
/// </para>
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

            UnderstandingEncounter? encounter;
            bool isValid;
            string message;
            StateProfile nextStateProfile;

            Understanding.ClearLastEncounter();

            try
            {
                (isValid, message, nextStateProfile) = spec.Allows(operation, request, response, stateProfile);
            }
            catch (InvalidSpecException ex) when (TryUnwrapUnderstandingException<AssumptionViolatedException>(ex, out var assumptionViolation))
            {
                // Understanding.Assume has no permissive mode - it always throws when a request/state
                // falls outside what the model covers. Out of scope, not a model violation.
                steps.Add(ReplayStepResult.OutOfScope(call.CallId, call.OperationName, assumptionViolation));
                break;
            }
            catch (InvalidSpecException ex) when (
                TryUnwrapUnderstandingException<UnknownRegionEncounteredException>(ex, out var unknownException))
            {
                // Only fires when the caller has ambiently opted into UnderstandingStrictness.Strict;
                // re-throw the original marker exception, not Accordant's wrapper - see the
                // "Understanding markers"/"Unwrapping Accordant's own wrapping" remarks on this type.
                ExceptionDispatchInfo.Capture(unknownException).Throw();
                throw; // unreachable; satisfies flow analysis.
            }
            catch (InvalidSpecException ex) when (
                TryUnwrapUnderstandingException<ProvisionalMatchEncounteredException>(ex, out var provisionalException))
            {
                ExceptionDispatchInfo.Capture(provisionalException).Throw();
                throw; // unreachable; satisfies flow analysis.
            }

            encounter = Understanding.LastEncounter;

            if (encounter is { Kind: UnderstandingKind.Unknown })
            {
                // No response or state-transition claim was made for this call, regardless of
                // whether UnderstandingStrictness.Accept or .Reject made isValid true or false above -
                // stop conservatively rather than trust or reject on that basis.
                steps.Add(ReplayStepResult.Unknown(call.CallId, call.OperationName, encounter));
                break;
            }

            if (!isValid)
            {
                steps.Add(ReplayStepResult.ModelViolation(call.CallId, call.OperationName, message));
                break;
            }

            steps.Add(encounter is { Kind: UnderstandingKind.Provisional }
                ? ReplayStepResult.ProvisionalMatch(call.CallId, call.OperationName, encounter)
                : ReplayStepResult.Conforming(call.CallId, call.OperationName));
            stateProfile = nextStateProfile;
            lastReliableStateProfile = nextStateProfile;
        }

        var fullyMatched =
            steps.Count == trace.Calls.Count &&
            steps.All(step => step.Outcome is ReplayStepOutcome.Conforming or ReplayStepOutcome.ProvisionalMatch);
        var hasProvisionalMatch = steps.Any(step => step.Outcome == ReplayStepOutcome.ProvisionalMatch);

        return new TraceReplayResult(
            trace.TraceId,
            fullyMatched
                ? hasProvisionalMatch ? TraceReplayStatus.Provisional : TraceReplayStatus.Conforming
                : TraceReplayStatus.Stopped,
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
    /// <param name="tracePath">The path of a trace file previously written by one of the <see cref="TraceStore.SaveAsync(string, RecordedTrace)"/> overloads.</param>
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
    /// Unwraps an understanding marker exception from underneath Accordant's own step-function-failure
    /// wrapping. A marker exception thrown from inside a validator surfaces from
    /// <c>Spec&lt;TState&gt;.Allows</c> as an <see cref="InvalidSpecException"/> whose
    /// <see cref="Exception.InnerException"/> is a <see cref="StepFunctionApplicationException"/>
    /// whose own <see cref="Exception.InnerException"/> is the original exception - see the
    /// "Unwrapping Accordant's own wrapping" remark on this type.
    /// </summary>
    private static bool TryUnwrapUnderstandingException<TException>(
        InvalidSpecException exception,
        out TException understandingException)
        where TException : UnderstandingException
    {
        if (exception.InnerException is StepFunctionApplicationException { InnerException: TException inner })
        {
            understandingException = inner;
            return true;
        }

        understandingException = null!;
        return false;
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
