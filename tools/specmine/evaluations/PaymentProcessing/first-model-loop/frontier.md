# Frontier

Scope for this investigation pass: `AuthorizePayment` idempotency behavior only (reuse of an `idempotencyKey` across repeated calls, with identical vs. changed payloads). Capture/void lifecycle is explicitly out of scope.

Schema facts observed from the public OpenAPI document (`target\openapi.json`), no calls made yet:
- `AuthorizePaymentRequest` requires `idempotencyKey`, `amount`, `currency`.
- `POST /payments/authorize` documents three success/near-success shapes: `200` (`oneOf` `PaymentResponse` | `DeclinedPaymentResponse`), `201` (`PaymentResponse`, described "Payment authorized"), plus `400` and `409` (`ErrorResponse`).
- `PaymentResponse` carries `id`, `idempotencyKey`, `amount`, `currency`, `status`. `DeclinedPaymentResponse` carries only `status`, `reason` (no `id`).

The distinct `201` vs `200` codes, and the fact `200` alone can be either a live `PaymentResponse` or a `DeclinedPaymentResponse`, are the basis for the hypotheses below. These are working assumptions based on shape alone, not yet observed behavior.

## H-001: Identical replay is idempotent-safe
**Claim:** Calling `AuthorizePayment` again with the same `idempotencyKey` and an unchanged payload (same `amount`, same `currency`) returns `200` with a `PaymentResponse` body identical to the original authorization (`id`, `amount`, `currency`, `status` all match), rather than minting a second payment.
**Scope:** Only the case where key+amount+currency exactly match a prior successful (`201`) authorization, replayed immediately after, no capture/void in between.
**Falsified by:** the replay returning `201`, a different `id`, a `4xx`, or any of `amount`/`currency`/`status` differing from the original response.

## H-002: Changed payload under a reused key is rejected as a conflict
**Claim:** Calling `AuthorizePayment` again with the same `idempotencyKey` but a **different** `amount` and/or `currency` returns `409 Conflict` (`ErrorResponse`), rather than creating a second payment or silently applying the new values to the existing one.
**Scope:** Tested independently for (a) amount changed / currency unchanged and (b) currency changed / amount unchanged, both immediately after a prior `201` authorization under the same key.
**Falsified by:** either variant instead returning `200`/`201` with a payment reflecting the new amount/currency, or returning the *original* payment silently (no `409`) with no error signal.

## H-003: Does a business decline reserve the idempotency key? (competing pair, exploratory)
**H-003a:** A decline (`200` + `DeclinedPaymentResponse`) still reserves the key: retrying the identical key+payload after a decline reproduces the same decline outcome (or a `409`), not an independent new attempt.
**H-003b:** A decline does **not** reserve the key for success purposes: retrying the same key+payload (or a changed payload) after a decline is free to proceed as if the key were unused (e.g., can still yield `201`).
**Scope:** Contingent on first discovering, black-box, an input that reliably produces a decline; the trigger is unknown and not assumed. If no decline can be elicited within the call budget, this pair remains **UNKNOWN/untested**, not resolved in either direction.
**Falsified by:** whichever branch (a or b) the evidence contradicts — e.g., H-003a is falsified if a changed-payload retry after a decline succeeds with `201`; H-003b is falsified if any retry after a decline is blocked/rejected because of the key alone.

## Evidence

### Trace 1 — `traces\trace-1-same-key-identical-payload\15b55e70eb4b4105bc9c60be7dc26898.json`
`Reset` -> `AuthorizePayment(idem-A1, 100.00 USD)` = `201 {id: 99cf448f..., idempotencyKey: idem-A1, amount: 100.00, currency: USD, status: authorized}` -> repeat identical call = `200` with the **exact same body** (same `id`, `amount`, `currency`, `status`).

### Trace 2 — `traces\trace-2-same-key-changed-payload\a25e38b287394c76abae7f2caea10550.json`
`Reset` -> `AuthorizePayment(idem-B1, 100.00 USD)` = `201` (new payment) -> same key, amount changed to `250.00 USD` = `409 {type: conflict, code: idempotency_conflict, message: "The idempotency key was already used with a different request."}` -> same key, currency changed to `100.00 EUR` (against the *original* 100/USD authorization, not the failed 250 attempt) = `409` with the identical `ErrorResponse` body.

