# Journal

## 2026-09-04 12:45 - Bootstrapping
- Read `tools\specmine\PROCESS.md` in full.
- Read `tools\specmine\README.md` sections covering the adapter/session SDK, `TraceRecorder`/`TraceStore`, the built-in OpenAPI adapter, `Understanding.Assume` / `Unknown` / `Provisional`, and `TraceReplayer`.
- Read framework/library source for `OpenApiTargetAdapter`, `OpenApiTargetSession`, `TraceRecorder`, `TraceReplayer`, and Accordant core `Spec<TState>` / `Expect`.
- Looked at the completed TaskWorkflow experiment only as a shape/style reference for artifacts and runner structure.
- Guardrail honored: did not open the PaymentProcessing benchmark README, any `.cs` file under `PaymentProcessing.Api`, or anything under `PaymentProcessing.Tests`.

## 2026-09-04 12:46 - Service startup
- Started `PaymentProcessing.Api` with `dotnet run --project tools\specmine\benchmarks\PaymentProcessing\PaymentProcessing.Api\PaymentProcessing.Api.csproj`.
- Observed Kestrel listening on `http://localhost:5000`.
- Confirmed `GET /health` -> `200 {"status":"ok"}`.

## 2026-09-04 12:47 - Contract pass and feature proposal
- Read only the committed public contract: `tools\specmine\benchmarks\PaymentProcessing\PaymentProcessing.Api\openapi.json`.
- First-pass feature proposal:
  - `pp-f1` = `AuthorizePayment` semantics, especially idempotency keys and changed retries.
  - `pp-f2` = `GetPayment` / `CapturePayment` / `VoidPayment` lifecycle behavior for an existing payment.
- `PROCESS.md` assumes a human would lock the roadmap. No human was available, so I explicitly self-reviewed this split and proceeded.

## 2026-09-04 12:48 - Black-box probing: authorize behavior
- `POST /payments/authorize` with `{"idempotencyKey":"k1","amount":10,"currency":"USD"}` -> `201` with a payment document containing:
  - an opaque nonblank payment id (observed shape looked like 32 lowercase hex characters),
  - echoed `idempotencyKey`, `amount`, and `currency`,
  - `status:"authorized"`.
- Repeating the exact same authorize request after success -> `200` with the same payment document. Hypothesis confirmed later: a successful authorization turns exact retries into idempotent replays.
- Repeating the same key with a changed amount or changed currency after success -> `409 {"type":"conflict","code":"idempotency_conflict","message":"The idempotency key was already used with a different request."}`.
- Semantic validation is stricter than the OpenAPI schema advertises:
  - blank / whitespace `idempotencyKey` -> `400`
  - `amount <= 0` -> `400`
  - currency not matching three uppercase letters (examples tried: `US`, `USDD`, `usd`) -> `400`
  - concrete error payload was always `{"type":"validation_error","code":"invalid_request","message":"idempotencyKey must be nonblank, amount must be positive, and currency must be three uppercase letters."}`
- Authorization/decline boundary:
  - amounts `1`, `10`, `500`, `1000`, and arbitrary uppercase-three-letter currencies such as `EUR`, `GBP`, `JPY`, `CAD`, and even `ZZZ` all authorized successfully;
  - amounts `1000.001`, `1000.01`, `1001`, `5000`, `10000` all returned `200 {"status":"declined","reason":"limit_exceeded"}`.
- Working hypothesis after those probes: requests with valid shape and `amount > 1000` decline; requests with valid shape and `amount <= 1000` authorize unless idempotency rules force replay/conflict.

