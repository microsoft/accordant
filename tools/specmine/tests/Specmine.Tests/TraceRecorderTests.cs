// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;
using NUnit.Framework;

[TestFixture]
public sealed class TraceRecorderTests
{
    // A minimal test-only ITargetSession whose operations are supplied as plain
    // JSON-in/JSON-out delegates, so each test can define exactly the handful of
    // operations its scenario needs without standing up a real target.
    private sealed class DelegateTargetSession : ITargetSession
    {
        private readonly Dictionary<string, Func<JsonElement, JsonElement>> _handlers;

        public IReadOnlyList<OperationDefinition> Operations { get; }

        public DelegateTargetSession(Dictionary<string, Func<JsonElement, JsonElement>> handlers)
        {
            _handlers = handlers;
            Operations = handlers.Keys
                .Select(name => new OperationDefinition(
                    name,
                    JsonDocument.Parse("true").RootElement,
                    JsonDocument.Parse("true").RootElement))
                .ToList();
        }

        public Task<JsonElement> ExecuteAsync(
            string operationName, JsonElement request, CancellationToken cancellationToken = default)
        {
            if (!_handlers.TryGetValue(operationName, out var handler))
            {
                throw new UnknownOperationException(operationName);
            }

            if (request.ValueKind == JsonValueKind.Undefined)
            {
                throw new ArgumentException("Request must be a valid JSON value.", nameof(request));
            }

            return Task.FromResult(handler(request));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(new { Text = value });

    [Test]
    public async Task RunAsync_CompletedExperiment_PersistsCallsInOrderWithSnapshots()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var session = new DelegateTargetSession(new Dictionary<string, Func<JsonElement, JsonElement>>
        {
            ["Echo"] = request => request,
        });

        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
        {
            var first = await recordingTarget.ExecuteAsync("Echo", Text("a"));

            await recordingTarget.ExecuteAsync("Echo", Text(first.GetProperty("Text").GetString() + "b"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(trace.SchemaVersion, Is.EqualTo(RecordedTrace.CurrentSchemaVersion));
            Assert.That(trace.TraceId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(trace.Status, Is.EqualTo(TraceStatus.Completed));
            Assert.That(trace.StartedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(trace.CompletedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(trace.CompletedAt, Is.GreaterThanOrEqualTo(trace.StartedAt));
            Assert.That(trace.Calls, Has.Count.EqualTo(2));
            Assert.That(trace.Calls.Select(c => c.CallId), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(trace.Calls.Select(c => c.OperationName), Is.EqualTo(new[] { "Echo", "Echo" }));
            Assert.That(trace.Calls[0].Error, Is.Null);
            Assert.That(trace.Calls[0].Response!.Value.GetProperty("Text").GetString(), Is.EqualTo("a"));
            Assert.That(trace.Calls[1].Request.GetProperty("Text").GetString(), Is.EqualTo("ab"));
            Assert.That(trace.Calls[1].Response!.Value.GetProperty("Text").GetString(), Is.EqualTo("ab"));
        });
    }

    [Test]
    public async Task ExecuteAsync_AllowsResponseDependentChaining()
    {
        using var tracesDirectory = new TestTracesDirectory();
        string? capturedId = null;
        var session = new DelegateTargetSession(new Dictionary<string, Func<JsonElement, JsonElement>>
        {
            ["Create"] = _ => JsonSerializer.SerializeToElement(new { Id = "generated-42" }),
            ["Complete"] = request => JsonSerializer.SerializeToElement(
                new { Id = request.GetProperty("Id").GetString(), Status = "done" }),
        });

        var (trace, _) = await TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
        {
            // Execute Create, inspect the returned ID, then execute Complete using that ID -
            // the pattern this API is meant to support naturally.
            var created = await recordingTarget.ExecuteAsync(
                "Create", JsonSerializer.SerializeToElement(new { Name = "widget" }));

            capturedId = created.GetProperty("Id").GetString();

            await recordingTarget.ExecuteAsync("Complete", JsonSerializer.SerializeToElement(new { Id = capturedId }));
        });

        Assert.Multiple(() =>
        {
            Assert.That(capturedId, Is.EqualTo("generated-42"));
            Assert.That(trace.Calls[1].Request.GetProperty("Id").GetString(), Is.EqualTo("generated-42"));
            Assert.That(trace.Calls[1].Response!.Value.GetProperty("Id").GetString(), Is.EqualTo("generated-42"));
        });
    }

    [Test]
    public void RunAsync_ExecutionDelegateThrows_RethrowsAndPersistsInterruptedTraceWithError()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var session = new DelegateTargetSession(new Dictionary<string, Func<JsonElement, JsonElement>>
        {
            ["Echo"] = request => request,
            ["FailingEcho"] = _ => throw new InvalidOperationException("boom"),
        });

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
            {
                await recordingTarget.ExecuteAsync("Echo", Text("a"));

                await recordingTarget.ExecuteAsync("FailingEcho", Text("b"));
            }));

        Assert.That(thrown!.Message, Is.EqualTo("boom"));

        var files = Directory.GetFiles(tracesDirectory.Path, "*.json");
        Assert.That(files, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_ExecutionDelegateThrows_InterruptedTraceHasSuccessCallAndErrorCall()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var session = new DelegateTargetSession(new Dictionary<string, Func<JsonElement, JsonElement>>
        {
            ["Echo"] = request => request,
            ["FailingEcho"] = _ => throw new InvalidOperationException("boom"),
        });

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
            {
                await recordingTarget.ExecuteAsync("Echo", Text("a"));

                await recordingTarget.ExecuteAsync("FailingEcho", Text("b"));
            }));

        var path = Directory.GetFiles(tracesDirectory.Path, "*.json").Single();
        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(TraceStatus.Interrupted));
            Assert.That(reloaded.Calls, Has.Count.EqualTo(2));
            Assert.That(reloaded.Calls[0].Error, Is.Null);
            Assert.That(reloaded.Calls[0].Response, Is.Not.Null);
            Assert.That(reloaded.Calls[1].Response, Is.Null);
            Assert.That(reloaded.Calls[1].Error, Is.Not.Null);
            Assert.That(reloaded.Calls[1].Error!.ExceptionType, Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(reloaded.Calls[1].Error!.Message, Is.EqualTo("boom"));
        });
    }

    [Test]
    public void RunAsync_NeverDisposesTheTargetSessionItWasGiven()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var session = new DisposeTrackingSession();

        Assert.DoesNotThrowAsync(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
            {
                await recordingTarget.ExecuteAsync("Echo", Text("a"));
            }));

        Assert.That(session.Disposed, Is.False);
    }

    private sealed class DisposeTrackingSession : ITargetSession
    {
        public bool Disposed { get; private set; }

        public IReadOnlyList<OperationDefinition> Operations { get; } = new[]
        {
            new OperationDefinition("Echo", JsonDocument.Parse("true").RootElement, JsonDocument.Parse("true").RootElement),
        };

        public Task<JsonElement> ExecuteAsync(
            string operationName, JsonElement request, CancellationToken cancellationToken = default) =>
            Task.FromResult(request);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
