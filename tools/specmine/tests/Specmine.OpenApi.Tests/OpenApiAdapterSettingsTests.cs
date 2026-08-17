// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using System.Text.Json;
using NUnit.Framework;
using Specmine.Adapters.OpenApi;

/// <summary>
/// Exercises <see cref="OpenApiTargetAdapter"/>'s ownership and validation of its own
/// <c>{ "document": string, "baseUrl": string }</c> settings shape, independent of any
/// particular OpenAPI document's content.
/// </summary>
[TestFixture]
public sealed class OpenApiAdapterSettingsTests
{
    [Test]
    public void ConnectAsync_SettingsNotAnObject_ThrowsOpenApiSettingsException()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = JsonSerializer.SerializeToElement("not an object");

        var thrown = Assert.ThrowsAsync<OpenApiSettingsException>(() => adapter.ConnectAsync(settings));
        Assert.That(thrown!.Message, Does.Contain("JSON object"));
    }

    [Test]
    public void ConnectAsync_MissingDocument_ThrowsOpenApiSettingsException()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = JsonSerializer.SerializeToElement(new { baseUrl = "http://localhost:1234" });

        var thrown = Assert.ThrowsAsync<OpenApiSettingsException>(() => adapter.ConnectAsync(settings));
        Assert.That(thrown!.Message, Does.Contain("document"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ConnectAsync_BlankDocument_ThrowsOpenApiSettingsException(string document)
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = JsonSerializer.SerializeToElement(new { document, baseUrl = "http://localhost:1234" });

        var thrown = Assert.ThrowsAsync<OpenApiSettingsException>(() => adapter.ConnectAsync(settings));
        Assert.That(thrown!.Message, Does.Contain("document"));
    }

    [Test]
    public void ConnectAsync_MissingBaseUrl_ThrowsOpenApiSettingsException()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = JsonSerializer.SerializeToElement(new { document = OpenApiFixturePaths.TaskWorkflow });

        var thrown = Assert.ThrowsAsync<OpenApiSettingsException>(() => adapter.ConnectAsync(settings));
        Assert.That(thrown!.Message, Does.Contain("baseUrl"));
    }

    [TestCase("not-a-url")]
    [TestCase("/relative/path")]
    [TestCase("ftp://example.com")]
    [TestCase("mailto:someone@example.com")]
    public void ConnectAsync_BaseUrlNotAbsoluteHttpOrHttps_ThrowsOpenApiSettingsException(string baseUrl)
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = JsonSerializer.SerializeToElement(new { document = OpenApiFixturePaths.TaskWorkflow, baseUrl });

        var thrown = Assert.ThrowsAsync<OpenApiSettingsException>(() => adapter.ConnectAsync(settings));
        Assert.That(thrown!.Message, Does.Contain("baseUrl"));
    }

    [Test]
    public void ConnectAsync_DocumentDoesNotExist_ThrowsFileNotFoundException()
    {
        var adapter = new OpenApiTargetAdapter();
        var missingPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.json");
        var settings = OpenApiAdapterTestSettings.Create(missingPath, "http://localhost:1234");

        var thrown = Assert.ThrowsAsync<FileNotFoundException>(() => adapter.ConnectAsync(settings));
        Assert.That(thrown!.FileName, Is.EqualTo(missingPath));
    }

    [Test]
    public async Task ConnectAsync_RelativeDocumentPath_ResolvesRelativeToCurrentProcessDirectory()
    {
        // The task requires only that a relative 'document' path resolve relative to the
        // current process, so this proves exactly that - via Path.GetRelativePath rather
        // than mutating the shared, process-wide Environment.CurrentDirectory, which
        // other tests may be relying on concurrently.
        var relativeDocumentPath = Path.GetRelativePath(Environment.CurrentDirectory, OpenApiFixturePaths.TaskWorkflow);
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(relativeDocumentPath, "http://localhost:1234");

        await using var session = await adapter.ConnectAsync(settings);

        Assert.That(session.Operations, Is.Not.Empty);
    }

    [Test]
    public async Task ConnectAsync_ValidSettings_ReturnsASessionWithTheDocumentsOperations()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.TaskWorkflow, "http://localhost:1234");

        await using var session = await adapter.ConnectAsync(settings);

        Assert.That(session.Operations.Select(o => o.Name), Does.Contain("CreateTask"));
    }
}