### Trace 3 — `traces\trace-3-decline-and-key-reservation\2a15d62d75ed433cbc7e9c1910d38ea5.json`
`Reset` -> `AuthorizePayment(idem-C1, 1,000,000.00 USD)` = `200 {status: declined, reason: limit_exceeded}` (first black-box input found that elicits a decline; no `id` in the body) -> identical retry (same key, same amount/currency) = `200` with the **same** decline body (`declined` / `limit_exceeded`), not `409` -> same key, amount changed to `50.00 USD` = `201 {id: 103f83c6..., ..., status: authorized}` — a **new** payment was created, not a `409` conflict.

## Status

**H-001 — SUPPORTED** (n=1). Identical-payload replay under the same key returned `200` with a byte-for-byte match on `id`/`amount`/`currency`/`status` against the original `201`. No counterevidence found.

**H-002 — SUPPORTED** (n=1 per variant, 2 variants). Both an amount-only change and a currency-only change against an already-successful (`201`) authorization, under the same key, independently produced `409` with the identical `idempotency_conflict` `ErrorResponse`. No new/updated payment was created in either case.

**H-003 — RESOLVED, asymmetric (not a simple a/b split).** Evidence splits the original competing pair:
- The **identical-payload** replay of a *declined* attempt behaves like H-003a: it reproduces the same declined outcome (not a fresh attempt, not `409`) — consistent with H-001's "identical payload -> identical cached outcome" pattern extended to declines.
- The **changed-payload** retry after a *declined* attempt behaves like H-003b, not like the post-success case in H-002: it was **not** rejected with `409`; it proceeded to a fresh authorization attempt and succeeded (`201`, new `id`).

This yields a corrected, minimal claim ready for modeling:

**H-004 (replacement, supported by Trace 3):** Idempotency-key conflict detection (`409`) is scoped to keys whose prior outcome was a *successful* (`201`) authorization. A key whose only prior outcome was a *decline* (`200` + `DeclinedPaymentResponse`) is not treated as "used" for conflict purposes when the payload changes — a new authorization attempt is permitted and can succeed. Identical-payload replay, however, is idempotent regardless of whether the cached outcome was a success or a decline.
**Falsified by:** a changed-payload retry after a decline instead returning `409`, or an identical-payload retry after a decline instead returning a *different* decline reason/outcome or a fresh new `id`.

## Explicit unknowns / not investigated (out of budget or out of role scope)
- Only one decline-triggering input was found (`amount = 1,000,000.00` -> `limit_exceeded`); other possible decline triggers/reasons are unexplored.
- The decline -> changed-payload -> success transition (H-004) has only **one observed instance**; not repeated for statistical confidence, and only one specific new amount (`50.00`) was tried.
- Conflict detection was tested for amount-only and currency-only mismatches, but not for both changed simultaneously, nor for cosmetic-only differences (e.g. `100.00` vs `100.0` string/number formatting, or key reuse with only whitespace differences).
- Whether a *third* call with the same key as a still-conflicting (`409`) chain behaves differently than the second was not tested (only one changed-payload attempt was made per key).
- Capture/void interaction with idempotency (e.g., does capturing a payment change how its authorize key behaves on replay?) is out of role scope for this pass and was not investigated.
- All observations are single-sample (`n=1`) per transition; repetition would strengthen but per instructions is not assumed to generalize on its own.

## Model M-001 (modeler pass) -- SUPERSEDED

