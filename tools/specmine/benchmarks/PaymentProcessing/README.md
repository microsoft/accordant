# Payment Processing benchmark

This benchmark is a small in-memory ASP.NET Core black box for experimental
specification-mining tools. Its committed OpenAPI document exposes callable
request and response shapes without describing the behavioral rules below.
The service has no dependency on Accordant.

## Run

From this directory:

```powershell
dotnet run --project .\PaymentProcessing.Api\PaymentProcessing.Api.csproj
```

The service exposes `GET /health`, `GET /openapi.json`, and the payment
operations. To run the ground-truth suite:

```powershell
dotnet test .\PaymentProcessing.Tests\PaymentProcessing.Tests.csproj
```

`POST /__test/reset` clears all in-memory payments and idempotency records and
returns `204 No Content`.

## Hidden ground truth

This section is for benchmark maintainers and must not be copied into the
OpenAPI description supplied to mining tools.

- `AuthorizePayment` requires a nonblank idempotency key, a positive amount,
  and an exactly three-letter uppercase currency. Invalid requests return the
  typed `invalid_request` error with HTTP 400.
- Amounts greater than 1000 return HTTP 200 with `declined` and
  `limit_exceeded`. A decline creates neither a payment nor an idempotency
  reservation.
- A successful authorization generates an ID and returns HTTP 201 in the
  `authorized` state. Repeating an identical request under the same key returns
  the original payment with HTTP 200. Reusing the key with a changed amount or
  currency returns the typed `idempotency_conflict` error with HTTP 409.
- `GetPayment` returns the stored payment or the typed `payment_not_found`
  error with HTTP 404.
- `CapturePayment` transitions `authorized` to `captured`; repeating capture
  is idempotent. Capturing `voided` returns typed HTTP 409.
- `VoidPayment` transitions `authorized` to `voided`; repeating void is
  idempotent. Voiding `captured` returns typed HTTP 409.
- Capture and void return typed HTTP 404 for missing IDs. Reset removes both
  payment and idempotency state.

The operation names and DTOs intentionally align with Accordant's
named-operation model: `AuthorizePayment`, `GetPayment`, `CapturePayment`, and
`VoidPayment`, each with explicit request and response types. Successful
authorization supplies the server-derived ID needed by later operations.
