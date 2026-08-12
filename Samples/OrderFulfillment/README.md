# Order fulfillment: model checking an implementation, not just a contract

This sample model-checks a small but implementation-shaped web application: a
checkout controller that commits database rows inside a transaction and queues
background work in the same commit, and a payment worker that reads the queued
row, calls an external payment gateway whose answer may be ambiguous, and
commits the result — retrying safely when it has to.

It is also the repository's case study for **authoring the same compiled model
through three frontends**: hand-written `IStepFunction`s, the `Operation`
adapter, and the experimental replay coroutine. All three produce ordinary
`IState`, `IStepFunction`, `StepResult`, and `StateGraph` objects and are
checked by the ordinary backend. Nothing here has a private path into
exploration, `ENABLED`, fairness, or refinement.

```powershell
dotnet test Samples\OrderFulfillment
```

## The system

| Table / system | Modeled as |
| --- | --- |
| `orders` | `StoreState.Orders[order]` — `Missing`, `Submitted`, `Rejected`, `Paid`, `Failed` |
| `payment_outbox` | `StoreState.Outbox[order]` — `Absent`, `Pending`, `Leased` — plus `LeaseHolder` |
| worker in-flight context | `StoreState.WorkerOrder[worker]`, `StoreState.WorkerAttempt[worker]` |
| payment gateway ledger | `StoreState.Charges[order]`, saturating at 2 |

**`POST /orders/{id}`** screens the order and commits one transaction:

* *accepted* — writes the order row **and** the outbox row together, and queues
  the background handlers for that order;
* *rejected* — writes only the order row, and queues nothing;
* *duplicate* — a real request with a real response that writes nothing.

**The payment worker** runs three sequential actions per attempt:

1. `pick-up` — read the outbox and take the lease.
2. `call-gateway` — call the external system. Four outcomes: charged and
   answered, **charged but timed out**, failed without charging, or permanently
   declined.
3. `settle` — one transaction that moves the order row and the outbox row
   together, or returns the row to the queue for a retry.

The third outcome of the gateway is the interesting one: the card is charged and
the worker still sees a retryable failure, so a correct worker must retry
*and* must not charge again.

### Deliberate finiteness

`Charges[order]` saturates at 2. Charging is monotone and every property here
only distinguishes "never charged", "charged once", and "charged more than
once", so collapsing counts above one can neither hide a double charge nor
invent one. Retries are otherwise unbounded, which is what gives the model a
real retry *cycle* rather than a finite unrolling.

## What is checked

Safety (`FulfillmentSafetyTests`, `FulfillmentProperties`):

| Property | Meaning |
| --- | --- |
| `NoChargeWithoutCommittedOrder` | the gateway is never called for an order whose accepting transaction did not commit, and never for a rejected one |
| `OutboxMatchesOrder` | `outbox row exists ⇔ the order is awaiting payment` — the transactional-outbox invariant |
| `ChargedAtMostOnce` | retrying an ambiguous call does not charge the customer twice |
| `NoPaidOrderWithoutCharge` | an order is never reported paid unless it really was charged |
| `ResolutionIsFinal` | a closed order is never reopened or closed again differently (a transition property) |

Progress (`FulfillmentLivenessTests`), under **explicitly stated** assumptions:

* `WorkersRun` — weak fairness on the worker actions: the worker process is not
  stopped forever.
* `GatewayAnswersDefinitively` — **strong** fairness on the gateway call that
  produces a definitive answer: *the payment gateway does not fail transiently
  forever*.
* `ControllerRuns` — weak fairness on the controller, needed only to claim that
  every order id is eventually submitted at all.

Both are needed, and the gateway assumption has to be strong: on the retry cycle
the gateway call is not continuously enabled, because the worker spends part of
every iteration picking the row up and settling it. `WeakGatewayFairnessIsNot
EnoughOnTheRetryCycle` checks that weak fairness leaves the property violated.

The idempotent duplicate submit is a **state-neutral** edge. It is a real
request with a real response, but Accordant counts only changing edges for
fairness and `ENABLED`, so a run that does nothing but re-submit forever leaves
the worker's obligation outstanding and is excluded — without the duplicate
edge ever being treated as progress.

Interleaving (`FulfillmentInterleavingTests`) checks that the model is not
secretly sequential: a second request can arrive at every stage of an in-flight
attempt, both orders can be in flight at the gateway in one run, two workers can
take the same row from the same state (and the lease resolves the race), and
the `Operation` frontend exposes the same interleavings with both
response-dependent outcomes still available mid-attempt.

### Measurements

| Graph | Nodes | Edges |
| --- | ---: | ---: |
| hand-written, 2 orders / 1 worker | 147 | 532 |
| `Operation` controller queueing the workers, same instance | 147 | 532 |
| `Operation` controller with workers active from the start | 147 | 532 |
| hand-written worker sub-model, 1 order / 1 worker | 12 | 15 |
| compiled coroutine worker, same sub-model | 21 | 24 |
| hand-written, 1 order / 2 workers | 21 | 52 |

