# Durable jobs: contract, process design, service, conformance

This sample follows one durable job through Accordant's intended proof and
testing chain:

```text
process design --safety refinement--> atomic contract
                                           |
                                           | generated sequential and
                                           | concurrent conformance tests
                                           v
                                  DurableJobService

process design <---- deliberate structural mirroring ----> service
                    (reviewed and tested, not a theorem)
```

The finite workload has one modeled job (`job-1`), one payload (`alpha`), one
result (`ALPHA`), one error, two attempts, one worker crash, and queue depth
`0..2`. API conformance also uses `job-2` to check identifier isolation.

```powershell
dotnet test Samples\DurableJobs\DurableJobs.csproj
```

The targeted suite currently has **33 tests**, including **41 generated
sequential cases** and **11 generated concurrent cases**.

## Atomic contract

`AtomicJobContract.cs` is both the API oracle and, through
`Accordant.ModelChecking.Operations`, a model-checking graph.

```text
Missing
Pending
Succeeded(result)
Failed(error)
Cancelled
```

| Operation | Contract behavior |
| --- | --- |
| `SubmitJob` | `Missing -> Pending`; a duplicate returns the existing logical job |
| `GetJob` | reports the complete current state without changing it |
| `CancelJob` | `Pending -> Cancelled`; missing and terminal states are stable |
| `CompleteSuccess` | `Pending -> Succeeded(result)` |
| `CompleteFailure` | `Pending -> Failed(error)` |

Cancellation and completion are competing atomic transitions. The first
terminal write wins; later calls return that outcome without changing it.
Responses drive state updates through Accordant's normal response-dependent
operation machinery. Requests for `job-2` cannot observe or mutate `job-1`.

The complete contract graph is **5 nodes / 25 edges**. Its liveness check is
deliberately conditional: `Pending` can be observed or re-submitted forever
without fairness, and becomes terminal under the stated completion fairness.

## Structured process detailed design

`DurableJobDesign.cs` is the single canonical detailed design. It uses the
repository's actual
`Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ProcessSystemModel`
API rather than a sample-specific state-machine wrapper.

The API is experimental and unpackaged. Every live process is advanced against
current shared state, guarded waits are re-evaluated after interleavings, and
failure-domain ownership is part of exact scheduler configuration.

### Shared implementation-shaped state

| Part | Modeled state |
| --- | --- |
| durable row | id, payload, status, result/error, attempt count |
| dispatch queue | depth `0..2`; every entry is the one fixed job id |
| active lease | job, payload, attempt, token |
| worker response | `None`, `Succeeded`, or `Failed`; volatile across a crash |
| worker lifecycle | `Idle`, `Running`, `Crashed`, `Stopped` |
| crash bound | `0..1` |

The row and lease are nested `[State]` objects matching the private `JobRow`
and public `WorkerLease` in `DurableJobService`. Queue depth is sufficient
because every bounded delivery carries the same fixed job id.

### Independently active processes

Outside the worker failure domain:

* recurring submit, get, and cancel actions;
* recurring duplicate delivery, dispatch loss, and durable-row reconstruction;
* a recurring lease-reaper action;
* the optional recurring row-loss defect used by a negative refinement test.

Inside the `worker-host` failure domain:

* the structured worker process;
* recurring success and failure response actions.

A crash discards all three worker-host continuations and clears only volatile
attempt-response state. The durable row, queue, lease, attempts, and crash count
remain. Lease expiry moves the host to `Stopped`; restart relaunches fresh
worker-host processes.

### Code-shaped worker control flow

The worker is ordinary sequential process code with an explicit iteration
frame and a real helper call frame:

```csharp
context => context.Forever(
    "worker-loop",
    allowCompletionAfterCancellation,
    WorkerIteration)

var token = await context.Read("lease-token", state => state.LeaseToken);
var outcome = await context.Call("await-attempt", token, AwaitAttempt);
```

That mirrors `ClaimNext`, `CompleteSuccess`, and `FailAttempt` in the service.
The one-step recurring roles use `RepeatedAction`, so they add no continuation
configurations.

The success/failure response is recorded by an ordinary **changing** model
action before the worker branches. Keeping it as a changing response-source
action mirrors the implementation-shaped environment and lets action-aware
fairness select the outcome family collectively.

### Exact configuration graph

`ProcessGraphDiagnostics` reports **61 exact configurations / 293 edges / 56
distinct shared-domain states**. Structured iteration, calls, and stateless
recurring actions remove continuation-only administrative configurations without
merging distinct executable continuations.

One checked `AbandonAttempt` edge leaves shared state unchanged but moves the
worker to a different continuation. The tests assert that its source and target
have equal domain state and different node fingerprints. This prevents a
state-only graph from accidentally merging distinct executable configurations.

No synthetic always-enabled stutter action is added. Duplicate API requests
and `Get` are real named state-neutral semantic actions. Synthetic terminal
stutter is excluded from action-aware fairness.

## Property suite

`DurableJobProperties.cs` keeps reusable formulas and fairness assumptions
separate from the tests.

### Safety

| Property | What it excludes |
| --- | --- |
| `OutcomeShapeIsConsistent` | success with an error, failure with a result, or nonterminal payload leakage |
| `AcceptedRowNeverDisappears` | resurrection of `Missing` after durable acceptance |
| `TerminalOutcomeIsStable` | changing status/result/error/attempts after a terminal decision |
| `TerminalDecisionIsSingleWinner` | a terminal write from a non-pending state or one that leaves queued/leased work |
| `QueueIsBoundedAndEligible` | queue overflow, delivery for a missing row, or terminal queued work |
| `LeaseWorkerAndAttemptsAreConsistent` | lease/worker disagreement, stale token/attempt identity, invalid response ownership, or exceeded bounds |
| `AttemptsNeverDecrease` | retry-budget rollback |

