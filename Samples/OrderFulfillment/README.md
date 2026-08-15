# Order fulfillment: model checking an implementation

This sample model-checks an implementation-shaped checkout service: a
controller commits an order and transactional-outbox row, and a payment worker
leases the row, calls an external gateway, and settles the result.

The same domain behavior is authored through three frontends:

1. hand-written `IStepFunction`s;
2. the `Operation` adapter for response-dependent controller calls;
3. a fully composed structured `ProcessSystemModel`.

All produce ordinary Accordant graphs and use the same formulas, fairness, and
refinement backend.

```powershell
dotnet test Samples\OrderFulfillment
```

## Modeled system

| Component | State |
| --- | --- |
| orders | `Missing`, `Submitted`, `Rejected`, `Paid`, `Failed` |
| payment outbox | `Absent`, `Pending`, `Leased`, plus lease holder |
| worker attempt | selected order and gateway result |
| gateway ledger | charge count, saturated at two |

An accepted submit writes the order and outbox row in one transaction. A
rejected submit writes only the order. A duplicate is a real, state-neutral
request/response edge.

Each payment attempt has three semantic actions:

1. `pick-up` — atomically select and lease a pending row;
2. `call-gateway` — charge/answer, charge/timeout, transient failure, or decline;
3. `settle` — close the order or return the row for retry.

The charge count saturates at two because the properties distinguish only
never charged, charged once, and charged more than once. Retries remain
unbounded and form a real graph cycle.

## Properties

Safety:

* `NoChargeWithoutCommittedOrder`
* `OutboxMatchesOrder`
* `ChargedAtMostOnce`
* `NoPaidOrderWithoutCharge`
* `ResolutionIsFinal`

Progress:

* `SubmittedOrdersResolve(order)`
* `EveryOrderResolves()`

The liveness ladder is intentional:

* no fairness: an accepted order can hang;
* worker scheduling alone: the gateway can fail transiently forever;
* weak definitive-answer fairness: still insufficient because the gateway
  action is only intermittently enabled around a retry;
* strong definitive-answer fairness: every accepted order resolves;
* resolving every possible order also needs controller fairness.

## Structured process frontend

`ProcessFrontend.cs` builds one structured controller process per order and one
worker process per worker:

```csharp
model.Process(
    ProcessFrontend.WorkerRole(worker),
    context => context.Forever(
        "attempt-loop",
        encodedArguments,
        WorkerIteration));
```

The worker atomically chooses and claims an eligible row with `ChooseStep`,
then invokes a real checkpoint-bearing helper:

```csharp
await context.Call(
    "call-gateway",
    encodedGatewayArguments,
    CallGateway);
```

The helper uses `ChooseStep` for the external outcome:

```csharp
await context.ChooseStep(
    "gateway-outcome",
    FulfillmentProcessAction.CallGateway,
    CallGatewayStep.GatewayOutcomes,
    (state, outcome) =>
        state.RecordGatewayCall(worker, outcome, idempotent),
    subject: _ => order);
```

Selection, continuation advance, gateway mutation, typed semantic action, and
order subject are one edge. There is no intermediate state-neutral `Choose`
configuration.

This also makes the environmental assumption direct:

```csharp
Fairness.StrongAction<ProcessTransition>(
    ProcessFrontend.IsDefinitiveGatewayAnswer);
```

The definitive-answer family is intentionally collective, preserving the
original gateway assumption. Worker actions use per-role/per-order weak action
fairness, and controller actions use per-order weak action fairness. A duplicate
submit cannot discharge another order's controller obligation.

### Composition is sound

Controllers and workers are active in one `ProcessSystemModel`. Reads, guards,
and state-derived choices are evaluated against the shared state when the role
is scheduled, after any interleaving.

`FullyComposedProcessesMatchTheManualDomainModel` checks equal projected domain
states and changing transitions. The structured graph satisfies the same
safety and liveness properties. The fully composed process system safety-refines
the hand-written system, and the structured worker also refines its
hand-written sub-model on fair temporal behavior.

### Measurements

| Graph | Configurations | Edges | Domain states |
| --- | ---: | ---: | ---: |
| hand-written, 2 orders / 1 worker | 147 | 532 | 147 |
| `Operation` compositions | 147 | 532 | 147 |
| structured processes, same instance | 147 | 532 | 147 |
| hand-written worker, 1 order / 1 worker | 12 | 15 | 12 |
| structured worker, same sub-model | 12 | 15 | 12 |
| hand-written, 1 order / 2 workers | 21 | 52 | 21 |

For this design the durable row/worker phase determines the continuation, so
exact configuration and domain-state counts happen to coincide.
`ProcessGraphDiagnostics` still computes them independently and reports the
worker's root, `foreveriteration:attempt-loop`, and `call:call-gateway` frames.

## Operation frontend

`OperationsFrontend.cs` expresses the controller as response-dependent
expectations. An accepted response commits domain state and queues background
handlers atomically; rejection queues nothing. A co-active composition keeps
the hand-written workers active from the root.

The tests check both compositions for safety, liveness, identical projected
domain behavior, refinement, and duplicate step-function identity rejection.

## Deliberately broken variants

| Switch | Defect | Detected by |
| --- | --- | --- |
| `WithDualWriteSubmit()` | publishes payment work before committing the order | `OutboxMatchesOrder`, `NoChargeWithoutCommittedOrder` |
| `WithNonIdempotentCharge()` | drops the gateway idempotency key on retry | `ChargedAtMostOnce` |
| `WithoutOutboxLease()` | lets two workers deliver one row concurrently | `ResolutionIsFinal`, `OutboxMatchesOrder` |

Each defect test is paired with a correct-design check, so the properties are
not vacuous.

## Files

| File | Purpose |
| --- | --- |
| `StoreState.cs` | database, worker context, gateway ledger, mutations |
| `FulfillmentConfig.cs` | model size and defect switches |
| `FulfillmentSteps.cs` | hand-written frontend and shared action labels |
| `FulfillmentModel.cs` | graph builders and domain projections |
| `FulfillmentProperties.cs` | safety, liveness, and native-step fairness |
| `OperationsFrontend.cs` | response-dependent controller adapter |
| `ProcessFrontend.cs` | structured controllers/workers and action fairness |
| `ProcessFrontendTests.cs` | composition, frames, `ChooseStep`, fairness, refinement |

## See also

* [Model-checking frontends](../../docs/concepts/model-checking-frontends.md)
* [Structured process sample](../ProcessModelChecking/README.md)
* [Checking refinement](../../docs/how-to/checking-refinement.md)
