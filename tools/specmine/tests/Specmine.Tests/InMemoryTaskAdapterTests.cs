// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;
using NUnit.Framework;

/// <summary>
/// Exercises <see cref="InMemoryTaskAdapter"/>/<see cref="InMemoryTaskSession"/> as a proof
/// of the full adapter/session SDK contract end to end: adapter-owned settings
/// interpretation, session state and response-dependent chaining across calls within one
/// experiment, JSON Schema operation enumeration, unknown-operation and cancellation/
/// execution failures becoming interrupted traces without being swallowed, and explicit
/// session disposal that the recorder never performs on the caller's behalf.
/// </summary>
[TestFixture]
public sealed class InMemoryTaskAdapterTests
{
    [Test]
    public async Task ConnectAsync_InterpretsAdapterOwnedSettings_IdPrefixFlowsIntoGeneratedIds()
    {
        var adapter = new InMemoryTaskAdapter();
        var settings = JsonSerializer.SerializeToElement(new { idPrefix = "widget" });

        await using var session = await adapter.ConnectAsync(settings);
        var response = await session.ExecuteAsync(
            "CreateTask", JsonSerializer.SerializeToElement(new { Title = "first widget" }));

        Assert.That(response.GetProperty("Id").GetString(), Does.StartWith("widget-"));
    }

