# Model-Checking Frontends: Stable vs Experimental

**Status:** Accepted. Applies to the coroutine replay prototype as of
`4880a0e` (loop replay study) and `b0594c2` (replay hardening).

A *transition-authoring frontend* is any way of describing model behavior that
ends up as ordinary `IState`, `IStepFunction`, `StepResult`, and `StateGraph`
objects. A *backend* is the machinery that consumes them: exploration,
`ENABLED`, LTL/RLTL checking, weak and strong fairness, and safety and temporal
refinement. The packaged `[State]` source generator remains the supported way
to generate state clone/freeze/identity plumbing; it does not author
transitions and is outside this decision.

Accordant now has three frontends at different maturity levels, and this page
records which ones are supported, which one is not being promoted, and what a
future concurrency frontend should look like.

## Decision

1. **Hand-written `IState` / `IStepFunction` models remain the supported,
   fully general frontend.** Everything else compiles down to them.
2. **The `Operation` adapter (`Accordant.ModelChecking.Operations`) remains a
   maintained repository frontend** for finite, response-dependent operation
   inputs. It is not currently shipped as a package.
3. **The coroutine replay API stays in
   `Microsoft.Accordant.ModelChecking.Experimental.Coroutines` and is not
   promoted.** It is kept as an executable prototype and as a soundness
   study, not as an API with a compatibility promise.
4. **A future stable coroutine/process/actor frontend must compile explicit or
   generated control state** — a Roslyn source generator, or a structured
   process DSL — **rather than replay arbitrary C# method bodies.**
5. **The backend does not change.** No frontend gets a private path into
   exploration, fairness, or refinement.

## Context

The coroutine prototype compiles an `async ModelTask` workflow into graph
steps by re-executing the workflow body from its start on every step and
replaying recorded checkpoint values (`Read`, `Choose`, `Step`, `Loop`,
`LoopState`) from a tape. Continuation identity is the replay tape, so
"where the program is" is encoded as "what has been replayed so far".

That works, and it produces genuinely ordinary graphs. The question this
decision answers is whether *replaying arbitrary C#* is a sound enough basis
for a supported API.

## Evidence

**The backend integration is real.** `CoroutineModel.Explore` returns a plain
`StateGraphNode`, and `CoroutineStep<TState>` derives from `BaseStepFunction`.
Existing checks work unmodified: `EnabledUsesTheFullCompiledGraphAndIgnores
StateNeutralChoose`, `LoopRebasesTheTapeAndCreatesAnOrdinaryGraphCycle` (weak
and strong fairness on the compiled cycle), and both
`TypedReplayMetadataLetsTheCoroutineRefineTheManualGraph` and
`ExistingTemporalRefinementAcceptsCoroutineGraphs` report `Refines`. Nothing in
this decision is caused by a backend defect.

**Equivalence holds only up to projection.** In
`Samples/CoroutineModelChecking`, the hand-written two-worker graph is 5 nodes
/ 4 edges; the coroutine graph is 7 nodes / 6 edges. They agree only after
hiding the state-neutral `Choose` configurations
(`HidingChooseMakesTheProjectedDomainStateAndTransitionSetsMatch`: 5 projected
states, 4 changing transitions). Control state is not free — it is already
visible in the graph, just implicitly.

**Loops need a trusted marker.** Without `Loop`, repeated checkpoint names grow
the tape forever: the sample reports `4 nodes, 3 edges, 4 continuation
identities, tape length 3, frontier=True, cycle=False` at depth bound 4, and
model checks over the omitted continuation return `InconclusiveBound`. With
`Loop`, the same workflow is `2 nodes, 2 edges, 1 continuation identities,
tape length 1, frontier=False, cycle=True`. `Loop` is a *declaration by the
author*, not an analysis result: it must be the first checkpoint, only one
direct boundary is supported, and every live local must be threaded through
`LoopState` by hand.

**Five holes cannot be closed at runtime.** Tests pin the enforceable boundary;
the omitted-local obligation is documented because the runtime cannot observe it:

| Hole | Pinned by |
| --- | --- |
| Synchronously completed foreign awaits are accepted | `ForeignAwaitsThatCompleteSynchronouslyAreNotRejectedByTheRuntime` |
| A synchronous loop reaching no checkpoint cannot be interrupted | `ASynchronousLoopThatTakesNoCheckpointCannotBeInterruptedByTheRuntime` |
| Omitted live locals in `LoopState` are undetectable | Coroutine sample README, "Runtime limitations" |
| Captured state is monitored by strength, not semantics | `CapturedInputsAreReportedWithTheirMonitoringStrength`, `StaticWorkflowCapturedInputsAreReportedAsNotAnalyzable` |
| Determinism verification is sampling, not proof | `TheDeterminismAuditRunsTheBodyTwiceAndCanBeDisabled` (2x cost, off by default) |

