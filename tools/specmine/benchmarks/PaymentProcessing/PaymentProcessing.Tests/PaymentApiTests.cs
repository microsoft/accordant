// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PaymentProcessing.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using PaymentProcessing.Api;

[TestFixture]
public sealed class PaymentApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void CreateHost()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    [SetUp]
    public async Task ResetState()
    {
        using var response = await _client.PostAsync("/__test/reset", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [OneTimeTearDown]
    public void DisposeHost()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task AuthorizationRetryReturnsOriginalPayment()
    {
        var request = new AuthorizePaymentRequest("retry-key", 125.50m, "USD");

        using var firstResponse = await _client.PostAsJsonAsync("/payments/authorize", request);
        using var retryResponse = await _client.PostAsJsonAsync("/payments/authorize", request);
        var first = await Read<PaymentResponse>(firstResponse);
        var retry = await Read<PaymentResponse>(retryResponse);

        Assert.Multiple(() =>
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(retryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retry, Is.EqualTo(first));
            Assert.That(first.Status, Is.EqualTo("authorized"));
        });
    }

    [TestCase(126.00, "USD")]
    [TestCase(125.50, "EUR")]
    [TestCase(1000.01, "USD")]
    public async Task ChangedAuthorizationPayloadConflicts(double amount, string currency)
    {
        await Authorize("conflict-key", 125.50m, "USD");

        using var response = await _client.PostAsJsonAsync(
            "/payments/authorize",
            new AuthorizePaymentRequest("conflict-key", (decimal)amount, currency));
        var error = await Read<ErrorResponse>(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(error.Code, Is.EqualTo("idempotency_conflict"));
        });
    }

    [Test]
    public async Task HealthAndOpenApiDocumentAreServed()
    {
        using var healthResponse = await _client.GetAsync("/health");
        var health = await Read<HealthResponse>(healthResponse);
        using var openApiResponse = await _client.GetAsync("/openapi.json");
        var openApi = await openApiResponse.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(healthResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(health.Status, Is.EqualTo("ok"));
            Assert.That(openApiResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(openApi, Does.Contain("\"operationId\": \"AuthorizePayment\""));
            Assert.That(openApi, Does.Not.Contain("1000"));
            Assert.That(openApi, Does.Not.Contain("limit_exceeded"));
        });
    }

    [Test]
    public async Task DeclinedAuthorizationDoesNotReserveKey()
    {
        using var declinedResponse = await _client.PostAsJsonAsync(
            "/payments/authorize",
            new AuthorizePaymentRequest("reusable-key", 1000.01m, "USD"));
        var declined = await Read<DeclinedPaymentResponse>(declinedResponse);

        using var acceptedResponse = await _client.PostAsJsonAsync(
            "/payments/authorize",
            new AuthorizePaymentRequest("reusable-key", 25m, "EUR"));
        var accepted = await Read<PaymentResponse>(acceptedResponse);

        Assert.Multiple(() =>
        {
            Assert.That(declinedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(declined.Status, Is.EqualTo("declined"));
            Assert.That(declined.Reason, Is.EqualTo("limit_exceeded"));
            Assert.That(acceptedResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(accepted.IdempotencyKey, Is.EqualTo("reusable-key"));
            Assert.That(accepted.Currency, Is.EqualTo("EUR"));
        });
    }

    [Test]
    public async Task ServerGeneratedIdCanDriveLaterOperations()
    {
        var authorized = await Authorize("derived-id-key", 42m, "GBP");

        using var getResponse = await _client.GetAsync($"/payments/{authorized.Id}");
        var retrieved = await Read<PaymentResponse>(getResponse);
        using var captureResponse = await _client.PostAsync($"/payments/{authorized.Id}/capture", null);
        var captured = await Read<PaymentResponse>(captureResponse);

        Assert.Multiple(() =>
        {
            Assert.That(authorized.Id, Is.Not.Empty);
            Assert.That(retrieved, Is.EqualTo(authorized));
            Assert.That(captured.Id, Is.EqualTo(authorized.Id));
            Assert.That(captured.Status, Is.EqualTo("captured"));
        });
    }

    [Test]
    public async Task CapturedPaymentIsIdempotentAndCannotBeVoided()
    {
        var payment = await Authorize("capture-key", 80m, "USD");

        using var firstResponse = await _client.PostAsync($"/payments/{payment.Id}/capture", null);
        using var retryResponse = await _client.PostAsync($"/payments/{payment.Id}/capture", null);
        using var voidResponse = await _client.PostAsync($"/payments/{payment.Id}/void", null);
        var first = await Read<PaymentResponse>(firstResponse);
        var retry = await Read<PaymentResponse>(retryResponse);
        var error = await Read<ErrorResponse>(voidResponse);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo("captured"));
            Assert.That(retry, Is.EqualTo(first));
            Assert.That(voidResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(error.Code, Is.EqualTo("invalid_payment_state"));
        });
    }

    [Test]
    public async Task VoidedPaymentIsIdempotentAndCannotBeCaptured()
    {
        var payment = await Authorize("void-key", 80m, "USD");

        using var firstResponse = await _client.PostAsync($"/payments/{payment.Id}/void", null);
        using var retryResponse = await _client.PostAsync($"/payments/{payment.Id}/void", null);
        using var captureResponse = await _client.PostAsync($"/payments/{payment.Id}/capture", null);
        var first = await Read<PaymentResponse>(firstResponse);
        var retry = await Read<PaymentResponse>(retryResponse);
        var error = await Read<ErrorResponse>(captureResponse);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo("voided"));
            Assert.That(retry, Is.EqualTo(first));
            Assert.That(captureResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(error.Code, Is.EqualTo("invalid_payment_state"));
        });
    }

    [Test]
    public async Task MissingPaymentReturnsTypedNotFoundForEveryOperation()
    {
        using var getResponse = await _client.GetAsync("/payments/missing");
        using var captureResponse = await _client.PostAsync("/payments/missing/capture", null);
        using var voidResponse = await _client.PostAsync("/payments/missing/void", null);

        var getError = await Read<ErrorResponse>(getResponse);
        var captureError = await Read<ErrorResponse>(captureResponse);
        var voidError = await Read<ErrorResponse>(voidResponse);

        Assert.Multiple(() =>
        {
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(captureResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(voidResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(getError.Code, Is.EqualTo("payment_not_found"));
            Assert.That(captureError.Code, Is.EqualTo("payment_not_found"));
            Assert.That(voidError.Code, Is.EqualTo("payment_not_found"));
        });
    }

    [Test]
    public async Task InvalidAuthorizationRequestsReturnTypedBadRequest()
    {
        var invalidRequests = new[]
        {
            new AuthorizePaymentRequest("", 10m, "USD"),
            new AuthorizePaymentRequest(" ", 10m, "USD"),
            new AuthorizePaymentRequest("zero", 0m, "USD"),
            new AuthorizePaymentRequest("negative", -1m, "USD"),
            new AuthorizePaymentRequest("short", 10m, "US"),
            new AuthorizePaymentRequest("long", 10m, "USDX"),
            new AuthorizePaymentRequest("lowercase", 10m, "usd"),
        };

        foreach (var request in invalidRequests)
        {
            using var response = await _client.PostAsJsonAsync("/payments/authorize", request);
            var error = await Read<ErrorResponse>(response);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(error.Type, Is.EqualTo("validation_error"));
                Assert.That(error.Code, Is.EqualTo("invalid_request"));
            });
        }
    }

    [Test]
    public async Task ResetClearsPaymentsAndIdempotencyRecords()
    {
        var original = await Authorize("reset-key", 30m, "USD");

        using var resetResponse = await _client.PostAsync("/__test/reset", null);
        using var missingResponse = await _client.GetAsync($"/payments/{original.Id}");
        using var replacementResponse = await _client.PostAsJsonAsync(
            "/payments/authorize",
            new AuthorizePaymentRequest("reset-key", 40m, "EUR"));
        var replacement = await Read<PaymentResponse>(replacementResponse);

        Assert.Multiple(() =>
        {
            Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(missingResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(replacementResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(replacement.Amount, Is.EqualTo(40m));
            Assert.That(replacement.Currency, Is.EqualTo("EUR"));
        });
    }

    private async Task<PaymentResponse> Authorize(string key, decimal amount, string currency)
    {
        using var response = await _client.PostAsJsonAsync(
            "/payments/authorize",
            new AuthorizePaymentRequest(key, amount, currency));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return await Read<PaymentResponse>(response);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>();
        Assert.That(value, Is.Not.Null);
        return value!;
    }
}
