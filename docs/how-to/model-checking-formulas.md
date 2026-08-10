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

Inspect `result.Status` for the three possible outcomes:

```csharp
switch (result.Status)
{
    case PropertyCheckingStatus.Holds:
        break;
    case PropertyCheckingStatus.Violated:
        Console.WriteLine(result.GetTraceString());
        break;
    case PropertyCheckingStatus.InconclusiveBound:
        Console.WriteLine("Increase the exploration depth.");
        break;
}
```

`result.Valid` is `true` or `false` for conclusive checks and `null` for a
bounded-inconclusive check.

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
synthetic self-loop used at terminal nodes. A bounded-depth frontier has an
unknown continuation and is not converted into a stutter loop. It also exposes
`Next`.

`AllowStutterSensitiveFormulas()` does not claim every resulting formula is
stutter-sensitive. It means Accordant's type system no longer guarantees
stutter invariance.

## Ask what a node enables

The sensitive builder also exposes `Enabled`, the node-level proposition
"some action of this kind is available here":

```csharp
var exact = f.AllowStutterSensitiveFormulas();

var canSend = exact.Enabled<SendStep>();

var property = exact.Always(exact.Implies(queued, canSend));
```

`Enabled(A)` holds at a graph node when at least one *changing* outgoing model
edge of that node carries an action satisfying `A`. It uses the same
enabledness Accordant already uses for fairness:

- A state-neutral edge never counts, so an action that leaves the state
  unchanged is neither enabled nor taken.
- A terminal node enables nothing.

`Enabled` accepts the same action shapes as `Fairness`:

```csharp
exact.Enabled<SendStep>();
exact.Enabled(action => action.StepFunctionId == "Send");
exact.Enabled((state, next) => next.Status == Status.Sent);
exact.Enabled((state, action, next) => action.StepFunctionId == "Send" && next.Sent);
exact.Enabled(f.ObserveTransition((state, next) => next.Sent));
```

`Enabled` is stutter-sensitive, and so lives only on the sensitive builder,
because a graph node is a (state, active step-function set) pair: two nodes
carrying equal states can enable different actions, and an inserted stutter
step changes which node a position refers to.

Enabledness is a property of a node's complete outgoing edge set, which a
depth-truncated or not-yet-expanded frontier does not have. A check whose
verdict depends on such a frontier reports
`PropertyCheckingStatus.InconclusiveBound` rather than reading "no edges yet"
as "nothing enabled". Increase the exploration depth to make it conclusive.

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

The `Enabled` proposition reports the same enabledness these constraints use,
so it can express the fairness antecedents directly: `Stabilizes(Enabled(A))`
is "A is continuously enabled from some point on" (the weak-fairness premise)
and `InfinitelyOften(Enabled(A))` is the strong-fairness premise.

`Enabled(A)` considers changing edges only. A raw stutter-sensitive
`ObserveTransition(...)` may also hold on a state-neutral edge, so
`A => Enabled(A)` is valid only when `A` denotes the corresponding changing
action occurrence.