The tests also establish non-vacuity:

* cancellation and successful completion are simultaneously available from a
  claimed, success-ready configuration and reach different terminal targets;
* first-attempt failure redispatches;
* final-attempt failure terminates;
* early lease expiry redispatches and stops the host;
* final-attempt lease expiry terminates.

### Liveness and fairness

`Pending -> eventually Terminal` holds under this explicit bundle:

| Obligation | Strength | Reason |
| --- | --- | --- |
| rebuild a missing dispatch | weak | while the durable pending row remains undispatched, rebuild stays enabled |
| claim dispatched work | **strong** | dispatch loss can disable claim between rebuilds, so claim is only intermittently enabled |
| record a success/failure response | weak | once an attempt runs without crashing, both finite response actions remain enabled |
| commit the recorded response | weak | the selected worker branch remains enabled until completion, cancellation, or the one bounded crash |
| expire a crashed lease | weak | the crashed lease stays eligible until expiry or cancellation |
| restart a stopped host | weak | restart stays enabled while the host is stopped |

Retries and crashes are bounded, so those assumptions force success,
exhausted failure, final-attempt expiry failure, or cancellation.

Negative checks are part of the specification:

* no fairness permits state-neutral API traffic forever;
* infrastructure fairness without response fairness permits a running attempt
  to wait forever;
* weak claim fairness permits an infinite lose/rebuild cycle;
* missing dispatch, crashed lease, and stopped-worker recovery each fail
  without their own assumption and hold with it;
* a job that has crashed still reaches a terminal outcome under the full
  fairness bundle.

These are scheduler/environment assumptions, not unconditional service
guarantees.

Every obligation is stated over typed `ProcessTransition` metadata. Response
fairness is intentionally collective: either success or failure discharges the
single attempt-outcome obligation. Job-scoped actions carry `job-1` as their
stable subject.

## Direct safety refinement

`DurableJobRefinement.cs` projects only the durable row:

```text
JobId   <- Row.JobId
Payload <- Row.Payload
Status  <- Row.Status
Result  <- Row.Result
Error   <- Row.Error
```

Queue state, lease state, attempt response, worker lifecycle, crash count, live
roles, and continuations are hidden.

| Process action | Atomic interpretation |
| --- | --- |
| `Submit`, `Get`, `Cancel` | corresponding contract action |
| `CompleteSuccess` | `CompleteSuccess` |
| final `FailAttempt` | `CompleteFailure` |
| final `ExpireLease` | `CompleteFailure` |
| retryable `FailAttempt` | hidden |
| claim, dispatch, response arrival, abandonment | hidden |
| crash, restart, completion control | hidden |

Every declaration is checked. A hidden transition must really leave the mapped
contract state unchanged; a visible transition must match the declared atomic
edge, including state-neutral duplicate/late calls.

Two deliberately broken variants still fail for direct reasons:

* preserving a live lease across cancellation lets the pending worker
  `CompleteSuccess` overwrite `Cancelled`;
* losing the accepted row maps `Pending` back to `Missing`.

## Service and conformance

`DurableJobService.cs` remains an independent thread-safe implementation:

* mutable durable row;
* real `Queue<string>`;
* attempt-number `WorkerLease`;
* lock-protected submit, cancel, claim, completion, retry, crash, expiry,
  restart, loss, and rebuild;
* deterministic worker controls for repeatable tests;
* a separate driver lock that serializes completion drivers without preventing
  cancellation from racing the terminal write.

Generated conformance uses the atomic contract as the oracle:

* **41 sequential cases** over both job ids and every response-dependent
  operation;
* **11 concurrent linearizability cases** over submit, cancel, and completion;
* a barrier-synchronized cancellation/completion race with exactly one winner;
* targeted duplicate-dispatch, crash, expiry, restart, retry exhaustion,
  dispatch reconstruction, stale-work cleanup, and alternate-id isolation
  checks.

## Proof boundary

Model checking establishes:

* the complete atomic graph satisfies its checked safety and fair-liveness
  properties;
* the complete process graph satisfies its checked safety properties and its
  liveness properties under the documented fairness bundle;
* every checked process-design transition safety-refines the atomic contract
  under the direct state mapping and action declarations.

Conformance testing establishes that the exercised implementation histories
are accepted by the atomic contract, including concurrent histories.

**There is no implementation-to-design theorem.** The service deliberately
mirrors the process design, and white-box tests cover the hidden mechanisms,
but Accordant does not prove that every C# execution is a process-design
execution.

**Temporal refinement is also intentionally deferred.** Design liveness and
contract liveness are checked independently under different, explicit
fairness assumptions. The sample does not claim that the design's fairness
bundle implements the contract's abstract completion fairness.

## Files

| File | Purpose |
| --- | --- |
| `Domain.cs` | finite workload and API types |
| `AtomicJobContract.cs` | response-dependent contract and atomic graph adapter |
| `DurableJobDesign.cs` | structured process design and failure domain |
| `DurableJobProperties.cs` | safety formulas, progress formulas, fairness |
| `DurableJobRefinement.cs` | direct state mapping and action declarations |
| `DurableJobService.cs` | independent thread-safe implementation |
| `AtomicJobContractTests.cs` | contract behavior, isolation, liveness, size |
| `DurableJobDesignTests.cs` | exact configuration, frames, and recurring roles |
| `DurableJobSafetyTests.cs` | design invariants and non-vacuity witnesses |
| `DurableJobLivenessTests.cs` | fairness ladder and recovery progress |
| `DurableJobRefinementTests.cs` | correct and broken safety refinements |
| `DurableJobConformanceTests.cs` | generated and targeted implementation checks |
