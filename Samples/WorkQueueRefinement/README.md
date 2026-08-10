# WorkQueueRefinement

A leased work queue with competing workers, retries, expiring leases,
cancellation and purging, checked against the ledger a client actually sees.

The ledger is deliberately **more committed than the implementation**: the
moment an entry is assigned it publishes the worker that first accepted it and
the result the entry will end with. That single choice forces all three
refinement mechanisms at once.

| Fact the ledger needs | Where it lives | Mechanism |
|---|---|---|
| which worker *first* accepted the entry | the concrete past — retries move the lease, expiry erases it | `.Augment(...)` |
| the result committed at assignment time | the concrete future | `.WithWitness(...)` |
| which ledger action a queue transition is | neither model's state | `.MapTransition(...)` |
| everything else | the concrete present | `.Map(...)` |

```csharp
Refinement
    .Between<QueueState, LedgerState>(WorkQueue.Explore(config), Ledger.Explore(config))
    .Augment(initial: _ => InitialHistory(config), next: RememberFirstClaimant)
    .WithWitness(initial: _ => WitnessChanges.None, next: TrackOutcomes)
    .Map(MapToLedger)
    .CheckTemporal(
        concreteFairness: WorkQueueFairness.Implementation,
        abstractFairness: WorkQueueFairness.LedgerLiveness);
```

## The two models

**`QueueState` — the implementation.** Two workers, two tasks, two attempts.

| Action | Meaning |
|---|---|
| `lease-w{w}-t{t}` | a worker takes a ready task |
| `expire-w{w}-t{t}` | the lease expires; **no attempt is spent**, so this is the source of infinite behavior |
| `complete-w{w}-t{t}` / `fail-w{w}-t{t}` | the attempt succeeds, or fails and is retried until the budget runs out |
| `request-cancel-t{t}` / `observe-cancel-w{w}-t{t}` / `cancel-ready-t{t}` | cooperative cancellation: a client asks, a worker honors it, or the queue closes an unleased entry |
| `purge-t{t}` | the queue removes an entry that already burned an attempt |

**`LedgerState` — the specification.** `Enqueued → Assigned(owner, result) →
Settled | Purged`, plus the direct `Enqueued → Settled(Cancelled, no owner)`
path for a task cancelled before assignment. Assignment nondeterminism is
modeled as several `StepResult`s of the single step function
`ledger-accept-t{t}`, so a fairness constraint names the action *"assign entry
t"* rather than one specific committed outcome.

## Prediction lifecycle

One operation identity per task, `outcome-t{t}`, with the domain
`{Completed, Dropped, Cancelled}`:

```text
first lease           Introduce  -> the proof branches three ways
expire, retry lease   None       -> the prediction rides through the retry
failure with budget   None       -> a failed attempt reveals nothing
complete / drop /
observe-cancel /
cancel-ready          Resolve    -> copies that predicted otherwise die
purge                 Cancel     -> the commitment is erased; all copies merge
```

Two predictions can be pending at once. Each has three possible values, so the
proof branches into up to `3 × 3 = 9` copies.

**Trust boundary.** The checker validates that a resolving value belongs to the
introduced domain, that an identity is not resolved twice, and that no
checker-local value is mutated. It **cannot** validate that the value is the
one the transition really reveals. Every `Resolve` in this sample reads its
value from the transition's step function and target state; resolving with a
guess would silently prune the sibling copies and could prove a wrong ledger.
That obligation is on the model, not on the checker — which is exactly why
`Cancel` exists for the purge case, where nothing is revealed.

## Weak versus strong fairness, in one model

Two ledger obligations need different strengths, and the reason is visible in
the counterexample lassos.

| Concrete fairness | Ledger obligation | Verdict |
|---|---|---|
| none | settlement cannot stay continuously enabled and untaken | `TemporalFairnessMismatch` — lease/expire forever |
| **weak** attempt resolution + cancellation | same | **still fails** |
| **strong** attempt resolution + cancellation | same | refines |
| weak leasing of task 1 + cancel sweep | assignment cannot stay continuously enabled and untaken | fails — the workers ping-pong on task 0 |
| **strong** leasing of task 1 | same | **still fails** — a cancellation request disables leasing |
| strong leasing + **weak** cancel sweep | same | refines — cancellation closes the entry and disables assignment |

* Settling is only enabled while a worker holds the lease, and the lasso
  passes through states where nobody does. It is *intermittently* enabled, so
  **weak fairness is powerless and strong fairness is required**.
* Sweeping a cancelled unleased entry stays enabled once enabled, so **weak
  fairness is sufficient** — asking for strong there would over-specify.
* A strong obligation on an action the implementation has *disabled* (leasing a
  cancelled task) is vacuous; liveness then has to come from the concrete
  alternative.

Accordant fairness is grouped by `StepFunctionId`. Consequently the
implementation bundle applies strong fairness separately to every
`lease-w{worker}-t{task}` and attempt-outcome action. This deliberately models a
strong per-worker scheduler. A coarser assumption such as “some worker
eventually leases task t” would model the worker choice as several
`StepResult`s of one `lease-t{task}` step, just as the ledger does for
assignment outcomes.

## Why an explicit action mapping is the next step

Two ledgers in `LedgerOptions` are perfectly reasonable specifications that a
state-valued mapping cannot align on its own:

