// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TaskWorkflow.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using TaskWorkflow.Api;

[TestFixture]
public sealed class TaskWorkflowApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [SetUp]
    public async Task SetUp()
    {
        var response = await _client.PostAsync("/__test/reset", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task HealthAndOpenApiAreAvailable()
    {
        var healthResponse = await _client.GetAsync("/health");
        var health = await healthResponse.Content.ReadFromJsonAsync<HealthResponse>();
        var openApiResponse = await _client.GetAsync("/openapi.json");
        var openApi = await openApiResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(healthResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(health, Is.EqualTo(new HealthResponse("healthy")));
            Assert.That(openApiResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(openApi.GetProperty("openapi").GetString(), Is.EqualTo("3.0.3"));
        });
    }

    [Test]
    public async Task CreateAndGetUseTheServerGeneratedId()
    {
        var createResponse = await CreateTask("write benchmark");
        var created = await ReadTask(createResponse);

        var getResponse = await _client.GetAsync($"/tasks/{created.Id}");
        var fetched = await ReadTask(getResponse);

        Assert.Multiple(() =>
        {
            Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(createResponse.Headers.Location?.ToString(), Is.EqualTo($"/tasks/{created.Id}"));
            Assert.That(Guid.TryParse(created.Id, out _), Is.True);
            Assert.That(created.Title, Is.EqualTo("write benchmark"));
            Assert.That(created.Status, Is.EqualTo("pending"));
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fetched, Is.EqualTo(created));
        });
    }

    [Test]
    public async Task CompleteIsIdempotent()
    {
        var task = await CreateAndReadTask("complete me");

        var firstResponse = await _client.PostAsync($"/tasks/{task.Id}/complete", null);
        var first = await ReadTask(firstResponse);
        var retryResponse = await _client.PostAsync($"/tasks/{task.Id}/complete", null);
        var retry = await ReadTask(retryResponse);

        Assert.Multiple(() =>
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(first.Status, Is.EqualTo("completed"));
            Assert.That(retryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retry, Is.EqualTo(first));
        });
    }

    [Test]
    public async Task CancelIsIdempotent()
    {
        var task = await CreateAndReadTask("cancel me");

        var firstResponse = await _client.PostAsync($"/tasks/{task.Id}/cancel", null);
        var first = await ReadTask(firstResponse);
        var retryResponse = await _client.PostAsync($"/tasks/{task.Id}/cancel", null);
        var retry = await ReadTask(retryResponse);

        Assert.Multiple(() =>
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(first.Status, Is.EqualTo("canceled"));
            Assert.That(retryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retry, Is.EqualTo(first));
        });
    }

    [TestCase("complete", "cancel")]
    [TestCase("cancel", "complete")]
    public async Task OppositeTerminalTransitionsConflict(string firstOperation, string conflictingOperation)
    {
        var task = await CreateAndReadTask("terminal transition");
        var firstResponse = await _client.PostAsync($"/tasks/{task.Id}/{firstOperation}", null);

        var conflictResponse = await _client.PostAsync($"/tasks/{task.Id}/{conflictingOperation}", null);
        var error = await ReadError(conflictResponse);

        Assert.Multiple(() =>
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(conflictResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(error, Is.EqualTo(new ErrorResponse(
                "invalid_transition",
                "The requested task transition conflicts with its current status.")));
        });
    }

    [TestCase("/tasks/missing")]
    [TestCase("/tasks/missing/complete")]
    [TestCase("/tasks/missing/cancel")]
    public async Task MissingIdsReturnTypedNotFound(string path)
    {
        var response = path.EndsWith("/missing", StringComparison.Ordinal)
            ? await _client.GetAsync(path)
            : await _client.PostAsync(path, null);
        var error = await ReadError(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(error, Is.EqualTo(new ErrorResponse(
                "not_found",
                "Task 'missing' was not found.")));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task BlankTitlesReturnTypedValidationError(string title)
    {
        var response = await CreateTask(title);
        var error = await ReadError(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(error, Is.EqualTo(new ErrorResponse(
                "validation_error",
                "Title must not be blank.")));
        });
    }

    [Test]
    public async Task ResetClearsAllTasks()
    {
        var task = await CreateAndReadTask("temporary");

        var resetResponse = await _client.PostAsync("/__test/reset", null);
        var getResponse = await _client.GetAsync($"/tasks/{task.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(resetResponse.Content.Headers.ContentLength, Is.EqualTo(0));
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    private async Task<TaskResponse> CreateAndReadTask(string title)
    {
        var response = await CreateTask(title);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return await ReadTask(response);
    }

    private Task<HttpResponseMessage> CreateTask(string title) =>
        _client.PostAsJsonAsync("/tasks", new CreateTaskRequest(title));

    private static async Task<TaskResponse> ReadTask(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<TaskResponse>()
        ?? throw new AssertionException("Expected a task response body.");

    private static async Task<ErrorResponse> ReadError(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ErrorResponse>()
        ?? throw new AssertionException("Expected an error response body.");
}
