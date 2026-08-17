// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using Specmine.Adapters.OpenApi;
using TaskWorkflow.Api;

/// <summary>
/// Executes the OpenAPI adapter end to end against the TaskWorkflow benchmark, hosted on a
/// real, ephemeral Kestrel listener (see <see cref="RealHttpServer"/>) rather than the
/// in-memory <c>TestServer</c> <see cref="WebApplicationFactory{TEntryPoint}"/> uses by
/// default - proving the adapter's own independently constructed <see cref="HttpClient"/>
/// can actually reach the target it was configured with, exactly as it would in real use.
/// </summary>
[TestFixture]
public sealed class TaskWorkflowExecutionTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _baseUrl = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>();
        _baseUrl = RealHttpServer.Start(_factory);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task RecordsResetCreateAndTwoCompletesUsingTheServerGeneratedId()
    {
        await using var session = await ConnectAsync();

        using var tracesDirectory = new TestTracesDirectory();
        string? taskId = null;
        JsonElement resetResponse = default;
        JsonElement createResponse = default;
        var completeResponses = new List<JsonElement>();

        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory.Path, session, async recordingTarget =>
        {
            resetResponse = await recordingTarget.ExecuteAsync("Reset", JsonDocument.Parse("{}").RootElement);

            createResponse = await recordingTarget.ExecuteAsync(
                "CreateTask", JsonSerializer.SerializeToElement(new { body = new { title = "write benchmark" } }));

            // The server-generated task ID is only known once CreateTask's real response
            // comes back - this is exactly the response-dependent chaining TraceRecorder
            // is meant to support.
            taskId = createResponse.GetProperty("body").GetProperty("id").GetString();

            for (var i = 0; i < 2; i++)
            {
                completeResponses.Add(await recordingTarget.ExecuteAsync(
                    "CompleteTask", JsonSerializer.SerializeToElement(new { path = new { id = taskId } })));
            }
        });

        Assert.That(taskId, Is.Not.Null.And.Not.Empty);
        var reloaded = await TraceStore.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(trace.Status, Is.EqualTo(TraceStatus.Completed));
            Assert.That(reloaded.Status, Is.EqualTo(TraceStatus.Completed));

            Assert.That(resetResponse.GetProperty("status").GetInt32(), Is.EqualTo(204));
            Assert.That(resetResponse.GetProperty("body").ValueKind, Is.EqualTo(JsonValueKind.Null));

            Assert.That(createResponse.GetProperty("status").GetInt32(), Is.EqualTo(201));

            foreach (var completeResponse in completeResponses)
            {
                Assert.That(completeResponse.GetProperty("status").GetInt32(), Is.EqualTo(200));
                Assert.That(completeResponse.GetProperty("body").GetProperty("id").GetString(), Is.EqualTo(taskId));
                Assert.That(completeResponse.GetProperty("body").GetProperty("status").GetString(), Is.EqualTo("completed"));
            }

            Assert.That(
                reloaded.Calls.Select(c => c.OperationName),
                Is.EqualTo(new[] { "Reset", "CreateTask", "CompleteTask", "CompleteTask" }));
            Assert.That(reloaded.Calls.Select(c => c.CallId), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(reloaded.Calls.All(c => c.Error is null), Is.True);

            foreach (var completeCall in reloaded.Calls.Skip(2))
            {
                Assert.That(completeCall.Request.GetProperty("path").GetProperty("id").GetString(), Is.EqualTo(taskId));
                Assert.That(completeCall.Response!.Value.GetProperty("body").GetProperty("id").GetString(), Is.EqualTo(taskId));
            }
        });
    }

    [Test]
    public async Task GetTask_UnknownId_IsAnOrdinaryNotFoundResponseNotAnException()
    {
        await using var session = await ConnectAsync();

        var response = await session.ExecuteAsync(
            "GetTask", JsonSerializer.SerializeToElement(new { path = new { id = "does-not-exist" } }));

        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(response.GetProperty("body").GetProperty("code").GetString(), Is.EqualTo("not_found"));
        });
    }

    [Test]
    public async Task ExecuteAsync_MissingRequiredPathSection_ThrowsWithoutContactingTheServer()
    {
        await using var session = await ConnectAsync();

        Assert.ThrowsAsync<ArgumentException>(() =>
            session.ExecuteAsync("CompleteTask", JsonSerializer.SerializeToElement(new { })));
    }

    [Test]
    public async Task ExecuteAsync_UnexpectedRequestSection_Throws()
    {
        await using var session = await ConnectAsync();

        Assert.ThrowsAsync<ArgumentException>(() =>
            session.ExecuteAsync("Reset", JsonSerializer.SerializeToElement(new { query = new { unexpected = "x" } })));
    }

    private async Task<ITargetSession> ConnectAsync()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.TaskWorkflow, _baseUrl);
        return await adapter.ConnectAsync(settings);
    }
}