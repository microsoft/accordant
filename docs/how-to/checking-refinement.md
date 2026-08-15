# Checking Safety Refinement

Safety refinement checks whether every concrete execution can be represented by
an abstract execution without introducing behavior that the abstract model
forbids.

Explore both models, then provide a functional mapping from concrete states to
abstract states:

```csharp
var result = Refinement
    .Between<ConcreteState, AbstractState>(concreteRoot, abstractRoot)
    .Map(concrete => new AbstractState
    {
        Value = concrete.VisibleValue
    })
    .Check();
```

`.Map(...)` **is** the state hiding: it is a projection from the concrete state
space onto the abstract one, and everything it does not carry over is hidden by
construction. There is no separate hiding operator, no quotient of the state
graph, and no second way to say "ignore this field". Whatever the mapping
drops, the abstract model never sees.

The mapped concrete initial state must equal the abstract initial state. Each
concrete transition must then correspond to either:

- one abstract transition, or
- abstract stutter, where the mapped abstract state does not change.

Consequently, several concrete implementation steps may realize one abstract
step:

```text
Concrete: c0 -> c1 -> c2 -> c3
Mapped:   a0 -> a0 -> a0 -> a1
```

The checker does not yet match one concrete edge against several abstract
edges.

## Inspect the result

```csharp
switch (result.Status)
{
    case RefinementCheckingStatus.Refines:
        break;

    case RefinementCheckingStatus.DoesNotRefine:
        Console.WriteLine(result.GetTraceString());
        break;

    case RefinementCheckingStatus.InconclusiveBound:
        Console.WriteLine("Explore both models more deeply.");
        break;
}
```

`result.Valid` is `true` or `false` for conclusive checks and `null` for an
inconclusive bounded check. A failure has `InitialStateMismatch`,
`TransitionMismatch`, or `TemporalFairnessMismatch` as its `FailureKind`.

The diagnostic trace contains:

- each concrete graph node;
- the concrete step and edge metadata that entered it;
- the abstract state produced by the mapping;
- the coherent abstract graph configurations still able to explain the
  concrete prefix;
- the deterministic augmentation state (`AuxiliaryState`) and the future
  witness values (`Witnesses`), when those mechanisms are used.

## Equal abstract states remain path coherent

An Accordant graph node contains both a state and its active step functions.
Two abstract graph nodes can therefore contain semantically equal states while
having different possible futures.

The public mapping remains state-valued, but the checker retains exact abstract
graph nodes internally. It never jumps between unrelated equal-state
configurations. An equal-state abstract edge may advance from one
configuration to another; abstract stutter retains the same configuration.

This is why a mapping can return a newly allocated abstract state:

```csharp
.Map(concrete => new AbstractState(concrete.VisibleValue))
```

State comparison uses the same semantic state hashes as graph identity.

## Check temporal refinement

Functional temporal refinement additionally checks infinite behaviors under
concrete and abstract fairness:

```csharp
var result = Refinement
    .Between<WorkerState, JobState>(concreteRoot, abstractRoot)
    .Map(MapWorkerToJob)
    .CheckTemporal(
        concreteFairness: Fairness.Weak<CompleteWork>(),
        abstractFairness: Fairness.Weak<CompleteJob>());
```

The condition is:

```text
every fair concrete behavior has a fair aligned abstract behavior
```

The
[`Samples/TemporalRefinement`](../../Samples/TemporalRefinement/)
sample makes the distinction visible. A worker may alternate forever between
two internal polling states, both mapped to one abstract `Pending` state:

```text
concrete: Idle -> Queued -> ProcessingA -> ProcessingB -> ProcessingA -> ...
abstract: Idle -> Pending -> Pending     -> Pending     -> Pending     -> ...
```

Without concrete fairness, polling forever is a valid concrete behavior but
violates the abstract model's weak fairness for `CompleteJob`. With weak
fairness for `CompleteWork`, that concrete behavior is excluded: completion is
continuously enabled while processing, so it must eventually occur, and the
mapped behavior takes the abstract completion step.

Temporal counterexamples are lassos. `RefinementTraceItem.IsInCycle` marks the
repeating part, and `TemporalFairnessMismatch` distinguishes a fairness
failure from a finite transition mismatch.

