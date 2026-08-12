# Experimental coroutine model checking

`Accordant.ModelChecking.Experimental.Coroutines` is an **experimental**
front-end that compiles an `async ModelTask` workflow into ordinary `IState`,
`IStepFunction`, `StepResult`, and `StateGraph` objects.

> **Status: prototype, not being promoted.** This API stays in the
> `Experimental` namespace, is not packaged, and carries no compatibility
> promise. The reasoning, the evidence behind it, and the migration boundary
> are recorded in
> [Model-Checking Frontends](../../docs/concepts/model-checking-frontends.md).
> For concurrent or multi-process models, write hand-written step functions
> with an explicit program counter, as `Samples/Peterson`,
> `Samples/DiningPhilosophers`, and `Samples/Paxos` do.

Run the executable studies with:

```powershell
dotnet run --project Samples\CoroutineModelChecking
```

The sample prints three groups of measurements: the worker case study, the
loop/replay study, and the safety-boundary study in
`SafetyHardeningCaseStudy.cs`, which shows which unsound workflows are
rejected and which one the runtime cannot see.

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
references are rejected, both when a checkpoint produces a value and again
when the value is recorded on a tape, so no internal path can put a mutable
reference into replay history.

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
are expected for iterations. Composition with independently active
hand-written steps remains deferred, and `Samples/OrderFulfillment` measures
why: a compiled step's *pending* checkpoint — including a state-derived
`Choose` set — was computed by advancing the workflow from the state the
coroutine arrived at, so the step is not a function of the state it is applied
to and an interleaved write between arrival and selection is invisible to it
(`TheCompiledCoroutineStepIsNotAFunctionOfItsInputState`). That sample also
shows a second consequence of compiling a decision point into a visible but
state-neutral `Choose`: because Accordant fairness counts changing edges only,
a fairness assumption about the externally chosen outcome cannot be attached to
the compiled graph at all
(`TheGatewayFairnessAssumptionCannotBeStatedOnTheCompiledGraph`). Environment
nondeterminism a fairness assumption must constrain belongs in an ordinary
changing model action.

## What the runtime checks

`verifyDeterminism: true` enables an opt-in diagnostic audit. Every replay
segment is then executed twice from the same frozen state and replay prefix,
`Read` and `Choose` selectors are evaluated twice on the same state, and
inspectable captured external variables are compared before and after the
body runs. This is sampling, not proof: it can miss nondeterminism and can
reject graph-irrelevant side effects. It also doubles execution of ordinary
workflow code and roughly doubles exploration cost, so it is off by default.

| Rejected | How |
| --- | --- |
| Selector that is not a function of the frozen state | selector evaluated twice per new checkpoint |
| Nondeterministic checkpoint sequence, kind, name, or value | whole segment replayed twice and the traces compared |
| Body writes to a captured external scalar or `State` | scalar and `State` captures compared around the body |
| Body mutates the shared model state outside `Step` | state string representation compared around the body |
| Replay reaching a different checkpoint than the tape recorded | checkpoint identity compared position by position |
| A checkpoint's value type changing between runs | recorded value type checked before it is handed back |
| Replay finishing without consuming its whole tape | consumed checkpoint count compared with tape length |
| A non-scalar replay or `LoopState` value | value type check at production and at recording |
| An incomplete foreign await | rejected by `ModelTaskMethodBuilder` |
| A second direct `Loop` boundary, or `Loop` after another checkpoint | tape rebase rules |
| A loop that only takes `Read`/`Loop` checkpoints | `maxInternalCheckpoints`, default 10,000 |
| A missing `Loop` boundary | `maxDepth`, default 16, reported as `InconclusiveBound` |

Identities are built from length-prefixed components, so a workflow name,
checkpoint name, or loop site containing `:`, `@`, or `|` cannot be confused
with a different decomposition. A visible action's identity also includes the
kind, name, loop site, pending value (for `Choose`, the canonicalized
materialized choice set), `Step` delegate method, and the full replay prefix.
A loop site is derived from
compile-time caller information using source file name, member, and line.
Same-named files at the same member and line can collide.

`CoroutineModel.DescribeCapturedInputs` reports the external variables a
workflow delegate captured and how closely each can be monitored
(`ImmutableScalar`, `ModelState`, `ReferenceIdentityOnly`, `NotAnalyzable`).
The executable sample prints, for the loop study's capturing workflow:

