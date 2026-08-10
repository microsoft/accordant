# Experimental coroutine model checking

`Accordant.ModelChecking.Experimental.Coroutines` is an **experimental**
front-end that compiles an `async ModelTask` workflow into ordinary `IState`,
`IStepFunction`, `StepResult`, and `StateGraph` objects.

Run the executable studies with:

```powershell
dotnet run --project Samples\CoroutineModelChecking
```

## Two-worker case study

`WorkerCompetitionCaseStudy.cs` compares two workers (`ada`, `grace`) racing
to claim one task and then finish it. The hand-written graph exposes
`claim(worker)` and `finish(worker)`; the coroutine is `Choose(worker)`, then
`Step("claim")`, then `Step("finish")`.

| graph | nodes | edges |
| --- | ---: | ---: |
| hand-written | 5 | 4 |
| coroutine | 7 | 6 |

The extra coroutine nodes and edges are visible, state-neutral
`Choose(worker)` configurations. After hiding `Choose`, the projected domain
states and changing `claim`/`finish` relation match the manual model. This is
not raw graph isomorphism or equality of enabled-action sets.

Both models satisfy the same safety and liveness checks. `Choose` is visible
but state-neutral, so it is not `ENABLED`; after selection, `claim` is
enabled. The case study also proves temporal refinement: `Choose` maps to
`AbstractResponse.Hidden`, while `claim` and `finish` map to their manual
actions using typed `CoroutineTransition` metadata.

## Executable loop/replay study

A natural loop may repeat checkpoint names:

```csharp
while (true)
{
    await context.Step("toggle", state => state.On = !state.On);
}
```

Without an explicit loop rebase, every iteration appends another
`step:toggle` entry. The domain state and source location repeat after two
iterations, but the complete collision-safe, length-prefixed replay tape
changes, so continuation identities cannot merge. With graph depth bounded at
four, the executable sample reports:

```text
naive loop (bound 4): 4 nodes, 3 edges, 4 continuation identities,
tape length 3, frontier=True, cycle=False
```

The ordinary `StateGraph` depth frontier is not a terminal state. Model checks
needing its omitted continuation therefore return `InconclusiveBound`.
`CoroutineModel.Explore` defaults to depth 16 so a missing loop boundary fails
conservatively before replay growth becomes extreme. Set `maxDepth` explicitly;
use `-1` only when intentionally accepting unbounded exploration.

Use trusted `Loop` at the repeated iteration boundary:

```csharp
while (true)
{
    await context.Loop("iteration");
    await context.Step("toggle", state => state.On = !state.On);
}
```

`Loop` is internal and creates no domain edge. At a later encounter, it
discards entries completed since that marker, retains its canonical entry, and
restarts replay. The compiled graph is finite and has a real cycle:

```text
Loop-rebased loop: 2 nodes, 2 edges, 1 continuation identities,
tape length 1, frontier=False, cycle=True
```

The ordinary compiled graph semantics apply to that cyclic SCC: `ENABLED`
sees `Step("toggle")`, and the tests check both weak and strong fairness.
Because Loop makes no edge, temporal refinement maps the `Step` action
normally; there is no loop control action to hide.

`LoopState` models iteration-local state:

```csharp
var phase = 0;
while (true)
{
    phase = await context.LoopState("iteration", phase);
    await context.Step("tick", state => state.Phase = phase);
    phase = (phase + 1) % 3;
}
```

Replaying from the method start returns the canonical recorded value. It is
part of the continuation identity, so equal domain states do not merge until
that persistent value repeats.

## Constraints and soundness boundary

`Read` and `Loop` are internal replay checkpoints. `Choose` makes one visible
edge per finite scalar choice; `Step` makes one visible edge and mutates only
a cloned state when selected. Replay values are limited to null, strings,
primitives, enums, and selected immutable scalar value types. Mutable
references are rejected.

`Loop` is a trusted modeling boundary, not source analysis. It must be the
first Accordant checkpoint in the workflow; ordinary setup code may precede
it, but `Read`, `Choose`, and `Step` belong after it. This prototype supports
one direct syntactic loop boundary per workflow and rejects a second direct
boundary rather than attempting unsound nested-loop rebasing. Do not hide
multiple dynamic boundaries behind one helper call site: caller metadata
cannot distinguish them. **Every local that
can influence a future iteration must be included in its immutable persistent
value (or represented in `TState`).** The runtime cannot detect omitted live
locals; omission can make any definitive verdict unsound, including a false
`Holds` or a fabricated counterexample. It does not snapshot mutable persistent
objects.

Checkpoint names must be stable along a replayed control path; repeated names
are expected for iterations. Incomplete external awaits are rejected, but this
is not a general async runtime. In particular, `while (true) { }` never yields
to the runtime and cannot be interrupted by a graph depth bound. Composition
with independently active hand-written steps remains deferred.