The first three can make a *definitive* verdict unsound — a false `Holds` or a
fabricated counterexample — which is worse than an inconclusive one.

**The cost ratio is unfavourable.** The replay runtime and its safety tests are
substantially larger than the `Operation` adapter while compiling only one
workflow with finite checkpoint branching and one trusted direct loop
boundary.

**Concurrency, the actual motivation, is not covered.** Composition with
independently active hand-written steps is deferred, and there is no
interleaving of two workflows. Meanwhile the repository already model-checks
Peterson, Dining Philosophers, Alternating Bit, EWD998 termination detection,
and Paxos — as hand-written per-process step functions guarded by an explicit
program counter (`GetPC(s, I) == PhilPC.Thinking`). Those models are the
workload a coroutine frontend would have to beat, and the prototype cannot
express them at all.

**Two further limits are now measured rather than assumed.**
`Samples/OrderFulfillment` compiles the same payment worker through the
hand-written and the coroutine frontends and pins both:

| Limit | Pinned by |
| --- | --- |
| A compiled coroutine step is not a function of the state it is applied to: its pending checkpoint — including a state-derived `Choose` set — was computed from the state the coroutine *arrived at*, so an interleaved write is invisible to it | `TheCompiledCoroutineStepIsNotAFunctionOfItsInputState` |
| Fairness over external nondeterminism is not statable on a compiled graph, because `Choose` is state-neutral and fairness counts changing edges only | `TheGatewayFairnessAssumptionCannotBeStatedOnTheCompiledGraph`, `AddingTheGatewayAssumptionToBothSidesMakesRefinementFail` |
| `verifyDeterminism` reports the C# compiler's `<>9__` lambda cache as a captured-variable write on a cold delegate | `AuditingAColdDelegateReportsTheCompilersLambdaCache` |

The second is the sharper one for the future direction: it is not a runtime
defect but a consequence of compiling a decision point into a visible
state-neutral edge. A generated control-state frontend has the same choice to
make, and the study's answer is that environment nondeterminism a fairness
assumption must constrain has to be an ordinary changing model action.

**The `Operation` adapter does compose, and that is now checked.** An
`OperationModelStep` is an ordinary step function whose outcome is a function of
the state it is applied to, so the ordinary exploration rules interleave it with
independently active steps with no special support.
`OperationModel.Explore` gained an `additionalSteps` overload that adds nothing
to the semantics and only extends the existing duplicate-identity validation to
the whole active set — two active steps sharing an id would silently change
graph node identity. `Samples/OperationsModelChecking/OperationCompositionTests.cs`
covers the overload, and `Samples/OrderFulfillment` uses it for a composed
controller-plus-workers model that satisfies the same properties as, and
refines, the hand-written model.

**Neither adapter ships today.**
`Accordant.ModelChecking.Experimental.Coroutines` and
`Accordant.ModelChecking.Operations` set `IsPackable=false` and are absent from
`nuget/Microsoft.Accordant.nuspec`. Keeping the prototype experimental costs
package consumers nothing, because they have never been able to take a
dependency on it.

## Alternatives considered

**Promote the replay API as-is.** Rejected. It would freeze 16 public types
and a `Loop`/`LoopState` contract whose correctness depends on review
obligations the runtime cannot enforce.

**Promote it behind a Roslyn analyzer.** Deferred, not rejected. An analyzer
closes the decidable holes (foreign awaits, impure selectors, mutated
captures, non-scalar `LoopState`), but omitted live locals and invisible side
effects can only be warnings, and an analyzer adds nothing to the missing
concurrency story. If the analyzer is worth building, its data-flow analysis is
better spent *generating* control state than *policing* replay.

**Delete the prototype.** Rejected. It is the cheapest executable evidence the
project has about where the replay boundary actually lies, and the sample's
measurements are reproducible with `dotnet run`.

**Compile explicit control state (chosen future direction).** Turn the loop
and await structure into a program counter and generated transitions at build
time. This is what the existing concurrency samples already do by hand, so the
target shape is known and already checkable.

**Structured process DSL (chosen as the cheaper first experiment).** Let
authors build labelled control states and transitions through an API, with no
compiler analysis at all. Less pleasant to write than `async`/`await`, but the
control graph is data rather than a replayed method body.