## 2026-09-04 12:50 - Black-box probing: changed retries after declines
- Key experiment: decline first, then retry with the same idempotency key.
- `POST /payments/authorize` with `{"idempotencyKey":"decl1","amount":1001,"currency":"USD"}` -> `200 declined`.
- Repeating the exact same declined request -> `200 declined` again.
- Changing the declined request but keeping it still over the limit (`1002`, `1500`) -> still `200 declined`, not `409`.
- Changing the request to `{"idempotencyKey":"decl1","amount":1000,"currency":"USD"}` after earlier declines -> `201 authorized`.
- After that first success, changing the payload again with the same key -> `409 idempotency_conflict`, while repeating the successful `1000 USD` request -> `200` replay of the created payment.
- Strong conclusion: declined attempts do **not** reserve the idempotency key. Only the first successful authorization binds the key to a request/payment tuple and starts rejecting changed retries.

## 2026-09-04 12:52 - Black-box probing: lifecycle spot-checks for feature splitting
- These probes were not used as model ground truth for `pp-f1`; they were only used to justify the roadmap split and understand the broader benchmark shape.
- `authorized -> captured` via `POST /payments/{id}/capture` returned `200` with `status:"captured"`.
- Repeating capture on an already captured payment returned `200` with the same captured payment again.
- `authorized -> voided` via `POST /payments/{id}/void` returned `200` with `status:"voided"`.
- Repeating void on an already voided payment returned `200` with the same voided payment again.
- `void` after capture -> `409 {"type":"conflict","code":"invalid_payment_state","message":"Payment '<id>': A captured payment cannot be voided."}`
- `capture` after void -> `409 {"type":"conflict","code":"invalid_payment_state","message":"Payment '<id>': A voided payment cannot be captured."}`
- `GET /payments/{id}` returned the current lifecycle state.
- Interesting cross-feature interaction: re-sending the original `AuthorizePayment` request with the same idempotency key after capture or after void returned `200` with the payment's **current** lifecycle status (`captured` / `voided`), not a frozen snapshot of the original `authorized` response. This reinforced the decision to keep lifecycle as its own follow-on feature.

## 2026-09-04 12:55 - Modeling plan for `pp-f1`
- Chosen feature: `pp-f1` authorization outcomes and idempotency-key retries.
- Model shape:
  - state = successful authorizations keyed by `idempotencyKey`;
  - `AuthorizePayment` with semantically invalid fields -> `400 invalid_request`, state unchanged;
  - semantically valid request with `amount > 1000` and no successful prior payment for that key -> `200 declined`, state unchanged;
  - semantically valid request with `amount <= 1000` and no successful prior payment for that key -> `201 authorized`, state gains a payment bound to that key;
  - repeated request with a successfully bound key:
    - exact same payload -> `200` replay of that payment,
    - changed payload -> `409 idempotency_conflict`.
- Deliberate scope boundary: this first feature does not try to model how later capture/void transitions alter authorize replays; that belongs to `pp-f2`.

