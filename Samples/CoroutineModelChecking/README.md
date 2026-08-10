# Experimental coroutine model checking

`Accordant.ModelChecking.Experimental.Coroutines` is an **experimental,
finite-workflow** front-end. It compiles an `async ModelTask` workflow into
ordinary `IState`, `IStepFunction`, `StepResult`, and `StateGraph` objects:

```csharp
var root = CoroutineModel.Explore("counter-workflow", new CounterState(), Workflow);
```

`Read` is a deterministic, internal replay checkpoint and therefore does not
make an edge. `Choose` makes one visible edge for each finite scalar choice,
and `Step` makes one visible edge that clones and mutates state only when that
edge is selected. `CoroutineTransition` supplies readable edge metadata.

Each continuation includes its replay tape in its internal step identity.
This hidden process control is model configuration, not part of `TState`, and
prevents states with different remaining workflows from being merged. Action
IDs begin with the readable workflow, checkpoint kind, and checkpoint name;
the replay identity follows after `@`. `CoroutineTransition` metadata formats
the shorter `choose:name=value` and `step:name` descriptions.

Replay values are limited to null, strings, primitives, enums, and selected
immutable scalar value types. Mutable references are rejected. The context has
no `State` property; selectors receive frozen state and actions receive only a
cloned state when their `Step` runs.

Only finite workflows with unique checkpoint names are supported. There is no
loop marker, loop compaction, or cycle claim in this prototype. Incomplete
external awaits are rejected by the custom builder; do not treat this as a
general async runtime or rely on it to police synchronously completed external
awaits. Multiple-process orchestration and composition with independently
active hand-written steps are intentionally deferred: simply putting consumed
step functions beside one another would not preserve them across transitions.
