// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The terminal status of a recorded trace.
/// </summary>
public enum TraceStatus
{
    /// <summary>
    /// Every call in the bounded experiment ran and produced its declared response.
    /// </summary>
    Completed,

    /// <summary>
    /// The experiment stopped early because a call's execution delegate failed to
    /// produce its declared response.
    /// </summary>
    Interrupted
}

/// <summary>
/// Captures the failure of an execution delegate that did not produce its declared
/// response: at least the runtime exception type and message are preserved.
/// </summary>
public sealed record RecordedError
{
    /// <summary>
    /// The full type name of the exception that was thrown.
    /// </summary>
    public string ExceptionType { get; }

    /// <summary>
    /// The exception's message.
    /// </summary>
    public string Message { get; }

    [JsonConstructor]
    public RecordedError(string exceptionType, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionType);
        ArgumentNullException.ThrowIfNull(message);

        ExceptionType = exceptionType;
        Message = message;
    }

    /// <summary>
    /// Creates a snapshot of a thrown exception.
    /// </summary>
    public static RecordedError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new RecordedError(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message);
    }
}

/// <summary>
/// A single recorded invocation of an operation within a trace. Exactly one of
/// <see cref="Response"/> or <see cref="Error"/> is present: the call either produced
/// its declared response, or its execution delegate failed to do so.
/// </summary>
public sealed record RecordedCall
{
    /// <summary>
    /// The stable, unique, 1-based ordinal of this call within its trace.
    /// </summary>
    public int CallId { get; }

    /// <summary>
    /// The name of the operation that was called.
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// A snapshot of the concrete request that was passed to the execution delegate.
    /// </summary>
    public JsonElement Request { get; }

    /// <summary>
    /// A snapshot of the concrete response the execution delegate produced. Present
    /// only when the call succeeded (i.e. <see cref="Error"/> is <c>null</c>).
    /// </summary>
    public JsonElement? Response { get; }

    /// <summary>
    /// The execution error. Present only when the execution delegate failed to
    /// produce its declared response (i.e. <see cref="Response"/> is <c>null</c>).
    /// </summary>
    public RecordedError? Error { get; }

    [JsonConstructor]
    public RecordedCall(
        int callId,
        string operationName,
        JsonElement request,
        JsonElement? response,
        RecordedError? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if ((response is null) == (error is null))
        {
            throw new ArgumentException(
                "A recorded call must have exactly one of a response or an error.",
                nameof(response));
        }

        CallId = callId;
        OperationName = operationName;
        Request = request;
        Response = response;
        Error = error;
    }
}

/// <summary>
/// A single-file, immutable snapshot of one bounded recording experiment: the ordered
/// list of operation calls made between a start and a completion (or interruption)
/// timestamp. Use <see cref="TraceRecorder"/> to produce one and <see cref="TraceStore"/>
/// to persist or reload it.
/// </summary>
public sealed record RecordedTrace
{
    /// <summary>
    /// The current schema version written by this library.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The schema version this trace was written with.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// A globally unique identifier for this trace.
    /// </summary>
    public Guid TraceId { get; }

    /// <summary>
    /// The UTC time the recording experiment started.
    /// </summary>
    public DateTime StartedAt { get; }

    /// <summary>
    /// The UTC time the recording experiment completed or was determined interrupted.
    /// </summary>
    public DateTime CompletedAt { get; }

    /// <summary>
    /// Whether the experiment ran to completion or was interrupted by an execution error.
    /// </summary>
    public TraceStatus Status { get; }

    /// <summary>
    /// The ordered list of calls made during the experiment.
    /// </summary>
    public IReadOnlyList<RecordedCall> Calls { get; }

    [JsonConstructor]
    public RecordedTrace(
        int schemaVersion,
        Guid traceId,
        DateTime startedAt,
        DateTime completedAt,
        TraceStatus status,
        IReadOnlyList<RecordedCall> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);

        if (startedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("StartedAt must be a UTC timestamp.", nameof(startedAt));
        }

        if (completedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("CompletedAt must be a UTC timestamp.", nameof(completedAt));
        }

        SchemaVersion = schemaVersion;
        TraceId = traceId;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Status = status;
        Calls = calls;
    }
}
