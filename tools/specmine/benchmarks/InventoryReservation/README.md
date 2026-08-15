# Inventory Reservation benchmark

This benchmark is a small black-box ASP.NET Core API for exercising experimental
spec-mining tools. Its OpenAPI document exposes callable request and response
shapes while intentionally omitting the state-transition rules below.

## Run

From this directory:

```powershell
dotnet run --project .\InventoryReservation.Api\InventoryReservation.Api.csproj
```

The server prints its listening URL. Health is available at `/health`, the
committed API contract at `/openapi.json`, and benchmark state can be reset with:

```powershell
Invoke-WebRequest -Method Post http://localhost:<port>/__test/reset
```

Run the ground-truth suite with:

```powershell
dotnet test .\InventoryReservation.Tests\InventoryReservation.Tests.csproj
```

## Hidden ground truth (maintainers)

- Initial inventory is `widget: 5` and `gadget: 2`.
- Inventory reads return the current available quantity; unknown SKUs return a
  typed `404`.
- Reservation quantities must be positive. Unknown SKUs return `404`, and
  requests above current availability return `409` without consuming stock.
- Successful creates atomically decrement stock and return a server-generated
  reservation ID with status `active`.
- A reservation can be retrieved by its generated ID.
- Releasing an active reservation restores its quantity and changes its status
  to `released`. Releasing it again returns the same released representation
  without restoring stock again.
- Reset restores the initial inventory and removes every reservation.
- Reservation creation and release are synchronized so concurrent requests
  cannot reserve more than the available quantity.
