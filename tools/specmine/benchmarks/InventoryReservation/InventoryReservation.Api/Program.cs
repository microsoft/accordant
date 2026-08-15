// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using InventoryReservation.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InventoryStore>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
    .WithName("Health");

app.MapGet("/openapi.json", () =>
{
    var stream = typeof(Program).Assembly.GetManifestResourceStream(
        "InventoryReservation.Api.openapi.json")
        ?? throw new InvalidOperationException("The embedded OpenAPI document was not found.");
    return Results.Stream(stream, "application/json");
})
    .WithName("OpenApi");

app.MapGet("/inventory/{sku}", (string sku, InventoryStore store) =>
        ToHttpResult(store.GetInventory(new GetInventoryRequest(sku))))
    .WithName("GetInventory");

app.MapPost("/reservations", (CreateReservationRequest request, InventoryStore store) =>
        ToHttpResult(store.CreateReservation(request), StatusCodes.Status201Created))
    .WithName("CreateReservation");

app.MapGet("/reservations/{id}", (string id, InventoryStore store) =>
        ToHttpResult(store.GetReservation(new GetReservationRequest(id))))
    .WithName("GetReservation");

app.MapPost("/reservations/{id}/release", (string id, InventoryStore store) =>
        ToHttpResult(store.ReleaseReservation(new ReleaseReservationRequest(id))))
    .WithName("ReleaseReservation");

app.MapPost("/__test/reset", (InventoryStore store) =>
{
    store.Reset();
    return Results.NoContent();
})
    .WithName("Reset");

app.Run();

static IResult ToHttpResult<T>(StoreResult<T> result, int successStatus = StatusCodes.Status200OK)
{
    if (result.Error is not null)
    {
        return Results.Json(result.Error, statusCode: result.ErrorStatus);
    }

    return Results.Json(result.Value, statusCode: successStatus);
}

public partial class Program;
