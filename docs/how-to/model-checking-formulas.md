# Model-Checking Formulas

Accordant distinguishes three related concepts:

- An **observation** is one Boolean fact about a state or transition.
- A **formula** combines observations with Boolean and temporal operators.
- A **named formula** is an optional label for reporting a checked assertion.

## Start with the stutter-safe language

Create a typed formula builder with `Formula.For<TState>()`:

```csharp
var f = Formula.For<OrderState>();

var submitted = f.Observe(state => state.Status == Status.Submitted);
var completed = f.Observe(state => state.Status == Status.Completed);

StutterSafeFormula property = f
    .LeadsTo(submitted, completed)
    .Named("Submitted orders eventually complete");

var result = root.Check(property);
```

The default builder exposes only constructs that guarantee invariance under
finite repetitions of an indistinguishable state. It includes state
observations, Boolean operators, `Always`, `Eventually`, `Until`, `Release`,
`LeadsTo`, and related derived operators. It does not expose `Next`.

Observation names are inferred from their expressions for diagnostics:

```csharp
var ready = f.Observe(state => state.Ready);
```

Supply a name when a shorter or domain-specific label is clearer:

```csharp
var ready = f.Observe(state => state.Ready, "Ready");
```

Formula names are optional. Use `Named` when reports or counterexamples should
identify a top-level assertion:

```csharp
var property = f.Always(ready).Named("System remains ready");
```

## Observe transitions safely

The default builder also supports relations over a source and target state:

```csharp
var advances = f.ObserveTransition(
    (state, next) => next.Version > state.Version);

var property = f.Always(advances);
```

A temporal position is anchored at its source state: state observations read
`s`, while transition observations read `(s, s')`. Accordant uses full
semantic state equality to classify an edge as changed or unchanged.

The safe operators lift transition observations according to their role:

| Operator role | Interpretation |
| --- | --- |
| Continuing condition | `Allowed(A) = Unchanged || A` |
| Trigger, goal, or occurrence | `Occurs(A) = Changed && A` |

Consequently, `Always(A)` permits stutter edges, `Eventually(A)` requires a
changing occurrence, and `Until(H, G)` means `Allowed(H) U Occurs(G)`.
`Release`, `InfinitelyOften`, `Stabilizes`, and `LeadsTo` follow the same
continuing-condition versus occurrence distinction. Mixed state/transition
overloads use ordinary source-position LTL semantics.

## Opt into stutter-sensitive formulas explicitly

Some properties must observe exact transition boundaries or use operators that
are not yet classified as stutter-invariant:

```csharp
var exact = f.AllowStutterSensitiveFormulas();

var changed = exact.ObserveTransition(
    (state, nextState) => state.Value != nextState.Value);

var changedNext = exact.Next(changed);
```

The sensitive builder interprets transition observations literally at every
physical edge position. That includes named unchanged model edges and the
synthetic self-loop used at terminal and bounded-depth frontier nodes. It also
exposes `Next`.

`AllowStutterSensitiveFormulas()` does not claim every resulting formula is
stutter-sensitive. It means Accordant's type system no longer guarantees
stutter invariance.

## Type propagation

Safe operators over safe inputs return `StutterSafeFormula`:

```csharp
StutterSafeFormula safe = f.Always(f.Eventually(ready));
```

Unrestricted constructs return `TemporalFormula`. Combining safe and
unrestricted formulas also returns `TemporalFormula`:

```csharp
TemporalFormula next = exact.Next(ready);
TemporalFormula mixed = safe & next;
```

This lets IntelliSense guide normal property authoring toward the guaranteed
subset while keeping the complete language available through an explicit
opt-out.

## Add fairness explicitly

Checks use `Fairness.None` unless a fairness constraint is supplied. Fairness
always concerns changing edges; an action that produces an unchanged state
does not count as enabled or taken for fairness.

```csharp
var byStep = Fairness.Weak<SendStep>();
var byRelation = Fairness.Strong<OrderState>(
    (state, next) => next.Status == Status.Sent);
var byEdge = Fairness.Weak<OrderState>(
    (state, action, next) =>
        action.StepFunctionId == "Send" && next.Status == Status.Sent);
```

Weak fairness requires a continuously enabled changing action to occur.
Strong fairness requires a changing action enabled infinitely often to occur
infinitely often. Constraints can be combined with `+`; use
`Fairness.WeakAll` to apply weak fairness to every changing step.
