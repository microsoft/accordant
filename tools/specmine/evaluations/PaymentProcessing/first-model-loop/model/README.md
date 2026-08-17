# Payment Model M-002 -- AuthorizePayment idempotency + the observed capture transition

This is a **partial** Accordant model, revised after both of M-001's principal claims were
falsified by an adversary pass (counterexamples CX-1 and CX-2, recorded in `..\journal.md`
and `..\frontier.md`). It is deliberately the smallest revision that explains the new
counterexamples without giving up any prior evidence: one new claim about request
validation, one corrected claim about what an idempotent replay returns, one **narrowed**
conflict claim, and one new lifecycle transition (`CapturePayment` success) -- added only
because CX-2 cannot be explained without knowing whether a payment is still authorized.

It still says nothing about which fresh requests succeed vs. decline, nothing about the
decline region, and nothing about `VoidPayment`. See "Unknown regions".

## What changed from M-001, and why

| Counterexample | Classification | M-002's response |
|---|---|---|
| **CX-1** -- after a successful authorization, a malformed changed payload (`currency: "usd"`, or `amount: -5.00`) returned `400 validation_error` where M-001 predicted `409 idempotency_conflict` | **Wrong claim, squarely inside the region M-001 owned.** M-001's Claim 2 was a *precedence* error, not a payload-equality error: it never modeled that the request is validated before the idempotency key is consulted at all. | New **Claim V**: a payload that is invalid on an observed axis is rejected with `400 validation_error/invalid_request` before any idempotency handling, whatever the key's history. Claim C (the old Claim 2) now only applies to payloads that pass validation. |
| **CX-2** -- after `CapturePayment`, an identical authorization replay returned the same payment identity/data but `status: "captured"`, where M-001 predicted the frozen `authorized` snapshot | **Wrong claim at a scope boundary.** M-001 declared capture/void "not represented at all", but wrote Claim 1 unconditionally over every key in `SuccessfulKeys`, so the call was checked anyway and failed. The substance of the error is that a replay returns the *live payment record*, not a cached response body. | Claim 1 becomes **Claim R**: identity/data fields are stable and checked exactly as before; `status` is checked against the payment's **current modeled lifecycle status**. To have such a status, M-002 adds the minimum lifecycle transition that produced it: **Claim L**, the first `CapturePayment` of an authorized payment. Model state now separates stable identity/data from mutable status. |

The structural lesson from CX-2 is applied throughout: **every claim states its precondition
explicitly**, in `ModelScope.cs`, which both the spec and the replay runner consult. A claim
is never allowed to range over a region its evidence did not cover, and the runner can never
check a call the spec does not actually claim to own.

## Files