## Consequences

* The experimental namespace is a warning label, not a staging area. Code there
  may change or be removed in any release, and it will not appear in the NuGet
  package.
* `Samples/CoroutineModelChecking` stays as a study of the soundness boundary.
  Its README's "Runtime limitations" section is normative for anyone using the
  prototype.
* `Samples/OrderFulfillment` is the applied companion: one implementation-shaped
  system authored through all three frontends, with the coroutine used only in a
  closed sub-model. After hiding `Choose`, its projected domain states and
  changing transitions equal the hand-written worker's, and it refines that
  worker rather than mixing with independently active steps.
* Users needing concurrency today write hand-written step functions with an
  explicit program counter, exactly as Peterson, Dining, EWD998, and Paxos do.
* Work on the replay runtime should stop except for what a study needs. New
  effort belongs in the generated-control-state experiments below.
* The prototype's non-obvious findings — visible-but-state-neutral `Choose`,
  length-prefixed identity encoding, typed transition metadata for refinement —
  are reusable by any future frontend and should be carried forward.

## Migration boundary

**Stable and safe to depend on:** `IState`, `IStepFunction`, `StepResult`,
`StateGraph`/`StateGraphNode`, `ENABLED`, the fairness predicates, and the
safety and temporal refinement APIs. A future frontend is required to emit
these and nothing more, so models written against them survive any frontend
decision.

**Maintained but currently unpackaged:** `Accordant.ModelChecking.Operations`.
It compiles to the stable surface and can be replaced by a hand-written model
mechanically.

**No compatibility promise:** every type under
`Microsoft.Accordant.ModelChecking.Experimental.Coroutines`, including
`ModelTask`, `ModelContext<TState>`, `ReplayTape`, `CoroutineTransition`, and
`CoroutineModel.Explore`.

**Migrating off the prototype** is mechanical because it is a frontend, not a
semantics: each `Step("name", ...)` becomes a step function whose
`IsEnabled` tests an explicit control field, each `Choose` becomes a step per
alternative (or an input parameter), and each `Loop` boundary becomes a
control field reset. The resulting model is what the prototype was compiling to
anyway, and it composes with other steps, which the prototype does not.

## Next implementation experiments

None of these are implemented, and none require the replay runtime.

1. **Two independently active processes, by hand.** Write the smallest model
   with two concurrent multi-step participants using explicit program counters,
   and record the ceremony. Success: a concrete, measured statement of what a
   generated frontend must remove.
2. **Structured process DSL spike.** An API for declaring labelled control
   states and transitions per process, compiling to per-process step functions.
   Success: it reproduces the Peterson graph node-for-node, with fairness and
   refinement unchanged, in materially less code than experiment 1.
3. **Source-generator spike on the same DSL shape.** Define a restricted
   `async`-looking input language and lower it to a generated program-counter
   state machine at build time — no replay, tape, or runtime determinism audit.
   Success: valid inputs produce graphs identical to experiment 2.
4. **Generator diagnostics.** Reject constructs outside that restricted input
   language, including foreign awaits and unsupported captures. Define
   clone/freeze/identity generation for hoisted locals; replay-only
   `LoopState` rules do not carry forward. Success: every restricted-language
   rule has a compile-time diagnostic with a failing and a passing test.
5. **Composition test.** Whatever frontend emerges must interleave with
   independently active hand-written steps in one graph, under weak and strong
   fairness. Success: a case study that mixes both and checks a liveness
   property that fails without fairness. *The `Operation` half of this is now
   done in `Samples/OrderFulfillment`; the coroutine half remains open, and
   `TheCompiledCoroutineStepIsNotAFunctionOfItsInputState` records why the
   replay prototype cannot supply it.*
6. **Fairness over a generated branch point.** A generated frontend must be able
   to state a fairness assumption about environment nondeterminism. If the
   branch point compiles to a state-neutral edge, it cannot, because fairness
   counts changing edges only. Success: the generated model reproduces
   `Samples/OrderFulfillment`'s hand-written result — the progress property is
   violated under weak fairness and holds under the strong gateway assumption.

## See also

* [Step Functions & Async](step-functions-and-async.md) — the stable backend surface
* [Model-Checking Formulas](../how-to/model-checking-formulas.md)
* [Checking Safety Refinement](../how-to/checking-refinement.md)
* [Samples](../samples.md) — `OperationsModelChecking`, `CoroutineModelChecking`,
  and `OrderFulfillment`
