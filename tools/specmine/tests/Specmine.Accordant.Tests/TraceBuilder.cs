// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant.Tests;

using System.Text.Json;

/// <summary>
/// Small helpers for building <see cref="RecordedTrace"/>/<see cref="RecordedCall"/> values
/// directly in tests, without needing a live <see cref="ITargetAdapter"/>/<see cref="ITargetSession"/>
/// or a real recording run for every scenario (some scenarios - a bad schema version, a
/// malformed request shape, out-of-order call IDs - describe traces a real recorder run
/// would never itself produce).
/// </summary>
internal static class TraceBuilder
{
    /// <summary>A call whose execution delegate produced a declared response.</summary>
    public static RecordedCall Call(int callId, string operationName, object request, object response) =>
        new(
            callId,
            operationName,
            JsonSerializer.SerializeToElement(request),
            JsonSerializer.SerializeToElement(response),
            error: null);

    /// <summary>
    /// A call whose request/response JSON is authored as raw text, for shapes that don't
    /// correspond to any real C# object (e.g. a different property naming convention, or a
    /// shape that will fail to deserialize into the operation's declared type).
    /// </summary>
    public static RecordedCall RawCall(int callId, string operationName, string requestJson, string responseJson) =>
        new(
            callId,
            operationName,
            ParseElement(requestJson),
            ParseElement(responseJson),
            error: null);

    /// <summary>A call whose execution delegate failed to produce its declared response.</summary>
    public static RecordedCall ErrorCall(
        int callId, string operationName, object request, string exceptionType, string message) =>
        new(
            callId,
            operationName,
            JsonSerializer.SerializeToElement(request),
            response: null,
            new RecordedError(exceptionType, message));

    public static RecordedTrace Trace(TraceStatus status, params RecordedCall[] calls) =>
        Trace(RecordedTrace.CurrentSchemaVersion, status, calls);

    public static RecordedTrace Trace(int schemaVersion, TraceStatus status, params RecordedCall[] calls) =>
        new(
            schemaVersion,
            Guid.NewGuid(),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc),
            status,
            calls);

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