| File | Purpose |
|---|---|
| `PaymentModel.csproj` | Buildable .NET 10 project. References `..\sdk\Accordant.dll`, `Accordant.Invariant.dll`, `Accordant.Operations.dll`, `Specmine.dll`, `Specmine.Accordant.dll`; loads `Accordant.SourceGenerator.dll` as a Roslyn analyzer for `[State]`. |
| `PaymentModelState.cs` | `[State]` model state: `SuccessfulKeys` (idempotency key -> stable `CapturedAuthorization`: payment id, key, amount, currency) and `LifecycleStatuses` (payment id -> current status). The split is the direct encoding of CX-2. |
| `ModelScope.cs` | **New in M-002.** The single source of truth for the scope boundary: classifies each recorded `AuthorizePayment`/`CapturePayment` call into a modeled claim or a named unknown region, and owns the payload-validity and payload-equality rules. |
| `PaymentContracts.cs` | Typed C# envelopes for the OpenAPI trace shape (`{ path, query, headers, body }` request / `{ status, body }` response) for `Reset`, `AuthorizePayment` and `CapturePayment`, deserialized by `TraceReplayer`/`JsonSerializer` with `ReplayJsonOptions.CreateDefault()` (camelCase, case-insensitive). |
| `PaymentSpec.cs` | The `Spec<PaymentModelState>`: `Reset`, `AuthorizePayment` (Claims V, R, C) and `CapturePayment` (Claim L). `Apply` throws for anything outside the modeled region rather than guessing. |
| `ReplayRunner.cs` | Console entry point (`Program.Main`). Replays every trace under `..\traces\`, one call at a time, and writes `replay-report.md`. |
| `replay-report.md` | Generated output of the last run (per-call classification, the claim each checked step was checked under, and the named unknown region each excluded step fell into). Regenerate with the command below. |

## Scope -- exactly what this model claims

**Operations modeled:** `Reset`, `AuthorizePayment`, `CapturePayment` (success transition only).

**State:** `SuccessfulKeys[idempotencyKey] = { PaymentId, IdempotencyKey, Amount, Currency }`
and `LifecycleStatuses[paymentId] in { "authorized", "captured" }`. Both are populated only
from real observed responses (never invented): a key enters `SuccessfulKeys` -- with an
initial `authorized` status -- only when a real `201` was observed for it. A key absent from
`SuccessfulKeys` is *not* modeled as "unused"; it is modeled as **out of scope**.

### Claim V -- request validation precedes idempotency handling *(new; from CX-1)*

A request whose `amount` is not positive, or whose `currency` is not exactly three uppercase
ASCII letters, must be answered `400 { type: "validation_error", code: "invalid_request" }`
with a non-empty message, and must change nothing -- **regardless of whether the idempotency
key is fresh, already successfully authorized, or anything else**. Validation therefore
preempts both the conflict check and the success/decline selection.

*Evidence:* trace 7 call 3 (successful key, `currency: "usd"`), call 4 (**fresh, never-used
key**, same malformed currency), call 5 (successful key, `amount: -5.00`); plus trace 5 call
5 (successful key, `"usd"`). Calls 3-5 of trace 7 were the adversary's own discrimination
experiment: they show the `400` is a property of the request alone, on two independent field
axes, and not of the replay path.

*Extrapolation, called out honestly:* only two violation shapes were ever observed --
all-lowercase 3-letter currency, and a negative amount. `amount == 0`, currencies of a
different length (`"US"`, `"EURO"`), non-alphabetic currencies (`"1US"`), and mixed case
(`"Usd"`) were never tested; the predicate generalizes to "positive amount, `^[A-Z]{3}$`
currency" partly on the strength of the target's own error message. See "Falsification
strategies".

*Deliberately **not** modeled:* the blank/whitespace `idempotencyKey` rule that the same
error message mentions. No recorded call has ever exercised it, so M-002 stays silent:
`ModelScope` routes any blank-key request to an unknown region instead of predicting `400`
from the target's prose.

### Claim R -- an identical-payload replay returns the live payment record *(corrects M-001 Claim 1; from CX-2)*

For a **valid** payload whose idempotency key is already in `SuccessfulKeys`, and whose
amount and currency match the captured payload, the response must be `200` with:
`id`, `idempotencyKey`, `amount`, `currency` exactly as captured at authorization time, and
`status` equal to the payment's **current** modeled lifecycle status. State is unchanged.

*Evidence:* trace 1 call 3; trace 4 calls 3, 4 (repeat replays) and 6 (replay after a `409`);
trace 5 call 3 (`100.0` vs `100.00`, so amount equality is decimal **value** equality) -- all
with status `authorized`; and trace 6 call 4 with status `captured` after a real capture.

### Claim C -- a valid changed payload conflicts while the payment is authorized *(narrows M-001 Claim 2)*

For a **valid** payload whose key is in `SuccessfulKeys`, whose amount and/or currency differ
from the captured payload, and whose payment is still `authorized`, the response must be
`409 { type: "conflict", code: "idempotency_conflict" }` with a non-empty message, and no
payment is created or mutated.

*Evidence:* trace 2 call 3 (amount changed), call 4 (currency changed), and trace 4 call 5
(**both changed simultaneously**). The simultaneous case was M-001's flagged extrapolation;
the adversary attacked it directly and it survived, so it is now corroborated evidence rather
than an assumption -- it stays in the claim.

*Narrowing:* every one of those observations was made while the payment was still
`authorized`. M-002 therefore attaches that precondition instead of ranging over all
lifecycle states the way M-001 did. A changed-payload replay **after** a capture is an
explicit unknown (below), not a prediction.

### Claim L -- the first capture of an authorized payment *(new; the minimum needed for CX-2)*

For a payment this model captured from a real `201` and believes is still `authorized`,
`CapturePayment` must return `200` with `id`, `idempotencyKey`, `amount`, `currency`
unchanged and `status: "captured"`; the modeled lifecycle status advances to `captured`.

*Evidence:* trace 6 call 3 (n=1). This is the smallest addition that lets Claim R be checked
honestly after a capture -- without it, the model cannot know that "the live status" changed,
which is exactly the knowledge CX-2 showed M-001 was missing.

### Reset

Always transitions to empty state (no captured keys, no statuses) and acknowledges with
`204`, regardless of prior state. Trivial infrastructure, included only so every trace can
start from a known state.

## Unknown regions (explicitly not modeled)

Each of these is a *named* region in `ModelScope.cs`; the replay report attributes every
excluded step to one of them.

1. **Fresh/declined-key success-vs-decline selection.** Which valid requests are authorized
   (`201`) and which are declined (`200` + `DeclinedPaymentResponse`) is uncharacterized:
   only one decline trigger has ever been seen (`1,000,000.00 USD` -> `limit_exceeded`, trace
   3), with no boundary, currency dependence, or alternate reason known. The model is
   **silent** here, not `OneOf(success, decline)` -- see "Why not `Expect.OneOf`".
2. **Decline-region behavior entirely.** Whether an identical retry reproduces a decline
   (trace 3 call 3) and whether a changed payload after a decline may succeed (trace 3 call
   4) are single-sample investigator observations (frontier H-003/H-004) that this model
   still does not encode. Trace 3 remains reported as not-applicable/unknown.
3. **Changed payload after a lifecycle transition.** Claim C is not extended past
   `authorized`; whether a changed payload after a capture still returns `409` is untested.
4. **`CapturePayment` outside the first-capture success case:** unknown payment id (the
   `404` region), re-capturing an already-captured payment (the `409` region), and capture
   after a void. None was ever observed.
5. **`VoidPayment` entirely** -- never called, so no `voided` status exists in the model. A
   trace containing it would be excluded as an unmodeled operation, and Claim R's status
   check would be unreliable across it (called out as an adversarial target below).
6. **Validation axes other than the two observed** (blank/whitespace key, `amount == 0`,
   non-3-letter or non-alphabetic currency, mixed case).
7. **Currency-case equality for conflict purposes.** Whether the target would consider `usd`
   the *same* currency as `USD` when comparing payloads is **unanswerable at this API
   surface**: validation rejects the request first (CX-1). This unknown does not resolve; it
   collapses into Claim V.
8. **A third call under an already-conflicting key** -- only one changed-payload attempt per
   key was made before trace 4, and trace 4 only re-ran the *identical* payload afterwards.

## Why not `Expect.OneOf` for the decline threshold?

`Expect.OneOf` (see `..\sdk\docs\operations-and-expect.md`) is Accordant's construct for
*genuine* external nondeterminism: the same request, from the same state, legitimately
producing different real outcomes because of timing outside either party's control (the
docs' network-timeout example). Whether a given amount is authorized or declined is almost
certainly a **deterministic** function of the request that this black-box investigation has
not characterized -- our own epistemic gap, not the target's nondeterminism. Modeling it as
`OneOf(success-shape, decline-shape)` would silently claim the target may answer either way
for *any* amount, making the model unable to ever flag a real bug in that region, and would
misrepresent "unknown" as "nondeterministic".

Because Accordant's `Apply` must return some `ExpectedOutcomes` for every input it is given,
and there is no "I don't know, skip this" outcome in the SDK, the honest choice is to keep
`Apply` **undefined** (it throws) outside the modeled region, and to make sure that branch is
never reached through `spec.Allows`. That exclusion lives in `ReplayRunner.cs`, which asks
`ModelScope` first: for an excluded call it reads the real observed response directly out of
the trace and -- only when that response was a real `201` -- captures the server-generated
id/amount/currency plus an initial `authorized` status into state, so later calls under that
key (and captures of that payment) become checkable. Nothing is captured from a decline, a
`400`, or an unmodeled capture.

## Evidence trace IDs

| Trace | File | Used for |
|---|---|---|
| 1 | `traces\trace-1-same-key-identical-payload\15b55e70eb4b4105bc9c60be7dc26898.json` | Claim R (call 3) |
| 2 | `traces\trace-2-same-key-changed-payload\a25e38b287394c76abae7f2caea10550.json` | Claim C (calls 3, 4) |
| 3 | `traces\trace-3-decline-and-key-reservation\2a15d62d75ed433cbc7e9c1910d38ea5.json` | **Entirely excluded** (decline region); justifies no claim |
| 4 | `traces\trace-4-adv-both-changed-and-post-conflict\94890cdae77a4beca5f2dec71d576bbe.json` | Claim R (calls 3, 4, 6), Claim C's simultaneous-change case (call 5) |
| 5 | `traces\trace-5-adv-equality-boundaries\0c64d43345a947d18fb7fa2bc4c4cc61.json` | Claim R's decimal-value amount equality (call 3), Claim V (call 5) |
| 6 | `traces\trace-6-adv-capture-then-identical-replay\86e4f8006fdc4fdab9865bd17cf936f6.json` | Claim L (call 3), Claim R's live status (call 4) -- **CX-2** |
| 7 | `traces\trace-7-adv-minimized-invalid-payload-precedence\80a65eb35e374238a8d8c32d64e85389.json` | Claim V (calls 3, 4, 5) -- **CX-1**, minimized |

## Falsification strategies / adversarial targets

Ordered by where this revision is most likely to be wrong.

1. **Break Claim V's extrapolation (most likely soft spot).** Only lowercase-3-letter
   currency and a negative amount were observed. Try `amount: 0`, `currency: "US"`,
   `"EURO"`, `"1US"`, `"Usd"`, and a whitespace `idempotencyKey`. Anything that is *not*
   `400 validation_error/invalid_request` -- especially a `409` under a used key or a `201`
   under a fresh one -- falsifies Claim V as generalized (the blank-key case is not modeled,
   so it can only expand the model, not break it).
2. **Break Claim L / Claim R via `VoidPayment`.** Void an authorized payment, then replay the
   identical authorization. M-002 has no `voided` status, so if the replay reports one, Claim
   R's status check needs the void transition too (and the model must say so rather than
   silently mis-predicting). Also try capture-then-void and void-then-capture.
3. **Attack the narrowed Claim C.** Capture a payment, then replay a *changed but valid*
   payload. M-002 deliberately makes no prediction; a `409` would justify widening Claim C
   back across the lifecycle, anything else would justify keeping it narrow. Either way this
   is the cheapest way to convert an unknown into evidence.
4. **Attack Claim L's preconditions.** Capture an unknown/garbage id (expect the unmodeled
   `404` region) and capture the same payment twice (the unmodeled `409` region). Neither is
   modeled, so these expand the model rather than falsify it.
5. **Break Claim R's stability.** Any identical-payload replay returning a different `id`, a
   fresh `201`, a changed `amount`/`currency`, or an error falsifies Claim R directly.
6. **Break Claim C's core.** A valid amount-only or currency-only changed replay against a
   still-authorized key that does *not* return `409 idempotency_conflict`.
7. **Probe the excluded decline region** (not falsification, since no claim is made):
   characterize the decline threshold, retry a declined key repeatedly, or run an invalid
   payload against a *declined* key -- the one history class where Claim V has no evidence.

## Build and replay

```powershell
cd model
dotnet build
dotnet run --no-build
```

`ReplayRunner` auto-discovers the workspace root by walking up from the current directory
looking for `workspace.json` (falling back to the parent of the current directory), then
replays every `*.json` file under `traces\` (recursively, in ordinal path order), and writes
`model\replay-report.md`. Pass an explicit workspace root as the first argument to replay a
different set of traces without touching this one:
`dotnet run --no-build -- "C:\path\to\workspace"`.

The runner exits `0` when there are zero model violations and `2` when at least one checked
step violates the model (it still writes the report either way, so partial/mixed results are
always visible).

**Replay granularity (small cleanup made in this revision).** M-001's runner replayed
consecutive in-scope calls as one batch. That is no longer viable now that `CapturePayment`
mutates state mid-trace -- a later call in the same batch would be classified against stale
lifecycle status -- and it also had a reporting flaw the adversary flagged: `TraceReplayer`
stops a batch at its first violation, so a checked call after a violation could silently
vanish from the report. M-002 replays **one call at a time**, so scope classification always
sees current state and every recorded call gets its own verdict.

## Last replay result (see `replay-report.md` for full detail)

7 traces replayed, 31 recorded steps: **21 conforming, 0 model violations, 10 intentionally
excluded** as not-applicable/unknown. Excluded steps are all in one region -- fresh/declined-key
success-vs-decline selection (7 establishment calls bootstrapped from a real `201` without
being checked, plus trace 3's 2 decline calls and 1 post-decline success). Checked steps by
claim: Reset 7, Claim R 6, Claim V 4, Claim C 3, Claim L 1. Trace 3 remains
**NOT APPLICABLE / UNKNOWN** (its only checked step is the trivial `Reset`); the other six
traces are **CONFORMING (partial)**. Both counterexample traces (6 and 7) now conform.

The predicates were confirmed load-bearing rather than vacuous by replaying six deliberately
mutated copies of the real traces through the same runner in a throwaway sandbox (since
deleted): a `409` where Claim V requires `400`; the post-capture replay reporting the frozen
`authorized` status (i.e. **M-001's exact prediction**, which M-002 now rejects); a replay
reporting `captured` with no capture; a `201` where Claim C requires `409`; a capture
reporting `authorized`; and a replay returning a different payment id. All six were caught as
model violations. A seventh mutant -- a valid *changed* payload after a capture -- was
correctly reported as not-applicable/unknown rather than checked, confirming the narrowed
Claim C precondition is real and that the spec's guard is never reached through the runner.
