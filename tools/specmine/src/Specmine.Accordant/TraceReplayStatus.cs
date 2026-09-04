// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// The overall status of replaying a whole <see cref="Specmine.RecordedTrace"/>.
/// </summary>
public enum TraceReplayStatus
{
    /// <summary>
    /// Every call in the trace replayed as <see cref="ReplayStepOutcome.Conforming"/>.
    /// </summary>
    Conforming,

    /// <summary>
    /// Every call matched an executable expectation, and at least one matched expectation
    /// was marked provisional.
    /// </summary>
    Provisional,

    /// <summary>
    /// Replay stopped before reaching the end of the trace because a call's outcome left
    /// the state no longer reliably known: an unknown region, an out-of-scope assumption
    /// violation, a model violation, an unmodeled operation, an execution error, or a
    /// request/response deserialization failure. See the last entry
    /// in <see cref="TraceReplayResult.Steps"/> for which one and why. This is a
    /// deliberately conservative choice - see the <c>Specmine.Accordant</c> README.
    /// </summary>
    Stopped,

    /// <summary>
    /// The trace's <see cref="Specmine.RecordedTrace.SchemaVersion"/> is not one this
    /// library understands how to replay. No calls were replayed.
    /// </summary>
    UnsupportedSchemaVersion,

    /// <summary>
    /// The trace's calls are not validly ordered (a non-positive, duplicate, or
    /// out-of-order <see cref="Specmine.RecordedCall.CallId"/> was found) and replay could
    /// not safely begin. No calls were replayed.
    /// </summary>
    InvalidTraceStructure
}
