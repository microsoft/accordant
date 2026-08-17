// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using System.Text.Json;
using NUnit.Framework;
using Specmine.Adapters.OpenApi;

/// <summary>
/// Exercises how <see cref="OpenApiTargetAdapter"/> reacts to malformed or unsupported
/// OpenAPI documents: each fixture under <c>Fixtures/Malformed</c> isolates exactly one
/// defect, and each test asserts the adapter fails clearly with a specific exception
/// naming the actual problem rather than hanging, silently ignoring the defect, or
/// inventing an unstable operation name/shape.
/// </summary>
[TestFixture]
public sealed class MalformedOpenApiDocumentTests
{
    [Test]
    public void ConnectAsync_DocumentIsNotWellFormedJson_ThrowsJsonException()
    {
        // System.Text.Json throws an internal JsonReaderException subclass of JsonException
        // for malformed syntax rather than the base type itself, so this accepts any
        // JsonException-derived failure (Assert.CatchAsync) instead of requiring the exact
        // base type (Assert.ThrowsAsync).
        var thrown = Assert.CatchAsync<JsonException>(() => Connect("not-json.json"));
        Assert.That(thrown, Is.Not.Null);
    }

    [Test]
    public void ConnectAsync_UnsupportedOpenApiVersion_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("unsupported-version.json"));
        Assert.That(thrown!.Message, Does.Contain("3.1.0"));
    }

    [Test]
    public void ConnectAsync_MissingPaths_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("missing-paths.json"));
        Assert.That(thrown!.Message, Does.Contain("'paths'"));
    }

    [Test]
    public void ConnectAsync_OperationMissingOperationId_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("missing-operation-id.json"));
        Assert.That(thrown!.Message, Does.Contain("operationId"));
    }

    [Test]
    public void ConnectAsync_DuplicateOperationId_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("duplicate-operation-id.json"));
        Assert.That(thrown!.Message, Does.Contain("Duplicate operationId 'Ping'"));
    }

    [Test]
    public void ConnectAsync_CookieParameter_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("unsupported-parameter-location.json"));
        Assert.That(thrown!.Message, Does.Contain("cookie"));
    }

    [Test]
    public void ConnectAsync_NonJsonRequestBodyContentType_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("unsupported-request-body-content-type.json"));
        Assert.That(thrown!.Message, Does.Contain("application/json"));
    }

    [Test]
    public void ConnectAsync_UnresolvableSchemaReference_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("unresolvable-schema-reference.json"));
        Assert.That(thrown!.Message, Does.Contain("DoesNotExist"));
    }

    [Test]
    public void ConnectAsync_CyclicSchemaReference_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("cyclic-schema-reference.json"));
        Assert.That(thrown!.Message, Does.Contain("Cyclic schema reference"));
    }

    [Test]
    public void ConnectAsync_UndeclaredPathPlaceholder_ThrowsOpenApiDocumentException()
    {
        var thrown = Assert.ThrowsAsync<OpenApiDocumentException>(() => Connect("path-placeholder-undeclared.json"));
        Assert.That(thrown!.Message, Does.Contain("undeclared path parameter"));
    }

    private static Task<ITargetSession> Connect(string fileName)
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.Malformed(fileName), "http://localhost:1234");
        return adapter.ConnectAsync(settings);
    }
}