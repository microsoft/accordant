# Understanding State

> **TL;DR**: State is the external observer's view of your system — what you need to know to define what operations should do. Keep it minimal: only track what's necessary to specify behavior.

---

## What is State?

When you write a spec, you need to describe what persists between operations. That's state.

But here's an important distinction: spec state is not the internal implementation state. It's what an **external observer** would see — the information you need to answer: "Given the current state and this request, what should happen?"

Consider a real banking system. The implementation might involve Entity Framework, SQL Server, connection pools, caching layers, retry policies, and thousands of lines of infrastructure code. But from the outside, what do you actually need to know to understand withdraw behavior?

Just the account balances.

```csharp
[State]
public partial class BankState : State
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}
```

That's the entire spec state for a bank account system. A dictionary mapping account IDs to balances. The spec doesn't care *how* the balance is stored — whether it's in SQL Server, PostgreSQL, or a flat file. It only cares *that* accounts have balances.

This asymmetry is deliberate. The spec captures the **semantics** — what should happen. The implementation handles the **mechanics** — how it happens.

---

## State Should Be Minimal

A good spec state includes exactly what operations need to determine their outcomes — nothing more.

When deciding whether to include something in state, ask yourself: "Will any operation need this to determine its response?" If yes, include it. If no, leave it out.

Simpler state has real benefits:

- **Easier to read and review.** A 10-line state class is easier to trust than a 200-line one.
- **Fewer states to explore.** Accordant generates tests by exploring the state space. Simpler state means smaller state space, which means faster, more focused test generation.
- **Higher confidence.** When the spec is simple, you can look at it and *know* it's correct.
- **Implementation independence.** You can swap databases, add caching, refactor internals — and the spec stays the same, because it only captures the observable behavior, not the implementation details.

---

## Immutability and the ThenState Pattern

In Accordant, operations don't modify state directly. Instead, they return **expected outcomes** that describe both what the response should look like and what the next state should be.

Here's a complete operation:

```csharp
spec.Operation<string, ApiResult<decimal>>("CreateAccount", (accountId, state) =>
{
    if (state.Accounts.ContainsKey(accountId))
    {
        return Expect.That<ApiResult<decimal>>(r => r.IsConflict)
                     .SameState();
    }

    return Expect.That<ApiResult<decimal>>(r => r.IsSuccess && r.Data == 0)
                 .ThenState<BankState>(nextState => nextState.Accounts[accountId] = 0);
});
```

Two patterns appear here:

- **`.SameState()`** means the operation doesn't change state. Use this for errors (like the conflict case above) or read-only operations (like GetBalance).

- **`.ThenState(nextState => ...)`** means the operation transitions to a new state. The lambda receives a cloned copy of the current state, which you can modify to describe the next state.

This keeps the original state untouched. You're not mutating anything — you're describing a transition.

---

## How Cloning Works

When you write `.ThenState(nextState => ...)`, the `nextState` parameter is already a **clone** of the current state. You can mutate it freely — you're not touching the original.

```csharp
.ThenState<BankState>(nextState => nextState.Accounts[accountId] = newBalance)
```

This makes state transitions easy to write. You don't need to manually copy anything or worry about accidentally modifying the pre-operation state.

### Advanced: ThenState with ObjectMap

For more complex states where objects reference other objects (a graph structure), there's a variant that provides a mapping from original objects to their clones:

```csharp
.ThenState<MyState>((nextState, objectMap) => { ... })
```

The `objectMap` lets you translate references from the original object graph to the cloned graph. Most specs use flat dictionaries and lists and won't need this — but it's available for advanced scenarios.

---

## How State Works Internally

Under the hood, Accordant needs to do several things with state objects:

- **Hash them** — to detect whether we've visited this state before during exploration
- **Compare them for equality** — to deduplicate equivalent states
- **Clone them** — to create the immutable copies that make `.ThenState(...)` work

All of this is driven by the **string representation** of the state. Two states with the same string representation are considered identical. This is how Accordant knows it's already explored a particular state and doesn't need to explore it again.

### Using the [State] Attribute

For most specs, mark your class with `[State]` and inherit from `State`:

```csharp
[State]
public partial class BankState : State
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}
```

The `[State]` attribute activates a source generator that handles everything — string representation, equality, cloning, and freeze logic. You just define your data as properties, and it all works automatically.

The key requirement is simple: **distinct logical states must produce distinct representations.** Just use plain data properties and you're good.

**A note on dictionaries:** Dictionary keys are sorted to ensure the same logical state always produces the same string representation. Supported key types are `string`, `int`, `long`, and `Guid`.

### Large Values and SharedState

Sometimes state includes large values — like binary image data — where deep cloning would be expensive. For these cases, you can mark a property with `[SharedState]`:

```csharp
[State]
public partial class ImageState : State
{
    public string Name { get; set; }
    
    [SharedState(Fingerprint = nameof(ContentFingerprint))]
    public List<byte> Content { get; set; }
    
    // Fingerprint used for equality/hashing instead of the full value
    public string ContentFingerprint => Content == null 
        ? null 
        : Convert.ToHexString(Content.ToArray());
}
```

The `[SharedState]` attribute tells Accordant to:
- **Shallow copy** the property during cloning (reference preservation)
- Use the specified **fingerprint method or property** for equality and hashing instead of the full value

**Important:** Since the value is shared by reference across clones, you must treat it as immutable — never modify it after it's set. If you need to change the value, create a new one.

This keeps cloning fast while still correctly detecting distinct states.

---

## Next Steps

- **See states in action** → [Quick Start](../quickstart.md)
- **Define operations on state** → [Tutorial 1: Your First Spec](../tutorials/01-your-first-spec.md)
- **Understand the broader picture** → [What is Model-Based Testing?](what-is-model-based-testing.md)