    [Test]
    public async Task ConnectAsync_WithoutSettingsProperty_UsesAdapterDefault()
    {
        var adapter = new InMemoryTaskAdapter();

        await using var session = await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));
        var response = await session.ExecuteAsync(
            "CreateTask", JsonSerializer.SerializeToElement(new { Title = "unprefixed" }));

        Assert.That(response.GetProperty("Id").GetString(), Does.StartWith("task-"));
    }

    [Test]
    public void ConnectAsync_NonStringIdPrefix_Throws()
    {
        var adapter = new InMemoryTaskAdapter();
        var settings = JsonSerializer.SerializeToElement(new { idPrefix = 42 });

        Assert.ThrowsAsync<ArgumentException>(() => adapter.ConnectAsync(settings));
    }

    [Test]
    public async Task Operations_EnumerationReturnsNamedJsonSchemas()
    {
        var adapter = new InMemoryTaskAdapter();
        await using var session = await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));

        Assert.Multiple(() =>
        {
            Assert.That(session.Operations.Select(o => o.Name), Is.EquivalentTo(new[] { "CreateTask", "CompleteTask" }));

            foreach (var operation in session.Operations)
            {
                Assert.That(operation.RequestSchema.ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(operation.ResponseSchema.ValueKind, Is.EqualTo(JsonValueKind.Object));
            }

            var createTask = session.Operations.Single(o => o.Name == "CreateTask");
            Assert.That(createTask.RequestSchema.GetProperty("properties").TryGetProperty("Title", out _), Is.True);
            Assert.That(createTask.ResponseSchema.GetProperty("properties").TryGetProperty("Id", out _), Is.True);

            var completeTask = session.Operations.Single(o => o.Name == "CompleteTask");
            Assert.That(completeTask.RequestSchema.GetProperty("properties").TryGetProperty("Id", out _), Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_UnknownOperation_Throws()
    {
        var adapter = new InMemoryTaskAdapter();
        await using var session = await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));

        var thrown = Assert.ThrowsAsync<UnknownOperationException>(() =>
            session.ExecuteAsync("DeleteTask", JsonSerializer.SerializeToElement(new { })));

        Assert.That(thrown!.OperationName, Is.EqualTo("DeleteTask"));
    }

    [Test]
    public void ExecuteAsync_UndefinedRequest_Throws()
    {
        var adapter = new InMemoryTaskAdapter();
        var session = adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { })).GetAwaiter().GetResult();

        Assert.Throws<ArgumentException>(() =>
            session.ExecuteAsync("CreateTask", default).GetAwaiter().GetResult());
    }

    [Test]
    public async Task TraceRecorder_SessionHoldsStateAcrossCalls_ResponseDerivedIdFlowsIntoLaterCall()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var adapter = new InMemoryTaskAdapter();
        await using var session = await adapter.ConnectAsync(
            JsonSerializer.SerializeToElement(new { idPrefix = "job" }));

        string? createdId = null;

        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
        {
            var created = await recordingTarget.ExecuteAsync(
                "CreateTask", JsonSerializer.SerializeToElement(new { Title = "write benchmark" }));

            // The task ID only exists because the session's own mutable dictionary
            // generated and held it - CompleteTask succeeding on it below proves the
            // session carried state from the first call to the second.
            createdId = created.GetProperty("Id").GetString();

            await recordingTarget.ExecuteAsync(
                "CompleteTask", JsonSerializer.SerializeToElement(new { Id = createdId }));
        });

        Assert.That(createdId, Does.StartWith("job-"));

        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(trace.Status, Is.EqualTo(TraceStatus.Completed));
            Assert.That(reloaded.Calls.Select(c => c.OperationName), Is.EqualTo(new[] { "CreateTask", "CompleteTask" }));
            Assert.That(reloaded.Calls.Select(c => c.CallId), Is.EqualTo(new[] { 1, 2 }));

            var createdResponse = reloaded.Calls[0].Response!.Value;
            Assert.That(createdResponse.GetProperty("Id").GetString(), Is.EqualTo(createdId));
            Assert.That(createdResponse.GetProperty("Status").GetString(), Is.EqualTo("open"));

            Assert.That(reloaded.Calls[1].Request.GetProperty("Id").GetString(), Is.EqualTo(createdId));

            var completedResponse = reloaded.Calls[1].Response!.Value;
            Assert.That(completedResponse.GetProperty("Id").GetString(), Is.EqualTo(createdId));
            Assert.That(completedResponse.GetProperty("Status").GetString(), Is.EqualTo("completed"));
        });
    }

    [Test]
    public async Task TraceRecorder_TypedConvenienceExtension_SerializesAndDeserializesOverTheJsonBoundary()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var adapter = new InMemoryTaskAdapter();
        await using var session = await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));

        TaskResponse? created = null;

        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
        {
            created = await recordingTarget.ExecuteAsync<CreateTaskRequest, TaskResponse>(
                "CreateTask", new CreateTaskRequest("typed request"));

            await recordingTarget.ExecuteAsync<TaskIdRequest, TaskResponse>(
                "CompleteTask", new TaskIdRequest(created.Id));
        });

        Assert.That(created, Is.Not.Null);

        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(trace.Status, Is.EqualTo(TraceStatus.Completed));

            // The typed extension is only a convenience layer: the persisted trace still
            // holds the same portable JSON a raw-JSON caller would have produced.
            Assert.That(reloaded.Calls[0].Request.GetProperty("Title").GetString(), Is.EqualTo("typed request"));
            Assert.That(reloaded.Calls[1].Response!.Value.GetProperty("Status").GetString(), Is.EqualTo("completed"));
        });
    }

    [Test]
    public async Task TraceRecorder_UnknownOperation_BecomesInterruptedTraceWithoutBeingSwallowed()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var adapter = new InMemoryTaskAdapter();
        await using var session = await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));

        var thrown = Assert.ThrowsAsync<UnknownOperationException>(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
            {
                await recordingTarget.ExecuteAsync(
                    "CreateTask", JsonSerializer.SerializeToElement(new { Title = "will not be reached again" }));

                await recordingTarget.ExecuteAsync("DeleteTask", JsonSerializer.SerializeToElement(new { }));
            }));

        Assert.That(thrown!.OperationName, Is.EqualTo("DeleteTask"));

        var path = Directory.GetFiles(tracesDirectory.Path, "*.json").Single();
        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(TraceStatus.Interrupted));
            Assert.That(reloaded.Calls, Has.Count.EqualTo(2));
            Assert.That(reloaded.Calls[0].Error, Is.Null);
            Assert.That(reloaded.Calls[1].Response, Is.Null);
            Assert.That(reloaded.Calls[1].Error!.ExceptionType, Is.EqualTo(typeof(UnknownOperationException).FullName));
        });
    }

    [Test]
    public async Task TraceRecorder_ExecutionFailure_BecomesInterruptedTraceWithoutBeingSwallowed()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var adapter = new InMemoryTaskAdapter();
        await using var session = await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
            {
                // No task with this ID was ever created, so the session's own domain logic
                // - not any SDK plumbing - rejects it.
                await recordingTarget.ExecuteAsync(
                    "CompleteTask", JsonSerializer.SerializeToElement(new { Id = "does-not-exist" }));
            }));

        Assert.That(thrown!.Message, Does.Contain("does-not-exist"));

        var path = Directory.GetFiles(tracesDirectory.Path, "*.json").Single();
        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(TraceStatus.Interrupted));
            Assert.That(reloaded.Calls, Has.Count.EqualTo(1));
            Assert.That(reloaded.Calls[0].Error!.ExceptionType, Is.EqualTo(typeof(InvalidOperationException).FullName));
        });
    }

    [Test]
    public async Task TraceRecorder_CancellationDuringExecution_BecomesInterruptedTraceWithoutBeingSwallowed()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var adapter = new InMemoryTaskAdapter();
        await using var session = await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));
        using var alreadyCanceled = new CancellationTokenSource();
        alreadyCanceled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
            {
                await recordingTarget.ExecuteAsync(
                    "CreateTask",
                    JsonSerializer.SerializeToElement(new { Title = "canceled before completion" }),
                    alreadyCanceled.Token);
            }));

        var path = Directory.GetFiles(tracesDirectory.Path, "*.json").Single();
        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(TraceStatus.Interrupted));
            Assert.That(reloaded.Calls, Has.Count.EqualTo(1));
            Assert.That(reloaded.Calls[0].Response, Is.Null);
            Assert.That(reloaded.Calls[0].Error, Is.Not.Null);
        });
    }

    [Test]
    public async Task SessionDisposal_IsExplicit_TraceRecorderNeverDisposesTheUnderlyingSession()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var adapter = new InMemoryTaskAdapter();
        var session = (InMemoryTaskSession)await adapter.ConnectAsync(JsonSerializer.SerializeToElement(new { }));

        await TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
        {
            await recordingTarget.ExecuteAsync(
                "CreateTask", JsonSerializer.SerializeToElement(new { Title = "still alive after RunAsync" }));
        });

        Assert.That(session.Disposed, Is.False, "TraceRecorder.RunAsync must not dispose the session it was given.");

        await session.DisposeAsync();

        Assert.That(session.Disposed, Is.True, "The caller's own DisposeAsync call must reach the underlying session.");
    }
}
