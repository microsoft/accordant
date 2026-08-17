# PaymentProcessing black-box model loop: first evaluation

> **Maintainer-only.** This file discusses the benchmark's hidden ground truth. Like
> `tools\specmine\benchmarks\PaymentProcessing\README.md`'s "Hidden ground truth" section, it
> must never be staged into an investigation workspace.

Durable record of the first complete *evidence -> model -> adversary -> refinement ->
model-driven test* loop against the `PaymentProcessing` benchmark
(`tools\specmine\benchmarks\PaymentProcessing`). It preserves the staged workspace's immutable
evidence (8 traces, frontier, journals), the durable model and promoted test source, and scores
all of it against maintainer ground truth. Nothing here modifies the SDK, adapters, benchmarks,
`solution`, `packages`, or the staged workspace - all of that was read-only input.

This is the successor to `..\..\TaskWorkflow\first-investigation`, which evaluated a two-agent
*investigation only*. The new thing being evaluated here is the rest of the loop: whether a
partial Accordant model, an adversary with read-only model access, a minimal refinement, and a
promoted live conformance test hold up against ground truth.

## 1. Cooperative black-box setup and visibility boundary

Five role transactions ran in sequence against one staged workspace
(`...\blackbox-payment-1\workspace\`), each in its own conversation with no shared context, with
a controller selecting the next role and recording why.

What the acting roles could see: `target\openapi.json` (the benchmark's committed,
behavior-silent document), `sdk\*.dll` plus `sdk\docs\`, the accumulating `traces\`,
`frontier.md` / `journal.md` / `controller-journal.md`, `workspace.json`, and the live service
at `http://127.0.0.1:5088`. What they never consulted: benchmark source, `PaymentApiTests.cs`,
the hidden ground-truth README, this repository, or git history. Each role's journal entry opens
with an explicit statement of what it read and wrote, and the adversary additionally held
*read-only* access to `model\` (it recorded the model report's SHA-256 before and after its pass
to prove it changed nothing).

**The boundary was cooperative, not enforced**, exactly as `tools\specmine\README.md` warns:
source and ground-truth tests were kept outside the staged workspace and no role asked for them,
but nothing sandboxed the roles from the wider filesystem. This evaluation, run afterward with
explicit permission to read the hidden ground truth, is what actually scores the result. Treat
section 8 as the score and the loop itself as an unenforced-but-clean run.

## 2. Transaction timeline and controller decisions

From `controller-journal.md`. The controller ran unattended ("auto"); each entry also records the
checkpoint an interactive controller would have paused at.

| # | Role | Why selected | Decision that shaped everything downstream | Interactive checkpoint that was auto-taken |
| --- | --- | --- | --- | --- |
| 1 | Investigator | Empty workspace: no evidence, no model | Bound the pass to authorization/idempotency; 3 traces / 15 calls | Model only the successful-key region **or** spend more budget on the decline region -> chose the narrow region, kept declines as an explicit unknown |
| 2 | Modeler | A coherent evidence-backed region existed | M-001 covers identical replay + changed-payload conflict on a successful key only | Accept M-001 after showing excluded steps and the state-bootstrap mechanism -> accepted as a *partial candidate* and sent straight to attack |
| 3 | Adversary | M-001 contained unchallenged generalizations | Spent the full 4-trace / 20-call budget on M-001's own flagged soft spots | Choose refinement strategy after CX-2 -> deferred to the refiner |
| 4 | Model refiner | Adversary produced minimized, classified counterexamples | Narrow M-001's scope to exclude lifecycle ops **or** absorb the observed capture transition -> chose the smallest useful cross-operation refinement (absorb capture) | Approve M-002 + the promotion candidate -> accepted because every applicable trace conformed, both counterexamples were explained, mutants were rejected, and unknowns stayed explicit |
| 5 | Test promoter | M-002 accepted with zero applicable violations | One live NUnit test over Reset -> setup 201 -> replay -> capture -> replay | Show the test sequence and the excluded setup call before promotion -> allowed because the sequence touches only explicit M-002 claims after setup |

The two consequential auto-decisions were #1 (narrow to the successful-key region) and #4 (extend
the model to `CapturePayment` rather than shrinking its scope). Both are defensible and both are
recorded with their rejected alternative, which is the property that makes the auto run
reviewable after the fact.

## 3. Initial hypotheses and trace evidence

The investigator formed three hypotheses from the OpenAPI document's *shape* alone (before any
call), which is worth noting because the document is deliberately behavior-silent: the split
between `201` and `200`, and the fact that `200` is `oneOf(PaymentResponse,
DeclinedPaymentResponse)`, are what suggested the questions.

| ID | Hypothesis | Outcome |
| --- | --- | --- |
| H-001 | Identical replay under the same key returns `200` with the original payment, not a second payment | Supported (trace 1), later corrected in substance by CX-2 |
| H-002 | Changed `amount`/`currency` under a reused key returns `409 idempotency_conflict` | Supported for amount-only and currency-only (trace 2), later narrowed by CX-1 |
| H-003a/b | Competing pair: does a decline reserve the key? | Resolved *asymmetrically*, replaced by H-004 |
| H-004 | Conflict detection is scoped to keys whose prior outcome was a **success**; a declined key is not "used" for conflict purposes | Supported (trace 3), deliberately left out of the model |

Investigator evidence (traces 1-3, 11 calls):

- **Trace 1** `Reset -> Authorize(idem-A1, 100.00 USD) = 201 -> identical repeat = 200`, same
  `id`/`amount`/`currency`/`status`.
- **Trace 2** `Reset -> Authorize(idem-B1, 100.00 USD) = 201 -> amount changed to 250.00 = 409
  -> currency changed to EUR = 409`, identical `ErrorResponse` bodies.
- **Trace 3** `Reset -> Authorize(idem-C1, 1,000,000.00 USD) = 200 declined/limit_exceeded ->
  identical retry = same decline -> amount changed to 50.00 = 201 with a new id`. This is the
  only decline ever elicited, and the changed-payload retry succeeding is what falsified the
  simple "a key is a key" story and produced H-004.

## 4. M-001: scope, replay treatment of unknown regions, limitations

M-001 (superseded; kept verbatim in `frontier.md` as the record of what was attacked) claimed
exactly two things about the **successful-key** region, plus trivial `Reset`:

- **Claim 1** - identical-payload replay returns the cached `PaymentResponse` verbatim as `200`.
- **Claim 2** - any deviation in `amount` and/or `currency` against a successfully-authorized key
  is `409 idempotency_conflict`. This *generalized* two independently-tested single-field changes
  into "any deviation", and the modeler flagged the both-fields-changed case as an adversarial
  target rather than hiding the extrapolation.

**Treatment of unknown regions.** Fresh-key success-vs-decline selection and the whole decline
region were excluded, not modeled. The reasoning (preserved in `model\README.md`, "Why not
`Expect.OneOf`") is the most transferable idea in this run: `Expect.OneOf` expresses *genuine
external nondeterminism*, and using it for "we have not characterized this rule" would encode
our own ignorance as sanctioned nondeterminism and make the model permanently unable to flag a
bug there. Accordant has no per-region `UNKNOWN` outcome, so the exclusion had to live in the
replay runner: it never calls `spec.Allows` for a key with no captured prior success, and instead
**bootstraps** model state directly from the observed response (capturing id/amount/currency only
on a real `201`, capturing nothing on a decline).

**Limitations, as stated at the time:** all observations n=1; the model can never reach a full
"CONFORMING" verdict because establishment calls are unchecked by construction; and the
`Apply`-throws-outside-scope design means an out-of-region call reaching the spec is a *usage
error*, not a model verdict. M-001's replay over traces 1-3: **11 steps, 6 conforming, 0
violations, 5 excluded**; trace 3 reported "not applicable / unknown" rather than "passed".

## 5. Adversarial pass: attacks, CX-1 / CX-2, minimization

The adversary spent its full budget (4 traces, 20 calls) and falsified **both** principal claims,
producing 4 violating steps across 3 of its 4 traces - all through the model's own
`spec.Allows`/`TraceReplayer` path, with the model unmodified.

**CX-1 - request validation preempts idempotency-conflict detection (in-region; falsifies Claim 2).**
Minimized to 3 calls (trace 7, calls 1-3):

```
Reset                                      -> 204
AuthorizePayment{adv-G1, 100.00, "USD"}    -> 201 (status authorized)
AuthorizePayment{adv-G1, 100.00, "usd"}    -> 400 validation_error / invalid_request
```

M-001 predicted `409 idempotency_conflict` for that third call, because under ordinal string
equality the payload "differs in currency". It cannot be shortened: the key must be established
before the model's `Apply` is even defined, and `Reset` is needed to start from a known state.
Exactly one property of one field differs from the establishing request. The adversary then added
two *discrimination* calls that separate competing explanations: a fresh, never-used key with the
same malformed currency also returns `400` (so the `400` is a property of the request, not of the
replay path), and an invalid **amount** with a valid currency against the established key also
returns `400` (so the precedence is not currency-specific). That is what turned "lowercase
currency is special" into "validation runs before idempotency lookup".

**CX-2 - an idempotent replay returns the live payment record, not a frozen snapshot (falsifies Claim 1).**
Already minimal at 4 calls (trace 6):

```
Reset                                      -> 204
AuthorizePayment{adv-F1, 100.00, "USD"}    -> 201 (status authorized)
CapturePayment{id from the 201}            -> 200 (status captured)
AuthorizePayment{adv-F1, 100.00, "USD"}    -> 200 (same id/key/amount/currency, status CAPTURED)
```

M-001 required the replay to return the authorization verbatim, `status: "authorized"`. Only
`status` moved; every identity/data field was stable. It cannot be shortened either: the capture
needs an existing payment, and the claim needs an established successful key.

The adversary classified these differently rather than lumping them: CX-1 is a **wrong claim
squarely inside the region M-001 owned**, CX-2 is a **scope-boundary failure** - `model\README.md`
said capture/void was "not represented at all", but `PaymentSpec.cs` stated Claim 1
unconditionally over every key in `SuccessfulKeys`, so the runner checked the call anyway and it
failed. That distinction is what drove the structural fix in M-002.

**Attacks that found no disagreement** (corroboration, not proof): simultaneous amount *and*
currency change (Claim 2's flagged extrapolation - survived, so it was retained as evidence
rather than assumption); two consecutive identical replays; identical replay *after* a `409`
(a conflict does not poison the cached record); and `100.0` vs `100.00` (so amount equality is
decimal **value** equality, not text equality).

The adversary also reported a genuinely unanswerable question: whether the target treats `usd` as
the *same* currency as `USD` for conflict purposes cannot be observed at this API surface,
because validation rejects the request first. That unknown **collapses into** the validation rule
instead of resolving - a distinction the refiner preserved.

## 6. M-002 refinement and all-trace replay

M-002 is the smallest revision explaining both counterexamples without discarding prior evidence:

| Claim | Statement | Change |
| --- | --- | --- |
| **V** | A payload with a non-positive `amount` or a currency that is not three uppercase letters is `400 validation_error/invalid_request` **before** any idempotency handling, whatever the key's history | New, from CX-1 |
| **R** | An identical-payload replay of a successfully-authorized key returns `200` with identity/data exactly as authorized and `status` equal to the payment's **current** modeled lifecycle status | Corrects Claim 1, from CX-2 |
| **C** | A **valid** payload differing in amount and/or currency, against a key whose payment is still `authorized`, is `409 idempotency_conflict` | Narrows Claim 2 (validity precondition + lifecycle precondition) |
| **L** | The first `CapturePayment` of a payment believed `authorized` returns `200` with identity/data unchanged and `status: captured`, advancing the modeled status | New; the minimum lifecycle knowledge Claim R needs |

Two structural changes matter more than the claims:

1. **State was split** into stable identity/data (`SuccessfulKeys`) and mutable lifecycle status
   (`LifecycleStatuses`) - the direct encoding of CX-2.
2. **`ModelScope.cs`** became the single classifier naming every modeled claim and every unknown
   region, consulted by *both* `Apply` and the replay runner. This is the structural fix for
   CX-2's root cause: a precondition can no longer exist only in prose, and the runner can no
   longer check a call the spec does not claim to own.

The refiner also declined two tempting over-reaches: it did **not** model the blank-`idempotencyKey`
rule that the target's own error message spells out (never exercised by any call, so `ModelScope`
routes it to an unknown region), and it did **not** extend Claim C past `authorized`, creating a
newly *named* unknown instead of an unflagged over-claim.

**Replay totals.** M-002 over the 7 traces that existed at refinement time: **31 steps, 21
conforming, 0 violations, 10 excluded** (all in the one fresh/declined-key region). Re-running the
copy in this record over all **8** traces, including the promoted test's live trace: **36 steps,
25 conforming, 0 violations, 11 excluded** (`verification\replay-report-all-8-traces.md`); the
trace 1-7 sections are line-for-line identical to the staged snapshot in `model\replay-report.md`.

Predicates were shown to be **load-bearing, not vacuous**: six mutated trace copies were replayed
in a throwaway sandbox and all were flagged, including one asserting the frozen `authorized`
status after a capture - M-001's exact prediction, which M-002 rejects. A seventh mutant (valid
changed payload after a capture) was correctly *excluded* rather than checked, confirming Claim
C's new precondition is real rather than decorative.

## 7. The promoted model-driven test

One durable NUnit test,
`AuthorizeCaptureLifecycleConformanceTests.FreshAuthorize_IdenticalReplay_Capture_IdenticalReplay_ConformsToM002`,
executes once against the live target:

```
Reset                                            [Claim: Reset scaffolding]
  -> AuthorizePayment(fresh key, 100.00 USD)     [SETUP - excluded, bootstraps state from the real 201]
  -> AuthorizePayment(same key, identical)       [Claim R, status authorized]
  -> CapturePayment(id from the setup response)  [Claim L]
  -> AuthorizePayment(same key, identical)       [Claim R, status captured]
```

It reproduces the CX-2 shape live, and the promoter extended `frontier.md`'s candidate with the
pre-capture replay so a single execution exercises Claim R in **both** lifecycle states.

**Confirmed: every behavioral assertion goes through the model.** Verified by reading the source
in this record, not by trusting the journal. `AuthorizeCaptureLifecycleConformanceTests.cs`
contains exactly five `Assert.That` sites: the recorded call count (5), the setup call's
`CallTreatment.ExcludedAsSetupBootstrap`, the setup call's bootstrapped payment id being
non-empty, the checked-step count (4), and - the only behavioral one - `ReplayStepResult.Outcome
== ReplayStepOutcome.Conforming` for each checked step, surfacing Accordant's own
`ReplayStepResult.Message` on failure. No line anywhere under `tests\` compares an HTTP status,
error `type`/`code`, payment `status`, `id`, `amount`, or `currency` to an expected value; each
such comparison exists exactly once, inside `PaymentSpec.cs`'s `Expect.That` predicates. The one
literal `201` under `tests\` is in `PartialModelReplay.cs` and is a *setup precondition guard*
that **throws** (not asserts) if the bootstrap call did not really succeed - deliberately not
disguised as either a pass or a conformance failure. Every unexpected scope classification throws
for the same reason.

The single live execution was recorded by `TraceRecorder` and preserved as trace 8; nothing was
executed twice to produce evidence. Staged result: `dotnet test` -> **1 test, 1 passed**, all 4
checked steps `Conforming`.

## 8. Comparison against hidden ground truth

Ground truth: `tools\specmine\benchmarks\PaymentProcessing\README.md` ("Hidden ground truth"),
`PaymentStore.cs`, and `PaymentApiTests.cs`. Summarized below, never copied into this record.

### Rules the loop got right

| M-002 claim | Ground truth | Verdict |
| --- | --- | --- |
| **V** - non-positive amount or currency not `^[A-Z]{3}$` -> `400 validation_error/invalid_request`, before idempotency handling, independent of key history | Validation requires a nonblank key, a positive amount, and exactly three uppercase letters, and runs before anything else | **Correct**, including the generalization from two observed violation shapes to the full amount/currency predicate. The key-history independence is also correct. |
| **R** - identical replay returns `200` with stable identity/data and the payment's **current** status | The idempotency path returns the stored payment's live projection, so the status field tracks the lifecycle | **Correct, and the sharpest result in the run.** The hidden README's own prose ("returns the original payment") glosses over this; the implementation returns the live record. The adversary found a semantic distinction the maintainer summary understates. |
| **C** - valid changed payload against a still-`authorized` key -> `409 idempotency_conflict` | A reused key with a changed amount or currency conflicts | **Correct**, and conservatively narrow (see below). Decimal *value* equality on amount is also correct. |
| **L** - first capture of an authorized payment -> `200`, `status: captured` | Capture transitions `authorized` -> `captured` | **Correct**, and conservatively narrow. |
| Reset -> `204`, clears all captured state | Reset removes payment and idempotency state | **Correct.** |
| H-004 (frontier, deliberately not modeled) - conflict detection applies only to keys with a prior **successful** authorization; a declined key stays free | A decline creates neither a payment nor an idempotency reservation | **Correct.** |

Notably, the currency-case question the adversary declared *unanswerable* really is: ground truth
compares currency ordinally, so `usd` vs `USD` would conflict - but validation rejects `usd`
first, so no experiment at this surface can observe it. The loop's epistemics were right.

### False or overstrong claims - found and corrected during the loop

Both of M-001's claims were **wrong against ground truth**, and both were caught by the adversary
rather than by ground-truth comparison:

1. **M-001 Claim 2** predicted `409` for any deviating payload, including malformed ones. Ground
   truth validates first, so this was a precedence error, not a payload-equality error. Corrected
   by Claim V + Claim C's validity precondition.
2. **M-001 Claim 1** predicted a frozen response snapshot. Ground truth returns the live record.
   Corrected by Claim R.

**No claim surviving in M-002 is false against ground truth.** The residual imprecision is all in
the conservative direction:

- **Claim C is narrower than truth.** Ground truth's conflict check does not depend on lifecycle
  state, so a valid changed payload after a *capture* also conflicts. M-002 declares that an
  unknown. Under-claiming, never wrong.
- **Claim L is narrower than truth.** Capture succeeds from `captured` too (it is idempotent) and
  fails only from `voided`.
- **Claim V is incomplete.** The blank/whitespace `idempotencyKey` conjunct is real ground truth
  and was deliberately left unmodeled because no call exercised it. This was the right call, and
  it is worth recording that the refiner resisted adopting a rule the target's own error message
  handed it for free.

### Overstrong claims that survive in the record (not in the model)

- **The decline "cache" framing is mechanistically wrong.** `frontier.md` H-004 and `journal.md`'s
  "smallest behavior region" describe an identical retry after a decline as returning "the cached
  prior outcome verbatim". Ground truth has no decline cache at all: a decline records nothing,
  so the retry simply re-runs the fresh path and re-declines because the rule is deterministic in
  the request. The *observations* are right and the model is silent here, so nothing false was
  encoded - but a future modeler reading that sentence would build the wrong state machine.
- **A mislabeled unknown region.** `model\README.md` "Unknown regions" #4 and `frontier.md`'s
  adversarial target #4 name re-capturing an already-captured payment "the `409` region". Ground
  truth: repeating a capture is **idempotent** (`200`); it is *voided* -> capture that conflicts.
  The model predicts nothing there, so no verdict is wrong, but the guess is baked into the
  region's *name* and would misdirect the next pass.

### Still-unknown ground-truth behavior

Never observed, correctly left open, and (except where noted) explicitly named as unknowns:

| Area | Ground truth the loop never reached |
| --- | --- |
| **Decline rule** | Authorization declines above a fixed amount ceiling (1000). Only one sample at 1,000,000 was ever taken; the boundary was never bracketed. This single uncharacterized rule accounts for **all 11 excluded replay steps**. |
| **`VoidPayment`** | Entire operation: `authorized` -> `voided`, void is idempotent, voiding a `captured` payment is a typed `409`. Never called once in 36 calls. |
| **`GetPayment`** | Entire operation, including typed `404 payment_not_found`. Never called. |
| **Capture edges** | Repeat capture is idempotent; capture of a `voided` payment is a typed `409`; capture of an unknown id is a typed `404`. |
| **Changed payload after a lifecycle transition** | Still `409` (the conflict check ignores lifecycle). Named as unknown by M-002. |
| **Conflict vs. decline precedence** | A changed payload *above* the decline ceiling against a successful key conflicts rather than declining. Claim C would predict this correctly, but it was never tested. |
| **Validation axes** | Blank/whitespace key, `amount == 0`, currency of the wrong length, non-alphabetic or mixed-case currency. |
| **Reset's effect** | Never verified that a pre-reset payment id is actually gone afterward; `Reset` was only ever used as scaffolding. |

### Missed bugs or semantics

**None.** The benchmark behaves as its hidden documentation describes, and no observed response
contradicted ground truth, so there was no bug to miss. The closest thing to a defect found is a
documentation-level one *in the benchmark*: the hidden README's phrase "returns the original
payment" understates the live-record semantics that CX-2 exposed. Consider tightening that
sentence so future evaluations do not score a correct model as over-precise.

## 9. Metrics

**Traces and calls per phase** (call counts computed from the trace files, not from prose):

| Phase | Traces | Calls | Mix | Budget |
| --- | --- | --- | --- | --- |
| Investigator | 3 (traces 1-3) | 11 | 3 Reset, 8 AuthorizePayment | 3 traces / 15 calls |
| Modeler (M-001) | 0 | 0 | replay only, no live calls | - |
| Adversary | 4 (traces 4-7) | 20 | 4 Reset, 15 AuthorizePayment, 1 CapturePayment | 4 traces / 20 calls - **fully spent** |
| Refiner (M-002) | 0 | 0 | replay only, no live calls | - |
| Test promotion | 1 (trace 8) | 5 | 1 Reset, 3 AuthorizePayment, 1 CapturePayment | 1 live execution |
| **Total** | **8** | **36** | 8 Reset, 26 AuthorizePayment, 2 CapturePayment | |

*Accounting discrepancy:* the investigator's journal claims 9 `AuthorizePayment` calls; the traces
contain 8. Budgets should be counted from the recorded traces, not from the role's prose.

**Replay steps before and after refinement:**

| Replay | Traces | Steps | Conforming | Violations | Excluded |
| --- | --- | --- | --- | --- | --- |
| M-001, modeler pass | 3 | 11 | 6 | 0 | 5 |
| M-001, adversary re-replay | 7 | 31 | 15 | **4** | 12 |
| M-002, refiner pass | 7 | 31 | 21 | 0 | 10 |
| M-002, this record's re-verification | 8 | 36 | 25 | 0 | 11 |

Checked steps by claim (8-trace run): Reset 8, Claim R 8, Claim V 4, Claim C 3, Claim L 2. All 11
excluded steps are in a single named region (fresh/declined-key success-vs-decline selection):
9 are establishment `201`s consumed as state bootstrap (one per trace, plus trace 5's second key),
and 2 are trace 3's declines. Trace 3 is still reported "not applicable / unknown" rather than
"passed", because its only checked step is the trivial `Reset`.

**Adversary results:** 2 distinct counterexamples, 4 violating steps across 3 of 4 traces, 4
attacks finding no disagreement, 1 question proven unanswerable at this API surface, 1
extrapolation promoted from assumption to corroborated evidence. Minimization: CX-1 reduced from
5 calls to 3 (provably minimal) plus 2 discrimination calls; CX-2 already minimal at 4.

**Non-vacuity:** 6 mutants flagged (7 violating steps), 1 mutant correctly excluded.

**Tests promoted:** 1, passing, judged entirely through `spec.Allows` via `TraceReplayer`.

## 10. Tooling and process friction; next abstractions

**Partial model scope and bootstrap have no first-class support.** This is the dominant friction.
Accordant's `Apply` has no per-region `UNKNOWN` outcome, so "this call is outside what I claim"
had to be hand-built three times: `ModelScope.cs` (classifier), `ReplayRunner.cs` (exclude +
seed state from the observed response), and again in `PartialModelReplay.cs` for the test project.
CX-2 is a direct consequence of the gap - the precondition existed in the model's prose but not in
its code. **Next abstraction:** a declarative scope predicate per operation plus an explicit
excluded/unknown replay outcome and a state-seeding hook, so a partial model can say "not my
region, learn this from the observed response" once and have both the runner and any promoted test
inherit it.

**Replay stopping behavior.** `TraceReplayer.Replay` stops a batch at its first violation, so a
later checked call in the same batch silently vanishes from the report - the adversary flagged
that "absent from the report" must not be read as "conforming". The refiner worked around it by
replaying **one call at a time**, which also fixed a second problem (a mutating `CapturePayment`
mid-batch left successors classified against stale state). Per-call verdicts should be the bridge
default, not something every model reinvents.

**Scratch disposal has no lifecycle.** `scratch\` still holds a full duplicate of traces 1-7 plus
two probe traces; `model\selfcheck-sandbox\` and `tests\_probe\` were created and manually deleted
mid-pass. Nothing marks a directory as disposable, and nothing distinguishes a durable trace from
a sandbox copy except hashing (done here: all 7 scratch copies are byte-identical, so the corpus
was never mutated). A harness-owned sandbox with a defined lifetime, and a "durable vs. scratch"
marker on trace directories, would remove a whole class of packaging risk.

**Operation execution ergonomics.** Every call is a hand-built `{path, query, headers, body}`
`JsonElement` with `{status, body}` navigated back out. Concrete friction the journals recorded:
`TraceRecorder`/`TraceStore` are static classes (`new TraceRecorder()` -> CS0712); whether
`TraceRecorder.RunAsync` auto-saves was undocumented and had to be established with a throwaway
reflection probe; a `ProjectReference` to the model is not enough to name `Spec<TState>` (CS0012)
so `Accordant.Operations.dll` must be referenced directly; and `System.IO.Hashing` must match
`Accordant.dll`'s exact dependency version or CS1705. A typed per-benchmark request/response
surface (or a small CLI) would remove most of this.

**Portability and packaging (found by this evaluation, not by the loop).** `workspace.json`
embeds an absolute, machine-specific `document` path, so it is not copied here (see Exclusions).
More importantly the promoted test is *coupled to the workspace layout*: `WorkspaceLocator`
requires a `workspace.json` ancestor, and `TraceRecorder` writes into the workspace's `traces\`.
The archived copy therefore builds but cannot be executed from the archive without either forging
a `workspace.json` or writing a new trace into an immutable corpus. A promoted test should be able
to take its target configuration and its trace sink as inputs.

**Controller checkpoints.** The auto run took a defensible default at all five checkpoints and, in
each case, recorded the alternative it rejected - which is what makes it reviewable. Two gaps: the
controller never recorded *what evidence would flip* a decision (e.g. "if the decline threshold
is ever bracketed, revisit the transaction-1 scope choice"), and there is no "once" mode between
interactive and auto for the single decision that actually mattered (transaction 4's
narrow-scope-vs-absorb-capture choice). Making the checkpoint contract explicit - decision,
rejected alternative, flip condition - would make an unattended run auditable without pausing it.

## 11. Did the adversary materially improve the result?

**Yes, decisively.** Investigator-only evidence was *consistent* with M-001: 3 traces, 0
violations, both claims "supported". Scored against ground truth, both of those claims are
**wrong** - one missing rule that sits in front of the modeled one (validation precedence), and
one wrong state abstraction (frozen snapshot vs. live record). More evidence of the same kind
would not have found either: every investigator trace stayed on the happy path of well-formed
payloads and never touched a lifecycle operation.

The adversary changed the outcome on four axes:

1. **Correctness** - 2 false rules removed, 2 correct rules added (V, L), 1 over-broad rule
   narrowed (C). M-002 has zero rules that are wrong against ground truth.
2. **Structure** - CX-2 forced preconditions out of prose and into a shared classifier used by
   both the spec and the runner. That is a permanent improvement in how the model can fail.
3. **Evidence quality** - the both-fields-changed case moved from flagged extrapolation to
   corroborated evidence, and decimal-value equality on amount was established rather than assumed.
4. **Epistemic hygiene** - one question was proven *unanswerable* at this API surface rather than
   left as an open unknown, which is strictly more useful to the next pass.

Cost: 4 traces and 20 calls, versus the investigator's 3 traces and 11 calls. The
minimization work (CX-1 from 5 calls to a provably minimal 3, plus 2 discrimination calls that
isolated the cause) is what made the refinement small and confident rather than speculative.

## 12. What is in this record

```
traces\                   8 immutable RecordedTrace files, byte-for-byte, grouped by
                          the original per-experiment subdirectory names
frontier.md               final snapshot (hypotheses H-001..H-006, M-001 verbatim,
                          adversary results, M-002, promotion record)
journal.md                final snapshot (all five role passes)
controller-journal.md     final snapshot (role selection and checkpoint rationale)
model\                    M-002 source, README, and the accepted replay-report.md snapshot
tests\                    the promoted conformance test project source and README
verification\             replay-report-all-8-traces.md, generated by *this* evaluation
                          (see below) - the only generated file in this record
Directory.Build.props     isolation shims so the archived projects build inside this
Directory.Packages.props  repository without being edited (see below)
```

### Exclusions

Not copied, per scope: `workspace.json` (contains an absolute, session-specific `document` path -
its relevant settings are simply `AdapterType: "openapi"` with a `document` pointing at the
benchmark's OpenAPI document and a `baseUrl` pointing at the locally running service);
`target\openapi.json` (identical to the benchmark's committed document); `sdk\*.dll` and
`sdk\docs\`; `scratch\` (disposable probe/sandbox artifacts, including duplicate trace copies);
all `bin\`/`obj\`, package caches, and test result files; and the deleted probe/self-check
sandboxes, which no longer exist.

### Integrity

- Every copied file was SHA-256 verified against its source after copying: **26/26 identical**,
  including all 8 traces. No trace was edited.
- All 7 trace copies that also existed under `scratch\` hash-match the durable originals, so the
  adversary's sandbox replay demonstrably never mutated the corpus.
- Traces were scanned for credentials, tokens, hostnames, and absolute paths before copying:
  **none present** (they contain only operation names, payment payloads, and server-generated ids).
- Of all copied source and documentation, **no file** contains an absolute or user-specific path;
  the only file that did was `workspace.json`, which is excluded. **No copied artifact was
  rewritten** - everything here is the staged bytes.

### Reproducing the build

The archived projects reference `..\sdk\*.dll` / `..\..\sdk\*.dll`, which are intentionally not
committed. To rebuild, stage the Accordant/Specmine assemblies into a `sdk\` directory beside
`model\`, then:

```powershell
dotnet build model\PaymentModel.csproj
cd model; dotnet run --no-build -- <path-to-this-directory>   # regenerates the replay report
dotnet build tests\PaymentConformance.Tests\PaymentConformance.Tests.csproj
```

`Directory.Build.props` and `Directory.Packages.props` in this directory exist only so the
archived projects keep building unedited inside this repository: MSBuild imports the *nearest*
file of each name, so they shield the archived copies from the repository's packaging defaults and
from Central Package Management, which rejects the projects' inline `PackageReference` versions
(NU1008). The archived `.csproj` files themselves are unmodified.

**Verified for this record** (.NET SDK 10.0.303, package restore redirected away from the
repository's `packages\` folder so nothing outside this directory was written):

- `model\PaymentModel.csproj` builds clean: 0 warnings, 0 errors.
- The replay runs over all 8 traces: 36 steps, 25 conforming, **0 violations**, 11 excluded; the
  trace 1-7 portion reproduces the staged `model\replay-report.md` line-for-line, and that
  snapshot was restored to its original bytes afterward.
- `tests\PaymentConformance.Tests\PaymentConformance.Tests.csproj` builds clean: 0 warnings,
  0 errors.
- The live test was **not** re-executed here. The staged run's result is recorded above (1 test,
  1 passed, 4 checked steps `Conforming`). The benchmark service was still reachable at
  `http://127.0.0.1:5088` at evaluation time, but re-running would have written a ninth trace into
  the immutable corpus (and needs a `workspace.json` that is deliberately absent). Replaying trace
  8 - the live execution's own recorded responses - through the archived model is the equivalent
  check, and it conforms.

## 13. Next recommended engineering slice

**Give partial models first-class scope in the Accordant/Specmine bridge.** Concretely: a
declarative per-operation scope predicate, an explicit `Excluded`/`Unknown` replay outcome
alongside `Conforming`/`Violation`, a state-seeding hook for out-of-scope calls that produced
real observable state, and per-call replay verdicts by default. This single slice subsumes the
three largest frictions in this run - the hand-rolled classifier duplicated across the runner and
the test project, the batch-stops-at-first-violation reporting gap, and the prose-only precondition
that caused CX-2 - and it is a prerequisite for reusing this loop on `InventoryReservation` without
rewriting the same few hundred lines of scaffolding.

Runner-up, if a behavioral slice is preferred instead: the decline rule is the single highest-value
unknown left (it alone accounts for every excluded replay step), and bracketing its threshold is
cheap - a handful of calls - which would let a future revision model fresh-key outcome selection
and finally produce a trace that conforms end to end.
