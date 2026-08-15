// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http.Json;
using InventoryReservation.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace InventoryReservation.Tests;

[TestFixture]
public sealed class InventoryReservationApiTests
{
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public void CreateApplication()
    {
        factory = new WebApplicationFactory<Program>();
        client = factory.CreateClient();
    }

    [SetUp]
    public async Task ResetState()
    {
        using var response = await client.PostAsync("/__test/reset", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [OneTimeTearDown]
    public void DisposeApplication()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task CapacityAccumulatesAcrossReservations()
    {
        var first = await CreateReservation("widget", 2);
        var second = await CreateReservation("widget", 3);
        using var rejected = await PostReservation("widget", 1);

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        });

        var inventory = await GetInventory("widget");
        Assert.That(inventory.Available, Is.Zero);
    }

    [Test]
    public async Task FailedReservationPreservesStock()
    {
        using var rejected = await PostReservation("gadget", 3);
        var error = await rejected.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(error?.Code, Is.EqualTo("insufficient_inventory"));
        });

        var inventory = await GetInventory("gadget");
        Assert.That(inventory.Available, Is.EqualTo(2));
    }

    [Test]
    public async Task GeneratedReservationIdCanRetrieveReservation()
    {
        var created = await CreateReservation("widget", 2);
        var createdBody = await created.Content.ReadFromJsonAsync<CreateReservationResponse>();

        Assert.That(createdBody?.Id, Is.Not.Null.And.Not.Empty);

        using var retrieved = await client.GetAsync($"/reservations/{createdBody!.Id}");
        var retrievedBody =
            await retrieved.Content.ReadFromJsonAsync<GetReservationResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(retrieved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retrievedBody?.Id, Is.EqualTo(createdBody.Id));
            Assert.That(retrievedBody?.Sku, Is.EqualTo("widget"));
            Assert.That(retrievedBody?.Quantity, Is.EqualTo(2));
            Assert.That(retrievedBody?.Status, Is.EqualTo("active"));
        });
    }

    [Test]
    public async Task ReleaseRestoresInventoryAndReportsReleasedStatus()
    {
        var created = await CreateReservation("gadget", 2);
        var reservation = await created.Content.ReadFromJsonAsync<CreateReservationResponse>();

        using var released = await client.PostAsync(
            $"/reservations/{reservation!.Id}/release",
            content: null);
        var releasedBody =
            await released.Content.ReadFromJsonAsync<ReleaseReservationResponse>();
        var inventory = await GetInventory("gadget");

        Assert.Multiple(() =>
        {
            Assert.That(released.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(releasedBody?.Id, Is.EqualTo(reservation.Id));
            Assert.That(releasedBody?.Status, Is.EqualTo("released"));
            Assert.That(inventory.Available, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RepeatedReleaseDoesNotRestoreInventoryTwice()
    {
        var first = await CreateReservation("widget", 3);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateReservationResponse>();

        using var initialRelease = await client.PostAsync(
            $"/reservations/{firstBody!.Id}/release",
            content: null);
        var second = await CreateReservation("widget", 5);
        using var repeatedRelease = await client.PostAsync(
            $"/reservations/{firstBody.Id}/release",
            content: null);
        var inventory = await GetInventory("widget");

        Assert.Multiple(() =>
        {
            Assert.That(initialRelease.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(repeatedRelease.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inventory.Available, Is.Zero);
        });
    }

    [Test]
    public async Task UnknownResourcesReturnTypedNotFoundResponses()
    {
        using var inventoryResponse = await client.GetAsync("/inventory/unknown");
        using var reservationResponse = await client.GetAsync("/reservations/missing");
        using var releaseResponse = await client.PostAsync(
            "/reservations/missing/release",
            content: null);

        var inventoryError =
            await inventoryResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        var reservationError =
            await reservationResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        var releaseError = await releaseResponse.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(inventoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(inventoryError?.Code, Is.EqualTo("sku_not_found"));
            Assert.That(reservationResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(reservationError?.Code, Is.EqualTo("reservation_not_found"));
            Assert.That(releaseResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(releaseError?.Code, Is.EqualTo("reservation_not_found"));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task NonPositiveQuantityReturnsTypedBadRequest(int quantity)
    {
        using var response = await PostReservation("widget", quantity);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        var inventory = await GetInventory("widget");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(error?.Code, Is.EqualTo("invalid_quantity"));
            Assert.That(inventory.Available, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task UnknownSkuReservationReturnsTypedNotFound()
    {
        using var response = await PostReservation("unknown", 1);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(error?.Code, Is.EqualTo("sku_not_found"));
        });
    }

    [Test]
    public async Task ResetRestoresInventoryAndClearsReservations()
    {
        var created = await CreateReservation("widget", 4);
        var reservation = await created.Content.ReadFromJsonAsync<CreateReservationResponse>();

        using var reset = await client.PostAsync("/__test/reset", content: null);
        var inventory = await GetInventory("widget");
        using var retrieved = await client.GetAsync($"/reservations/{reservation!.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(inventory.Available, Is.EqualTo(5));
            Assert.That(retrieved.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task ConcurrentReservationsCannotExceedAvailableStock()
    {
        var attempts = Enumerable.Range(0, 12)
            .Select(_ => PostReservation("widget", 1))
            .ToArray();

        var responses = await Task.WhenAll(attempts);
        try
        {
            var successfulQuantity = 0;
            foreach (var response in responses.Where(
                         response => response.StatusCode == HttpStatusCode.Created))
            {
                var reservation =
                    await response.Content.ReadFromJsonAsync<CreateReservationResponse>();
                successfulQuantity += reservation!.Quantity;
            }

            var inventory = await GetInventory("widget");

            Assert.Multiple(() =>
            {
                Assert.That(successfulQuantity, Is.EqualTo(5));
                Assert.That(successfulQuantity, Is.LessThanOrEqualTo(5));
                Assert.That(inventory.Available, Is.Zero);
                Assert.That(
                    responses.Count(response => response.StatusCode == HttpStatusCode.Conflict),
                    Is.EqualTo(7));
            });
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task HealthAndOpenApiDocumentsAreAvailable()
    {
        using var healthResponse = await client.GetAsync("/health");
        using var openApiResponse = await client.GetAsync("/openapi.json");
        var health = await healthResponse.Content.ReadFromJsonAsync<HealthResponse>();
        var openApi = await openApiResponse.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(healthResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(health?.Status, Is.EqualTo("ok"));
            Assert.That(openApiResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(openApi, Does.Contain("\"operationId\": \"CreateReservation\""));
        });
    }

    private async Task<HttpResponseMessage> CreateReservation(string sku, int quantity)
    {
        var response = await PostReservation(sku, quantity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return response;
    }

    private Task<HttpResponseMessage> PostReservation(string sku, int quantity) =>
        client.PostAsJsonAsync(
            "/reservations",
            new CreateReservationRequest(sku, quantity));

    private async Task<InventoryResponse> GetInventory(string sku) =>
        await client.GetFromJsonAsync<InventoryResponse>($"/inventory/{sku}")
        ?? throw new InvalidOperationException("Inventory response body was empty.");
}
