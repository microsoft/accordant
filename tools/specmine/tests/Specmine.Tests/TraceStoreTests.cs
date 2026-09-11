// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;
using NUnit.Framework;

[TestFixture]
public sealed class TraceStoreTests
{
    private sealed record SampleRequest(string Name, int Count);

    private sealed record SampleResponse(string Id);

    [Test]
    public async Task SaveAsync_ThenLoadAsync_ReloadsWithFullFidelity()
    {
        using var tracesDirectory = new TestTracesDirectory();

        var successRequest = JsonSerializer.SerializeToElement(new SampleRequest("widget", 3));
        var successResponse = JsonSerializer.SerializeToElement(new SampleResponse("abc-123"));
        var errorRequest = JsonSerializer.SerializeToElement(new SampleRequest("gadget", 5));

        var original = new RecordedTrace(
            RecordedTrace.CurrentSchemaVersion,
            Guid.NewGuid(),
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 12, 0, 1, DateTimeKind.Utc),
            TraceStatus.Interrupted,
            new List<RecordedCall>
            {
                new(1, "Create", successRequest, successResponse, error: null),
                new(2, "Create", errorRequest, response: null, new RecordedError("System.InvalidOperationException", "boom")),
            });

        var path = await TraceStore.SaveAsync(tracesDirectory.Path, original);
        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SchemaVersion, Is.EqualTo(original.SchemaVersion));
            Assert.That(reloaded.TraceId, Is.EqualTo(original.TraceId));
            Assert.That(reloaded.StartedAt, Is.EqualTo(original.StartedAt));
            Assert.That(reloaded.StartedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(reloaded.CompletedAt, Is.EqualTo(original.CompletedAt));
            Assert.That(reloaded.Status, Is.EqualTo(original.Status));
            Assert.That(reloaded.Calls, Has.Count.EqualTo(2));

            Assert.That(reloaded.Calls[0].CallId, Is.EqualTo(1));
            Assert.That(reloaded.Calls[0].OperationName, Is.EqualTo("Create"));
            Assert.That(JsonElement.DeepEquals(reloaded.Calls[0].Request, original.Calls[0].Request), Is.True);
            Assert.That(JsonElement.DeepEquals(reloaded.Calls[0].Response!.Value, original.Calls[0].Response!.Value), Is.True);
            Assert.That(reloaded.Calls[0].Error, Is.Null);

            Assert.That(reloaded.Calls[1].CallId, Is.EqualTo(2));
            Assert.That(JsonElement.DeepEquals(reloaded.Calls[1].Request, original.Calls[1].Request), Is.True);
            Assert.That(reloaded.Calls[1].Response, Is.Null);
            Assert.That(reloaded.Calls[1].Error, Is.EqualTo(original.Calls[1].Error));
        });
    }

    [Test]
    public async Task SaveAsync_WritesIndentedJsonWithLowercaseStatus()
    {
        using var tracesDirectory = new TestTracesDirectory();

        var trace = new RecordedTrace(
            RecordedTrace.CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            TraceStatus.Completed,
            Array.Empty<RecordedCall>());

        var path = await TraceStore.SaveAsync(tracesDirectory.Path, trace);
        var text = await File.ReadAllTextAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("\n"));
            Assert.That(text, Does.Contain("\"Status\": \"completed\""));
        });
    }

    [Test]
    public async Task SaveAsync_NeverOverwritesAnExistingTraceFile()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var traceId = Guid.NewGuid();

        var completed = new RecordedTrace(
            RecordedTrace.CurrentSchemaVersion,
            traceId,
            DateTime.UtcNow,
            DateTime.UtcNow,
            TraceStatus.Completed,
            Array.Empty<RecordedCall>());

        var path = await TraceStore.SaveAsync(tracesDirectory.Path, completed);
        var originalContent = await File.ReadAllTextAsync(path);

        // A second trace that happens to share the same trace ID must never clobber
        // the file already on disk, regardless of its own status.
        var conflicting = new RecordedTrace(
            RecordedTrace.CurrentSchemaVersion,
            traceId,
            DateTime.UtcNow,
            DateTime.UtcNow,
            TraceStatus.Interrupted,
            new List<RecordedCall>
            {
                new(1, "ShouldNotBePersisted", JsonSerializer.SerializeToElement(new { }), response: null,
                    new RecordedError("System.Exception", "should not overwrite")),
            });

        Assert.ThrowsAsync<InvalidOperationException>(
            () => TraceStore.SaveAsync(tracesDirectory.Path, conflicting));

        var contentAfterAttempt = await File.ReadAllTextAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(contentAfterAttempt, Is.EqualTo(originalContent));
            Assert.That(Directory.GetFiles(tracesDirectory.Path, "*.tmp"), Is.Empty);
            Assert.That(Directory.GetFiles(tracesDirectory.Path, "*.json"), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public async Task SaveAsync_WithName_WritesToNamedFile()
    {
        using var tracesDirectory = new TestTracesDirectory();

        var trace = new RecordedTrace(
            RecordedTrace.CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            TraceStatus.Completed,
            Array.Empty<RecordedCall>());

        var path = await TraceStore.SaveAsync(tracesDirectory.Path, trace, "HappyPath");

        Assert.That(path, Is.EqualTo(Path.Combine(tracesDirectory.Path, "HappyPath.json")));

        var reloaded = await TraceStore.LoadAsync(path);
        Assert.That(reloaded.TraceId, Is.EqualTo(trace.TraceId));
    }

    [Test]
    public async Task SaveAsync_WithName_OverwritesAPreviouslyNamedTrace()
    {
        using var tracesDirectory = new TestTracesDirectory();

        var first = new RecordedTrace(
            RecordedTrace.CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            TraceStatus.Completed,
            new List<RecordedCall>
            {
                new(1, "Create", JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), error: null),
            });

        var second = new RecordedTrace(
            RecordedTrace.CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            TraceStatus.Completed,
            Array.Empty<RecordedCall>());

        await TraceStore.SaveAsync(tracesDirectory.Path, first, "HappyPath");
        var path = await TraceStore.SaveAsync(tracesDirectory.Path, second, "HappyPath");

        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.TraceId, Is.EqualTo(second.TraceId));
            Assert.That(reloaded.Calls, Is.Empty);
            Assert.That(Directory.GetFiles(tracesDirectory.Path, "*.tmp"), Is.Empty);
            Assert.That(Directory.GetFiles(tracesDirectory.Path, "*.json"), Has.Length.EqualTo(1));
        });
    }
}
