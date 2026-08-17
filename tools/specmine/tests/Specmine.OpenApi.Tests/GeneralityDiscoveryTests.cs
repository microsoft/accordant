// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using System.Text.Json;
using NUnit.Framework;
using Specmine.Adapters.OpenApi;

/// <summary>
/// Proves discovery generalizes beyond TaskWorkflow by parsing the PaymentProcessing and
/// InventoryReservation benchmark documents too - checking only publicly documented shape
/// (operation names, parameter/response structure), never either benchmark's actual
/// behavioral ground truth.
/// </summary>
[TestFixture]
public sealed class GeneralityDiscoveryTests
{
    [Test]
    public async Task PaymentProcessing_ExposesEveryDocumentedOperationIdExactlyOnce()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.PaymentProcessing, "http://localhost:1234");
        await using var session = await adapter.ConnectAsync(settings);

        Assert.That(
            session.Operations.Select(o => o.Name),
            Is.EquivalentTo(new[]
            {
                "Health", "GetOpenApiDocument", "Reset", "AuthorizePayment", "GetPayment", "CapturePayment", "VoidPayment",
            }));
    }

    [Test]
    public async Task PaymentProcessing_AuthorizePayment_ResponseUnionFlattensNestedOneOfAndDeduplicatesAcrossStatuses()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.PaymentProcessing, "http://localhost:1234");
        await using var session = await adapter.ConnectAsync(settings);

        var operation = session.Operations.Single(o => o.Name == "AuthorizePayment");
        var oneOf = operation.ResponseSchema.GetProperty("properties").GetProperty("body").GetProperty("oneOf");
        var kinds = oneOf.EnumerateArray().Select(DescribePaymentBodyOption).ToList();

        // The document's 200 response is itself a top-level 'oneOf' of PaymentResponse and
        // DeclinedPaymentResponse; that must be flattened into this union rather than
        // appearing as a nested 'oneOf', and the 201/400/409 responses' schemas
        // (PaymentResponse again, and ErrorResponse repeated across 400/409) must collapse
        // to their distinct shapes plus the trailing 'null' option.
        Assert.That(kinds, Is.EqualTo(new[] { "PaymentResponse", "DeclinedPaymentResponse", "ErrorResponse", "null" }));
    }

    [Test]
    public async Task InventoryReservation_ExposesEveryDocumentedOperationIdExactlyOnce()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.InventoryReservation, "http://localhost:1234");
        await using var session = await adapter.ConnectAsync(settings);

        Assert.That(
            session.Operations.Select(o => o.Name),
            Is.EquivalentTo(new[]
            {
                "Health", "OpenApi", "Reset", "GetInventory", "CreateReservation", "GetReservation", "ReleaseReservation",
            }));
    }

    [Test]
    public async Task InventoryReservation_CreateReservation_RequestSchemaRequiresOnlyBody()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.InventoryReservation, "http://localhost:1234");
        await using var session = await adapter.ConnectAsync(settings);

        var operation = session.Operations.Single(o => o.Name == "CreateReservation");
        var request = operation.RequestSchema;

        Assert.Multiple(() =>
        {
            Assert.That(request.GetProperty("properties").EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "body" }));
            Assert.That(request.GetProperty("required").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "body" }));

            var body = request.GetProperty("properties").GetProperty("body");
            Assert.That(
                body.GetProperty("required").EnumerateArray().Select(e => e.GetString()),
                Is.EquivalentTo(new[] { "sku", "quantity" }));
        });
    }

    [Test]
    public async Task InventoryReservation_GetInventory_RequestSchemaRequiresOnlyPathSku()
    {
        var adapter = new OpenApiTargetAdapter();
        var settings = OpenApiAdapterTestSettings.Create(OpenApiFixturePaths.InventoryReservation, "http://localhost:1234");
        await using var session = await adapter.ConnectAsync(settings);

        var operation = session.Operations.Single(o => o.Name == "GetInventory");
        var request = operation.RequestSchema;
        var path = request.GetProperty("properties").GetProperty("path");

        Assert.Multiple(() =>
        {
            Assert.That(request.GetProperty("required").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "path" }));
            Assert.That(path.GetProperty("required").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "sku" }));
        });
    }

    private static string DescribePaymentBodyOption(JsonElement schema)
    {
        if (schema.GetProperty("type").GetString() == "null")
        {
            return "null";
        }

        var propertyNames = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();

        if (propertyNames.SetEquals(new[] { "id", "idempotencyKey", "amount", "currency", "status" }))
        {
            return "PaymentResponse";
        }

        if (propertyNames.SetEquals(new[] { "status", "reason" }))
        {
            return "DeclinedPaymentResponse";
        }

        if (propertyNames.SetEquals(new[] { "type", "code", "message" }))
        {
            return "ErrorResponse";
        }

        return string.Join(",", propertyNames.OrderBy(n => n, StringComparer.Ordinal));
    }
}