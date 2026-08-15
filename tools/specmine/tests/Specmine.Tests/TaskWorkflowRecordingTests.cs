// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using TaskWorkflow.Api;

/// <summary>
/// Records a bounded sequence of calls against the in-memory TaskWorkflow benchmark and
/// verifies the persisted trace preserves call order and the response-dependent task ID
/// across calls.
/// </summary>
[TestFixture]
public sealed class TaskWorkflowRecordingTests
{
    // A marker request for the parameterless reset endpoint - there is no reset contract
    // type in TaskWorkflow.Api since the endpoint takes no body, but ExecuteAsync always
    // records a concrete request snapshot.
    private sealed record ResetRequest;

    // The route-bound ID a Complete/Cancel call needs; TaskWorkflow.Api addresses these
    // operations via the URL rather than a request body contract type.
    private sealed record TaskIdRequest(string Id);

    // A test-defined operation response capturing status code and parsed body, per the
    // scope of this slice: Specmine itself has no generic transport-metadata abstraction.
    private sealed record TaskCallResponse(int StatusCode, TaskResponse? Task, ErrorResponse? Error);

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task RecordsResetCreateAndTwoCompletesWithTheServerGeneratedId()
    {
        using var tracesDirectory = new TestTracesDirectory();
        string? taskId = null;

        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory.Path, async recorder =>
        {
            await recorder.ExecuteAsync("ResetBenchmark", new ResetRequest(), async _ =>
            {
                var response = await _client.PostAsync("/__test/reset", content: null);
                return new TaskCallResponse((int)response.StatusCode, Task: null, Error: null);
            });

            var created = await recorder.ExecuteAsync(
                "CreateTask",
                new CreateTaskRequest("write benchmark"),
                async request =>
                {
                    var response = await _client.PostAsJsonAsync("/tasks", request);
                    var task = response.IsSuccessStatusCode
                        ? await response.Content.ReadFromJsonAsync<TaskResponse>()
                        : null;
                    return new TaskCallResponse((int)response.StatusCode, task, Error: null);
                });

            // The task ID is only known once CreateTask's real response comes back - this
            // is exactly the response-dependent chaining the recorder is meant to support.
            taskId = created.Task!.Id;

            for (var i = 0; i < 2; i++)
            {
                await recorder.ExecuteAsync(
                    "CompleteTask",
                    new TaskIdRequest(taskId),
                    async request =>
                    {
                        var response = await _client.PostAsync($"/tasks/{request.Id}/complete", content: null);
                        var task = response.IsSuccessStatusCode
                            ? await response.Content.ReadFromJsonAsync<TaskResponse>()
                            : null;
                        return new TaskCallResponse((int)response.StatusCode, task, Error: null);
                    });
            }
        });

        Assert.That(taskId, Is.Not.Null.And.Not.Empty);
        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(trace.Status, Is.EqualTo(TraceStatus.Completed));
            Assert.That(reloaded.Status, Is.EqualTo(TraceStatus.Completed));

            Assert.That(reloaded.Calls.Select(c => c.OperationName),
                Is.EqualTo(new[] { "ResetBenchmark", "CreateTask", "CompleteTask", "CompleteTask" }));
            Assert.That(reloaded.Calls.Select(c => c.CallId), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(reloaded.Calls.All(c => c.Error is null), Is.True);

            var createdTaskId = reloaded.Calls[1].Response!.Value.GetProperty("Task").GetProperty("Id").GetString();
            Assert.That(createdTaskId, Is.EqualTo(taskId));

            foreach (var completeCall in reloaded.Calls.Skip(2))
            {
                Assert.That(completeCall.Request.GetProperty("Id").GetString(), Is.EqualTo(taskId));

                var completedTask = completeCall.Response!.Value.GetProperty("Task");
                Assert.That(completedTask.GetProperty("Id").GetString(), Is.EqualTo(taskId));
                Assert.That(completedTask.GetProperty("Status").GetString(), Is.EqualTo("completed"));
            }
        });
    }
}