* `IncludeCloseCancelled` adds `ledger-close-cancelled-t{t}`, which performs
  the same state change as `ledger-settle-t{t}`. The intended correspondence is
  `observe-cancel-w{w}-t{t} → ledger-settle-t{t}` and
  `cancel-ready-t{t} → ledger-close-cancelled-t{t}`, but the mapping only
  produces states, so temporal refinement reports
  `AmbiguousTemporalRefinementException` with `MatchCount == 2`.
* `IncludeRecordAttempt` adds a ledger action with **no state footprint**. Now
  every concrete transition that leaves the ledger alone has two abstract
  responses — stutter, or that action. Accordant fairness is intentionally
  defined only over changing edges, and alignment fails before fairness
  analysis anyway. An explicit action mapping must first say which response
  the concrete transition represents.

Both ledgers still pass `Check()`: safety refinement carries a *set* of
coherent abstract configurations and never has to choose. The gap is specific
to deterministic temporal alignment.

## Declaring the ledger action

`WorkQueueRefinementCheck.LedgerActions(options)` is the transition mapping.
It names one ledger action per queue transition and leaves everything else
`Unconstrained`:

```csharp
Refinement
    .Between<QueueState, LedgerState>(...)
    .Augment(...).WithWitness(...).Map(MapToLedger)
    .MapTransition(WorkQueueRefinementCheck.LedgerActions(ledgerOptions))
    .CheckTemporal(
        concreteFairness: WorkQueueFairness.Implementation,
        abstractFairness: WorkQueueFairness.LedgerLiveness);
```

| Queue transition | Ledger response |
|---|---|
| first `lease-w{w}-t{t}` | `ledger-accept-t{t}` — the assignment the ledger commits at |
| retry `lease`, `expire`, `request-cancel` | stutter |
| `fail` inside the retry budget | `ledger-record-attempt-t{t}`, or stutter when the ledger has no such action |
| `fail` that exhausts the budget, `complete`, `observe-cancel` | `ledger-settle-t{t}` |
| `cancel-ready-t{t}`, entry never assigned | `ledger-cancel-unassigned-t{t}` |
| `cancel-ready-t{t}`, entry already assigned | `ledger-close-cancelled-t{t}`, or `ledger-settle-t{t}` |
| `purge-t{t}` | `ledger-purge-t{t}` |

**Two rows need the claim history, not just the transition.** A queue entry is
`Ready` both before its first lease and between retries. The concrete step
alone therefore cannot say whether the ledger entry is already assigned, which
is exactly what decides whether a lease is the assignment or a retry, and
whether closing an unleased entry is `ledger-cancel-unassigned` or
`ledger-close-cancelled`. `.MapTransition(...)` mirrors the arity of the
`.Map(...)` it follows, so the declaration reads `ClaimHistory` — the same
augmentation the state mapping reads, taken at the position the transition
departs from.

The declaration only narrows the responses the state mapping already made
state-consistent, so:

* the same declarations applied to the **default** ledger change nothing — it
  had no ambiguity to resolve;
* `AlwaysStutter` is a `TransitionMismatch`, not a pass, because stutter is
  not state-consistent for a transition that moves the ledger;
* `SettleCancelledAsUnassigned` makes an action-level error visible to
  `Check()`, which state-only safety refinement accepted in silence.

`ledger-record-attempt-t{t}` remains **state-neutral after it is aligned**.
Accordant fairness is defined over changing edges by design, so weak or strong
fairness for that action adds no obligation: it can be neither starved nor
discharged. What the declaration buys there is the abstract *configuration*
the run continues from, not fairness accounting.

## Measured sizes

| Model | Nodes | Edges |
|---|---|---|
| concrete queue (2 workers, 2 tasks, 2 attempts) | 292 | 964 |
| ledger | 256 | 608 |
| refinement proof configurations (concrete × history × predictions) | **6 417** | — |
| the same lifecycle without the purge `Cancel` | 7 129 | — |
| concrete queue with a third task | 4 360 | 19 500 |
| ledger with a third task | 4 096 | 14 592 |
| proof configurations with a third task | **413 127** | — |

The mapping is memoized per `(concrete node, proof identity)`, so counting
mapping invocations counts distinct proof configurations. The three-task
measurement is an `[Explicit]` test — it takes about 80 seconds.

This is more than ordinary graph growth: the witness multiplier is exponential
in simultaneously pending finite domains (`3^tasks` here), and it combines
with distinct augmentation histories. The default grows from 292 concrete
states to 6,417 proof configurations; three tasks grow to 413,127. That
measured distinction will determine whether witness-specific bounds are worth
adding.

## Running

```bash
cd Samples/WorkQueueRefinement
dotnet test
```

## Files

| File | Contents |
|---|---|
| `WorkQueueModels.cs` | `QueueState` and the eight implementation actions |
| `LedgerModels.cs` | `LedgerState`, the ledger actions, and `LedgerOptions` |
| `WorkQueueRefinementCheck.cs` | `ClaimHistory`, `OutcomeWitness`, the lifecycle, the mapping, the ledger-action declarations, the fairness bundles, and the deliberately broken variants |
| `WorkQueueRefinementTests.cs` | safety refinement, diagnostics, prediction lifecycle, measurements |
| `WorkQueueTemporalFairnessTests.cs` | the weak/strong fairness ladder |
| `WorkQueueActionAmbiguityTests.cs` | the two ledgers that motivate an action mapping, and the declarations that resolve them |