The coroutine's extra nodes and edges are exactly its state-neutral `Choose`
control configurations. Both Operation compositions have the same graph size
as the hand-written model, and their projected domain behavior is identical.

## Deliberately broken variants

Each defect is a real mistake, and each produces a definitive counterexample
(`FulfillmentDefectTests`).

| `FulfillmentConfig` switch | Defect | Broken property |
| --- | --- | --- |
| `WithDualWriteSubmit()` | payment work is published in one transaction and the order row committed in another | `OutboxMatchesOrder`, and `NoChargeWithoutCommittedOrder` — the trace charges an order that risk screening then *rejected* |
| `WithNonIdempotentCharge()` | the retry drops the idempotency key | `ChargedAtMostOnce` |
| `WithoutOutboxLease()` | two workers deliver the same outbox row | `ResolutionIsFinal` and `OutboxMatchesOrder` — a closed order is reopened by the loser of the race |

Every defect test is paired with a test showing the correct implementation has
no such behavior, so the invariants are not vacuous.

## The three frontends

### 1. Hand-written `IStepFunction` — the baseline

`FulfillmentSteps.cs`. The supported, fully general frontend: guard on the
state, clone, mutate the clone inside one application (that is what makes the
commit atomic), re-emit the step so node identity is stable. The worker's
program counter is not an artificial field — it is the outbox row itself
(`Pending` / `Leased`) plus the worker's in-flight context.

### 2. The `Operation` adapter — the controller

`OperationsFrontend.cs`. The controller is written exactly as it would be for
conformance testing:

```csharp
return Expect.OneOf(
    Expect.That(response => response == SubmitOutcome.Accepted, "risk screening passed")
        .ThenState((_, next) => next.CommitAcceptedOrder(order),
                   mock: () => SubmitOutcome.Accepted)
        .Triggers(FulfillmentModel.QueuedWorkFor(config, order).ToArray()),
    Expect.That(response => response == SubmitOutcome.Rejected, "risk screening refused")
        .ThenState((_, next) => next.CommitRejectedOrder(order),
                   mock: () => SubmitOutcome.Rejected));
```

`Triggers` is the queued background work: the worker handlers for an order exist
in the graph only after the accepting transaction committed. `Rejected` queues
nothing.

**Composition with independently active steps is sound and is exercised.** An
`OperationModelStep` is an ordinary step function whose outcome is a function of
the state it is applied to, so the ordinary exploration rules already interleave
it with a background process. `OperationModel.Explore` has an overload taking
`additionalSteps` which only extends the adapter's duplicate-identity validation
to the whole active set. `OperationsFrontendTests` builds the composed graph
both ways — controller queueing the workers, and controller interleaving with
workers that are active from the start — and checks that

* both satisfy every safety property above,
* both need the same fairness assumptions for progress,
* both have exactly the hand-written model's projected domain states and
  changing domain transitions,
* the composed graph **refines** the hand-written model, on safety and
  temporally under the stated fairness.

> One authoring detail matters: an outcome without a `mock:` response is
> explored with the response type's default value. The duplicate branch supplies
> `mock: () => SubmitOutcome.Duplicate` for exactly that reason.

### 3. The experimental replay coroutine — the worker

`PaymentWorkerCoroutine.cs`. The worker's logic is genuinely sequential, so this
is what it looks like written the way the implementation is written:

```csharp
while (true)
{
    await context.Loop("attempt");

    var phase = await context.Read("outbox-row", store => store.Outbox[order]);
    if (phase == OutboxPhase.Absent) return;
    if (phase == OutboxPhase.Pending)
    {
        await context.Step("pick-up", store => store.PickUpOutboxRow(order, worker, lease: true));
        continue;
    }

    var outcome = await context.Choose("gateway", CallGatewayStep.GatewayOutcomes);
    await context.Step("call-gateway", store => store.RecordGatewayCall(worker, outcome, idempotentCharge));
    await context.Step("settle", store => store.SettleAttempt(worker));
}
```

> **Experimental.** `Microsoft.Accordant.ModelChecking.Experimental.Coroutines`
> is unpackaged, carries no compatibility promise, and its runtime cannot
> enforce several of its own rules — omitted live locals in `LoopState`,
> synchronously completed foreign awaits, and loops that reach no checkpoint.
> See [Model-Checking Frontends](../../docs/concepts/model-checking-frontends.md)
> and [the coroutine sample](../CoroutineModelChecking/README.md).

The `Loop` boundary turns the retry loop into a finite graph with a real cycle:
the manual worker sub-model is 12 nodes / 15 edges, the compiled coroutine is
21 nodes / 24 edges, and the difference is exactly the state-neutral `Choose`
control configurations. After hiding `Choose`, the projected domain states and
changing domain transitions match the hand-written worker, and the coroutine
**refines** it (safety, and temporally with concrete `WeakAll`).

