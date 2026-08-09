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

This first refinement mode checks strict step-aligned safety only. It does not
yet compare actions or edge metadata, apply fairness, check temporal
properties, use relational correspondence, or search finite abstract paths per
concrete step.
