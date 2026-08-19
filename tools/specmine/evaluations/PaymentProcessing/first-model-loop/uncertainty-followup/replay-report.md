# Uncertainty-aware Payment replay

Replayed 11 immutable traces call-by-call through the M-002 behavior with explicit research annotations.

| Outcome | Calls |
| --- | ---: |
| Conforming | 14 |
| ProvisionalMatch | 21 |
| Unknown | 15 |
| ModelViolation | 0 |
| OperationNotModeled | 0 |
| ExecutionError | 0 |
| RequestDeserializationFailed | 0 |
| ResponseDeserializationFailed | 0 |

## Calls

- `15b55e70eb4b4105bc9c60be7dc26898.json` call 1 `Reset`: **Conforming**
- `15b55e70eb4b4105bc9c60be7dc26898.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'idem-A1' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `15b55e70eb4b4105bc9c60be7dc26898.json` call 3 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `a25e38b287394c76abae7f2caea10550.json` call 1 `Reset`: **Conforming**
- `a25e38b287394c76abae7f2caea10550.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'idem-B1' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `a25e38b287394c76abae7f2caea10550.json` call 3 `AuthorizePayment`: **Conforming**
- `a25e38b287394c76abae7f2caea10550.json` call 4 `AuthorizePayment`: **Conforming**
- `2a15d62d75ed433cbc7e9c1910d38ea5.json` call 1 `Reset`: **Conforming**
- `2a15d62d75ed433cbc7e9c1910d38ea5.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'idem-C1' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
- `2a15d62d75ed433cbc7e9c1910d38ea5.json` call 3 `AuthorizePayment`: **Unknown** — replay skipped because a prior unknown call left model state unreliable.
- `2a15d62d75ed433cbc7e9c1910d38ea5.json` call 4 `AuthorizePayment`: **Unknown** — prior unknown state; recovered successful authorization for downstream replay.
- `94890cdae77a4beca5f2dec71d576bbe.json` call 1 `Reset`: **Conforming**
- `94890cdae77a4beca5f2dec71d576bbe.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-D1' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `94890cdae77a4beca5f2dec71d576bbe.json` call 3 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `94890cdae77a4beca5f2dec71d576bbe.json` call 4 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `94890cdae77a4beca5f2dec71d576bbe.json` call 5 `AuthorizePayment`: **Conforming**
- `94890cdae77a4beca5f2dec71d576bbe.json` call 6 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `0c64d43345a947d18fb7fa2bc4c4cc61.json` call 1 `Reset`: **Conforming**
- `0c64d43345a947d18fb7fa2bc4c4cc61.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-E1' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `0c64d43345a947d18fb7fa2bc4c4cc61.json` call 3 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `0c64d43345a947d18fb7fa2bc4c4cc61.json` call 4 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-E2' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `0c64d43345a947d18fb7fa2bc4c4cc61.json` call 5 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `86e4f8006fdc4fdab9865bd17cf936f6.json` call 1 `Reset`: **Conforming**
- `86e4f8006fdc4fdab9865bd17cf936f6.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-F1' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `86e4f8006fdc4fdab9865bd17cf936f6.json` call 3 `CapturePayment`: **ProvisionalMatch** — first-capture-transition: Does first capture behave this way across more payments and boundary conditions?
- `86e4f8006fdc4fdab9865bd17cf936f6.json` call 4 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `80a65eb35e374238a8d8c32d64e85389.json` call 1 `Reset`: **Conforming**
- `80a65eb35e374238a8d8c32d64e85389.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-G1' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `80a65eb35e374238a8d8c32d64e85389.json` call 3 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `80a65eb35e374238a8d8c32d64e85389.json` call 4 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `80a65eb35e374238a8d8c32d64e85389.json` call 5 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `2a03ec1481fc462ca73ae0c8943c3634.json` call 1 `Reset`: **Conforming**
- `2a03ec1481fc462ca73ae0c8943c3634.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'live-promoted-11af16ba018947f7a492a9affee1474d' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `2a03ec1481fc462ca73ae0c8943c3634.json` call 3 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `2a03ec1481fc462ca73ae0c8943c3634.json` call 4 `CapturePayment`: **ProvisionalMatch** — first-capture-transition: Does first capture behave this way across more payments and boundary conditions?
- `2a03ec1481fc462ca73ae0c8943c3634.json` call 5 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `25190434b979491d82f60b0fdbd93847.json` call 1 `Reset`: **Conforming**
- `25190434b979491d82f60b0fdbd93847.json` call 2 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `25190434b979491d82f60b0fdbd93847.json` call 3 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `25190434b979491d82f60b0fdbd93847.json` call 4 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-boundary-pos-cent' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `25190434b979491d82f60b0fdbd93847.json` call 5 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `25190434b979491d82f60b0fdbd93847.json` call 6 `AuthorizePayment`: **ProvisionalMatch** — authorize-validation-boundary: Which exact amount and currency boundaries define a valid authorization request?
- `b7923945e6164a759e6df1272bb9f012.json` call 1 `Reset`: **Conforming**
- `b7923945e6164a759e6df1272bb9f012.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-void-live-replay' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `b7923945e6164a759e6df1272bb9f012.json` call 3 `VoidPayment`: **Unknown** — void-lifecycle-transition: Void behavior is not modeled; an observed successful transition may recover the live status for downstream replay.
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `b7923945e6164a759e6df1272bb9f012.json` call 4 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
- `eea9d9f580064b5dbbb7e37f69515f32.json` call 1 `Reset`: **Conforming**
- `eea9d9f580064b5dbbb7e37f69515f32.json` call 2 `AuthorizePayment`: **Unknown** — authorize-fresh-key-outcome: AuthorizePayment for idempotency key 'adv-capture-low' carries a valid payload but the key has no captured prior successful authorization. Whether such a request is authorized (201) or declined (200 DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it (see model\README.md, "Unknown regions" and "Why not Expect.OneOf").
  - Recovered model state from the observed response for downstream replay; the call remains Unknown.
- `eea9d9f580064b5dbbb7e37f69515f32.json` call 3 `CapturePayment`: **ProvisionalMatch** — first-capture-transition: Does first capture behave this way across more payments and boundary conditions?
- `eea9d9f580064b5dbbb7e37f69515f32.json` call 4 `AuthorizePayment`: **ProvisionalMatch** — authorize-live-replay: Does identical replay reflect the live record after every lifecycle transition?
