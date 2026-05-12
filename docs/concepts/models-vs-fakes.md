# Models and Fakes

> **TL;DR**: Models and fakes share surface-level similarities — both encode domain logic about how a system should behave. But they serve different purposes: fakes **execute** (replacing the real system), while models **validate** (checking the real system). This distinction becomes crucial when handling non-determinism: server-generated values, uncertain outcomes, and async processes.

---

## Terminology: What We Mean by "Fake"

When we say "fake" in this document, we mean an in-memory implementation that simulates service behavior — sometimes called a simulator, test double, or fake service. We don't mean "mock" in the NMock/Moq sense (verifying method calls were made). A fake is code that *does what the real service does*, just simpler.

---

## The Similarities: A Key-Value Store

Let's start with a trivial example: a key-value store with `Put` and `Get` operations.

### As a Fake

```csharp
public class KeyValueFake
{
    private readonly Dictionary<string, string> _store = new();

    public void Put(string key, string value)
    {
        _store[key] = value;
    }

    public string? Get(string key)
    {
        return _store.TryGetValue(key, out var value) ? value : null;
    }
}
```

### As a Model

```csharp
[State]
public partial class KeyValueState : State
{
    public Dictionary<string, string> Store { get; set; } = new();
}

public class PutOperation : Operation<(string Key, string Value), Unit, KeyValueState>
{
    public PutOperation() : base("Put") { }

    public override ExpectedOutcomes Apply((string Key, string Value) request, KeyValueState state)
    {
        return Expect.Unit()
                     .ThenState(next => next.Store[request.Key] = request.Value);
    }
}

public class GetOperation : Operation<string, string?, KeyValueState>
{
    public GetOperation() : base("Get") { }

    public override ExpectedOutcomes Apply(string key, KeyValueState state)
    {
        var expected = state.Store.TryGetValue(key, out var value) ? value : null;
        return Expect.That(r => r == expected, $"should return '{expected}'")
                     .SameState();
    }
}
```

### They Look Almost Identical

Same state structure (`Dictionary<string, string>`). Same conditionals (`TryGetValue`). Same logic. If you squint, the model is just the fake with some extra wrapping.

You could even use the fake to validate the real system:

```csharp
// Call the fake
fake.Put("x", "hello");
var fakeResult = fake.Get("x");

// Call the real system
await realService.PutAsync("x", "hello");
var realResult = await realService.GetAsync("x");

// Compare
Assert.Equal(fakeResult, realResult);
```

At this level, they're interchangeable. So why have a "model" abstraction at all?

The differences emerge when determinism breaks down.

---

## Difference #1: Server-Generated Values

Real services generate values that the client doesn't control: last-modified timestamps, ETags, server-generated IDs, version numbers. Let's use `LastModified` as a simple example.

### The Problem

Your fake returns:
```csharp
public class KeyValueFake
{
    private readonly Dictionary<string, Entry> _store = new();

    public PutResponse Put(string key, string value)
    {
        var lastModified = DateTime.UtcNow;
        _store[key] = new Entry { Value = value, LastModified = lastModified };
        return new PutResponse { Key = key, LastModified = lastModified };
    }
}
```

The real service returns:
```json
{ "key": "x", "lastModified": "2026-03-10T14:32:17.4829Z" }
```

Now you try to use the fake to validate:

```csharp
// Call the fake
var fakeResult = fake.Put("x", "hello");

// Call the real system
var realResult = await realService.PutAsync("x", "hello");

// Compare
Assert.Equal(fakeResult.LastModified, realResult.LastModified);  // FAILS!
```

Both responses are correct. But `2026-03-10T14:32:17.1234Z != 2026-03-10T14:32:17.4829Z`, so equality fails.

### Fake Approach: Equivalence + State Mutation

To use a fake for validation, you need a different structure:

1. Call the fake and the real system
2. Check *equivalence* instead of equality
3. **Mutate the fake's state** to match what the real system returned

