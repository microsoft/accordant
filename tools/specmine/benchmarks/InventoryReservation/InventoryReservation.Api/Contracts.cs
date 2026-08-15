// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace InventoryReservation.Api;

public sealed record GetInventoryRequest(string Sku);

public sealed record InventoryResponse(string Sku, int Available);

public sealed record CreateReservationRequest(string Sku, int Quantity);

public sealed record CreateReservationResponse(
    string Id,
    string Sku,
    int Quantity,
    string Status);

public sealed record GetReservationRequest(string Id);

public sealed record GetReservationResponse(
    string Id,
    string Sku,
    int Quantity,
    string Status);

public sealed record ReleaseReservationRequest(string Id);

public sealed record ReleaseReservationResponse(
    string Id,
    string Sku,
    int Quantity,
    string Status);

public sealed record ErrorResponse(string Code, string Message);

public sealed record HealthResponse(string Status);