```text
captured input: budget : System.Int32 (ImmutableScalar)
captured input: seen : System.Collections.Generic.List`1[...] (ReferenceIdentityOnly)
```

## Runtime limitations

These are **not** enforced. Treat them as review obligations.

* **Foreign awaits that complete synchronously cannot be rejected.** When an
  awaiter reports `IsCompleted`, the C# compiler calls `GetResult` inline and
  never calls `AwaitOnCompleted` on the custom builder, so
  `await Task.CompletedTask`, `await Task.FromResult(x)`, an already-finished
  `ValueTask`, or a custom awaiter reading a clock reaches no interception
  point. The sample prints
  `synchronously completed foreign await accepted (runtime limitation): True`.
  Such an await is caught only through its effects: if it changes which
  checkpoints are reached or what they produce, the determinism audit reports
  it (the sample's `varying foreign await` line); if it is a constant, it is
  harmless but invisible.
* **A synchronous loop that reaches no checkpoint cannot be interrupted.**
  `while (true) { }` never returns control to the runtime, so neither
  `maxDepth` nor `maxInternalCheckpoints` applies; only a loop that takes
  `Read` or `Loop` checkpoints hits the internal bound. A test asserting this
  with a literal empty loop would hang forever, so the test suite uses a loop
  that reaches no checkpoint but can be released by the test thread: it proves
  the runtime cannot interrupt it, then releases it instead of hanging.
* **Side effects that do not change checkpoints are invisible.** Writes to
  captured objects that are only compared by reference, writes performed
  inside a `Step` action, and I/O in ordinary code are not detected. Step
  identity distinguishes delegate methods, but cannot prove a delegate is pure
  or that captured values at one source location are stable. Captured
  inputs are only enumerated for compiler-generated closures: a workflow passed
  as a static or instance method group reports `NotAnalyzable` and is not
  monitored at all.
* **Omitted live locals in `LoopState` are not detected**, as described above.
* **Determinism verification is sampling, not proof.** Two identical runs of a
  segment do not prove determinism; a value that changes only every third
  evaluation can still slip through.
* **The determinism audit reports the C# compiler's lambda cache.** The
  compiler caches a workflow's non-escaping lambdas in `<>9__` fields on the
  closure it captured the workflow's parameters into, and writes them the first
  time each lambda is evaluated. `verifyDeterminism: true` compares captured
  variables around the body, so a freshly created workflow delegate fails its
  own audit with a diagnostic about compiler-generated state. Explore the same
  delegate instance once before the audited run; see
  `Samples/OrderFulfillment`'s `AuditingAColdDelegateReportsTheCompilersLambdaCache`
  and `PaymentWorkerCoroutine.BuildAuditedGraph`.

## Analyzer feasibility

Some of the limitations above are decidable in source, so a Roslyn analyzer is
the right place for them. Nothing below is implemented yet, and these rules are
now scoped as an experiment for a *generated* control-state frontend rather
than as hardening for this replay runtime — see
[Model-Checking Frontends](../../docs/concepts/model-checking-frontends.md).

* **Foreign awaits (decidable).** In an `async ModelTask` method, require every
  `await` operand to be an invocation of `ModelContext<TState>.Read`, `Choose`,
  `Step`, `Loop`, or `LoopState`. This closes the synchronously completed
  foreign await hole exactly, because the rule is syntactic and does not depend
  on whether the awaiter completes. Escapes remain for `dynamic` operands and
  awaits reached through non-`ModelTask` helper types.
* **Impure selectors (decidable, conservative).** Flag lambdas passed to `Read`
  and `Choose` that reference any symbol other than their parameter and
  compile-time constants, and flag well-known nondeterministic sources
  (`DateTime.Now`, `Random`, `Guid.NewGuid`, `Environment.TickCount`).
* **Mutable captured inputs (decidable, conservative).** Flag assignments to
  captured locals and fields inside the workflow body, which the runtime can
  only detect for scalar and `State` captures.
* **Non-scalar `LoopState` values (decidable).** The value's static type is
  known at the call site, so the runtime's dynamic check can be moved to build
  time.
* **Omitted live locals (feasible with caveats).** Roslyn's
  `SemanticModel.AnalyzeDataFlow` over the loop body yields the locals declared
  outside the loop that flow in and are written inside; comparing that set with
  the `LoopState` value's members would catch the common omission. It is
  conservative around aliasing, `ref` locals, and helper calls, so it can only
  warn.
* **Empty synchronous loops (partially decidable).** A literal
  `while (true) { }` with no checkpoint in the body is easy to flag; general
  termination is not decidable.
