# Model-checking frontends

A transition-authoring frontend ultimately produces ordinary `IState`,
`IStepFunction`, `StepResult`, and `StateGraphNode` objects. Exploration,
formulas, fairness, and refinement remain shared backend facilities.

## Current choices

1. Hand-written `IState` / `IStepFunction` models are the supported, fully
   general frontend.
2. `Accordant.ModelChecking.Operations` is a maintained repository frontend for
   finite response-dependent operations. It is currently unpackaged.
3. `ProcessSystemModel<TState>` in
   `Accordant.ModelChecking.Experimental.Coroutines` is the canonical
   experimental process frontend. It is also unpackaged and carries no
   compatibility promise.

## Structured process model

Processes are independently scheduled and share one frozen domain state.
Continuation state is scheduler configuration, not a field added to
`TState`.

```csharp
var model = new ProcessSystemModel<MyState>(initial);

model.Process(
    "worker",
    context => context.Forever("worker-loop", WorkerIteration));

model.RepeatedAction(
    "timer",
    state => state.TimerEnabled,
    MyAction.Tick,
    state => state.Tick(),
    subject: "timer");
```

Every graph node is identified by the complete configuration:

* domain-state identity;
* each live process role and its structured continuation frames;
* immutable checkpoint-history values local to each frame;
* failure-domain running/crashed state.

Configurations are never merged merely because their domain states are equal.
`ProcessGraphDiagnostics.Describe(root)` reports both counts, graph
completeness, edges, and the continuation forms observed per role.

### Structured iteration

`Forever(name, body)` creates an explicit iteration frame. When the body
completes, its frame and iteration-local checkpoint history are discarded and the
same semantic edge canonicalizes directly to the next iteration. Call/return
and iteration reset are administration, not graph edges.

An iteration must produce a visible `Choose`, `ChooseStep`, or `Step`; returning
without productive progress is rejected. Guards and reads are not speculatively
evaluated while the previous edge is being normalized.

### Structured calls

`Call(name, helper)` invokes a checkpoint-bearing helper through an explicit
frame. Scalar arguments and return values can be recorded:

```csharp
var result = await context.Call("attempt", attemptId, RunAttempt);
```

Completed call frames are discarded. Directly awaiting another `ModelTask` is
rejected because it would not establish an explicit process-frame boundary.

### Checkpoints

* `Read` records an immutable scalar observed from the state against which the
  process is currently scheduled.
* `When` / `WaitUntil` block internally and re-evaluate their guards against
  current shared state.
* `StepWhen` atomically tests a live guard and mutates a cloned state. Use it
  for shared-resource claims, not as a defensive guard between naturally
  sequential local steps.
* `Choose` creates one visible state-neutral control edge per finite scalar
  alternative.
* `ChooseStep` creates one edge per alternative and atomically records the
  selected value, advances control, and applies its state mutation. It avoids
  an intermediate neutral choice configuration.
* `Step` creates one semantic mutation edge.

Finite choice sets must be nonempty, distinct, and use the existing immutable
scalar whitelist.

### Recurring actions

`RepeatedAction` represents a role that repeatedly offers one guarded atomic
action and has no continuation. It carries stable role, typed semantic action,
optional subject, and failure-domain metadata. A semantic no-op is a genuine
model self-loop; it does not create an artificial continuation state.

Registering the action through a `ProcessFailureDomain<TState>` makes it
unavailable while that domain is crashed.

### Failure domains

Ownership is structural: processes, launches, and recurring actions registered
through a failure-domain object belong to it. A crash mutates durable/domain
state as declared and discards every owned continuation. Restart relaunches
persistent processes with fresh root frames. External processes registered on
the model survive.

The current runtime supports one failure domain.

## Semantic actions and fairness

`ProcessTransition` carries process role, domain, control kind, checkpoint,
typed semantic action, and optional subject. Prefer metadata-aware fairness:

