# Journal

## Initialization

Created a fresh black-box workspace from the Payment target's public OpenAPI document. No experiments or model exist yet.

## Investigator pass 1 — AuthorizePayment idempotency (bounded evidence gathering)

**Scope:** `AuthorizePayment` idempotency-key reuse only (identical vs. changed payload). Capture/void lifecycle intentionally not investigated. No Accordant repo, benchmark source, git history, or hidden ground truth was consulted — only `target\openapi.json`, the SDK DLLs under `sdk\`, and live traces through the recording session were used.

**Tooling notes (SDK plumbing, established via a disposable scratch reflection/plumbing probe, not preserved):**
- `Workspace.LoadAsync(workspaceRoot)` loads `workspace.json`; `TargetAdapterRegistry` + `OpenApiTargetAdapter` + `WorkspaceActivator.ConnectAsync` produce an `ITargetSession` bound to `http://127.0.0.1:5088`.
- `TraceRecorder.RunAsync(name, session, body)` returns `(RecordedTrace, string)`; the trace is **not** auto-saved. `TraceStore.SaveAsync(directory, trace)` (static) writes `<directory>\<traceIdHex>.json` — used `traces\<trace-name>\<traceIdHex>.json` per trace for organization.
- OpenAPI operation calls use `rec.ExecuteAsync(operationName, JsonElement, ct)` with a request shaped `{ "path": {...}, "query": {...}, "headers": {...}, "body": {...} }` (unused sections omitted, e.g. `Reset` needs only `{}`); responses come back as `{ "status": <int>, "body": <json|null> }`.

**Operation-call budget used:** 3 traces, 3 `Reset` calls (free per role instructions) + 9 substantive operation calls (`AuthorizePayment` x9) = 9 of the 15-call cap on real evidence calls, well within bounds.

### Trace 1 — `trace-1-same-key-identical-payload` (TraceId `15b55e70-eb4b-4105-bc9c-60be7dc26898`, file `traces\trace-1-same-key-identical-payload\15b55e70eb4b4105bc9c60be7dc26898.json`)
Sequence: `Reset()` -> `AuthorizePayment({idempotencyKey: idem-A1, amount: 100.00, currency: USD})` -> `AuthorizePayment({idempotencyKey: idem-A1, amount: 100.00, currency: USD})` (byte-identical repeat).
Observed: call 2 = `201` new payment `id=99cf448fd189440b8bd8be9b2a04165a`, `status=authorized`. Call 3 = `200`, same `id`, same `amount`/`currency`/`status` — exact match to call 2's body.

### Trace 2 — `trace-2-same-key-changed-payload` (TraceId `a25e38b2-8739-4c76-abae-7f2caea10550`, file `traces\trace-2-same-key-changed-payload\a25e38b287394c76abae7f2caea10550.json`)
Sequence: `Reset()` -> `AuthorizePayment({idem-B1, 100.00, USD})` -> `AuthorizePayment({idem-B1, 250.00, USD})` (amount changed) -> `AuthorizePayment({idem-B1, 100.00, EUR})` (currency changed, vs. the original amount).
Observed: call 2 = `201` (`id=40ce4a9d8a0342e1972ee4faa65564c8`). Call 3 = `409 {type: conflict, code: idempotency_conflict, message: "The idempotency key was already used with a different request."}`. Call 4 = `409` with the identical `ErrorResponse` body. No second payment resource was created in either case.

### Trace 3 — `trace-3-decline-and-key-reservation` (TraceId `2a15d62d-75ed-433c-bc7e-9c1910d38ea5`, file `traces\trace-3-decline-and-key-reservation\2a15d62d75ed433cbc7e9c1910d38ea5.json`)
Sequence: `Reset()` -> `AuthorizePayment({idem-C1, 1000000.00, USD})` (probe for a decline trigger) -> since it declined: `AuthorizePayment({idem-C1, 1000000.00, USD})` (identical retry) -> `AuthorizePayment({idem-C1, 50.00, USD})` (changed payload after a decline).
Observed: call 2 = `200 {status: declined, reason: limit_exceeded}` (no `id` field present, matching the `DeclinedPaymentResponse` schema). Call 3 = `200`, same declined body (same reason), not `409`. Call 4 = `201 {id: 103f83c688be439aa3d879e10137205a, amount: 50.00, currency: USD, status: authorized}` — a brand-new successful payment, not a conflict.

**Limitations:** every transition above is a single sample (n=1); no statistical replication was attempted. Only one decline-triggering input was found (a very large amount) and not characterized further (e.g., threshold boundary, other currencies, negative/zero amounts were not needed once the large-amount probe worked). Capture/void interactions with idempotency were not investigated (out of role scope). See `frontier.md` "Explicit unknowns" for the full list.

