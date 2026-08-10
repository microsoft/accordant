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
abstract edge or abstract stutter). If stutter and an equal-state abstract edge,
parallel abstract edges, or several equal-state configurations provide
multiple responses, `AmbiguousTemporalRefinementException` reports the
ambiguity. The checker never silently chooses one response and risks a wrong
verdict.

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
functional mapping made expressive with past-derived augmentation and
future-validated witnesses. It does not compare actions or edge metadata,
search finite abstract paths per concrete step, or perform general
nondeterministic relational temporal inclusion. Witness domains are finite and
enumerated by the model; there is no symbolic or unbounded witness domain.

The action-comparison gap is visible in
[`Samples/WorkQueueRefinement`](../../Samples/WorkQueueRefinement/). Two
reasonable specifications cannot be aligned temporally today: one where two
abstract actions perform the same abstract state change, and one where an
abstract action has no state footprint at all. Both report
`AmbiguousTemporalRefinementException`; adding fairness for the state-neutral
action cannot choose between that action and abstract stutter. Fairness is
defined over changing edges, and alignment fails before fairness analysis.
Both
specifications still pass `Check()`, since safety refinement carries a set of
coherent abstract configurations and never has to choose a response.
