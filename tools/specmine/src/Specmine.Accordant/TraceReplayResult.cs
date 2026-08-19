// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

using Microsoft.Accordant;

/// <summary>
/// The result of replaying a whole <see cref="Specmine.RecordedTrace"/> against an
/// Accordant <c>Spec&lt;TState&gt;</c>.
/// </summary>
public sealed record TraceReplayResult
{
    /// <summary>
    /// The replayed trace's <see cref="Specmine.RecordedTrace.TraceId"/>.
    /// </summary>
    public Guid TraceId { get; }

    /// <summary>
    /// The overall status of the replay.
    /// </summary>
    public TraceReplayStatus Status { get; }

    /// <summary>
    /// A human-readable explanation of <see cref="Status"/> when it describes a whole-trace
    /// problem rather than a per-step one (<see cref="TraceReplayStatus.UnsupportedSchemaVersion"/>
    /// or <see cref="TraceReplayStatus.InvalidTraceStructure"/>); <c>null</c> otherwise, since
    /// <see cref="Steps"/> already carries a per-step explanation for
    /// <see cref="TraceReplayStatus.Conforming"/> and <see cref="TraceReplayStatus.Stopped"/>.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// The per-call replay results, in trace order. Empty when <see cref="Status"/> is
    /// <see cref="TraceReplayStatus.UnsupportedSchemaVersion"/> or
    /// <see cref="TraceReplayStatus.InvalidTraceStructure"/>, since replay could not safely
    /// begin. Otherwise holds one entry per call that was actually replayed - which, for a
    /// <see cref="TraceReplayStatus.Stopped"/> result, is a strict prefix of the trace's
    /// full call list.
    /// </summary>
    public IReadOnlyList<ReplayStepResult> Steps { get; }

    /// <summary>
    /// The <see cref="StateProfile"/> after the last call that replayed as
    /// <see cref="ReplayStepOutcome.Conforming"/> or
    /// <see cref="ReplayStepOutcome.ProvisionalMatch"/> - the most advanced point at which
    /// the state is still reliably known. This is the initial state profile, unmodified,
    /// when zero calls matched (including an empty trace). <c>null</c> when <see cref="Status"/>
    /// is <see cref="TraceReplayStatus.UnsupportedSchemaVersion"/> or
    /// <see cref="TraceReplayStatus.InvalidTraceStructure"/>, since replay never validly
    /// began and no state can be considered reliable.
    /// </summary>
    public StateProfile? FinalStateProfile { get; }

    public int AcceptedMatchCount => Steps.Count(step => step.Outcome == ReplayStepOutcome.Conforming);

    public int ProvisionalMatchCount => Steps.Count(step => step.Outcome == ReplayStepOutcome.ProvisionalMatch);

    public int UnknownCount => Steps.Count(step => step.Outcome == ReplayStepOutcome.Unknown);

    public int ViolationCount => Steps.Count(step => step.Outcome == ReplayStepOutcome.ModelViolation);

    public TraceReplayResult(
        Guid traceId,
        TraceReplayStatus status,
        string? message,
        IReadOnlyList<ReplayStepResult> steps,
        StateProfile? finalStateProfile)
    {
        ArgumentNullException.ThrowIfNull(steps);

        TraceId = traceId;
        Status = status;
        Message = message;
        Steps = steps;
        FinalStateProfile = finalStateProfile;
    }
}
