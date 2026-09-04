# Contracts: PaymentProcessing public surface

Source: `tools\specmine\benchmarks\PaymentProcessing\PaymentProcessing.Api\openapi.json`

## Public operations

- `GET /health` (`Health`)
  - `200` -> `{ status: string }`
- `GET /openapi.json` (`GetOpenApiDocument`)
  - `200` -> generic JSON object
- `POST /__test/reset` (`Reset`)
  - `204`, no body
- `POST /payments/authorize` (`AuthorizePayment`)
  - request body: `{ idempotencyKey: string, amount: number, currency: string }`
  - `201` -> `PaymentResponse`
  - `200` -> `PaymentResponse` or `DeclinedPaymentResponse`
  - `400` -> `ErrorResponse`
  - `409` -> `ErrorResponse`
- `GET /payments/{id}` (`GetPayment`)
  - path param: `id: string`
  - `200` -> `PaymentResponse`
  - `404` -> `ErrorResponse`
- `POST /payments/{id}/capture` (`CapturePayment`)
  - path param: `id: string`
  - `200` -> `PaymentResponse`
  - `404` -> `ErrorResponse`
  - `409` -> `ErrorResponse`
- `POST /payments/{id}/void` (`VoidPayment`)
  - path param: `id: string`
  - `200` -> `PaymentResponse`
  - `404` -> `ErrorResponse`
  - `409` -> `ErrorResponse`

## Contract notes relevant to this experiment

- `AuthorizePayment` is the interesting entry point:
  - it is the only operation with a request body;
  - it has both `200` and `201` success shapes;
  - its `200` shape is explicitly ambiguous (`PaymentResponse` or `DeclinedPaymentResponse`).
- `PaymentResponse.status` is just `string` in the contract; the OpenAPI document does not enumerate lifecycle states.
- `PaymentResponse.id` is also just `string`; unlike TaskWorkflow, the public contract does not claim UUID format.
- `ErrorResponse` has `type`, `code`, and `message`, but the contract does not disclose the concrete values or when each branch is used.
- The public contract says nothing about idempotency-key reuse, whether declines are persisted, or which capture/void transitions are legal; those had to be learned from live probing.
- For recorded traces, the model uses the OpenAPI adapter's portable envelope types:
  - request: `{ path?, query?, headers?, body? }`
  - response: `{ status, body }`