```csharp
public bool AreEquivalent(PutResponse fakeResult, PutResponse realResult)
{
    // Both should have a LastModified value
    return fakeResult.LastModified != default &&
           realResult.LastModified != default;
}

// The validation pattern becomes:
var fakeResult = fake.Put("x", "hello");
var realResult = await realService.PutAsync("x", "hello");

Assert.True(AreEquivalent(fakeResult, realResult), "responses should be equivalent");

// Now mutate the fake to track the real value for future comparisons
fake.UpdateLastModified("x", realResult.LastModified);

// Subsequent GET will now return the real system's LastModified
var fakeGet = fake.Get("x");
var realGet = await realService.GetAsync("x");
Assert.Equal(fakeGet.LastModified, realGet.LastModified);  // Now this works
```

This works, but the structure is awkward: call both, check equivalence, mutate state, repeat. Your fake now has validation logic mixed with simulation logic, and you're constantly syncing its state with external observations.

### Model Approach: Predicates + Capture

The model naturally separates "what properties must hold" from "what exact value is returned":

```csharp
public override ExpectedOutcomes Apply(PutRequest request, KeyValueState state)
{
    return Expect.That(r => r.LastModified != default, "should have a LastModified timestamp")
                 .ThenState((response, next) => {
                     // Capture the real LastModified when we observe it
                     next.Store[request.Key] = new Entry
                     {
                         Value = request.Value,
                         LastModified = response.LastModified
                     };
                 });
}

// Subsequent GET enforces consistency
public override ExpectedOutcomes Apply(string key, KeyValueState state)
{
    var entry = state.Store[key];
    return Expect.That(r => r.LastModified == entry.LastModified,
                       $"LastModified should be {entry.LastModified}")
                 .SameState();
}
```

The model says: "I don't know what `LastModified` will be — I didn't have to produce one. But once I observe it, I'll remember it and expect consistency in subsequent operations."

This same pattern applies to ETags, server-generated IDs, version numbers, or any value the server controls.

---

## Difference #2: Under-Specification

A fake must return a concrete value for every field — it's an implementation. If the response has 20 fields, the fake populates all 20.

A model can be partial. It expresses *what matters*, not *everything*:

```csharp
// Fake: must return something for every field
return new UserResponse
{
    Id = userId,
    Name = name,
    CreatedAt = DateTime.UtcNow,      // Must pick something
    UpdatedAt = DateTime.UtcNow,      // Must pick something
    Version = 1,                       // Must pick something
    // ... every field needs a value
};

// Model: only checks what matters
return Expect.That(r => r.IsSuccess && r.Data.Id == userId && r.Data.Name == name,
                   "user created with correct id and name")
             .ThenState(next => next.Users[userId] = new UserState { Name = name });
```

The model doesn't care about `CreatedAt`, `UpdatedAt`, or `Version` — they're not relevant to the behavior being specified. This makes the model simpler and more stable: if the real service adds a new field, the model doesn't need to change.

