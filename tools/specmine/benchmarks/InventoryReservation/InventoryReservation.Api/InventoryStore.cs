// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace InventoryReservation.Api;

internal sealed class InventoryStore
{
    private static readonly IReadOnlyDictionary<string, int> InitialInventory =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["widget"] = 5,
            ["gadget"] = 2,
        };

    private readonly Lock stateLock = new();
    private readonly Dictionary<string, int> inventory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Reservation> reservations = new(StringComparer.Ordinal);

    public InventoryStore()
    {
        Reset();
    }

    public StoreResult<InventoryResponse> GetInventory(GetInventoryRequest request)
    {
        lock (stateLock)
        {
            return inventory.TryGetValue(request.Sku, out var available)
                ? StoreResult<InventoryResponse>.Success(
                    new InventoryResponse(request.Sku, available))
                : StoreResult<InventoryResponse>.Failure(
                    404,
                    new ErrorResponse("sku_not_found", $"SKU '{request.Sku}' was not found."));
        }
    }

    public StoreResult<CreateReservationResponse> CreateReservation(
        CreateReservationRequest request)
    {
        lock (stateLock)
        {
            if (request.Quantity <= 0)
            {
                return StoreResult<CreateReservationResponse>.Failure(
                    400,
                    new ErrorResponse(
                        "invalid_quantity",
                        "Quantity must be greater than zero."));
            }

            if (!inventory.TryGetValue(request.Sku, out var available))
            {
                return StoreResult<CreateReservationResponse>.Failure(
                    404,
                    new ErrorResponse("sku_not_found", $"SKU '{request.Sku}' was not found."));
            }

            if (request.Quantity > available)
            {
                return StoreResult<CreateReservationResponse>.Failure(
                    409,
                    new ErrorResponse(
                        "insufficient_inventory",
                        $"Insufficient inventory for SKU '{request.Sku}'."));
            }

            var reservation = new Reservation(
                Guid.NewGuid().ToString("N"),
                request.Sku,
                request.Quantity,
                ReservationStatus.Active);

            inventory[request.Sku] = available - request.Quantity;
            reservations.Add(reservation.Id, reservation);

            return StoreResult<CreateReservationResponse>.Success(
                new CreateReservationResponse(
                    reservation.Id,
                    reservation.Sku,
                    reservation.Quantity,
                    "active"));
        }
    }

    public StoreResult<GetReservationResponse> GetReservation(GetReservationRequest request)
    {
        lock (stateLock)
        {
            return reservations.TryGetValue(request.Id, out var reservation)
                ? StoreResult<GetReservationResponse>.Success(
                    new GetReservationResponse(
                        reservation.Id,
                        reservation.Sku,
                        reservation.Quantity,
                        ToWireStatus(reservation.Status)))
                : StoreResult<GetReservationResponse>.Failure(
                    404,
                    new ErrorResponse(
                        "reservation_not_found",
                        $"Reservation '{request.Id}' was not found."));
        }
    }

    public StoreResult<ReleaseReservationResponse> ReleaseReservation(
        ReleaseReservationRequest request)
    {
        lock (stateLock)
        {
            if (!reservations.TryGetValue(request.Id, out var reservation))
            {
                return StoreResult<ReleaseReservationResponse>.Failure(
                    404,
                    new ErrorResponse(
                        "reservation_not_found",
                        $"Reservation '{request.Id}' was not found."));
            }

            if (reservation.Status == ReservationStatus.Active)
            {
                inventory[reservation.Sku] += reservation.Quantity;
                reservation = reservation with { Status = ReservationStatus.Released };
                reservations[reservation.Id] = reservation;
            }

            return StoreResult<ReleaseReservationResponse>.Success(
                new ReleaseReservationResponse(
                    reservation.Id,
                    reservation.Sku,
                    reservation.Quantity,
                    ToWireStatus(reservation.Status)));
        }
    }

    public void Reset()
    {
        lock (stateLock)
        {
            inventory.Clear();
            foreach (var item in InitialInventory)
            {
                inventory.Add(item.Key, item.Value);
            }

            reservations.Clear();
        }
    }

    private static string ToWireStatus(ReservationStatus status) =>
        status == ReservationStatus.Active ? "active" : "released";

    private sealed record Reservation(
        string Id,
        string Sku,
        int Quantity,
        ReservationStatus Status);

    private enum ReservationStatus
    {
        Active,
        Released,
    }
}

internal sealed record StoreResult<T>(T? Value, int? ErrorStatus, ErrorResponse? Error)
{
    public static StoreResult<T> Success(T value) => new(value, null, null);

    public static StoreResult<T> Failure(int status, ErrorResponse error) =>
        new(default, status, error);
}
