// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using Specmine.Adapters.OpenApi;
using Specmine.OpenApi.Tests.Fixture;

/// <summary>
/// Exercises path, query, header, and JSON request body parameter mapping together against
/// a small fixture API (see <c>Fixture/FixtureProgram.cs</c> and
/// <c>Fixtures/fixture.openapi.json</c>), since no single committed benchmark operation
/// combines all four locations. Runs against a real, ephemeral Kestrel listener, exactly
/// as <see cref="TaskWorkflowExecutionTests"/> does.
/// </summary>
[TestFixture]
public sealed class FixtureParameterMappingTests
{
    private FixtureWebApplicationFactory _factory = null!;
    private string _baseUrl = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new FixtureWebApplicationFactory();
        _baseUrl = RealHttpServer.Start(_factory);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task AnnotateWidget_AllSectionsProvided_MapsPathQueryHeadersAndBodyCorrectly()
    {
        await using var session = await ConnectAsync();

         // Anonymous C# types cannot have a property literally named "X-Trace-Id", so the
         // request is built as raw JSON here rather than through JsonSerializer.SerializeToElement.
         var requestJson = """
            {
              "path": { "id": "widget-1" },
              "query": { "priority": 5, "verbose": true },
              "headers": { "X-Trace-Id": "trace-abc", "X-Optional-Tag": "tag-1" },
              "body": { "note": "hello" }
            }
            """;
        var response = await session.ExecuteAsync("AnnotateWidget", JsonDocument.Parse(requestJson).RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetInt32(), Is.EqualTo(200));

            var body = response.GetProperty("body");
            Assert.That(body.GetProperty("id").GetString(), Is.EqualTo("widget-1"));
            Assert.That(body.GetProperty("priority").GetInt32(), Is.EqualTo(5));
            Assert.That(body.GetProperty("verbose").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("traceId").GetString(), Is.EqualTo("trace-abc"));
            Assert.That(body.GetProperty("optionalTag").GetString(), Is.EqualTo("tag-1"));
            Assert.That(body.GetProperty("note").GetString(), Is.EqualTo("hello"));
        });
    }

    [Test]
    public async Task AnnotateWidget_OptionalQueryAndHeaderOmitted_StillSucceeds()
    {
        await using var session = await ConnectAsync();

        var requestJson = """
            {
              "path": { "id": "widget-2" },
              "query": { "priority": 1 },
              "headers": { "X-Trace-Id": "trace-xyz" },
              "body": { "note": "minimal" }
            }
            """;
        var response = await session.ExecuteAsync("AnnotateWidget", JsonDocument.Parse(requestJson).RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetInt32(), Is.EqualTo(200));

            var body = response.GetProperty("body");
            Assert.That(body.GetProperty("id").GetString(), Is.EqualTo("widget-2"));
            Assert.That(body.GetProperty("priority").GetInt32(), Is.EqualTo(1));
            Assert.That(body.GetProperty("traceId").GetString(), Is.EqualTo("trace-xyz"));
            Assert.That(body.GetProperty("optionalTag").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task AnnotateWidget_NegativePriority_IsAnOrdinaryBadRequestResponseNotAnException()
    {
        await using var session = await ConnectAsync();

        var requestJson = """
            {
              "path": { "id": "widget-3" },
              "query": { "priority": -1 },
              "headers": { "X-Trace-Id": "trace-neg" },
              "body": { "note": "bad" }
            }
            """;
        var response = await session.ExecuteAsync("AnnotateWidget", JsonDocument.Parse(requestJson).RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetInt32(), Is.EqualTo(400));
            Assert.That(response.GetProperty("body").GetProperty("message").GetString(), Does.Contain("negative"));
        });
    }

    [Test]
    public async Task AnnotateWidget_MissingRequiredQueryParameter_ThrowsWithoutContactingTheServer()
    {
        await using var session = await ConnectAsync();

        var requestJson = """
            {
              "path": { "id": "widget-4" },
              "headers": { "X-Trace-Id": "trace-missing-query" },
              "body": { "note": "no priority" }
            }
            """;

        Assert.ThrowsAsync<ArgumentException>(() =>
            session.ExecuteAsync("AnnotateWidget", JsonDocument.Parse(requestJson).RootElement));
    }

    [Test]
    public async Task AnnotateWidget_MissingRequiredHeader_ThrowsWithoutContactingTheServer()
    {
        await using var session = await ConnectAsync();

        var requestJson = """
            {
              "path": { "id": "widget-5" },
              "query": { "priority": 1 },
              "body": { "note": "no trace id" }
            }
            """;

        Assert.ThrowsAsync<ArgumentException>(() =>
            session.ExecuteAsync("AnnotateWidget", JsonDocument.Parse(requestJson).RootElement));
    }

    [Test]
    public async Task AnnotateWidget_MissingRequiredBody_ThrowsWithoutContactingTheServer()
    {
        await using var session = await ConnectAsync();

        var requestJson = """
            {
              "path": { "id": "widget-6" },
              "query": { "priority": 1 },
              "headers": { "X-Trace-Id": "trace-no-body" }
            }
            """;

        Assert.ThrowsAsync<ArgumentException>(() =>
            session.ExecuteAsync("AnnotateWidget", JsonDocument.Parse(requestJson).RootElement));
    }

    [Test]
    public async Task TouchWidget_PathOnly_ReturnsNoContentWithANullBody()
    {
        await using var session = await ConnectAsync();

        var request = JsonSerializer.SerializeToElement(new { path = new { id = "widget-7" } });
        var response = await session.ExecuteAsync("TouchWidget", request);

        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("status").GetInt32(), Is.EqualTo(204));
            Assert.That(response.GetProperty("body").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    private async Task<ITargetSession> ConnectAsync()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.Fixture, _baseUrl);
        return await adapter.ConnectAsync(settings);
    }
}