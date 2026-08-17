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

    public ReplayStepResult(int callId, string operationName, ReplayStepOutcome outcome, string? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        CallId = callId;
        OperationName = operationName;
        Outcome = outcome;
        Message = message;
    }

    internal static ReplayStepResult Conforming(int callId, string operationName) =>
        new(callId, operationName, ReplayStepOutcome.Conforming, message: null);

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