## The boundary: where the coroutine frontend stops

The coroutine is used only in a **closed sub-model** — one worker, one
already-committed order, nothing else active. That is not caution; two
executable findings pin it, and both are tests.

### Finding 1: a compiled coroutine step is not a function of the state it is applied to

`TheCompiledCoroutineStepIsNotAFunctionOfItsInputState`.

A compiled `CoroutineStep` carries a *pending* checkpoint that was computed by
advancing the workflow from the state the coroutine arrived at — the previous
visible edge's target. Its internal `Read` values, and for a state-derived
`Choose` its materialized choice set, are therefore fused into the *preceding*
transition.

The test takes a compiled step out of a graph built from one state and applies
it to a state another process has since written to — which is exactly what
interleaving with an independently active step does — and shows it still offers
the choice set it computed earlier, including a branch the workflow's own
selector excludes in that state. A model mixing the frontends this way could
therefore fabricate a counterexample, or hide a real interleaving behind a false
`Holds`. `CoroutineModel.Explore` also exposes no way to add steps, so this is
a boundary rather than a bug: the prototype does not claim composition.

**The sound alternative used here** is comparison, not mixing: after hiding
`Choose`, the coroutine and hand-written worker have equal projected domain
states and changing transitions, and the coroutine refines the hand-written
worker in the closed sub-model. The composed system model then uses the
hand-written worker.

### Finding 2: fairness over external nondeterminism is not statable on the compiled graph

`TheGatewayFairnessAssumptionCannotBeStatedOnTheCompiledGraph`.

The hand-written worker offers all four gateway outcomes as changing edges of a
single action, so "the gateway eventually answers definitively" is expressible
as strong fairness and it closes the retry cycle.

The coroutine decides that outcome at a `Choose`, which is visible but
**state-neutral**. Accordant's fairness counts changing edges only, so on the
compiled retry cycle there is no changing edge producing a definitive answer to
attach an obligation to. The progress property therefore stays violated under
`Fairness.WeakAll + Fairness.Strong(_ => true) + GatewayAnswersDefinitively`.
The test does not merely assert this; it checks the reason, that no node of the
reported bad cycle has a changing outgoing edge producing a definitive answer,
while the hand-written retry cycle does.

`AddingTheGatewayAssumptionToBothSidesMakesRefinementFail` shows the same gap
through refinement: `RefinementFailureKind.TemporalFairnessMismatch`.

The consequence is concrete and general: **environment nondeterminism that a
fairness assumption must constrain has to be an ordinary changing model action,
not a coroutine `Choose`.**

### Finding 3: the determinism audit reports the compiler's lambda cache

`AuditingAColdDelegateReportsTheCompilersLambdaCache`.

`verifyDeterminism: true` compares captured external variables around the
workflow body. The C# compiler caches a workflow's non-escaping lambdas in
`<>9__` fields on the closure that captured `worker` and `order`, and writes
them the first time each lambda is evaluated — so a freshly created workflow
delegate fails its own audit with a diagnostic about compiler-generated state
rather than about the model. `PaymentWorkerCoroutine.BuildAuditedGraph` explores
the *same delegate instance* once before the audited run for exactly this
reason, and `TheDeterminismAuditAcceptsTheWorkflow` then passes.

## Files

| File | Contents |
| --- | --- |
| `StoreState.cs` | the database, worker context, and gateway ledger, with the transactional mutators |
| `FulfillmentConfig.cs` | instance size and the three defect switches |
| `FulfillmentSteps.cs` | the hand-written frontend and the shared `FulfillmentAction` edge label |
| `FulfillmentModel.cs` | graph builders and the domain projection helpers |
| `FulfillmentProperties.cs` | the invariants, progress properties, and fairness assumptions |
| `OperationsFrontend.cs` | the controller as an `Operation`, and the composed graphs |
| `PaymentWorkerCoroutine.cs` | the worker as an experimental replay coroutine, and the composition-boundary study |
| `FulfillmentSafetyTests.cs` | the invariants of the correct implementation |
| `FulfillmentLivenessTests.cs` | progress, and which fairness assumption each step of the argument needs |
| `FulfillmentDefectTests.cs` | the three broken variants and their counterexamples |
| `FulfillmentInterleavingTests.cs` | evidence that requests and background work really interleave |
| `OperationsFrontendTests.cs` | the `Operation` frontend and its composition with hand-written workers |
| `CoroutineFrontendTests.cs` | the coroutine frontend, its projected behavior, refinement, and boundary |

## See also

* [Model-Checking Frontends](../../docs/concepts/model-checking-frontends.md)
* [Model checking operation specifications](../OperationsModelChecking/README.md)
* [Experimental coroutine model checking](../CoroutineModelChecking/README.md)
* [Checking Refinement](../../docs/how-to/checking-refinement.md)
