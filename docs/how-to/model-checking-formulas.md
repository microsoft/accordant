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

## Opt out of the guarantee explicitly

Some properties must observe exact transition boundaries or use operators that
are not yet classified as stutter-invariant:

```csharp
var exact = f.WithoutStutterGuarantee();

var changed = exact.ObserveTransition(
    (state, nextState) => state.Value != nextState.Value);

var changedNext = exact.Next(changed);
```

`WithoutStutterGuarantee()` does not mean the resulting formula is necessarily
stutter-sensitive. It means Accordant's type system no longer guarantees that
it is stutter-invariant.

Full transition observations can inspect the source state, action and target
state:

```csharp
var sent = exact.ObserveTransition(
    (state, transition, nextState) => transition.ActionId == "Send");
```

At terminal and bounded-depth frontier nodes, model checking presents the
synthetic self-loop as a transition whose `IsStutter` property is `true`.

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