## 2026-09-04 12:57 - Implementation
- Created `tools\specmine\evaluations\PaymentProcessing\process-experiment-1\model\` as a fresh net10 console project.
- Added project references to:
  - `tools\specmine\src\Specmine\Specmine.csproj`
  - `tools\specmine\src\Specmine.Accordant\Specmine.Accordant.csproj`
  - `tools\specmine\src\Specmine.Adapters.OpenApi\Specmine.Adapters.OpenApi.csproj`
- Implemented:
  - portable request/response contract types for the OpenAPI adapter envelope;
  - `PaymentProcessingState`, tracking only successfully bound idempotency keys for `pp-f1`;
  - `PaymentProcessingSpec` with a single `AuthorizePayment` operation modeling validation, decline, success, replay, and conflict branches;
  - a runner program that health-checks the service, resets between scenarios, records live traces through `TraceRecorder`, replays them through `TraceReplayer`, and writes `replay-summary.json`.

## 2026-09-04 12:58 - First build issue
- The first `dotnet build` failed because I mistakenly used the operation-local `Expect` context as though it were the static generic `Expect.That<TResponse>(...)` API.
- Fix: switched to the context-aware overloads (`Expect.That(...)`, `.ThenState(...)`) that infer the operation's response/state types.

## 2026-09-04 12:59 - Recording and replay results
- `dotnet run --project tools\specmine\evaluations\PaymentProcessing\process-experiment-1\model\model.csproj` completed successfully.
- Recorded 8 completed traces under `traces\`:
  - `f091381d-de40-4b93-9cb7-390ab52a5580` - fresh-authorize-then-exact-replay
  - `b7555d6d-3632-4601-9cf2-0f5468fa97dd` - successful-authorize-then-changed-amount-conflict
  - `d5db7e8e-9b3d-4a2d-aeca-fc7e04226838` - successful-authorize-then-changed-currency-conflict
  - `8c975224-a217-4575-85ef-0e558fc861e5` - declined-request-repeat
  - `7d3606fa-9c2d-4ea6-8133-f07d0acfd439` - declines-do-not-reserve-key
  - `6304e549-385f-4ceb-ae7d-b53089505918` - validation-errors
  - `42409b1d-7b15-4d13-8640-632e3546090e` - boundary-1000-vs-1000-01
  - `ea4e3487-d27a-4d94-a035-bc8bf3998c8a` - arbitrary-uppercase-currency-authorizes
- Replayed all 8 traces with `UnderstandingStrictness.Reject`.
- Result: every replay finished `Conforming`; zero provisional matches, zero unknowns, zero out-of-scope hits, zero violations.
- Wrote persistent replay output to `replay-summary.json`.

## 2026-09-04 13:00 - Final validation / exit decision
- `dotnet build tools\specmine\evaluations\PaymentProcessing\process-experiment-1\model\model.csproj` succeeded with 0 warnings and 0 errors.
- Exit decision for `pp-f1`: good enough to stop.
  - No `Unknown` markers remain.
  - No `Provisional` markers remain.
  - The remaining `Assume` boundary is deliberate and narrow: the feature only claims adapter-generated `AuthorizePayment` requests that include a body object.
- Moved `pp-f1` to `past-roadmap.md`; left `pp-f2` as the sole active roadmap entry.

## 2026-09-04 13:06 - Re-entering for `pp-f2`
- Restarted `PaymentProcessing.Api`.
- Reconfirmed `GET /health` -> `200 {"status":"ok"}`.
- Picked up the remaining active roadmap entry `pp-f2` and kept the same black-box discipline: probe the live service only, do not read benchmark internals.

## 2026-09-04 13:07 - Black-box probing: lifecycle and retrieval
- `GET /payments/not-a-real-id` -> `404 {"type":"not_found","code":"payment_not_found","message":"Payment 'not-a-real-id' was not found."}`
- `POST /payments/not-a-real-id/capture` -> same `404 payment_not_found`.
- `POST /payments/not-a-real-id/void` -> same `404 payment_not_found`.
- Fresh success followed by retrieval:
  - authorize valid request -> `201` payment with `status:"authorized"`
  - immediate `GET /payments/{id}` -> `200` same payment, same fields, same status
- Capture branch:
  - `POST /payments/{id}/capture` from `authorized` -> `200` same payment with `status:"captured"`
  - `GET /payments/{id}` after capture -> `200` with `status:"captured"`
  - repeating capture on already captured payment -> `200` same captured payment again
  - `POST /payments/{id}/void` after capture -> `409 {"type":"conflict","code":"invalid_payment_state","message":"Payment '<id>': A captured payment cannot be voided."}`
- Void branch:
  - `POST /payments/{id}/void` from `authorized` -> `200` same payment with `status:"voided"`
  - `GET /payments/{id}` after void -> `200` with `status:"voided"`
  - repeating void on already voided payment -> `200` same voided payment again
  - `POST /payments/{id}/capture` after void -> `409 {"type":"conflict","code":"invalid_payment_state","message":"Payment '<id>': A voided payment cannot be captured."}`

## 2026-09-04 13:09 - Black-box probing: cross-feature authorize replay after lifecycle changes
- Exact replay of the original `AuthorizePayment` request after a capture returned `200` with the same payment id and fields but `status:"captured"`.
- Exact replay of the original `AuthorizePayment` request after a void returned `200` with the same payment id and fields but `status:"voided"`.
- Changed authorize payloads after capture/void (same key, different amount) still returned `409 idempotency_conflict`.
- Conclusion: once a payment exists, exact authorize replays surface the **current** persisted payment state, not the status from the original authorization response.

## 2026-09-04 13:11 - Modeling plan for `pp-f2`
- Extended the shared payment state from "successful authorizations keyed by idempotency key" to "known payments with current status", still keyed by idempotency key but queriable by payment id.
- Added three new modeled operations:
  - `GetPayment`
  - `CapturePayment`
  - `VoidPayment`
- Refined `AuthorizePayment` to use the current stored payment snapshot when replaying an already-bound key, so pp-f1 + pp-f2 can compose honestly.
- Scope decision: no `Unknown`/`Provisional` markers were necessary after the additional probes, because every lifecycle branch I chose to model was exercised directly and replayed cleanly.

## 2026-09-04 13:12 - Implementation + one build fix
- Added `PaymentByIdCall` / `PaymentPath` contract types and new response validators for:
  - `payment_not_found`
  - `invalid_payment_state` conflicts for captured->void and voided->capture
- Added `GetPaymentOperation`, `CapturePaymentOperation`, and `VoidPaymentOperation`.
- Extended the runner to regenerate the whole suite and added 6 pp-f2-oriented scenarios:
  - missing-payment get
  - missing-payment capture
  - missing-payment void
  - capture lifecycle + authorize replay + changed-authorize conflict + void-after-capture conflict
  - void lifecycle + authorize replay + changed-authorize conflict + capture-after-void conflict
  - capture then get current state
- First build issue for pp-f2: in a static helper I again mixed up operation-local `Expect` inference with the static `Expect.That<TResponse>(...)` API.
- Fix: used static generic `Expect.That<ApiCallResponse>(...)` and `ThenState<PaymentProcessingState>(...)` in the shared lifecycle helper.

## 2026-09-04 13:13 - Recording and replay results for `pp-f2`
- `dotnet run --project tools\specmine\evaluations\PaymentProcessing\process-experiment-1\model\model.csproj` completed successfully after the extension.
- The runner regenerated the full suite and recorded 14 completed traces total, including 6 that specifically exercise `pp-f2`:
  - `f59beebc-9e7d-4fdc-81b9-6fe9953e8c63` - get-missing-payment
  - `b21df58a-8c8a-4b1c-8517-42fe5a19ff5e` - capture-missing-payment
  - `76896ce2-b712-4d53-b2be-fe3360644308` - void-missing-payment
  - `461041a0-cf3e-4fc3-8e8d-aa01748e546c` - capture-lifecycle-and-authorize-replay
  - `f639094b-9621-4361-b50f-eca1a6c5e8df` - void-lifecycle-and-authorize-replay
  - `d4803915-d0ca-49fc-8bfe-50382a5e71bb` - capture-then-get-current-state
- Replayed all 14 traces with `UnderstandingStrictness.Reject`.
- Result: every replay finished `Conforming`; zero provisional matches, zero unknowns, zero out-of-scope hits, zero violations.

## 2026-09-04 13:14 - Exit decision for `pp-f2`
- `dotnet build tools\specmine\evaluations\PaymentProcessing\process-experiment-1\model\model.csproj` succeeded with 0 warnings and 0 errors after the extension.
- Exit decision for `pp-f2`: good enough to stop.
  - No remaining `Unknown` markers.
  - No remaining `Provisional` markers.
  - New `Assume` markers are only narrow adapter-envelope boundaries for payment-by-id requests.
- Moved `pp-f2` to `past-roadmap.md`.