```csharp
var workerRuns = Fairness.WeakAction<ProcessTransition>(
    transition => transition.SemanticAction is MyAction.Work);

var eachClientReports = Fairness.StrongEach<ProcessTransition, object>(
    transition => transition.SemanticAction is MyAction.Report,
    transition => transition.Subject);
```

`WeakAction` / `StrongAction` create one collective obligation.
`WeakEach` / `StrongEach` create one obligation per stable key. Selected
state-neutral semantic edges count; unselected administration and synthetic
terminal stutter do not. Enabledness is evaluated over exact configurations.

## Sound composition

The scheduler advances each process against the current shared state whenever
that role is considered. A `Read`, guard, or state-derived choice reached after
another process interleaves is therefore rebuilt from that current state. The
structured continuation records only completed frame-local checkpoints.

`Samples/OrderFulfillment` checks this by composing controller and worker
processes, matching the hand-written model's domain states and changing
transitions, preserving safety and liveness, and refining the hand-written
worker. Its gateway uses `ChooseStep`, so definitive outcomes are directly
selectable by action-aware fairness.

## Current restrictions

Projects that reference `Accordant.SourceGenerator` as an analyzer receive
these compile-time errors for the experimental process API:

| Diagnostic | Enforced subset |
|---|---|
| `ACC1001` | Directly awaiting `ModelTask` or `ModelTask<T>` in a `ModelTask` workflow. Invoke the helper through `ModelContext<TState>.Call`. |
| `ACC1002` | Awaiting any type other than the Accordant `ModelAwaitable<T>` returned by a model checkpoint. This includes `Task`, `ValueTask`, custom awaiters, and already-completed foreign awaits. |
| `ACC1003` | A raw `while`, `for`, `foreach`, or `do` loop that contains an Accordant model checkpoint. A finite synchronous loop with no checkpoint remains valid. |
| `ACC1004` | A clearly unsupported compile-time value type for `Read`, `Choose`, `ChooseStep`, `WaitUntil`, or `Call` results; explicit `Call` / `Forever` arguments; and `Step`, `StepWhen`, `ChooseStep`, or `RepeatedAction` subjects. |

`ACC1004` accepts null, strings, primitive scalars, enums, `DateTime`,
`DateTimeOffset`, `TimeSpan`, `Guid`, `ModelUnit`, and nullable forms. It is
deliberately conservative: type parameters, `dynamic`, `object`, interfaces,
and other statically unknown value flows remain runtime-validated.

The process runtime remains experimental, and these checks remain runtime-only:

* frame-history, argument, result, semantic-subject, and choice values use the
  immutable scalar whitelist when their compile-time type is unknown;
* `Call` and `Forever` frame delegates must be static or capture only supported
  immutable scalar values. The runtime rejects non-analyzable instance
  delegates and unsupported compiler-generated captures, and detects observed
  writes to captured scalar fields. There is no interprocedural captured-local
  mutation diagnostic; root process/action delegates that retain configuration
  objects, and mutation hidden behind reference identity, remain review
  obligations;
* the runtime rejects incomplete foreign awaits and direct nested `ModelTask`
  awaits as a backstop. A synchronously completed foreign await can only be
  rejected by `ACC1002`, so do not suppress or omit the analyzer for model
  workflow projects;
* finite choices are checked at runtime for null sets, emptiness, duplicates,
  and runtime-only value types;
* structured frame capture validity, deterministic frame re-execution, stable checkpoint
  identity/order, and productive `Forever` iterations are runtime checks;
* synchronous code that never reaches a checkpoint cannot be interrupted;
* only one failure domain is supported;
* no partial-order reduction is implemented.

These restrictions are authoring obligations, not backend limitations. The
produced graph still uses the ordinary exact state-graph, formula, fairness,
and refinement semantics.

## See also

* [Step Functions & Async](step-functions-and-async.md)
* [Model-Checking Formulas](../how-to/model-checking-formulas.md)
* [Checking Safety Refinement](../how-to/checking-refinement.md)
* [Samples](../samples.md)
