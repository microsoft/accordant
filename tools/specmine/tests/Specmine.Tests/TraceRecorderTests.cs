// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class TraceRecorderTests
{
    private sealed record EchoRequest(string Text);

    private sealed record EchoResponse(string Text);

    private sealed record CreateRequest(string Name);

    private sealed record CreateResponse(string Id);

    private sealed record CompleteRequest(string Id);

    private sealed record CompleteResponse(string Id, string Status);

    [Test]
    public async Task RunAsync_CompletedExperiment_PersistsCallsInOrderWithSnapshots()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var echo = new ExecutableOperation<EchoRequest, EchoResponse>(
            "Echo",
            request => Task.FromResult(new EchoResponse(request.Text)));

        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory.Path, async recorder =>
        {
            var first = await recorder.ExecuteAsync(echo, new EchoRequest("a"));

            await recorder.ExecuteAsync(echo, new EchoRequest(first.Text + "b"));
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
        var create = new ExecutableOperation<CreateRequest, CreateResponse>(
            "Create",
            _ => Task.FromResult(new CreateResponse(Id: "generated-42")));
        var complete = new ExecutableOperation<CompleteRequest, CompleteResponse>(
            "Complete",
            request => Task.FromResult(new CompleteResponse(request.Id, "done")));

        var (trace, _) = await TraceRecorder.RunAsync(tracesDirectory.Path, async recorder =>
        {
            // Execute Create, inspect the returned ID, then execute Complete using that ID -
            // the pattern this API is meant to support naturally.
            var created = await recorder.ExecuteAsync(create, new CreateRequest("widget"));

            capturedId = created.Id;

            await recorder.ExecuteAsync(complete, new CompleteRequest(created.Id));
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
        var echo = new ExecutableOperation<EchoRequest, EchoResponse>(
            "Echo",
            request => Task.FromResult(new EchoResponse(request.Text)));
        var failingEcho = new ExecutableOperation<EchoRequest, EchoResponse>(
            "Echo",
            _ => throw new InvalidOperationException("boom"));

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, async recorder =>
            {
                await recorder.ExecuteAsync(echo, new EchoRequest("a"));

                await recorder.ExecuteAsync(failingEcho, new EchoRequest("b"));
            }));

        Assert.That(thrown!.Message, Is.EqualTo("boom"));

        var files = Directory.GetFiles(tracesDirectory.Path, "*.json");
        Assert.That(files, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_ExecutionDelegateThrows_InterruptedTraceHasSuccessCallAndErrorCall()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var echo = new ExecutableOperation<EchoRequest, EchoResponse>(
            "Echo",
            request => Task.FromResult(new EchoResponse(request.Text)));
        var failingEcho = new ExecutableOperation<EchoRequest, EchoResponse>(
            "Echo",
            _ => throw new InvalidOperationException("boom"));

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            TraceRecorder.RunAsync(tracesDirectory.Path, async recorder =>
            {
                await recorder.ExecuteAsync(echo, new EchoRequest("a"));

                await recorder.ExecuteAsync(failingEcho, new EchoRequest("b"));
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
}
