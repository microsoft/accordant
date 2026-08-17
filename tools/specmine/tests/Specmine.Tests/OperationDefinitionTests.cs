// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;
using NUnit.Framework;

[TestFixture]
public sealed class OperationDefinitionTests
{
    private sealed record SampleRequest(string Title);

    private sealed record SampleResponse(string Id, string Title);

    [Test]
    public void Constructor_InvalidArguments_Throw()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => new OperationDefinition(" ", schema, schema));
            Assert.Throws<ArgumentException>(() => new OperationDefinition("Op", default, schema));
            Assert.Throws<ArgumentException>(() => new OperationDefinition("Op", schema, default));
        });
    }

    [Test]
    public void Constructor_SnapshotsSchemas_SurvivingDisposalOfSourceDocument()
    {
        OperationDefinition definition;
        using (var sourceDocument = JsonDocument.Parse("""{"type":"object"}"""))
        {
            // Construct while the source document is still alive - a correct
            // implementation clones the elements here rather than keeping them alive by
            // reference into the source document's buffer.
            definition = new OperationDefinition("Op", sourceDocument.RootElement, sourceDocument.RootElement);
        }

        // The source document is now disposed. If the definition had not snapshotted its
        // own independent copies, reading these here would throw.
        Assert.Multiple(() =>
        {
            Assert.That(definition.RequestSchema.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(definition.ResponseSchema.GetProperty("type").GetString(), Is.EqualTo("object"));
        });
    }

    [Test]
    public void Create_DerivesJsonSchemaFromRequestAndResponseTypes()
    {
        var definition = OperationDefinition.Create<SampleRequest, SampleResponse>("CreateSample");

        Assert.Multiple(() =>
        {
            Assert.That(definition.Name, Is.EqualTo("CreateSample"));

            Assert.That(definition.RequestSchema.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(definition.RequestSchema.GetProperty("properties").GetProperty("Title").GetProperty("type").GetString(),
                Is.EqualTo("string"));
            Assert.That(
                definition.RequestSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()),
                Is.EqualTo(new[] { "Title" }));

            Assert.That(definition.ResponseSchema.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(
                definition.ResponseSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name),
                Is.EquivalentTo(new[] { "Id", "Title" }));
        });
    }
}
