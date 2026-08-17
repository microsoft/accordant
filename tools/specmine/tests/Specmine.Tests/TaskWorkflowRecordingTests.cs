// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using TaskWorkflow.Api;

/// <summary>
/// Records a bounded sequence of calls against the in-memory TaskWorkflow benchmark and
/// verifies the persisted trace preserves call order and the response-dependent task ID
/// across calls. The benchmark is exposed as a test-local HTTP-backed
/// <see cref="ITargetSession"/> - a stand-in for the built-in OpenAPI adapter, which is a
/// later slice.
/// </summary>
[TestFixture]
public sealed class TaskWorkflowRecordingTests
{
    // A marker request for the parameterless reset endpoint - there is no reset contract
    // type in TaskWorkflow.Api since the endpoint takes no body, but every recorded call
    // needs a concrete request snapshot.
    private sealed record ResetRequest;

    // The route-bound ID a Complete/Cancel call needs; TaskWorkflow.Api addresses these
    // operations via the URL rather than a request body contract type.
    private sealed record TaskIdRequest(string Id);

    // A test-defined operation response capturing status code and parsed body, per the
    // scope of this slice: Specmine itself has no generic transport-metadata abstraction.
    private sealed record TaskCallResponse(int StatusCode, TaskResponse? Task, ErrorResponse? Error);

    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task RecordsResetCreateAndTwoCompletesWithTheServerGeneratedId()
    {
        using var tracesDirectory = new TestTracesDirectory();
        string? taskId = null;

        await using var session = new HttpTaskWorkflowSession(_factory.CreateClient());

        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
        {
            await recordingTarget.ExecuteAsync<ResetRequest, TaskCallResponse>("ResetBenchmark", new ResetRequest());

            var created = await recordingTarget.ExecuteAsync<CreateTaskRequest, TaskCallResponse>(
                "CreateTask", new CreateTaskRequest("write benchmark"));

            // The task ID is only known once CreateTask's real response comes back - this
            // is exactly the response-dependent chaining the recorder is meant to support.
            taskId = created.Task!.Id;

            for (var i = 0; i < 2; i++)
            {
                await recordingTarget.ExecuteAsync<TaskIdRequest, TaskCallResponse>(
                    "CompleteTask", new TaskIdRequest(taskId));
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

    /// <summary>
    /// A test-local <see cref="ITargetSession"/> that talks to the TaskWorkflow benchmark
    /// over HTTP. It owns the <see cref="HttpClient"/> it is given (created fresh per
    /// session by the test) and disposes it in <see cref="DisposeAsync"/>, exactly as the
    /// session lifetime contract requires.
    /// </summary>
    private sealed class HttpTaskWorkflowSession : TargetSessionBase
    {
        private readonly HttpClient _client;

        public HttpTaskWorkflowSession(HttpClient client)
            : base(new[]
            {
                OperationDefinition.Create<ResetRequest, TaskCallResponse>("ResetBenchmark"),
                OperationDefinition.Create<CreateTaskRequest, TaskCallResponse>("CreateTask"),
                OperationDefinition.Create<TaskIdRequest, TaskCallResponse>("CompleteTask"),
            })
        {
            _client = client;
        }

        protected override async Task<JsonElement> ExecuteOperationAsync(
            OperationDefinition operation, JsonElement request, CancellationToken cancellationToken)
        {
            var response = operation.Name switch
            {
                "ResetBenchmark" => await ResetAsync(cancellationToken).ConfigureAwait(false),
                "CreateTask" => await CreateTaskAsync(request, cancellationToken).ConfigureAwait(false),
                "CompleteTask" => await CompleteTaskAsync(request, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"No handler registered for operation '{operation.Name}'."),
            };

            return JsonSerializer.SerializeToElement(response);
        }

        private async Task<TaskCallResponse> ResetAsync(CancellationToken cancellationToken)
        {
            var httpResponse = await _client.PostAsync("/__test/reset", content: null, cancellationToken)
                .ConfigureAwait(false);
            return new TaskCallResponse((int)httpResponse.StatusCode, Task: null, Error: null);
        }

        private async Task<TaskCallResponse> CreateTaskAsync(JsonElement request, CancellationToken cancellationToken)
        {
            var typedRequest = JsonSerializer.Deserialize<CreateTaskRequest>(request)!;
            var httpResponse = await _client.PostAsJsonAsync("/tasks", typedRequest, cancellationToken)
                .ConfigureAwait(false);
            var task = httpResponse.IsSuccessStatusCode
                ? await httpResponse.Content.ReadFromJsonAsync<TaskResponse>(cancellationToken).ConfigureAwait(false)
                : null;
            return new TaskCallResponse((int)httpResponse.StatusCode, task, Error: null);
        }

        private async Task<TaskCallResponse> CompleteTaskAsync(JsonElement request, CancellationToken cancellationToken)
        {
            var typedRequest = JsonSerializer.Deserialize<TaskIdRequest>(request)!;
            var httpResponse = await _client
                .PostAsync($"/tasks/{typedRequest.Id}/complete", content: null, cancellationToken)
                .ConfigureAwait(false);
            var task = httpResponse.IsSuccessStatusCode
                ? await httpResponse.Content.ReadFromJsonAsync<TaskResponse>(cancellationToken).ConfigureAwait(false)
                : null;
            return new TaskCallResponse((int)httpResponse.StatusCode, task, Error: null);
        }

        public override ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
