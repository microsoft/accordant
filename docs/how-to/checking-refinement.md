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
edges. Weak or observational path refinement is a separate future mode.

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
inconclusive bounded check. A failure has either
`InitialStateMismatch` or `TransitionMismatch` as its `FailureKind`.

The diagnostic trace contains:

- each concrete graph node;
- the concrete step and edge metadata that entered it;
- the abstract state produced by the mapping;
- the coherent abstract graph configurations still able to explain the
  concrete prefix.

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

## Retain several abstract explanations

Use relational correspondence when the concrete state does not yet determine
one abstract state:

```csharp
var result = Refinement
    .Between<ConcreteState, AbstractState>(concreteRoot, abstractRoot)
    .Corresponds((concrete, abstraction) =>
        concrete.Stage == abstraction.Stage &&
        (concrete.RevealedChoice == null ||
            concrete.RevealedChoice == abstraction.Choice))
    .Check();
```

The checker retains every corresponding abstract configuration reachable by
an abstract step or stutter. Later concrete states prune candidates that are no
longer consistent:

```text
concrete choice unknown:  { red, blue }
concrete reveals blue:    { blue }
```

Candidate selection is path coherent. A later concrete state cannot switch to
an abstract configuration whose earlier history was already pruned. This
supports many finite-state prophecy-style correspondences without adding a
future-choice field to the concrete model.

### Representative example: a choice revealed late

The
[`Samples/RelationalRefinement`](../../Samples/RelationalRefinement/)
sample models an order sent to either a card or bank gateway.

The abstract model chooses the gateway when the order is sent. The
implementation does not record which gateway handled the order until the
authorization response arrives:

```text
position       concrete                 surviving abstract candidates
initial        New / unknown            { New }
sent           Sent / unknown           { Sent(card), Sent(bank) }
authorized     Authorized / bank        { Authorized(bank) }
completed      Completed / bank         { Completed(bank) }
```

The relation says that stages must agree and, once the implementation reveals
the gateway, the gateway must agree too:

```csharp
.Corresponds((implementation, specification) =>
    implementation.Stage == specification.Stage &&
    (implementation.ResolvedGateway == null ||
        implementation.ResolvedGateway == specification.ChosenGateway))
```

This is not permission to choose an abstract history after every step. Both
histories coexist while the gateway is unknown. Revealing `bank` permanently
prunes the `card` history. If only the card history can complete, the final
concrete bank step fails refinement; it cannot revive the discarded card
candidate.

The sample tests also demonstrate the quantifiers: **every** concrete
nondeterministic branch must have **some one coherent** abstract explanation.
One unsupported concrete gateway is therefore enough to produce a
counterexample.

Functional `.Map(...)` and relational `.Corresponds(...)` use the same
step-aligned engine. Functional mapping is preferable when one abstract state
is already determined because it is simpler and generally retains fewer
candidates.

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
multiple responses, `AmbiguousTemporalRefinementException` reports that the
general omega-language inclusion engine is required. The checker never
silently chooses one response and risks a wrong verdict.

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
checks that mapping, correspondence, and update callbacks do not mutate it.
The update receives a `RefinementTransition<TConcrete>` containing the source,
step function, edge metadata, and target.

Augmentation works with functional safety, relational safety, and functional
temporal checks:

```csharp
.Augment(...)
.Corresponds((concrete, auxiliary, abstraction) => ...)
.Check();

.Augment(...)
.Map((concrete, auxiliary) => ...)
.CheckTemporal(concreteFairness, abstractFairness);
```

This mechanism covers history variables, counters, remembered actions,
accumulated flags or sets, and deterministic monitor state. It cannot predict
the future or branch existentially; witness or prophecy support is a separate
future mode.

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

Refinement remains strict and step-aligned. It does not compare actions or
edge metadata, search finite abstract paths per concrete step, or perform
general nondeterministic relational temporal inclusion.