> **Status: falsified by adversary pass 1 and superseded by M-002** (see "Model M-002" at
> the end of this file). The section is kept verbatim as the record of what M-001 actually
> claimed when it was attacked; `model\` no longer contains this revision.

**Source:** `model\` (buildable .NET 10 Accordant spec: `PaymentSpec.cs`, `PaymentModelState.cs`,
`PaymentContracts.cs`, `ReplayRunner.cs`). Full scope statement, unknowns, and falsification
strategies: `model\README.md`. Full per-call replay results: `model\replay-report.md`.

**Scope:** Only the `AuthorizePayment` **successful-key** idempotency region -- i.e., what
happens on a *second* `AuthorizePayment` call for an idempotency key that already has a
captured, real, successful (`201`) authorization. `Reset` is modeled trivially (clears state,
`204`) to establish initial state for traces. Nothing else is modeled.

**Claims accepted this revision:**
- **Claim 1 (== investigator H-001):** identical-payload replay of a successfully-authorized
  key returns the cached `PaymentResponse` verbatim as `200` (same `id`/`amount`/`currency`/`status`).
- **Claim 2 (generalizes investigator H-002):** a payload that differs in amount and/or
  currency, reused against a successfully-authorized key, is rejected as `409
  idempotency_conflict`. This generalizes the two independently-tested single-field-change
  cases (amount-only, currency-only) to "any deviation from the exact captured payload" --
  the simultaneous-both-fields-changed case was never itself tested and is flagged as an
  adversarial target.

**Evidence used:** trace 1 call 3 (Claim 1); trace 2 calls 3 & 4 (Claim 2). Trace 3 (the
decline region) contributes **no** accepted claim -- every AuthorizePayment call in it
(fresh-key decline, identical-decline replay, changed-payload-after-decline success) is
intentionally excluded from model acceptance, per the objective's directive not to make the
decline threshold or investigator H-003/H-004 precise merely to replay every trace.

**Why decline/fresh-key behavior is excluded rather than modeled as `OneOf`:** see
`model\README.md`, "Why not `Expect.OneOf`". In short: the choice between success and
decline for a given amount is (almost certainly) a deterministic business rule this
investigation has not characterized, not genuine protocol nondeterminism -- modeling it as
`OneOf(success, decline)` would misrepresent that epistemic gap as sanctioned
nondeterminism and make the model unable to ever flag a bug there. Accordant's `Apply` has
no native "unknown, skip" outcome, so the exclusion is implemented in the replay runner
(`ReplayRunner.cs`), which never calls `spec.Allows` for a key without a captured prior
success; it bootstraps state directly from the observed response instead (capturing the
server-generated id on a real `201`, capturing nothing on a decline).

**Replay result (reproduce with `cd model; dotnet build; dotnet run --no-build`):** 3
traces replayed, 6 steps conforming, **0 model violations**, 5 steps intentionally excluded
as not-applicable/unknown. Trace 1 and trace 2 are reported "Conforming (partial)" (their
substantive AuthorizePayment checks pass; one bootstrap call each is excluded). Trace 3 is
reported "Not applicable / unknown" (its only in-scope step is the trivial Reset; all three
AuthorizePayment calls belong to the excluded decline region). No trace or step is reported
conforming without having actually been checked via `spec.Allows`/`TraceReplayer`.

**Adversarial targets for the next pass (see model\README.md for full detail):**
1. Falsify Claim 1: any identical-payload replay of a successful key returning a different
   id, a fresh `201`, a changed field, or an error.
2. Falsify Claim 2 (core): an amount-only or currency-only changed replay that does *not*
   return `409 idempotency_conflict`.
3. Falsify Claim 2 (extrapolation): change **both** amount and currency simultaneously
   against a successful key -- if this is not a `409`, Claim 2 must be narrowed back to the
   two literally-tested single-field variants.
4. Probe Claim 1's equality definition with cosmetic-only differences (`100.0` vs `100.00`,
   currency case).
5. Characterize the decline threshold and decline-region behavior (H-003/H-004) further --
   currently 100% out of model scope, not something this model claims to get right or wrong.

## Adversary pass 1 -- results against model M-001

**Outcome: M-001 is FALSIFIED.** Two distinct counterexamples, 4 checked steps in model
violation across 3 of 4 new traces, all produced by the model's own
`spec.Allows`/`TraceReplayer` path with no change to the model. Budget: 4 traces (cap), 20
recorded operation calls (cap). Full sequences, responses and caveats: `journal.md`,
"Adversary pass 1".

### Counterexamples

**CX-1 -- request validation preempts idempotency conflict detection (in-region, falsifies Claim 2).**
Minimized sequence (3 calls, trace `trace-7-adv-minimized-invalid-payload-precedence`,
TraceId `80a65eb3-5e37-4238-a8d8-c32d64e85389`, calls 1-3):

```
Reset()                                        -> 204
AuthorizePayment{adv-G1, 100.00, "USD"}        -> 201 (id 6ca979150bd645068c6aafbe75693c77, authorized)
AuthorizePayment{adv-G1, 100.00, "usd"}        -> 400 {type: validation_error, code: invalid_request}
```

Model prediction: `409 {type: conflict, code: idempotency_conflict}` (the payload "differs
in currency" under ordinal string equality). Observed: `400 validation_error`. Exactly one
property of one field (the letter case of `currency`) differs from the establishing
request; the sequence cannot be shortened, because the key must first be established for
the model's `Apply` to be defined at all. First sighted at
`trace-5-adv-equality-boundaries` (TraceId `0c64d433-45a9-47d1-8fb7-fa2bc4c4cc61`) call 5,
which is preserved unchanged.

Discriminated by two further calls in the same trace: a fresh, never-used key with the same
malformed currency also returns `400` (call 4), and an invalid *amount* (`-5.00`) with a
valid currency against the established successful key also returns `400` (call 5, itself a
second model violation on a different field axis). The rule is therefore not about currency
and not about the replay path.

**CX-2 -- the idempotent replay returns the live payment record, not a frozen response snapshot (falsifies Claim 1).**
Sequence (4 calls, already minimal, trace `trace-6-adv-capture-then-identical-replay`,
TraceId `86e4f800-6fdc-4fda-b986-5bd17cf936f6`):

```
Reset()                                        -> 204
AuthorizePayment{adv-F1, 100.00, "USD"}        -> 201 (id e6baf3c27add442385d6c54cb48bbef7, status authorized)
CapturePayment{id: e6baf3c2...}                -> 200 (status captured)
AuthorizePayment{adv-F1, 100.00, "USD"}        -> 200 (id e6baf3c2..., amount 100.00, currency USD, status CAPTURED)
```

Model prediction: the cached authorization verbatim, `status == "authorized"`. Observed:
same `id`/`idempotencyKey`/`amount`/`currency`, but `status: "captured"`. Cannot be
shortened: the capture requires an existing payment, and the claim requires an established
successful key. This is a **scope-boundary** falsification -- `model\README.md` declares
capture/void "not represented at all" -- but `PaymentSpec.cs` states Claim 1
unconditionally for every key in `SuccessfulKeys`, with no precondition that would exclude
the call, so the runner checks it and it fails. Reported as a distinct category from CX-1,
which is a wrong prediction squarely inside the region the model claims to own.

### Attacks that found no disagreement (M-001 survived these; not proof)

| Attack | Trace / call | Observed | Verdict |
|---|---|---|---|
| Simultaneous amount **and** currency change (`250.00 EUR`) -- Claim 2's flagged extrapolation | trace 4 (`94890cda-...`) call 5 | `409 idempotency_conflict` | model correct (n=1) |
| Two consecutive identical replays (replay stability) | trace 4 calls 3-4 | `200` cached body both times, same `id` | model correct |
| Identical replay *after* a `409` (does a conflict poison the record?) | trace 4 call 6 | `200`, original cached body unchanged | model correct |
| Numerically equal, textually different amount (`100.0` vs `100.00`) | trace 5 call 3 | `200`, cached body (`amount: 100.00`) | model correct: decimal value equality is the right abstraction |

### Status of the modeler's numbered adversarial targets

1. Falsify Claim 1 by a differing id / fresh 201 / changed field / error -- **DONE via CX-2**
   (`status` changes). `id`, `amount`, `currency` remained stable under every attack tried.
2. Falsify Claim 2 core (amount-only / currency-only change not returning 409) -- **not
   falsified for *valid* payloads**; every well-formed changed payload still returned `409`.
3. Falsify Claim 2's both-fields-changed extrapolation -- **attempted, survived** (trace 4
   call 5). The extrapolation is now corroborated rather than merely assumed.
4. Probe Claim 1's equality definition with cosmetic differences -- **split result.** Amount
   representation (`100.0` vs `100.00`): model correct. Currency case (`usd` vs `USD`): the
   question is **unanswerable at this API surface**, because validation rejects any
   non-three-uppercase-letter currency before any idempotency comparison occurs (CX-1). This
   unknown does not resolve; it collapses into the validation rule.
5. Decline threshold -- **deliberately not attacked** (out of this pass's mandate).

## H-005: Request validation precedes idempotency handling (from CX-1)
**Claim:** `AuthorizePayment` validates the request body first and returns `400 {type:
validation_error, code: invalid_request}` for any request with a blank `idempotencyKey`, a
non-positive `amount`, or a `currency` that is not three uppercase letters -- regardless of
the key's history (unused, successfully authorized, or otherwise). Only a well-formed
request ever reaches idempotency lookup, so `409 idempotency_conflict` presupposes a valid
payload.
**Evidence:** trace 7 calls 3 (successful key, `usd`), 4 (fresh key, `usd`), 5 (successful
key, `-5.00`), all `400`; plus trace 5 call 5. The target's own message states the rule.
**Scope:** observed for the `currency`-case and negative-`amount` violations only. Blank /
whitespace `idempotencyKey`, zero amount, non-alphabetic 3-char currency, and validity
interaction with a *declined* key were not probed.
**Falsified by:** any invalid payload that returns something other than `400
validation_error/invalid_request` -- e.g. a `409` for an invalid payload under a used key,
or a `201` for an invalid payload under a fresh key.
**Status: ACCEPTED INTO THE MODEL as M-002 Claim V**, with two deliberate narrowings: the
blank-`idempotencyKey` axis is **not** modeled (never observed; the model stays silent rather
than adopting the rule from the target's own error message), and the claim's generalization
to "positive amount, `^[A-Z]{3}$` currency" is flagged as the model's most likely soft spot.
The key-history independence *is* modeled, because it was observed on both a fresh key and a
successful key (trace 7 calls 4 and 3/5); the declined-key history class remains untested.

## H-006: Idempotent replay reflects the payment's current state, not the original response (from CX-2)
**Claim:** An identical-payload replay of a successfully-authorized key returns `200` with
the payment resource **as it currently stands** -- stable `id`, `idempotencyKey`, `amount`,
`currency`, but a `status` that tracks the payment lifecycle (`authorized` -> `captured`
after `CapturePayment`). It is not a frozen snapshot of the original `201` body.
**Evidence:** trace 6 call 4 (n=1, capture only).
**Scope:** demonstrated for `CapturePayment` only. `VoidPayment` was never called.
**Falsified by:** a replay after a lifecycle transition that still reports the original
`status`, or that reports a status inconsistent with `GetPayment` for the same id.
**Status: ACCEPTED INTO THE MODEL as M-002 Claim R**, together with the minimum lifecycle
transition needed to know the live status (M-002 Claim L, the first `CapturePayment` of an
authorized payment). `VoidPayment` is still not modeled, so M-002 has no `voided` status --
the top adversarial target for the next pass.

### Remaining attack surface (untested, for the next pass)

- `VoidPayment` -> identical replay: does `status` become `voided` too (H-006 generalizes),
  and does a *changed*-payload replay after a void still return `409`?
- Changed-payload (valid) replay **after** a capture: still `409`, or does the lifecycle
  transition alter conflict handling?

## Model M-002 (modeler pass 2 -- minimal refinement after falsification)

**Source:** `model\` (buildable .NET 10 Accordant spec: `PaymentSpec.cs`, `ModelScope.cs`,
`PaymentModelState.cs`, `PaymentContracts.cs`, `ReplayRunner.cs`). Full scope statement,
evidence, unknowns and falsification strategies: `model\README.md`. Full per-call replay
results: `model\replay-report.md`.

### Counterexample classification

- **CX-1 = wrong claim, squarely inside the region M-001 owned; a *precedence* error.** The
  target validates the request body before it consults the idempotency key at all, so a
  malformed changed payload never reaches conflict detection. M-001's Claim 2 was not merely
  too broad on payload equality -- it was missing a rule that sits *in front of* it. Fixed by
  adding **Claim V** and making Claim C apply only to payloads that pass validation.
- **CX-2 = wrong claim at a declared scope boundary; a *state-representation* error.**
  M-001's README said capture/void was "not represented at all", but its Claim 1 was written
  unconditionally over every key in `SuccessfulKeys`, so the runner checked the post-capture
  replay and it failed. The substance is that an idempotent replay returns the **live payment
  record**, not a frozen response snapshot. Fixed by splitting state into stable identity/data
  vs. mutable lifecycle status (**Claim R**) and adding the single observed transition that
  moves that status (**Claim L**).

### Claims accepted this revision

| Claim | Statement | Evidence |
|---|---|---|
| **V** (new) | A payload with a non-positive `amount` or a currency that is not three uppercase letters is rejected `400 validation_error/invalid_request` **before** any idempotency handling, whatever the key's history; nothing is recorded. | trace 7 calls 3 (successful key), 4 (fresh key), 5 (negative amount); trace 5 call 5 |
| **R** (corrects M-001 Claim 1) | An identical-payload replay of a successfully-authorized key returns `200` with `id`/`idempotencyKey`/`amount`/`currency` exactly as authorized and `status` equal to the payment's **current** lifecycle status. | trace 1 call 3; trace 4 calls 3, 4, 6; trace 5 call 3; trace 6 call 4 (`captured`) |
| **C** (narrows M-001 Claim 2) | A **valid** payload differing in amount and/or currency, against a key whose payment is still `authorized`, is rejected `409 idempotency_conflict`. | trace 2 calls 3, 4; trace 4 call 5 (**both fields changed at once** -- M-001's flagged extrapolation, attacked and survived, so it is retained as corroborated) |
| **L** (new) | The first `CapturePayment` of a payment this model captured and believes is `authorized` returns `200` with identity/data unchanged and `status: captured`; the modeled status advances. | trace 6 call 3 (n=1) |

`Reset` remains trivial scaffolding (clears both state maps, `204`).

**Structural change:** every claim now states its precondition explicitly, in a shared
`ModelScope` classifier used by *both* the spec's `Apply` and the replay runner. This is the
direct structural fix for CX-2: a claim can no longer range over a region its evidence did not
cover, and the runner can no longer check a call the spec does not claim to own.

### Unknowns preserved (unchanged or newly named)

- **Fresh/declined-key success-vs-decline selection** -- still silent, still *not*
  `Expect.OneOf`; the reasoning in M-001's section stands unchanged. All 10 excluded replay
  steps are in this one region.
- **Decline-region behavior** (H-003/H-004) -- still encodes no rule; trace 3 is still
  reported "not applicable / unknown", not "passed".
- **Changed payload after a lifecycle transition** -- newly named unknown created by Claim C's
  narrowing (previously an unflagged over-claim).
- **`CapturePayment` 404 / re-capture / post-void regions, and `VoidPayment` entirely.**
- **Validation axes other than the two observed** (blank key, `amount == 0`, non-3-letter or
  non-alphabetic or mixed-case currency).
- **Currency-case equality for conflict purposes** -- unanswerable at this API surface; it
  collapses into Claim V rather than resolving.

### Replay result

Reproduce with `cd model; dotnet build; dotnet run --no-build`. **7 traces, 31 recorded steps:
21 conforming, 0 model violations, 10 intentionally excluded** (all fresh/declined-key
selection). Checked steps by claim: Reset 7, R 6, V 4, C 3, L 1. Traces 1, 2, 4, 5, 6, 7 are
"conforming (partial)"; **trace 3 remains "not applicable / unknown"** because its only checked
step is the trivial `Reset`. Both counterexample traces now conform: trace 7's three `400`s are
checked under Claim V, and trace 6's capture and post-capture replay are checked under Claims L
and R. Nothing was reported conforming without a real `spec.Allows`/`TraceReplayer` check.

Predicates were confirmed load-bearing (not vacuous) by replaying six mutated copies of the real
traces in a throwaway sandbox, including one that reports the frozen `authorized` status after a
capture -- M-001's exact prediction, which M-002 rejects. All six were flagged as violations; a
seventh mutant (valid changed payload after capture) was correctly *excluded*, confirming Claim
C's new precondition is real.

### Adversarial targets for the next pass

1. **Claim V's generalization** (the most likely soft spot): `amount: 0`, `"US"`, `"EURO"`,
   `"1US"`, `"Usd"`, whitespace key.
2. **`VoidPayment` -> identical replay:** M-002 has no `voided` status; if a replay reports one,
   Claims R/L need the void transition.
3. **Valid changed payload after a capture:** M-002 deliberately predicts nothing -- cheapest
   unknown to convert into evidence.
4. **Claim L's preconditions:** capture an unknown id (`404` region) or the same payment twice
   (`409` region).
5. **Claim R's stability / Claim C's core** -- the surviving M-001 attacks, still open.
6. **Invalid payload against a *declined* key** -- the one key-history class where Claim V has
   no evidence.

### Promotion candidate (for the eventual live conformance test) -- **STATUS: PROMOTED**

`Reset -> AuthorizePayment(fresh key, 100.00 USD) -> AuthorizePayment(same key, identical
payload) -> CapturePayment(id from the 201) -> AuthorizePayment(same key, identical payload)`.
It exercises Claims R and L plus the CX-2 regression in one sequence, needs only the
response-derived payment id (no unknown region is touched), and every step after the
establishment call is checkable by `spec.Allows`. The establishment `201` itself must be treated
as setup, not as an assertion, because fresh-key outcome selection is still unmodeled.

**Promoted as `tests\PaymentConformance.Tests\AuthorizeCaptureLifecycleConformanceTests.cs`**,
with one deliberate extension of this candidate: an extra identical-payload replay is inserted
*before* the capture (so the checked sequence is Reset -> setup(201, excluded) -> identical
replay #1 (Claim R, `authorized`) -> Capture (Claim L) -> identical replay #2 (Claim R,
`captured`)). This exercises Claim R in both live lifecycle states within one execution instead
of only the post-capture one, without touching any unknown region. Executed once against the
real running target (M-002, unchanged) via `dotnet test`: **PASSED**, all 4 checked steps
(Reset, both replays, capture) reported `Conforming` by `Specmine.Accordant.TraceReplayer`
(`Spec<PaymentModelState>.Allows` under the hood); the setup authorization was correctly
excluded and bootstrapped from a real `201`. The execution's `RecordedTrace` was preserved
at `traces\trace-8-live-authorize-replay-capture-replay\2a03ec1481fc462ca73ae0c8943c3634.json`
(TraceId `2a03ec14-81fc-462c-a73a-e0c8943c3634`). Full rationale, friction notes and how
validation flows through `spec.Allows`: `journal.md`, "Test promotion". This does not resolve
any unknown region below -- all remain open for the next adversary/investigator pass.
- Blank / whitespace-only `idempotencyKey`, `amount: 0`, `amount: 0.001`, non-alphabetic
  3-character currency, unknown-but-well-formed currency (e.g. `ZZZ`): boundary of H-005's
  validation rule.
- Invalid payload against a **declined** key (interaction of H-005 with the decline region).
- Whether `409` conflict comparison is itself case- or representation-sensitive is
  **unreachable** through this API while H-005 holds -- any probe that would answer it is
  rejected as invalid first. A modeler must treat currency-case equality as unobservable,
  not as decided.
- `GetPayment` was never called; whether the replay body and `GetPayment` agree after a
  lifecycle transition is untested (would strengthen or refute H-006's "live record" framing).
- Everything in the decline region remains untouched by this pass, by instruction.
