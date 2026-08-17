// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using System.Text.Json;
using NUnit.Framework;
using Specmine.Adapters.OpenApi;

/// <summary>
/// Exercises operation discovery and the composite request/response JSON Schemas the
/// adapter builds against TaskWorkflow's committed OpenAPI document - the benchmark named
/// explicitly by this slice - checking operation names generally and the CreateTask and
/// CompleteTask schemas specifically, field by field, rather than as an opaque blob.
/// </summary>
[TestFixture]
public sealed class TaskWorkflowDiscoveryTests
{
    private ITargetSession _session = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.TaskWorkflow, "http://localhost:1234");
        _session = await adapter.ConnectAsync(settings);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _session.DisposeAsync();

    [Test]
    public void Operations_ExposesEveryDocumentedOperationIdExactlyOnce()
    {
        Assert.That(
            _session.Operations.Select(o => o.Name),
            Is.EquivalentTo(new[] { "Health", "CreateTask", "GetTask", "CompleteTask", "CancelTask", "Reset" }));
    }

    [Test]
    public void CreateTask_RequestSchema_IsAnObjectSectionWithOnlyARequiredBody()
    {
        var request = GetOperation("CreateTask").RequestSchema;

        Assert.Multiple(() =>
        {
            Assert.That(request.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(request.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(NamesOf(request.GetProperty("required")), Is.EqualTo(new[] { "body" }));

            var properties = request.GetProperty("properties");
            Assert.That(properties.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "body" }));

            var body = properties.GetProperty("body");
            Assert.That(body.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(body.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(NamesOf(body.GetProperty("required")), Is.EqualTo(new[] { "title" }));
            Assert.That(body.GetProperty("properties").GetProperty("title").GetProperty("type").GetString(), Is.EqualTo("string"));
        });
    }

    [Test]
    public void CreateTask_ResponseSchema_IsStatusAndBodyUnionOfTaskErrorOrNull()
    {
        var response = GetOperation("CreateTask").ResponseSchema;

        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(response.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(NamesOf(response.GetProperty("required")), Is.EquivalentTo(new[] { "status", "body" }));
            Assert.That(response.GetProperty("properties").GetProperty("status").GetProperty("type").GetString(), Is.EqualTo("integer"));

            var oneOf = response.GetProperty("properties").GetProperty("body").GetProperty("oneOf");
            var kinds = oneOf.EnumerateArray().Select(DescribeBodyOption).ToList();

            // 201 (Task) is documented before 400 (ErrorResponse) in the source document,
            // and a trailing 'null' option is always appended regardless of what is
            // documented, since an undocumented status or an empty body is always possible
            // at execution time.
            Assert.That(kinds, Is.EqualTo(new[] { "Task", "ErrorResponse", "null" }));
        });
    }

    [Test]
    public void CompleteTask_RequestSchema_IsAnObjectSectionWithOnlyARequiredPathId()
    {
        var request = GetOperation("CompleteTask").RequestSchema;

        Assert.Multiple(() =>
        {
            Assert.That(request.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(NamesOf(request.GetProperty("required")), Is.EqualTo(new[] { "path" }));

            var properties = request.GetProperty("properties");
            Assert.That(properties.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "path" }));

            var path = properties.GetProperty("path");
            Assert.That(path.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(path.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(NamesOf(path.GetProperty("required")), Is.EqualTo(new[] { "id" }));
            Assert.That(path.GetProperty("properties").GetProperty("id").GetProperty("type").GetString(), Is.EqualTo("string"));
        });
    }

    [Test]
    public void CompleteTask_ResponseSchema_IsStatusAndBodyUnionOfTaskErrorOrNull()
    {
        var response = GetOperation("CompleteTask").ResponseSchema;
        var oneOf = response.GetProperty("properties").GetProperty("body").GetProperty("oneOf");
        var kinds = oneOf.EnumerateArray().Select(DescribeBodyOption).ToList();

        // 200 (Task), 404 (ErrorResponse), and 409 (ErrorResponse) are documented; the
        // repeated ErrorResponse schema across 404/409 is deduplicated to a single entry.
        Assert.That(kinds, Is.EqualTo(new[] { "Task", "ErrorResponse", "null" }));
    }

    private OperationDefinition GetOperation(string name) =>
        _session.Operations.Single(o => o.Name == name);

    private static string[] NamesOf(JsonElement stringArray) =>
        stringArray.EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static string DescribeBodyOption(JsonElement schema)
    {
        if (schema.GetProperty("type").GetString() == "null")
        {
            return "null";
        }

        var propertyNames = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        if (propertyNames.SetEquals(new[] { "id", "title", "status" }))
        {
            return "Task";
        }

        if (propertyNames.SetEquals(new[] { "code", "message" }))
        {
            return "ErrorResponse";
        }

        return string.Join(",", propertyNames.OrderBy(n => n, StringComparer.Ordinal));
    }
}