(In theory, you could also choose not to model certain fields in a fake — just return `null` or default values. But then those fields aren't being tested at all, and if the real system breaks them, you won't notice. With a model, you're explicitly saying "I'm validating these properties and not those" — the partiality is intentional and clear.)

---

## Difference #3: Uncertain Outcomes

### The Problem

Imagine you're testing a key-value service. You call `Put("x", "hello")` and... you get a socket timeout.

**Pause and think**: What would you expect the state of the service to be? Does the key `"x"` exist, or doesn't it?

Some people instinctively think: "I got a timeout, so it didn't work — the key doesn't exist." But that's not necessarily true.

There are two possibilities:

- **Possibility A**: The request never reached the server. The key `"x"` doesn't exist.
- **Possibility B**: The request reached the server and succeeded. The server sent back a response, but somewhere on the way back — maybe a router dropped the packet, maybe the connection died — you never received it. The key `"x"` exists with value `"hello"`.

From your perspective as a client, you cannot distinguish between these cases. Both are valid states for the system to be in.

This isn't a theoretical concern. If you're testing real distributed systems — especially with chaos engineering, network partitions, or event-driven architectures — these uncertain outcomes happen. You need a way to handle them.

### Fake Approach: Pick One

A fake has to do *something*:

```csharp
public void PutWithTimeout(string key, string value)
{
    // Roll the dice?
    if (Random.Shared.NextDouble() < 0.5)
        _store[key] = value;
    
    throw new SocketTimeoutException();
}
```

But now how do you use this fake to validate the real system? You don't know which path the fake took, and you don't know which path the real system took. Comparing them is meaningless.

### Model Approach: Track All Possibilities

The model doesn't have to decide — it says "all of these are possible":

```csharp
public override ExpectedOutcomes Apply(PutRequest request, KeyValueState state)
{
    // Factor out the shared state transition
    Action<KeyValueState> applyPut = next => next.Store[request.Key] = request.Value;

    return Expect.OneOf(
        // Normal success
        Expect.That(r => r.Success, "put succeeded")
              .ThenState(applyPut),
        // Timeout, but request went through
        Expect.Throws<SocketTimeoutException>()
              .ThenState(applyPut),
        // Timeout, request didn't go through  
        Expect.Throws<SocketTimeoutException>()
              .SameState()
    );
}
```

Three possibilities: success, timeout-but-succeeded, timeout-and-failed. The model tracks all of them. When you subsequently call `Get("x")`, the result can be explained in light of the possibilities — the key exists (if we took the first or second path), or it doesn't (if we took the third). Both are acceptable because from what we've observed, both are possible.

As you make more observations, the possibilities narrow. If `Get("x")` returns `"hello"`, we now know the request went through, and only that possibility remains.

But possibilities can also *widen*. If a second `Put("y", "world")` also times out, now we have even more possible states: `x` exists or not, crossed with `y` exists or not.

As long as there's at least one consistent explanation of all observed behavior, we're good. The model maintains that possibility space and narrows (or widens) it as observations come in.

The model is comfortable with "I don't know yet."

---

## Difference #4: Async and Background Processes

This pattern is everywhere: you create a resource, it goes into a "Provisioning" state, background processing happens, and eventually it transitions to "Succeeded" or "Failed."

Azure resource provisioning. Job queues. Image processing pipelines. Workflow orchestration.

### The Problem

You call `PUT /resources/my-resource` to create a resource. The service returns immediately with `{ "status": "Provisioning" }`. Some time later, background processing completes. You poll with `GET /resources/my-resource` until you see `"Succeeded"` or `"Failed"`.

### Fake Approach: Simulate the Async

A fake can simulate this:

```csharp
public PutResponse Put(string resourceId)
{
    _resources[resourceId] = new Resource { Status = "Provisioning" };
    
    // Simulate background work
    Task.Run(async () => {
        await Task.Delay(100);
        _resources[resourceId].Status = "Succeeded";
    });
    
    return new PutResponse { ResourceId = resourceId, Status = "Provisioning" };
}
```

This works for running the fake in isolation. But how do you use this fake to *validate* the real system's async behavior? The fake's timing won't match the real system's timing. The fake might always succeed; the real system might sometimes fail.

### Model Approach: Track Evolving Possibilities

The model expresses that async work triggers state transitions — without committing to when or which:

```csharp
public override ExpectedOutcomes Apply(PutResourceRequest request, ResourceState state)
{
    return Expect.That(r => r.Status == "Provisioning", "returns Provisioning")
                 .ThenState(next => next.Resources[request.ResourceId] = new Resource { Status = "Provisioning" })
                 .Triggers(AsyncOperation.Create<ResourceState>(
                     isTerminal: s => s.Resources[request.ResourceId].Status != "Provisioning",
                     transitions: new Action<ResourceState>[]
                     {
                         next => next.Resources[request.ResourceId].Status = "Succeeded",
                         next => next.Resources[request.ResourceId].Status = "Failed"
                     }
                 ));
}
```

The model says: "After this operation, background work will eventually transition the resource to either Succeeded or Failed. I don't know which, and I don't know when."

When you poll with `GET`, the model accepts any status consistent with the possibilities. Once you observe "Succeeded," the model knows that's the path the real system took.

**Fakes commit to a path. Models maintain the possibility space.**

---

## Synthesis: The Core Distinction

| | Fake | Model |
|---|---|---|
| **Purpose** | Replace the real system | Validate the real system |
| **Action** | Executes and returns values | Checks if values are acceptable |
| **Determinism** | Must commit to one path | Tracks multiple possibilities |
| **Completeness** | Returns concrete values for everything | Can be partial (predicates, "don't care") |
| **State** | Mirrors implementation | Minimal — only what defines behavior |

The logic in both looks similar because both encode domain knowledge. But the *intent* is different:

- A fake says: "Here's what I return."
- A model says: "Here's what's acceptable."

---

## Deriving One from the Other

### Model → Fake: Possible

You can derive a fake from a model. You do need to provide **mock responses** alongside your predicates — a concrete value to return, not just a predicate to validate against:

```csharp
return Expect.That(r => r.Success && r.Value == expected, "returns correct value",
                   mockResponse: () => new GetResponse { Success = true, Value = expected })
             .ThenState(next => next.Store[key] = value);
```

That's extra annotation work. But here's what you get: the machinery of non-determinism handled generically. The model tracks all possible states during validation; the fake just picks one (randomly) and commits:

```csharp
public class KeyValueFake
{
    private readonly PutOperation _putOp = new();
    private readonly GetOperation _getOp = new();
    private KeyValueState _state = new();

    public PutResponse Put(string key, string value)
        => Execute(_putOp, new PutRequest { Key = key, Value = value });

    public GetResponse Get(string key)
        => Execute(_getOp, key);

    private TResponse Execute<TRequest, TResponse>(
        Operation<TRequest, TResponse, KeyValueState> operation,
        TRequest request)
    {
        ExpectedOutcomes outcomes = operation.Apply(request, _state);
        ExpectedOutcome outcome = outcomes.PickRandom();  // Commit to one path
        TResponse response = outcome.GetMockResponse<TResponse>();
        _state = (KeyValueState)outcome.GetNextState(response);
        return response;
    }
}
```

Both `Put` and `Get` use the same generic `Execute` — and so would any other operation. The business logic stays in the operations; you don't write state-tracking code per operation.

### Fake → Model: Awkward

Going the other direction is harder. Starting from a fake, you'd need to:

1. Wrap return values in predicates
2. Add capture logic for server-generated values
3. Implement possibility tracking for non-determinism
4. Handle async state evolution

You'd essentially be re-implementing what the framework already provides. In some sense, you'd be rediscovering the model abstraction from first principles.

If you're going to write the domain logic anyway, writing it as a model gives you validation power that a fake can't offer — and you can derive a fake from it if you need one.

---

## Beyond Validation: What Models Enable

Once you have a model, validation is just the beginning. A model is a formal, executable specification — and that opens up possibilities that fakes can't offer:

- **State-space exploration**: The model defines all possible states and transitions. You can systematically explore them, generating test cases that cover paths you'd never think to write by hand.

- **Linearizability checking**: Given a set of concurrent operations and their responses, the model can answer: "Is there *any* sequential ordering that explains these results?" This is how you find race conditions.

A fake can only simulate. A model helps you reason.

---

## Conclusion

Models and fakes encode the same domain knowledge. They look similar. But fakes execute; models validate. Fakes commit to one path; models track possibilities.

You can derive a fake from a model. Going the other way is awkward — you'd reinvent the abstraction.

The model is not a fancy fake. It's a specification you can explore, validate against, and reason about.

---

**See also:** [What is Model-Based Testing?](what-is-model-based-testing.md) · [Understanding State](understanding-state.md)
