// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PaymentProcessing.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<PaymentStore>();

        var app = builder.Build();

        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
            .WithName("Health");

        app.MapGet("/openapi.json", (IWebHostEnvironment environment) =>
            Results.File(
                Path.Combine(environment.ContentRootPath, "openapi.json"),
                "application/json"))
            .WithName("GetOpenApiDocument");

        app.MapPost("/__test/reset", (PaymentStore store) =>
            {
                store.Reset();
                return TypedResults.NoContent();
            })
            .WithName("Reset");

        app.MapPost("/payments/authorize", (AuthorizePaymentRequest request, PaymentStore store) =>
                store.Authorize(request))
            .WithName("AuthorizePayment");

        app.MapGet("/payments/{id}", (string id, PaymentStore store) =>
                store.Get(new GetPaymentRequest(id)))
            .WithName("GetPayment");

        app.MapPost("/payments/{id}/capture", (string id, PaymentStore store) =>
                store.Capture(new CapturePaymentRequest(id)))
            .WithName("CapturePayment");

        app.MapPost("/payments/{id}/void", (string id, PaymentStore store) =>
                store.Void(new VoidPaymentRequest(id)))
            .WithName("VoidPayment");

        app.Run();
    }
}
