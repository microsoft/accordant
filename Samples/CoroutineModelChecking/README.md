# Experimental coroutine model checking

`Accordant.ModelChecking.Experimental.Coroutines` is an **experimental,
finite-workflow** front-end. It compiles an `async ModelTask` workflow into
ordinary `IState`, `IStepFunction`, `StepResult`, and `StateGraph` objects.

## Executable two-worker case study

`WorkerCompetitionCaseStudy.cs` compares two workers (`ada`, `grace`) racing
to claim one task and then finish it:

* The hand-written graph exposes `claim(worker)` and `finish(worker)`.
* The coroutine is `Choose(worker)`, then `Step("claim")`, then
  `Step("finish")`.

Run it with:

```powershell
dotnet run --project Samples\CoroutineModelChecking
```

Measured eager graph sizes are:

| graph | nodes | edges |
| --- | ---: | ---: |
| hand-written | 5 | 4 |
| coroutine | 7 | 6 |

The two extra coroutine nodes and two extra edges are the visible,
state-neutral `Choose(worker)` control configurations: one branch for each
selected worker. The projected domain-state set has 5 states and, after hiding
`Choose`, the projected changing transition relation has the same 4
`claim(worker)`/`finish(worker)` transitions as the hand-written model.
This is equality of the deduplicated domain-state and changing-transition
sets after a silent control step, not raw graph isomorphism, strong
bisimulation, or equality of enabled-action sets.

Both models are checked by the same ordinary `StateGraph` model-checking
backend. They satisfy the same safety property (only the claiming worker can
finish) and liveness property (the task eventually finishes), with both
`Fairness.None` and `Fairness.WeakAll`. This finite model has no nonterminal
cycle for fairness to exclude, so this result is deliberately not evidence of
fairness preservation; the loop study supplies the cyclic evidence later.
Fairness still operates on the complete
compiled graph: state-neutral `Choose` edges neither enable nor take a
fairness action under Accordant's changing-edge definition.

`ENABLED` deliberately exposes that full-graph distinction. Manual
`claim(worker)` is enabled at the root. Coroutine `Choose(worker)` is visible
but state-neutral, so it is **not** `ENABLED`; after a worker is selected,
coroutine `claim` is enabled. This is a semantic difference in control
configuration, not false parity to normalize away.

The case study proves functional safety and temporal refinement from the
coroutine graph to the manual graph. Its state map is the domain projection;
`Choose` maps to `AbstractResponse.Hidden`; and `claim`/`finish` map to the
matching manual worker action. `CoroutineTransition.ReplayPrefix` and
`ICoroutineCheckpointStep` expose typed checkpoint and replay information for
this purpose, so the selected worker is not inferred by parsing an opaque
step ID.

## Front-end constraints

`Read` is a deterministic, internal replay checkpoint and therefore does not
make an edge. `Choose` makes one visible edge for each finite scalar choice,
and `Step` makes one visible edge that clones and mutates state only when that
edge is selected. Each continuation includes its replay tape in its internal
step identity, preventing different remaining workflows from being merged.

Replay values are limited to null, strings, primitives, enums, and selected
immutable scalar value types. Mutable references are rejected. The context has
no `State` property; selectors receive frozen state and actions receive only a
cloned state when their `Step` runs.

Only finite workflows with unique checkpoint names are supported. There is no
loop marker, loop compaction, or cycle claim in this prototype. Incomplete
external awaits are rejected by the custom builder; do not treat this as a
general async runtime or rely on it to police synchronously completed external
awaits. Multiple-process orchestration and composition with independently
active hand-written steps are intentionally deferred. This case study does not
change loop behavior.
