// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// The result of replaying one <see cref="Specmine.RecordedCall"/> from a trace.
/// </summary>
public sealed record ReplayStepResult
{
    /// <summary>
    /// The replayed call's <see cref="Specmine.RecordedCall.CallId"/>.
    /// </summary>
    public int CallId { get; }

    /// <summary>
    /// The replayed call's <see cref="Specmine.RecordedCall.OperationName"/>.
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// What happened when this call was replayed.
    /// </summary>
    public ReplayStepOutcome Outcome { get; }

    /// <summary>
    /// A human-readable explanation of <see cref="Outcome"/>. <c>null</c> for
    /// <see cref="ReplayStepOutcome.Conforming"/>, where no explanation is needed; populated
    /// for every other outcome (e.g. Accordant's own explanation for a
    /// <see cref="ReplayStepOutcome.ModelViolation"/>, or the deserialization exception's
    /// message for a deserialization failure).
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Stable understanding marker ID for <see cref="ReplayStepOutcome.ProvisionalMatch"/>,
    /// <see cref="ReplayStepOutcome.Unknown"/>, or <see cref="ReplayStepOutcome.OutOfScope"/>
    /// (the <see cref="UnderstandingException.Id"/> of the <see cref="AssumptionViolatedException"/>);
    /// otherwise <c>null</c>.
    /// </summary>
    public string? MarkerId { get; }

    /// <summary>
    /// Understanding marker kind when <see cref="MarkerId"/> is populated for a
    /// <see cref="ReplayStepOutcome.ProvisionalMatch"/> or <see cref="ReplayStepOutcome.Unknown"/>
    /// step; <c>null</c> for <see cref="ReplayStepOutcome.OutOfScope"/>, since an assumption
    /// violation isn't an <see cref="UnderstandingKind"/>.
    /// </summary>
    public UnderstandingKind? MarkerKind { get; }

    public ReplayStepResult(
        int callId,
        string operationName,
        ReplayStepOutcome outcome,
        string? message,
        string? markerId = null,
        UnderstandingKind? markerKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        CallId = callId;
        OperationName = operationName;
        Outcome = outcome;
        Message = message;
        MarkerId = markerId;
        MarkerKind = markerKind;
    }

    internal static ReplayStepResult Conforming(int callId, string operationName) =>
        new(callId, operationName, ReplayStepOutcome.Conforming, message: null);

    internal static ReplayStepResult ProvisionalMatch(
        int callId,
        string operationName,
        UnderstandingEncounter encounter) =>
        new(
            callId,
            operationName,
            ReplayStepOutcome.ProvisionalMatch,
            $"{encounter.Id}: {encounter.Detail}",
            encounter.Id,
            encounter.Kind);

    internal static ReplayStepResult Unknown(
        int callId,
        string operationName,
        UnderstandingEncounter encounter) =>
        new(
            callId,
            operationName,
            ReplayStepOutcome.Unknown,
            $"{encounter.Id}: {encounter.Detail}",
            encounter.Id,
            encounter.Kind);

    internal static ReplayStepResult OutOfScope(
        int callId,
        string operationName,
        AssumptionViolatedException exception) =>
        new(
            callId,
            operationName,
            ReplayStepOutcome.OutOfScope,
            $"{exception.Id}: {exception.Reason}",
            exception.Id);

    internal static ReplayStepResult ModelViolation(int callId, string operationName, string message) =>
        new(callId, operationName, ReplayStepOutcome.ModelViolation, message);

    internal static ReplayStepResult OperationNotModeled(int callId, string operationName, string message) =>
        new(callId, operationName, ReplayStepOutcome.OperationNotModeled, message);

    internal static ReplayStepResult ExecutionError(int callId, string operationName, RecordedError error) =>
        new(callId, operationName, ReplayStepOutcome.ExecutionError, $"{error.ExceptionType}: {error.Message}");

    internal static ReplayStepResult RequestDeserializationFailed(int callId, string operationName, string message) =>
        new(callId, operationName, ReplayStepOutcome.RequestDeserializationFailed, message);

    internal static ReplayStepResult ResponseDeserializationFailed(int callId, string operationName, string message) =>
        new(callId, operationName, ReplayStepOutcome.ResponseDeserializationFailed, message);
}