**Smallest behavior region ready for modeling:** a 3-outcome `AuthorizePayment` state machine keyed by `idempotencyKey`, where (1) a fresh key attempts authorization and yields either `201` (success, persisted `PaymentResponse`) or `200` (decline, ephemeral `DeclinedPaymentResponse`, no persisted id); (2) replay with an identical payload always returns the cached prior outcome verbatim, whether success or decline; (3) replay with a changed payload against a key whose cached outcome was a *success* is rejected with `409 idempotency_conflict`; (4) replay with a changed payload against a key whose cached outcome was a *decline* is **not** blocked and may proceed to a new authorization attempt.

## Modeler pass 1 -- AuthorizePayment successful-key model (M-001)

**Scope decision:** built only the "successful-key" idempotency region (frontier.md H-001,
H-002), per the objective's directive to model coherently evidence-backed behavior and not
invent a precise decline-threshold rule or treat any decline-region outcome as universally
legitimate. Decline/fresh-key behavior (H-003/H-004, trace 3) was deliberately left
unmodeled -- see `model\README.md` and the new "Model M-001" section of `frontier.md` for
the full reasoning.

**Durable artifacts created (all under `model\`):**
- `PaymentModel.csproj` -- buildable .NET 10 console project referencing
  `..\sdk\Accordant.dll`, `Accordant.Invariant.dll`, `Accordant.Operations.dll`,
  `Specmine.dll`, `Specmine.Accordant.dll`, with `Accordant.SourceGenerator.dll` wired in as
  a Roslyn `<Analyzer>` for `[State]`. Needed an explicit `System.IO.Hashing` 9.0.7
  `PackageReference` (resolved from the local NuGet cache; the referenced version had to
  match Accordant.dll's exact dependency version, 9.0.0.7, to avoid CS1705).
- `PaymentModelState.cs` -- `[State] PaymentModelState.SuccessfulKeys` : a dictionary from
  idempotency key to the captured `CapturedAuthorization` (server id/amount/currency),
  populated only from real observed `201` responses.
- `PaymentContracts.cs` -- typed request/response envelope contracts matching the OpenAPI
  trace shape (`{path,query,headers,body}` / `{status,body}`) for `Reset` and
  `AuthorizePayment`, deserialized with `ReplayJsonOptions.CreateDefault()`.
- `PaymentSpec.cs` -- the `Spec<PaymentModelState>`. `AuthorizePayment`'s `Apply` throws for
  any key without a captured prior success (see the class remarks for the rationale: this
  is an intentional exclusion boundary, not a bug). Confirmed via a temporary, since-removed
  in-memory self-check that the model actually rejects a wrong-id identical replay and a
  wrongly-accepted `201` for a changed payload -- the predicates are load-bearing, not
  vacuous.
- `ReplayRunner.cs` -- the replay runner/report generator. Per call: Reset is always checked
  via a real `spec.Allows`/`TraceReplayer` pass; an `AuthorizePayment` call is checked the
  same way *only if* its idempotency key is already in `SuccessfulKeys`, otherwise it is
  excluded and, only on an observed real `201`, its id/amount/currency are captured directly
  into state (bypassing `Apply` entirely) so later calls under that key become checkable.
- `README.md` -- scope, claims, unknown regions, evidence trace IDs, falsification
  strategies, and why `Expect.OneOf` was rejected for the decline threshold.
- `replay-report.md` -- generated by `dotnet run --no-build` from `model\`: 3 traces
  replayed, 6 steps conforming, **0 model violations**, 5 steps intentionally excluded.
  Trace 1 & 2 verdict "Conforming (partial)"; trace 3 verdict "Not applicable / unknown"
  (its only in-scope step is the trivial Reset).

**Next role:** an adversary, per `controller-journal.md`'s planned loop, with read-only
model access, targeting the adversarial list at the end of `model\README.md` /
`frontier.md`'s "Model M-001" section (Claim 2's both-fields-changed extrapolation and
cosmetic-equality assumption are the most likely soft spots; the decline region is
explicitly out of scope, not a target since no claim is made there).

## Adversary pass 1 -- falsification attempts against model M-001

**Role scope:** read-only against `model\`; no Accordant repo, benchmark source, tests,
README beyond `model\`, git history, parent directories, or other sessions were consulted.
Only `model\` (read), `traces\`, `target\openapi.json`, `frontier.md`, `journal.md`,
`controller-journal.md`, `sdk\docs\`, and the live target were used. The target was treated
as a black box.

**Budget used:** 4 new traces (the cap), 20 recorded operation calls (the cap), consisting
of 4 `Reset` + 15 `AuthorizePayment` + 1 `CapturePayment`. One additional non-recorded
liveness probe (`GET /health` via `curl`, returned `{"status":"ok"}`) was made before
spending any trace budget; it is not part of any trace and made no state change.

**Result: 2 distinct counterexamples found, 4 checked steps in model violation across 3 of
the 4 new traces.** Four other attacks found no disagreement. The model source, model
README, and `model\replay-report.md` were not modified (the existing report's SHA-256,
`3B7C512A75EFA0B3D85900A657F27E578E39ED75642D032AB237C65FAD1D7ABF`, is unchanged; the
replay described below was pointed at a disposable sandbox workspace root under `scratch\`
so the runner wrote its regenerated report there instead).

### Trace 4 -- `trace-4-adv-both-changed-and-post-conflict`
TraceId `94890cda-e77a-4bec-a5f2-dec71d576bbe`,
file `traces\trace-4-adv-both-changed-and-post-conflict\94890cdae77a4beca5f2dec71d576bbe.json`.
Attacks: Claim 2's flagged both-fields-changed extrapolation; repeated-replay stability;
altered payload after repeated replay; identical replay after a 409 (history mutation
inside the successful-key region).

| Call | Request | Observed |
|---|---|---|
| 1 | `Reset {}` | `204` |
| 2 | `AuthorizePayment {adv-D1, 100.00, USD}` | `201 {id: f6c5a4b5cf1a4beb8264fc51821d8109, amount: 100.00, currency: USD, status: authorized}` |
| 3 | `AuthorizePayment {adv-D1, 100.00, USD}` (identical replay #1) | `200`, same `id`/`amount`/`currency`/`status: authorized` |
| 4 | `AuthorizePayment {adv-D1, 100.00, USD}` (identical replay #2) | `200`, identical body again |
| 5 | `AuthorizePayment {adv-D1, 250.00, EUR}` (**both** fields changed at once) | `409 {type: conflict, code: idempotency_conflict, message: "The idempotency key was already used with a different request."}` |
| 6 | `AuthorizePayment {adv-D1, 100.00, USD}` (identical replay *after* the 409) | `200`, original cached body unchanged (`id` f6c5a4b5..., `status: authorized`) |

**No disagreement.** Claim 2's extrapolation to simultaneous amount+currency change is
corroborated (n=1); repeated replay is stable; a 409 does not poison or overwrite the
cached record.

### Trace 5 -- `trace-5-adv-equality-boundaries`
TraceId `0c64d433-45a9-47d1-8fb7-fa2bc4c4cc61`,
file `traces\trace-5-adv-equality-boundaries\0c64d43345a947d18fb7fa2bc4c4cc61.json`.
Attacks: Claim 1's decimal-value equality on amount; Claim 2's ordinal string equality on
currency. Two independent keys were used so neither probe could contaminate the other
(and so a violation in one could not truncate the other's replay batch).
All request bodies were emitted as raw JSON text, so the on-the-wire number literal
(`100.0` vs `100.00`) is exactly as written -- confirmed in the saved trace file.

| Call | Request | Observed |
|---|---|---|
| 1 | `Reset {}` | `204` |
| 2 | `AuthorizePayment {adv-E1, 100.00, USD}` | `201 {id: 3895bfb8ac0a4cbcb455a1e67262a5e7, ...}` |
| 3 | `AuthorizePayment {adv-E1, 100.0, USD}` (numerically equal, textually different) | `200`, cached body verbatim (`id` 3895bfb8..., `amount: 100.00`) |
| 4 | `AuthorizePayment {adv-E2, 100.00, USD}` | `201 {id: bca52fe630844d91a8e743203e6cc86e, ...}` |
| 5 | `AuthorizePayment {adv-E2, 100.00, usd}` (lowercase currency) | **`400 {type: validation_error, code: invalid_request, message: "idempotencyKey must be nonblank, amount must be positive, and currency must be three uppercase letters."}`** |

Call 3: no disagreement -- decimal *value* equality is the right abstraction on the amount
axis; `100.0` is treated as an identical payload, not a changed one.
Call 5: **COUNTEREXAMPLE CX-1 (first sighting).** The model predicts `409
idempotency_conflict` (its `prior.Currency == body.Currency` ordinal comparison makes
`usd` a "changed payload"); the target returned `400 validation_error`.

### Trace 6 -- `trace-6-adv-capture-then-identical-replay`
TraceId `86e4f800-6fdc-4fda-b986-5bd17cf936f6`,
file `traces\trace-6-adv-capture-then-identical-replay\86e4f8006fdc4fdab9865bd17cf936f6.json`.
Attack: an operation-history mutation that stays inside the successful-key region --
`CapturePayment` (an operation M-001 does not model at all) interleaved between the
establishing `201` and an otherwise byte-identical replay.

| Call | Request | Observed |
|---|---|---|
| 1 | `Reset {}` | `204` |
| 2 | `AuthorizePayment {adv-F1, 100.00, USD}` | `201 {id: e6baf3c27add442385d6c54cb48bbef7, status: authorized}` |
| 3 | `CapturePayment {path: {id: e6baf3c27add442385d6c54cb48bbef7}}` | `200 {id: e6baf3c2..., idempotencyKey: adv-F1, amount: 100.00, currency: USD, status: captured}` |
| 4 | `AuthorizePayment {adv-F1, 100.00, USD}` (identical replay) | **`200 {id: e6baf3c2..., idempotencyKey: adv-F1, amount: 100.00, currency: USD, status: captured}`** |

**COUNTEREXAMPLE CX-2.** Claim 1 requires the replay to return the captured authorization
*verbatim*, explicitly including `status == "authorized"`. The replay instead reflected the
payment's **current** lifecycle state (`captured`). `id`, `idempotencyKey`, `amount` and
`currency` were all stable; only `status` moved.

### Trace 7 -- `trace-7-adv-minimized-invalid-payload-precedence` (minimization of CX-1)
TraceId `80a65eb3-5e37-4238-a8d8-c32d64e85389`,
file `traces\trace-7-adv-minimized-invalid-payload-precedence\80a65eb35e374238a8d8c32d64e85389.json`.

| Call | Request | Observed |
|---|---|---|
| 1 | `Reset {}` | `204` |
| 2 | `AuthorizePayment {adv-G1, 100.00, USD}` | `201 {id: 6ca979150bd645068c6aafbe75693c77, status: authorized}` |
| 3 | `AuthorizePayment {adv-G1, 100.00, usd}` | **`400 validation_error / invalid_request`** |
| 4 | `AuthorizePayment {adv-G2, 100.00, usd}` (fresh key, same malformed currency) | `400 validation_error / invalid_request` |
| 5 | `AuthorizePayment {adv-G1, -5.00, USD}` (valid currency, invalid amount, successful key) | **`400 validation_error / invalid_request`** |

**Minimization.** Calls 1-3 are the minimized counterexample for CX-1: three calls, two of
them substantive, exactly one field differing from the establishing request by exactly one
property (letter case of `currency`). It cannot be shortened further -- the successful key
must be established before the model's `Apply` is even defined, and `Reset` is required to
start from a known state. Trace 5 reached the same disagreement in 5 calls with a spare
key and a second probe; trace 7 strips both away.

**Parameter minimization / discrimination (calls 4 and 5).** These separate two competing
explanations of CX-1:
- Call 4 shows the `400` is a property of the *request alone*: a fresh, never-used key with
  the same malformed currency gets the identical `400`. So the `400` is not produced by the
  idempotency-replay path.
- Call 5 shows the precedence is not currency-specific: an invalid *amount* (`-5.00`) with a
  perfectly valid currency, against an established successful key, also returns `400` and
  never reaches the conflict check.

Together: **request validation runs before idempotency lookup**, and any invalid request
short-circuits to `400 validation_error/invalid_request` regardless of key history. The
target's own error message states the validation rule verbatim: `idempotencyKey must be
nonblank, amount must be positive, and currency must be three uppercase letters.` Call 5 is
independently a second instance of the same violation class (the runner flags it as a
separate model violation), on a different field axis than CX-1.

### Replay through the unmodified model

Ran the model's own runner with no source changes: `cd model; dotnet build; dotnet run
--no-build -- <sandbox>`, where `<sandbox>` is a throwaway workspace root under
`scratch\replay-sandbox\` holding a copy of `traces\` plus an empty `model\` directory. The
explicit-root argument is a documented feature of `ReplayRunner` (see `model\README.md`)
and was used purely so the regenerated report landed in the sandbox rather than
overwriting `model\replay-report.md`. The runner classified all 7 traces and exited `2`.

| Trace | Runner verdict |
|---|---|
| 1, 2 | CONFORMING (partial) -- unchanged from the modeler's run |
| 3 | NOT APPLICABLE / UNKNOWN -- unchanged |
| 4 (adv) | CONFORMING (partial) -- 4 checked steps conforming, incl. the both-fields-changed 409 and the post-conflict replay |
| 5 (adv) | **MODEL VIOLATION** at call 5 (CX-1) |
| 6 (adv) | **MODEL VIOLATION** at call 4 (CX-2) |
| 7 (adv) | **MODEL VIOLATION** at calls 3 and 5 (CX-1 minimized, plus the amount-axis instance) |

Totals: 7 traces, 15 steps conforming, **4 steps in model violation**, 12 steps excluded as
not-applicable/unknown. Every violation was produced by a real `spec.Allows`/`TraceReplayer`
check inside the model's own declared successful-key region -- none is an artifact of the
runner's exclusion logic, and none required touching the model.

### Limitations and honest caveats

- **Nothing here proves the model correct.** Four attacks found no disagreement
  (simultaneous amount+currency change; two consecutive identical replays; identical replay
  after a `409`; `100.0` vs `100.00`). Each is a single sample against a black box; they
  raise confidence in the surviving parts of Claims 1 and 2 but do not establish them.
- **CX-1 is an in-region falsification.** The key was in `SuccessfulKeys`, so the model's
  `Apply` was defined and made a definite, wrong prediction. It is not a scope artifact.
- **CX-2 is a scope-boundary falsification, and should be reported as such.** `model\README.md`
  already says capture/void is "not represented at all"; but Claim 1 as *written in
  `PaymentSpec.cs`* is unconditional over any key in `SuccessfulKeys`, so the runner checks
  it and it fails. The model has no guard (precondition, or state tracking) that would make
  this call out of scope, and `ReplayRunner` classifies the unmodeled `CapturePayment` as
  "not applicable" and then keeps checking. Whether a modeler treats this as "the claim is
  wrong" or "the model must declare a precondition" is a modeling decision, but it cannot be
  left as-is: the current text says `status` never changes, and it does.
- **The currency-case equality question the model flagged as adversarial target #4 is not
  answerable at this API surface.** Validation rejects any non-3-uppercase-letter currency
  before idempotency comparison ever happens, so whether the target would treat `usd` as the
  *same* currency as `USD` for conflict purposes cannot be observed. That unknown collapses
  into the validation rule rather than being resolved.
- **Untested combinations remain**, listed in `frontier.md` under "Remaining attack surface".
  In particular: `VoidPayment` was never called (CX-2 was demonstrated with capture only);
  no invalid-payload probe was run against a *declined* key; whitespace/blank
  `idempotencyKey` was never probed; and no attack was made on the decline region, per the
  role's instruction not to spend budget characterizing the decline threshold.
- **Runner limitation, not a conformance result:** `TraceReplayer` stops a batch at its
  first violation, so a checked call following a violation within the same consecutive
  in-scope batch may go unreported. This did not bite here (trace 7's call 5 was reported
  because the out-of-scope call 4 forced a batch flush between them), but it means "absent
  from the report" must not be read as "conforming".

## Modeler pass 2 -- refinement of M-001 into M-002 after falsification

**Role scope:** model refinement only. No live target calls, no conformance tests, no
Accordant repo / benchmark source / tests / git history / parent directories / other
sessions consulted. Reads: `traces\` (all 7), `frontier.md`, `journal.md`,
`controller-journal.md`, `model\` (M-001 sources, README, replay report), `sdk\docs\`,
`target\openapi.json`, `workspace.json`. Writes: `model\` only, plus the model/counterexample
sections of `frontier.md` and this journal entry. Traces, scratch, SDK, target and the
controller journal were not modified.

### Counterexample classification

- **CX-1 -> validation precedence (in-region wrong claim).** Not a payload-equality bug: the
  target validates the body *before* consulting the idempotency key, so M-001's Claim 2 was
  missing a rule that sits in front of it. The adversary's own discrimination calls (trace 7
  call 4, fresh key + `usd`; call 5, successful key + `-5.00`) show the `400` is a property of
  the request alone, on two independent field axes and two different key-history classes.
- **CX-2 -> live-record replay (scope-boundary wrong claim).** M-001 declared capture/void
  unrepresented but wrote Claim 1 unconditionally over every key in `SuccessfulKeys`, so the
  post-capture replay was checked and failed. The substantive fact is that a replay returns the
  payment record *as it currently stands*, not a frozen response snapshot.

### M-002 -- what was added, and why it is the smallest fix

- **Claim V (new).** Invalid payload (non-positive amount, or currency not three uppercase
  letters) -> `400 validation_error/invalid_request`, before idempotency handling, regardless of
  key history. Modeled as key-history-independent because that is exactly what was observed
  (fresh key *and* successful key); this does **not** touch the fresh-key success/decline
  selection, which only arises for *valid* payloads and stays unknown. Deliberately **not**
  modeled: the blank-`idempotencyKey` axis mentioned in the target's error message but never
  exercised by any call -- `ModelScope` routes such requests to an unknown region instead.
  The public OpenAPI document declares no `pattern`/`minimum` constraints at all (only
  `required` + types), so every part of this rule rests on observed counterexamples; the
  generalization from "lowercase 3-letter" to `^[A-Z]{3}$` and from "negative" to
  "non-positive" is flagged in `model\README.md` as the model's most likely soft spot.
- **Claim R (corrects Claim 1).** Identity/data fields checked exactly as before; `status`
  checked against the payment's current modeled lifecycle status.
- **Claim L (new, minimal).** The first `CapturePayment` of a payment believed `authorized`
  returns `200` with identity/data unchanged and `status: captured`, and advances the modeled
  status. This is the only lifecycle transition added -- without it the model cannot know that
  the live status changed, which is precisely what CX-2 exposed. `VoidPayment`, capture `404`,
  re-capture and post-void capture stay unknown.
- **Claim C (narrowed Claim 2).** Kept for valid changed payloads, *including* the
  simultaneous amount+currency change that the adversary corroborated (trace 4 call 5), but now
  carries the precondition its evidence actually had: the payment is still `authorized`. A
  changed payload after a capture became a newly named unknown rather than an unflagged
  over-claim.
- **State split.** `SuccessfulKeys` (key -> stable `{PaymentId, IdempotencyKey, Amount,
  Currency}`) and `LifecycleStatuses` (payment id -> `authorized|captured`) are now separate, so
  "what never changes" and "what moves" are structurally distinct.
- **`ModelScope.cs` (new).** One classifier naming every modeled claim and every unknown region,
  used by both `Apply` (which throws outside its region) and the runner (which never checks a
  call outside it). This is the structural fix for CX-2: preconditions can no longer live only
  in prose.

### Bootstrap/scope cleanup inside `model\` (documented, no SDK bridge change)

`ReplayRunner` now replays **one call at a time** instead of batching consecutive in-scope
calls. Two reasons: `CapturePayment` mutates state mid-trace, so a batched successor would be
classified against stale lifecycle status; and the adversary's noted limitation ("`TraceReplayer`
stops a batch at its first violation, so a later checked call can vanish from the report") is
eliminated -- every recorded call now gets its own verdict. Response-derived payment-id capture
is retained and extended: a real `201` under a fresh/declined key still bootstraps identity/data
verbatim from the observed response, now together with an initial `authorized` status; nothing is
captured from a decline, a `400`, or an unmodeled capture. The report additionally names, per
step, which claim checked it or which unknown region excluded it, and summarizes both.

### Replay of all accumulated evidence

`cd model; dotnet build; dotnet run --no-build` -> exit `0`. **7 traces, 31 recorded steps: 21
conforming, 0 model violations, 10 excluded** (all in the fresh/declined-key success-vs-decline
region: 7 establishment `201`s bootstrapped without being checked, plus trace 3's two declines and
its post-decline success). Checked by claim: Reset 7, R 6, V 4, C 3, L 1. Traces 1, 2, 4, 5, 6, 7
= "conforming (partial)"; **trace 3 = "not applicable / unknown"** -- its decline-region substance
is still unmodeled, so it is reported unknown rather than passed. Both counterexamples now conform
(trace 7 calls 3-5 under Claim V; trace 6 calls 3-4 under Claims L and R).

**Non-vacuity check (temporary, since deleted).** Six mutated copies of the real traces were
replayed through the same runner in a throwaway sandbox under `model\selfcheck-sandbox\` (created
and removed within this pass; `scratch\` was not touched): `409` where Claim V requires `400`;
post-capture replay reporting the frozen `authorized` status -- **M-001's exact prediction**;
replay reporting `captured` with no capture; `201` where Claim C requires `409`; capture reporting
`authorized`; replay returning a different payment id. All six were flagged as model violations
(7 violating steps, since the mutated capture also invalidates the following replay). A seventh
mutant -- a valid *changed* payload after a capture -- was correctly reported not-applicable/unknown
rather than checked, confirming Claim C's narrowed precondition is real and that `Apply`'s
out-of-region guard is never reached through the runner.

### Honest limitations of M-002

- Claim L rests on a single observation (n=1); Claim R's `captured` case likewise.
- Claim V's rule text is generalized beyond the two observed violation shapes, partly on the
  strength of the target's own error message; the zero-amount and non-3-letter currency
  boundaries are untested, and no invalid payload was ever tried against a *declined* key.
- The model still cannot say anything about fresh-key outcome selection, so 10 of 31 steps
  remain unchecked by construction, and no trace can reach a full "CONFORMING" verdict.
- Nothing here proves M-002 correct; it explains all accumulated evidence, which is a weaker
  claim.

**Next role:** an adversary again (targets listed in `model\README.md` / `frontier.md`, headed by
Claim V's generalization and `VoidPayment`). If instead the loop moves to promotion, the
model-driven live conformance test should be the sequence `Reset -> AuthorizePayment(fresh key,
100.00 USD) -> identical replay -> CapturePayment(id from the 201) -> identical replay`: it
exercises Claims R and L plus the CX-2 regression, touches no unknown region after the
establishment call, and needs only the response-derived payment id.

## Test promotion -- one durable live M-002 conformance test

**Role scope:** test promotion only, against the accepted M-002 (unchanged; `model\` was read,
never modified). Read: `model\` (README, `PaymentSpec.cs`, `ModelScope.cs`,
`PaymentModelState.cs`, `PaymentContracts.cs`, `replay-report.md`), `frontier.md`'s promotion
candidate, `target\openapi.json`, `sdk\docs\` (conformance-testing, operations-and-expect,
request-derivations), the `sdk\*.dll` assemblies (inspected via reflection to confirm exact
public signatures before writing code -- no Accordant repo/benchmark source/tests/README/git
history/parent directories/other sessions consulted), and the live target at
`http://127.0.0.1:5088` (already running). Write: `tests\` only, plus this journal entry and the
test-promotion portion of `frontier.md`; `model\`, `traces\`'s six pre-existing trace files,
`scratch\`, `target\`, `sdk\`, `workspace.json`, and `controller-journal.md` were not touched.

**Sequence promoted (an extension of frontier.md's candidate, per the promotion brief):**
`Reset -> AuthorizePayment(fresh key, 100.00 USD) [setup/precondition, excluded] ->
AuthorizePayment(same key, identical payload) [Claim R, status still authorized] ->
CapturePayment(id read from the setup call's own response) [Claim L] ->
AuthorizePayment(same key, identical payload) [Claim R again, status now captured]`. The extra
pre-capture replay (not in frontier.md's original candidate) was added so the one live execution
exercises Claim R against both lifecycle statuses it can take, not only the post-capture one --
preserving a richer, self-contained operation history in a single trace rather than relying on
the historical traces 1/4/5 for the `authorized`-status case.

**Why the setup call is out of model scope, not asserted:** fresh/declined-key
success-vs-decline selection is M-002's largest named unknown region (`model\README.md`,
"Unknown regions" #1); the model's `PaymentSpec.Apply` throws if asked to judge it, and its own
`ReplayRunner` never calls `spec.Allows` for such a call -- it bootstraps state from the real
response only when that response was a genuine `201`. The promoted test reproduces this exact
bootstrap (not a new mechanism): `tests\PaymentConformance.Tests\PartialModelReplay.cs` calls
`PaymentModel.ModelScope.ClassifyAuthorize` on each recorded call, and for the one call it
classifies as `AuthorizeScope.UnknownKeyOutcomeSelection`, captures the observed
id/amount/currency plus an initial `authorized` status into a fresh `PaymentModelState` --
identical in effect to `ReplayRunner.cs`'s own bootstrap branch. If that call had classified as
anything else, or its response were not a real `201`, `PartialModelReplay` throws instead of
reporting a verdict (an infrastructure/setup failure, never disguised as conformance).

**How validation flows through the model (no manual behavioral assertions):** every other call
in the sequence -- `Reset` and the three `AuthorizePayment`/`CapturePayment` steps after setup --
is checked one call at a time by wrapping it in a single-call `RecordedTrace` and calling
`Specmine.Accordant.TraceReplayer.Replay(spec, state, subTrace, jsonOptions)`, where `spec` is
`PaymentModel.PaymentSpec.Build()` -- M-002 itself, unmodified. `TraceReplayer.Replay` is backed
by `Spec<PaymentModelState>.Allows` (confirmed by reflecting on `Accordant.Operations.dll`:
`Spec<TState>.Allows(IOperation, object request, object response, TState state)` returns
`(bool isValid, string message, StateProfile)`, exactly `sdk\docs\conformance-testing.md`'s
documented API). The test asserts only `ReplayStepResult.Outcome == Conforming` and surfaces
`ReplayStepResult.Message` on failure -- Accordant's own verdict and explanation, nothing
re-derived. No line in the test project compares an HTTP status, error `type`/`code`, payment
`status`, `id`, `amount`, or `currency` itself; those checks live only inside
`PaymentSpec.cs`'s `Expect.That` predicates, which this test does not duplicate.

**Live execution and durable trace.** The test connects through
`Specmine.Adapters.OpenApi.OpenApiTargetAdapter` using `workspace.json`'s existing
`http://127.0.0.1:5088` target (already running; verified live via `GET /openapi.json` before
any test code was written), and executes the five-call sequence exactly once, wrapped in
`Specmine.TraceRecorder.RunAsync(tracesSubdirectory, session, body)`. `TraceRecorder.RunAsync`
both records *and saves* the resulting `RecordedTrace` itself (confirmed by a disposable
reflection/plumbing probe under `tests\_probe\`, built, run, and deleted before any durable file
was written) -- so the same single execution that `PartialModelReplay` validates is also the one
preserved as evidence; no operation is executed twice. The saved trace:
`traces\trace-8-live-authorize-replay-capture-replay\2a03ec1481fc462ca73ae0c8943c3634.json`,
TraceId `2a03ec14-81fc-462c-a73a-e0c8943c3634`. It shows exactly the five calls above: `204`,
then `201` (fresh payment, `status: authorized`), `200` (identical replay, `status: authorized`),
`200` (capture, `status: captured`), `200` (identical replay, `status: captured`) -- the same
shape as the CX-2 trace (6) that motivated M-002's Claim R/L split, now reproduced live rather
than only replayed from a stored file. The session (`Specmine.ITargetSession`, an
`OpenApiTargetSession`) is disposed explicitly (`await session.DisposeAsync()` in a `finally`
block) after the trace is recorded and validated.

**Project.** `tests\PaymentConformance.Tests\PaymentConformance.Tests.csproj` -- a buildable
.NET 10 NUnit project (`Microsoft.NET.Test.Sdk` 17.14.0, `NUnit` 4.3.2, `NUnit3TestAdapter`
5.0.0, `NUnit.Analyzers` 4.7.0, all already present in the local NuGet cache -- no network
restore was needed, consistent with `nuget.org` being disabled in this environment). It holds a
`ProjectReference` to `..\..\model\PaymentModel.csproj` (the durable model project, unmodified)
for `PaymentSpec`/`ModelScope`/`PaymentModelState`/the contract types, plus direct
`<Reference>`s to `sdk\Accordant.dll`, `Accordant.Operations.dll`, `Specmine.dll`,
`Specmine.Accordant.dll` and `Specmine.Adapters.OpenApi.dll` (with the same `System.IO.Hashing`
9.0.7 pin as `model\PaymentModel.csproj`, since `Accordant.dll` needs it at runtime). No absolute
path appears in any source file: `WorkspaceLocator.cs` walks up from the test assembly's own
build output directory looking for `workspace.json`, with a
`PAYMENT_CONFORMANCE_WORKSPACE_ROOT` environment-variable override for other layouts.

**Result:** `dotnet test` from `tests\PaymentConformance.Tests\` -- **1 test, 1 passed**, run
`FreshAuthorize_IdenticalReplay_Capture_IdenticalReplay_ConformsToM002`. See `frontier.md`'s
updated "Promotion candidate" entry for the durable summary.

**Friction encountered (all resolved, none required touching the model or the trace corpus):**
- `Specmine.TraceRecorder` and `Specmine.TraceStore` are both **static** classes (`RunAsync` /
  `SaveAsync` are static methods) -- an initial attempt to `new TraceRecorder()` failed to
  compile (`CS0712`); fixed by calling `TraceRecorder.RunAsync(...)` directly.
- Referencing `PaymentModel.csproj` via `ProjectReference` alone was not enough for the test
  project's own code to name `Microsoft.Accordant.Spec<TState>` directly: the compiler reported
  `CS0012` ("defined in an assembly that is not referenced") until `Accordant.Operations.dll` was
  added as an explicit `<Reference>` in `PaymentConformance.Tests.csproj`, even though
  `PaymentModel.csproj` already references it privately.
- Whether `TraceRecorder.RunAsync` auto-saves the trace (vs. requiring a separate
  `TraceStore.SaveAsync` call) was not documented in `sdk\docs\`; confirmed empirically with a
  disposable probe project under `tests\_probe\` (created, run once against the live target with
  a minimal `Reset`-only body, inspected, then deleted in full -- no probe artifacts remain under
  `tests\`).
- The full five-call scenario (setup/replay/capture/replay) was validated end-to-end with the
  same probe before writing the final test, to confirm `ModelScope.ClassifyAuthorize`/
  `ClassifyCapture` classify each call exactly as expected against a truly live target (not just
  the stored traces) before committing to the final project structure.