The first temporal mode is exact for **deterministic alignment**: every
concrete transition must have at most one known abstract response (one
abstract edge or abstract stutter). If stutter and a state-neutral abstract
edge, parallel abstract edges, or several equal-state configurations provide
multiple responses, `AmbiguousTemporalRefinementException` reports the
ambiguity and lists the responses. The checker never silently chooses one
response and risks a wrong verdict. Declare the intended response with
[`.MapTransition(...)`](#declare-the-abstract-action).

## Declare the abstract action

A state-valued mapping decides *which abstract state* a concrete transition
arrives at. It cannot decide *which abstract action* the transition
represents, and two abstract actions can perform the same state change:

```csharp
var result = Refinement
    .Between<QueueState, LedgerState>(concreteRoot, abstractRoot)
    .Map(MapToLedger)
    .MapTransition(transition => transition.StepFunction switch
    {
        ObserveCancelStep => AbstractResponse.Step<LedgerSettleStep>(),
        ExpireLeaseStep => AbstractResponse.Stutter,
        _ => AbstractResponse.Unconstrained
    })
    .CheckTemporal(concreteFairness, abstractFairness);
```

### Exact semantics

For one concrete edge leaving one abstract configuration:

```text
responses      = { abstract stutter } + { the abstract configuration's edges }
stateConsistent = responses whose target state equals the mapped abstract state
admitted        = stateConsistent responses the declared response admits
```

- safety refinement keeps every admitted response as a coherent abstract
  candidate;
- temporal refinement requires exactly one admitted response, exactly as it
  requires exactly one state-consistent response today.

The declaration only ever **narrows**. It is applied after state matching, so
it can never admit a response the mapping already excluded. Two consequences
are worth stating outright:

- Refinement proved with a declaration implies refinement without it.
  Declaring actions strengthens the property, it never weakens it.
- For temporal refinement, the aligned response changes to a *different*
  response only where the check previously reported
  `AmbiguousTemporalRefinementException` and produced no verdict at all.
  Everywhere else a declaration can only turn `Refines` into a mismatch, never
  a mismatch into `Refines`.

Declaring a response that is not state-consistent is therefore a
`TransitionMismatch`, not a pass. Declaring `AbstractResponse.Stutter` for a
transition that moves the abstract state is the common first mistake — see
[hide an internal action](#hide-an-internal-action);
`AbstractResponse.Unconstrained` is the right default for every transition the
model does not need to name.

### Responses

| Response | Admits |
|---|---|
| `AbstractResponse.Unconstrained` | every response — reproduces state-only matching exactly |
| `AbstractResponse.Stutter` | abstract stutter only, never a state-neutral edge |
| `AbstractResponse.Hidden` | abstract stutter only — `Stutter` under the name that says the concrete action is internal |
| `AbstractResponse.Step<TStep>()` | an abstract edge with that step function, never stutter |
| `AbstractResponse.Step(step => ...)` | an abstract edge whose step matches, never stutter |
| `AbstractResponse.Matching<TAbstract>((source, step, target) => ...)` | a typed source, step and target predicate, never stutter |
| `AbstractResponse.Matching(response => ...)` | the full `AbstractTransition` view, including stutter |

`Stutter` and `Hidden` are the same check. `AbstractResponse.HidesConcreteAction`
is true for both, and diagnostics report which name the model used.

`AbstractTransition` is the typed view of one response:

```csharp
response.Source          // abstract state departed from
response.StepFunction    // null for abstract stutter
response.Metadata        // null for abstract stutter
response.Target          // abstract state arrived at
response.IsStutter       // the abstract model did not move
response.ChangesState    // the abstract state is different
```

The view carries states, a step function and edge metadata — never graph
nodes. A declaration therefore cannot inspect or expand either graph, and it
triggers no additional lazy exploration: it filters responses that state
matching has already enumerated.

If two responses have the same step function, the same metadata and equal
source and target states, no declaration can separate them. They differ only
in abstract configuration, and `AmbiguousTemporalRefinementException` is still
the answer.

### Hide an internal action

A concrete action the abstraction hides is already representable: it is a
concrete transition that maps to *abstract stutter*. `AbstractResponse.Hidden`
is that declaration under the name that says why:

```csharp
.Map(concrete => new AbstractState(concrete.VisibleValue))
.MapTransition(transition => transition.StepFunction switch
{
    ExpireLeaseStep => AbstractResponse.Hidden,
    RetryStep => AbstractResponse.Hidden,
    _ => AbstractResponse.Unconstrained
})
```

Four things follow, and they are the whole of Accordant's hiding story.

**Hiding is checked, never assumed.** The declaration is applied after state
matching, so `Hidden` only succeeds where abstract stutter was already a
state-consistent response — that is, where the mapped abstract state genuinely
does not change across the transition. Hiding an action the abstraction records
is a `TransitionMismatch`:

```text
Refinement failed: a concrete transition has no coherent abstract match.
  ...
  The declaration hides this concrete transition, but the mapping does not:
  the mapped abstract state is not the one the abstract model stays at.
```

There is deliberately **no unchecked way to suppress a transition**. If a
concrete action should be invisible, make it invisible in `.Map(...)` — that is
what the projection is for — and then, if you want the fact recorded and
enforced, declare it `Hidden`.

**Hiding is not stutter-equivalence to a state-neutral abstract action.**
`Hidden` admits abstract stutter only. An abstract edge that happens to leave
the abstract state unchanged is a real abstract action, not hiding; see
[state-neutral abstract edges](#state-neutral-abstract-edges).

**Hiding changes nothing about the concrete model.** A declaration reads a
transition and filters abstract responses. It adds no concrete state, removes
no concrete transition, and changes no concrete enabledness, so concrete
fairness is computed on exactly the same graph with and without it.

**Hidden divergence is not erased.** An infinite concrete loop of hidden
actions is still an infinite concrete behavior, in which the abstract model
stutters forever and therefore takes no abstract action at all. Accordant adds
no divergence assumption and no weak-fairness-on-internal-actions default: a
hidden loop never discharges an abstract obligation, so if the abstract model
requires progress, the check reports `TemporalFairnessMismatch`:

```text
Refinement failed: a fair concrete behavior has no fair aligned abstract behavior.
  ...
  Every concrete transition in the repeating part is hidden, so the abstract
  model stutters forever there.
  Hidden actions never discharge an abstract fairness obligation. This concrete
  divergence is a real behavior: exclude it with concrete fairness if the
  implementation cannot actually run it forever.
```

The fix is a concrete fairness assumption that rules the loop out — the same
mechanism as any other unwanted infinite concrete behavior:

```csharp
.CheckTemporal(
    concreteFairness: Fairness.Strong<CompleteWork>(),
    abstractFairness: Fairness.Weak<CompleteJob>());
```

The infinite completion Accordant adds for a genuinely terminal concrete state
is a checker artifact, not a hidden concrete action, and this diagnostic never
reports it as concrete divergence.

[`Samples/WalRefinement`](../../Samples/WalRefinement/) is the worked example:
crash, restart, recovery and write-back are hidden, an infinite crash loop is a
real behavior that no declaration removes, and the actions a crash *disables* —
recovery analysis, client reconnection and acknowledgement — need **strong**
fairness. Restart needs no fairness in that model because every down state has
restart as its only successor; weak fairness is evaluated over a complete
cycle, not merely over the interval while an action is enabled.

### State-neutral abstract edges

An abstract edge whose source and target states are equal is *state-neutral*.
It is a real abstract action that moves to another abstract configuration —
possibly one with different enabled abstract steps — without changing the
abstract state. It is not abstract stutter:

```text
abstract stutter          IsStutter = true    ChangesState = false
state-neutral edge        IsStutter = false   ChangesState = false
ordinary abstract edge    IsStutter = false   ChangesState = true
```

A state-neutral edge may be aligned explicitly, and doing so is often the
point: it selects the abstract configuration the rest of the run continues
from. It stays outside the compatibility fairness APIs: `Weak`, `Strong`, and
`WeakAll` retain changing-domain-state semantics. Under those APIs a
state-neutral abstract edge:

- never contributes to abstract enabledness, so it cannot raise a weak or
  strong obligation;
- never counts as taken, so it cannot discharge one.

Declaring a state-neutral edge instead of stutter therefore changes which
abstract configuration the alignment continues from without changing legacy
fairness accounting.

Metadata-aware fairness can select the real edge explicitly without adding a
fake state footprint:

```csharp
Func<ProcessTransition, bool> accepts =
    transition => transition.SemanticAction is Accept;

var collective = Fairness.WeakAction(accepts);
var perClient = Fairness.StrongEach(
    accepts,
    transition => transition.Subject);
```

The selector is authoritative. A selected state-neutral abstract edge
participates in enabledness and taken-ness; unrelated hidden administration
does not. `WeakAction`/`StrongAction` create one family obligation, while
`WeakEach`/`StrongEach` create one obligation per stable key. Temporal
refinement applies the same exact-configuration semantics on both the concrete
and abstract sides as ordinary property checking.

The infinite completion Accordant adds for a genuinely terminal concrete
state is a checker artifact, not a concrete action. It always aligns with
abstract stutter, never with a real state-neutral abstract edge, and does not
invoke `.MapTransition(...)` or satisfy a metadata-aware fairness obligation.

### Reading proof state

`.MapTransition(...)` mirrors the arity of the `.Map(...)` it follows, so a
declaration reads the same information the state mapping reads:

```csharp
.Map(concrete => ...)
    .MapTransition(transition => ...)

.Augment(...).Map((concrete, auxiliary) => ...)
    .MapTransition((transition, auxiliary) => ...)

.WithWitness(...).Map((concrete, witnesses) => ...)
    .MapTransition((transition, witnesses) => ...)

.Augment(...).WithWitness(...).Map((concrete, auxiliary, witnesses) => ...)
    .MapTransition((transition, auxiliary, witnesses) => ...)
```

Every arity also accepts a transition-only declaration, for the common case
where the abstract action depends on nothing but the concrete step — naming
internal actions is the usual example:

```csharp
.Augment(...).WithWitness(...).Map((concrete, auxiliary, witnesses) => ...)
    .MapTransition(transition => transition.StepFunction is ExpireLeaseStep
        ? AbstractResponse.Hidden
        : AbstractResponse.Unconstrained)
```

The two overloads are the same declaration; a check still declares its
transition mapping exactly once.

This is necessary, not decorative. The concrete transition alone is often
unable to name the abstract action:

- a queue entry is `Ready` both before its first lease and between retries, so
  only the recovered claim history says whether the entry is already assigned
  and therefore which ledger action a cancellation is;
- a transition that *resolves* a prediction cannot read that prediction from
  the concrete state, because the concrete state is exactly what was missing.

The proof state passed to a declaration is the one the transition **departs
from**. The augmentation value is the one derived from the concrete prefix
before the transition, and the witness collection still contains a prediction
this transition is about to resolve. The proof state a transition arrives at
is a *set* — a witness introduction branches it — so only the departing side
is a single, well-defined value.

Because a declaration may read the proof state, sibling witness copies can
declare different abstract actions for the same concrete edge. That is sound:
every copy is checked, and all of them must succeed.

### Diagnostics

Each trace position reports the declaration for the concrete step that entered
it, and temporal traces also report the response that was aligned:

```text
--observe-cancel-w0-t0--> concrete Queue(...); mapped abstract Ledger(...);
    declared step LedgerCancelUnassignedStep; candidates 0
      state-consistent abstract responses: step ledger-settle-t0
```

- `RefinementTraceItem.DeclaredAbstractResponse` — what the model asked for;
- `RefinementTraceItem.AlignedAbstractTransition` — the response temporal
  refinement chose;
- `RefinementTraceItem.StateConsistentAbstractTransitions` — the responses the
  declaration rejected, reported where a declaration admitted none of them;
- `AmbiguousTemporalRefinementException.Responses` and `.DeclaredResponse` —
  the responses that remain ambiguous and the declaration that failed to
  separate them.

Declaring the transition mapping twice is an error: combine the cases in one
callback. Returning null from a declaration is an error too — return
`AbstractResponse.Unconstrained` instead.

### The projected trace

`GetTraceString()` reports the refinement search. `GetProjectionString()`
reports the same counterexample as the *projection* the mapping defines —
each concrete position, the abstract state it maps to, and what the concrete
step became abstractly:

```text
Concrete behavior projected onto the abstract model by the mapping:
  start Queue(...)  =>  Ledger(...)
  --lease-w0-t0--> Queue(...)  =>  Ledger(...)   abstract step ledger-accept-t0
  --expire-w0-t0--> Queue(...)  =>  Ledger(...)   hidden action (abstract stutter)
  --fail-w0-t0--> Queue(...)  =>  Ledger(...)   state-neutral abstract step ledger-record-attempt-t0
  [cycle] --lease-w1-t0--> Queue(...)  =>  Ledger(...)   hidden action (abstract stutter)
```

`RefinementTraceItem.ProjectionKind` is the same classification as a value:

| `AbstractProjectionKind` | Meaning |
|---|---|
| `Start` | the first position; no concrete step entered it |
| `CheckerCompletion` | synthetic infinite stutter completing a genuinely terminal concrete behavior |
| `HiddenAction` | aligned with abstract stutter — the abstraction hides this concrete action |
| `StateNeutralStep` | aligned with a real abstract edge that does not change the abstract state |
| `AbstractStep` | aligned with an abstract edge that changes the abstract state |
| `AbstractUnchanged` | the mapped abstract state did not change, and the check did not pin down stutter versus a state-neutral edge |
| `Unaligned` | no known abstract response survived — a mismatch position or bounded frontier |

Safety refinement carries a *set* of coherent abstract configurations rather
than one aligned response, so an unchanged abstract state is reported as
`AbstractUnchanged` unless the model declared the transition hidden. Temporal
refinement reports the exact aligned response where one exists, and
`Unaligned` at and after a mismatch.

The infinite completion of a terminal concrete behavior is printed as
`checker completion of a terminal concrete behavior`, so it is never mistaken
for a hidden concrete loop.

### Bounds

A declaration never converts uncertainty into a verdict. The depth-frontier
flags are computed before the declaration filters anything, so a declaration
that no *known* response satisfies at an abstract frontier yields
`InconclusiveBound`, not `DoesNotRefine`.

### It is not a correspondence relation

`.MapTransition(...)` is a filter on step-aligned responses. It does not
introduce a relation between concrete and abstract states, it does not search
finite abstract paths per concrete step, and it does not perform fair
simulation or ω-language inclusion. Safety refinement still carries a *set* of
coherent abstract candidates, and the declaration prunes that set under an
existential: a `Refines` verdict means every concrete behavior has *some*
abstract behavior that matches the state mapping *and* takes exactly the
declared actions.

## Add deterministic checker-local state

Use `.Augment(...)` when refinement needs finite information derived from the
concrete execution prefix but intentionally absent from the concrete model:

```csharp
var result = Refinement
    .Between<ConcreteResource, AbstractResource>(
        concreteRoot,
        abstractRoot)
    .Augment(
        initial: _ => new OwnershipAuxiliary { Owner = "none" },
        next: (auxiliary, transition) =>
        {
            var owner = transition.StepFunction is ClaimConcrete claim
                ? claim.Owner
                : auxiliary.Owner;
            return new OwnershipAuxiliary { Owner = owner };
        })
    .Map((concrete, auxiliary) => new AbstractResource
    {
        Stage = MapStage(concrete.Stage),
        Owner = auxiliary.Owner
    })
    .Check();
```

The augmentation value is checker-local. It does not add fields to the
concrete model, change enabled steps, add transitions, or remove concrete
behaviors. Every concrete path has exactly one augmentation path:

```text
concrete:      Unclaimed --claim-bob--> Active -> Completed
augmentation: none      --claim-bob--> bob    -> bob
```

The
[`Samples/AugmentedRefinement`](../../Samples/AugmentedRefinement/)
sample shows why the augmentation participates in search identity. Claims by
Alice and Bob reach the same concrete `Active` graph node, but the checker
retains separate `(Active, alice)` and `(Active, bob)` refinement
configurations.

Augmentation state must derive from `State`. Accordant freezes each initial
and successor value, uses its semantic state hash in graph identity, and
checks that mapping and update callbacks do not mutate it.
The update receives a `RefinementTransition<TConcrete>` containing the source,
step function, edge metadata, and target.

Augmentation works with functional safety and functional temporal checks, and
composes with witnesses:

```csharp
.Augment(...)
.Map((concrete, auxiliary) => ...)
.CheckTemporal(concreteFairness, abstractFairness);

.Augment(...)
.WithWitness(...)
.Map((concrete, auxiliary, witnesses) => ...)
.Check();
```

On one concrete transition Accordant computes the augmentation update and the
witness lifecycle independently from the same transition. Neither update
callback reads the other's proof state. Resolution removes the witness, so
keep a resolved result in the concrete target state or in deterministic
augmentation when a later position still needs it.

This mechanism covers history variables, counters, remembered actions,
accumulated flags or sets, and deterministic monitor state. It cannot predict
the future; use [`.WithWitness(...)`](#retain-a-result-the-concrete-model-reveals-later)
for that.

## Retain a result the concrete model reveals later

Use `.WithWitness(...)` when a pending concrete operation has finitely many
possible future results and the abstract specification commits to one of them
before the implementation reveals it:

```csharp
var result = Refinement
    .Between<ConcreteState, AbstractState>(concreteRoot, abstractRoot)
    .WithWitness(
        initial: _ => WitnessChanges.None,
        next: (pending, transition) =>
            transition.Source.Stage == ConcreteStage.Idle
                ? WitnessChanges.Introduce(
                    "r1",
                    new OutcomeWitness { Color = Color.Red },
                    new OutcomeWitness { Color = Color.Blue })
                : transition.Target.Result != null
                    ? WitnessChanges.Resolve(
                        "r1",
                        new OutcomeWitness { Color = transition.Target.Result })
                    : WitnessChanges.None)
    .Map((concrete, witnesses) => concrete.Stage switch
    {
        ConcreteStage.Idle => new AbstractState(AbstractStage.Idle),
        ConcreteStage.Working => new AbstractState(
            AbstractStage.Chosen,
            witnesses.Get<OutcomeWitness>("r1").Color),
        _ => new AbstractState(AbstractStage.Done, concrete.Result)
    })
    .Check();
```

Introducing an operation branches the refinement proof over its declared
possible values:

```text
Start(r1)  -> (Working, {r1 -> Red})  maps to Chosen(Red)
           -> (Working, {r1 -> Blue}) maps to Chosen(Blue)
```

The resolving concrete transition keeps only the copies that predicted the
revealed value:

```text
Complete(r1, Red): {r1 -> Red}  --> {} and maps to Done(Red)
                   {r1 -> Blue} --> no successor
```

A wrong prediction is a finite dead branch at that concrete edge. It is not a
refinement failure and it never becomes an infinite stuttering behavior.

`Check()` and `CheckTemporal()` quantify over different objects at this point.
The finite safety check inspects every reachable witness prefix, including a
prefix that a later resolution refutes. The temporal check considers infinite
fair witness-extended behaviors, so a finite copy with no successor is not a
temporal behavior and cannot produce a temporal counterexample.

### Operation lifecycle

| Change | Meaning |
|---|---|
| `WitnessChanges.Introduce(id, values...)` | The operation becomes pending with a finite, nonempty, duplicate-free domain. The proof branches. |
| `WitnessChanges.Resolve(id, value)` | The concrete transition reveals the value. Only matching copies continue. |
| `WitnessChanges.Cancel(id)` | The operation disappears without constraining its value. Every copy survives and identical copies merge. |
| `WitnessChanges.None` | Unrelated transition. Every pending operation keeps its prediction. |

One atomic transition may change several operations. Combine changes with
`.And(...)` or `WitnessChanges.Combine(...)`:

```csharp
WitnessChanges
    .Introduce("payment", Approved, Declined)
    .And(WitnessChanges.Introduce("fraud", Clear, Flagged))
```

Any number of operations may be pending at once. Reachable witness assignments
along a concrete prefix are exactly the cross product of the pending domains,
and each operation is pruned independently:

```text
{A -> Red,  B -> Accepted}     resolve B = Rejected     {A -> Red,  B -> Rejected}
{A -> Red,  B -> Rejected}    ------------------------> {A -> Blue, B -> Rejected}
{A -> Blue, B -> Accepted}
{A -> Blue, B -> Rejected}
```

For one identity, resolution or cancellation followed by reintroduction in the
same transition is legal and processed in that order — that models a retry.

### Reading witnesses in the mapping

The mapping receives an immutable `WitnessCollection`:

- `Get<T>(id)` returns the predicted value and throws a descriptive error for
  an absent identity or a different value type;
- `TryGet<T>(id, out value)` and `IsPending(id)` keep mappings total when they
  also span positions where the operation is not pending;
- `Count` and `PendingOperations` describe the current collection.

Operation identities are deterministic strings the model already has: a
request ID, message ID, process ID, or queue slot. Accordant never invents
them. Witness values derive from `State`, so heterogeneous results get the
same freezing, mutation detection, and semantic equality as `.Augment(...)`.

Lifecycle callbacks receive a `PendingWitnesses` view exposing only pending
identities and their declared domains — never the selected prediction. The
lifecycle therefore stays a pure function of the concrete transition, so
sibling copies branch and resolve identically.

### Conservative extension and quantifiers

Witnesses overlay proof state on the existing concrete graph. They never add
or remove a concrete state or transition:

```text
erase witnesses from valid extended behaviors = original concrete behaviors
```

The proof shape is the standard auxiliary-variable one:

```text
ConcreteSpec = exists witnesses : WitnessExtendedSpec
WitnessExtendedSpec => MappedAbstractSpec
```

**Every** valid witness-extended behavior must satisfy the mapping. Accordant
never searches for one favorable prediction after a mapping failure. In
particular, an unresolved prediction that survives forever — because the
concrete state is genuinely terminal, or because completion never becomes
required — must still map to a valid abstract behavior.

Accordant rejects malformed witness definitions with
`WitnessDefinitionException` instead of silently proving a smaller model:

```csharp
Introduce("r1")                                  // empty domain
Introduce("r1", Red, Red)                        // duplicate semantic values
Introduce("r1", ...) twice for one pending id    // already pending
Resolve("missing", Red)                          // no such pending operation
Introduce("r1", Red) then Resolve("r1", Blue)    // the Blue concrete edge
                                                 // would have no extension
```

These are definition errors, clearly distinct from a genuine refinement
mismatch or a fairness failure, which are reported as
`RefinementCheckingResult` outcomes.

A resolving value must be observable from the concrete transition — its
source, step function, edge metadata, or target state — because that is all
the lifecycle callback receives.

This is a trust boundary. Accordant validates that a resolving value belongs
to the introduced domain, that an identity is not resolved or cancelled twice,
and that no checker-local value is mutated. It cannot validate that the value
is the one the transition actually reveals: resolving with a guess silently
prunes the sibling copies and can prove a specification the implementation
does not refine. When a transition reveals nothing, keep the prediction with
`WitnessChanges.None`, or drop it with `WitnessChanges.Cancel(...)` if the
abstract commitment no longer matters.

### Fairness with witnesses

Concrete enabledness is always evaluated on the **original** concrete graph,
never on witness-filtered successors:

```text
original Working edges: Poll, Complete(Red), Complete(Blue)
Red witness copy:       Poll, Complete(Red)
Blue witness copy:      Poll, Complete(Blue)
```

Completion stays continuously enabled at `Working` even though each copy keeps
only one outcome edge. Weak concrete fairness for `Complete` therefore still
excludes the infinite polling behavior. Killed copies contribute no taken
edges and no synthetic terminal stutter; synthetic stutter is created only
when the original concrete node has no edges, and it retains unresolved
witnesses.

Abstract fairness is checked universally over every valid witness-extended
behavior.

### Diagnostics

`RefinementTraceItem.Witnesses` reports the witness collection at each trace
position, separately from `RefinementTraceItem.AuxiliaryState`:

```text
--complete--> concrete Concrete(Done,blue); mapped abstract Abstract(Done,blue);
              auxiliary History(Working); witnesses {}
```

### Cost

Finite domains and simultaneous pending operations grow the proof state
proportionally to the combinations of outstanding predictions. Two operations
with two possible values each give four copies while both are pending.

### Representative example

The
[`Samples/WitnessRefinement`](../../Samples/WitnessRefinement/)
sample dispatches two independent checks. The specification commits to the
operator and to both verdicts when the order is dispatched; the implementation
learns each verdict only when that response arrives, and never records the
operator. The sample combines both mechanisms:

```csharp
.Augment(initial: ..., next: RememberDispatcher)   // past-derived operator
.WithWitness(PredictVerdicts, ResolveVerdicts)     // future-validated verdicts
.Map((implementation, auxiliary, witnesses) => new SpecificationOrder
{
    Stage = implementation.Stage,
    Operator = auxiliary.Operator,
    PaymentOutcome = implementation.PaymentResult
        ?? witnesses.Get<PaymentWitness>("payment").Verdict,
    FraudOutcome = implementation.FraudResult
        ?? witnesses.Get<FraudWitness>("fraud").Verdict
})
```

### Choosing between the two mechanisms

```text
.Augment(...)
    The value is uniquely computable from the concrete past.
    Example: remember which actor took an earlier action.

.WithWitness(...)
    A pending operation has finitely many possible future results.
    All result copies are created now; the future validates one.
```

For the Red/Blue example `.Augment(...)` cannot help, because the result is
not in the past. `.WithWitness(...)` makes the future choice explicit so the
final mapping stays functional and can participate in both safety and
temporal refinement.

### A case study combining both under fairness

The
[`Samples/WorkQueueRefinement`](../../Samples/WorkQueueRefinement/)
sample is a leased work queue with competing workers, retries, expiring
leases, cancellation and purging, checked against the ledger a client sees.
The ledger commits at assignment time to the worker that *first* accepted an
entry — recovered with `.Augment(...)` because retries move the lease — and to
the entry's final result — predicted with `.WithWitness(...)`. It exercises
the whole lifecycle: two predictions pending at once, a prediction that
survives a failed attempt, resolution by the transition that reveals the
result, and `Cancel` when a purge erases the commitment so the copies merge.

It also makes the fairness distinction concrete. Settling an entry is enabled
only while a worker holds the lease, so weak fairness cannot force it and
strong fairness is required; sweeping a cancelled unleased entry stays
enabled, so weak fairness is both necessary and sufficient. A strong
obligation on an action the implementation has disabled is vacuous, and
liveness then has to come from the concrete alternative.

Two of its ledger variants need
[`.MapTransition(...)`](#declare-the-abstract-action): one adds a second
ledger action that performs the same state change as settling, and one adds a
ledger action with no state footprint at all. Both report
`AmbiguousTemporalRefinementException` until the model declares which ledger
action each queue transition is, and both declarations have to read the
recovered claim history, because the concrete step alone cannot say whether
the entry was ever assigned.

## Bounded graphs

A genuine terminal state is complete and does not make safety refinement
inconclusive. A construction-time depth frontier has unknown successors.

- A reachable concrete frontier makes an otherwise successful check
  `InconclusiveBound`.
- If a required abstract transition is absent only because an abstract
  candidate is a frontier, the result is `InconclusiveBound`.
- A known abstract edge or known abstract stutter remains a valid match even
  at an abstract frontier.
- Any definitive finite mismatch takes precedence over frontier uncertainty.

## Current scope

Refinement remains strict and step-aligned. It uses one methodology: a
functional mapping made expressive with past-derived augmentation,
future-validated witnesses, and explicitly declared abstract actions. Actions
and edge metadata are compared only where the model names them with
`.MapTransition(...)`; the checker never infers an action correspondence, and
a declaration can only narrow the responses the state mapping already allows.
It does not search finite abstract paths per concrete step, and it does not
perform general nondeterministic relational temporal inclusion. Witness
domains are finite and enumerated by the model; there is no symbolic or
unbounded witness domain.

Abstract responses that share a step function, edge metadata and both states
differ only in abstract configuration. No declaration can separate them, and
temporal refinement still reports them as irreducibly ambiguous rather than
choosing one.

State hiding is `.Map(...)` and nothing else. Accordant has no relational
refinement checker, no state-graph quotient, no fair-simulation or ω-language
inclusion mode, and no separate hiding DSL. Hiding a concrete action means
mapping it to abstract stutter, and Accordant assumes nothing about
divergence: an infinite hidden loop stays a real concrete behavior until
concrete fairness excludes it.
