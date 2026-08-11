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
`LeadsTo`, related derived operators, and the changing-step regular patterns
described below. It does not expose `Next`.

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

## Match regular patterns over changing steps

Some properties are about a *sequence* of things happening, which temporal
operators express only awkwardly. The default builder offers regular patterns
for that, written with `SafeRegex`.

A `SafeRegex` denotes a language over the behaviour's **changing steps**. A
step is one transition `s → s'`. It is *unchanged* when `s` and `s'` are
semantically equal — the same test used by `Always`, `Eventually`, `ENABLED`
and fairness — and *changing* otherwise. Unchanged steps are invisible to a
pattern: it never counts them, never matches them, and never changes its
verdict when they are inserted or removed. That covers both a named model edge
whose action leaves the state alone and the synthetic self-loop the checker
adds at a terminal node.

```csharp
var f = Formula.For<OrderState>();

var submitted = f.Observe(state => state.Status == Status.Submitted);
var paid = f.Observe(state => state.Status == Status.Paid);
var advances = f.ObserveTransition((state, next) => next.Version > state.Version);

// One changing step whose source state is Submitted.
SafeRegex fromSubmitted = f.ChangingStep(submitted);

// One changing step across which the version advances.
SafeRegex versionBump = f.ChangingStep(advances);

// Any single changing step, no changing step at all, and the empty language.
SafeRegex anyStep = f.AnyChangingStep;
SafeRegex noSteps = f.NoChangingSteps;
SafeRegex never = f.NeverMatches;
```

A state observation `p(s)` is read at the **source** state of the changing
step; a transition observation `p(s, s')` is read across it.

### Two operators consume a pattern

| Operator | Meaning |
| --- | --- |
| `After(R, φ)` | *some* prefix matches `R` and the remaining suffix satisfies `φ` |
| `Whenever(R, φ)` | *every* prefix matching `R` is followed by a suffix satisfying `φ` |

`Whenever` is the safety dual of `After`; both return `StutterSafeFormula`.

```csharp
// Every Submitted-step followed by a Paid-step lands in a shipped state.
var property = f.Whenever(
    f.ChangingStep(submitted).Then(f.ChangingStep(paid)),
    f.Observe(state => state.Shipped));

// A forbidden pattern, written either way.
var forbidden = f.Whenever(badPattern, f.False);
var same      = !f.After(badPattern, f.True);
```

The split point is the position just after the last changing step the pattern
consumed, up to invisible unchanged steps.

```csharp
// Counting is over changing steps, so an action that leaves the state alone —
// an idle tick, a no-op retry, a re-read that returns the same value — does
// not shift the count.
var afterTwoSteps = f.Whenever(
    f.AnyChangingStep.Then(f.AnyChangingStep),
    f.Observe(state => state.Version == 2));
```

### The surviving algebra

| Operator | Written | Meaning over changing steps |
| --- | --- | --- |
| Concatenation | `a.Then(b)` | `a` then `b` |
| Union | `a \| b` | `a` or `b` |
| Intersection | `a & b` | `a` and `b` |
| Complement | `!a` | every changing-step word except those matching `a` |
| Star | `a.Star()` | zero or more repetitions |
| Plus | `a.Plus()` | one or more repetitions |
| Optional | `a.Optional()` | `a`, or no changing step at all |

Every operator except fusion survives the lift. Complement is taken relative to
*all* changing-step words, so `!f.NoChangingSteps` is "at least one changing
step" rather than "any word containing an unchanged step".

### How a pattern is lowered

Before use, a pattern is compiled to the inverse image of the erasure
homomorphism `h` that deletes unchanged steps. Writing `⌈R⌉` for the compiled
form, `U` for an unchanged step and `C` for a changing one, a single-step
observation `A` becomes

```
⌈A⌉ = Unchanged* · (Changed ∧ A) · Unchanged*
```

and the rest follows structurally:

| Pattern | Compiled form | Why |
| --- | --- | --- |
| `∅` | `∅` | |
| `ε` | `U*` | no visible step still allows any number of unchanged ones |
| `A` | `U* · (C ∧ A) · U*` | one visible step, padded on both sides |
| `R · S` | `⌈R⌉ · ⌈S⌉` | every split of a lowered word induces a split of its erasure |
| `R + S`, `R ∩ S` | `⌈R⌉ + ⌈S⌉`, `⌈R⌉ ∩ ⌈S⌉` | `h⁻¹` is a Boolean-algebra morphism |
| `~R` | `~⌈R⌉` | `h` is total, so `h⁻¹(C* \ L) = Σ* \ h⁻¹(L)` |
| `R*` | `U* + ⌈R⌉*` | `ε ∈ L(R*)` always, so every all-unchanged word must match |
| `R+`, `R?` | `⌈R⌉ · ⌈R*⌉`, `⌈R⌉ + U*` | |

The result is exactly `h⁻¹(L)` of the intended visible language `L`, which is
what makes the pattern insensitive to inserted or deleted unchanged steps.

### No action predicates

Patterns observe state change, not action identity or edge metadata. The safe
surface deliberately follows the existing LTL proposition policy and does not
add "the step whose step function is `Send`", even though a changing-step guard
could support a restricted action-aware design. Express the model-level effect
as a transition observation instead:

```csharp
var sent = f.ObserveTransition((state, next) => next.Status == Status.Sent);
var property = f.Whenever(f.ChangingStep(sent), f.Always(f.Observe(s => s.Sent)));
```

Action-shaped predicates remain available through `ENABLED` on the
stutter-sensitive builder.

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

### Overlapping regex operators

The sensitive builder also exposes the full set of RLTL regex-prefix operators,
over `RegexPattern` — whose letters are *physical* transitions, unchanged ones
included:

| Sensitive operator | Meaning | Safe counterpart |
| --- | --- | --- |
| `SeqPrefix(R, φ)` | some prefix matches `R`, then `φ` | `After(R, φ)` |
| `Trigger(R, φ)` | every prefix matching `R` is followed by `φ` | `Whenever(R, φ)` |
| `OvlPrefix(R, φ)` | as `SeqPrefix`, but the last matched letter is also the first letter of the suffix | none |
| `Match(R, φ)` | as `Trigger`, with the same overlap | none |
| `R.Fusion(S)` | the last letter of an `R`-match is the first letter of an `S`-match | none |

`OvlPrefix`, `Match` and `Fusion` have no stutter-safe counterpart because they
share one **physical** transition between the two sides, and an inserted
unchanged step moves that transition. Concretely, with `a` and `b` changing
letters and `u` an unchanged one, the fused pattern `⌈a⌉ : ⌈b⌉` rejects `ab`
but accepts `aub` — although the two behaviours differ only by stuttering.
`After` and `Whenever` split *between* steps instead, which is why they survive
the lift.

`RegexPattern.Sigma` is `Σ*`, the language of every finite word, not a single
letter. A single physical letter is an observation atom, for example
`f.Observe(state => true)`. Note that `p | !p` is also every word, because ERE
complement is a whole-language complement.

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
