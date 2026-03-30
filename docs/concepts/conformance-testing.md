# Conformance Testing

> **TL;DR**: Conformance testing checks whether your implementation's behavior is allowed by the specification. Execute operations, observe responses, and verify they match what the spec permits.

---

## What Is Conformance Testing?

You have a spec that defines how your system should behave. You have an implementation — the real system. Conformance testing answers the question: does the implementation's behavior match what the spec allows?

The key word here is "allows." A spec doesn't always prescribe a single correct answer. It might permit multiple valid responses, especially when dealing with concurrency or non-determinism. Conformance testing checks whether the *actual* response from your system is among the responses the spec *permits*.

Academically, this is sometimes described as checking whether the implementation's behaviors are a subset of the spec-allowed behaviors. If the implementation ever does something the spec doesn't allow, that's a conformance failure — a bug.

---

## The Execution Loop

Conformance testing runs test cases against your real system. Each test case is a sequence of operations. The basic loop looks like this:

1. **Start from a known initial state.** The spec's logical state corresponds to the system's actual state. Both begin in corresponding initial states.

2. **Execute an operation.** Send a request to the real system and observe the response.

3. **Check conformance.** Ask the spec: given the current state and this request, is this response allowed? If yes, the spec tells us what the next state(s) could be.

4. **Repeat.** Continue with the next operation in the sequence.

5. **Reset before the next test case.** Each test case starts fresh from the initial state.

This loop is the heart of conformance testing. Execute, observe, validate, update state, repeat.

---

## State Profiles: When Multiple States Are Possible

Sometimes, after an operation, the system could be in one of several possible states. This happens when there's non-determinism — typically from asynchronous or background operations.

Consider a job queue. You call `CreateJob`, which starts background processing. The processing might complete quickly, or it might still be running. When you later call `GetJobStatus`, the system might return "Completed" or "InProgress" — both are valid, depending on timing.

From the spec's perspective, after `CreateJob`, the system is in a **state profile**: a set of possible states. Maybe the job completed (state A), or maybe it's still running (state B). We don't know which until we observe the system's behavior.

When validating, we check: does the observed response match *at least one* of the possible states in the profile? If the system returns "Completed," and state A allows that response, we're good — even if state B would not have allowed it. The response tells us which branch of non-determinism actually occurred.

As long as one interpretation explains the behavior, conformance holds.

---

## Validating Sequential Test Cases

Sequential test cases are straightforward. You execute operations one at a time and validate each response against the spec.

Here's the algorithm:

1. Start with the current state profile (initially just the starting state).

2. Execute the operation against the real system. Get the actual response.

3. For each possible state in the state profile:
   - Call the spec's `Apply` method with (state, request).
   - Get back the set of allowed outcomes — the responses the spec permits from this state.

4. Check: is the actual response in the allowed outcomes for *at least one* state?
   - If yes: conformance holds for this step. Update the state profile to the resulting states.
   - If no: conformance failure. The system did something the spec doesn't allow.

5. Repeat for each operation in the sequence.

### Example: Successful Validation

Suppose we're testing a bank account:

- **Current state**: `{ Balance: 100 }`
- **Operation**: `Withdraw(50)`
- **Actual response from system**: `{ Success: true, NewBalance: 50 }`

We ask the spec: from a state with Balance 100, what responses does `Withdraw(50)` allow?

The spec says: `{ Success: true, NewBalance: 50 }` — exactly one allowed outcome.

The actual response matches. Conformance holds. We update the state to `{ Balance: 50 }` and continue.

### Example: Conformance Failure

Same setup, but something's wrong with the implementation:

- **Current state**: `{ Balance: 100 }`
- **Operation**: `Withdraw(50)`
- **Actual response from system**: `{ Success: true, NewBalance: 30 }`

We ask the spec: what's allowed?

The spec says: `{ Success: true, NewBalance: 50 }`.

The actual response says NewBalance is 30. That's not in the allowed set. **Conformance failure.** The implementation computed the wrong balance — a bug.

### Using spec.Allows in Code

In a test method, you validate responses using `spec.Allows`:

```csharp
[TestMethod]
public async Task ManualConformanceTest()
{
    var spec = new BankAccountSpec();
    var stateProfile = new StateProfile(new BankState { Balance = 100 });
    
    // Execute an operation against the real system
    var response = await system.Withdraw(50);
    
    // Validate the response is allowed by the spec
    (bool isValid, string message, stateProfile) = 
        spec.Allows(spec.Withdraw, new WithdrawRequest(50), response, stateProfile);
    
    Assert.IsTrue(isValid, message);
    
    // stateProfile is now updated for the next operation
    
    // ... more operations ...
}
```

The `Allows` method returns three values:
- **isValid**: Whether the response is allowed from at least one state in the profile
- **message**: An error description if invalid, empty otherwise
- **updatedStateProfile**: The new set of possible states after this operation

This is exactly what `spec.RunTests` does internally — it loops through each operation in a test case, calls `spec.Allows`, and tracks the state profile.

---

## Validating Concurrent Test Cases

Concurrent test cases are more complex. Multiple operations execute at the same time, and we observe all their responses — but we don't know in what order the system actually processed them.

A concurrent test case has two parts:

1. **Sequential prefix**: A sequence of operations that sets up a particular state.
2. **Concurrent operations**: N operations that execute simultaneously from that state.

After the concurrent operations complete, we have N responses. The challenge: find an ordering of those N operations — a linearization — where the spec allows each response in sequence.

### The Algorithm

1. Consider all possible orderings of the N concurrent operations. There are N! permutations.

2. For each ordering, simulate sequential execution against the spec:
   - Start from the state after the sequential prefix.
   - Apply each operation in order, checking if its actual response is allowed.
   - Track the state after each operation.

3. If *any* ordering produces a valid trace where every response is allowed, conformance holds.

4. If *no* ordering explains the observed responses, conformance fails — the system did something that no valid serialization permits.

### Example: Valid Concurrent Execution

Consider two concurrent withdrawals:

- **State after prefix**: `{ Balance: 100 }`
- **Concurrent operations**: `[Withdraw(60), Withdraw(60)]`
- **Observed responses**: `[{ Success: true, NewBalance: 40 }, { Success: false, Reason: InsufficientFunds }]`

We need to find an ordering. Let's try:

**Ordering 1**: First withdrawal, then second.
- `Withdraw(60)` from Balance 100 → Spec allows `{ Success: true, NewBalance: 40 }`. Actual matches. ✓
- `Withdraw(60)` from Balance 40 → Spec allows `{ Success: false, Reason: InsufficientFunds }`. Actual matches. ✓

Both responses are explained by this ordering. **Conformance holds.**

### Example: Race Condition Bug

Same setup, but the implementation has a race condition:

- **State after prefix**: `{ Balance: 100 }`
- **Concurrent operations**: `[Withdraw(60), Withdraw(60)]`
- **Observed responses**: `[{ Success: true, NewBalance: 40 }, { Success: true, NewBalance: 40 }]`

Both withdrawals succeeded? Let's check if any ordering allows this.

**Ordering 1**: First withdrawal, then second.
- `Withdraw(60)` from Balance 100 → Spec allows `{ Success: true, NewBalance: 40 }`. Actual matches. ✓
- `Withdraw(60)` from Balance 40 → Spec allows `{ Success: false, InsufficientFunds }`. But actual says Success! ✗

**Ordering 2**: Second withdrawal, then first.
- Same problem in reverse. ✗

No ordering explains both withdrawals succeeding. **Conformance failure.** This is a classic race condition — both operations read the balance as 100 before either wrote, leading to an impossible outcome.

### Practical Considerations

The factorial explosion is real. With N concurrent operations, there are N! orderings to check:
- 2 operations: 2 orderings
- 3 operations: 6 orderings
- 4 operations: 24 orderings
- 5 operations: 120 orderings

In practice, 3-4 concurrent operations is usually sufficient. Most concurrency bugs manifest with small numbers of concurrent operations. If the system under test is experiencing load or stress — other requests being processed, background work running — even a few concurrent test operations can expose race conditions that would otherwise be hidden.

### Using spec.AllowsConcurrent in Code

For concurrent validation, use `spec.AllowsConcurrent`:

```csharp
[TestMethod]
public async Task ManualConcurrentConformanceTest()
{
    var spec = new BankAccountSpec();
    var stateProfile = new StateProfile(new BankState { Balance = 100 });
    
    // Execute concurrent operations and collect responses
    var responses = await Task.WhenAll(
        Task.Run(() => system.Withdraw(60)),
        Task.Run(() => system.Withdraw(60)));
    
    var concurrentCalls = new (IOperation, object, object)[]
    {
        (spec.Withdraw, new WithdrawRequest(60), responses[0]),
        (spec.Withdraw, new WithdrawRequest(60), responses[1])
    };
    
    // Check if there's a valid linearization
    (bool isValid, string message, stateProfile) = 
        spec.AllowsConcurrent(stateProfile, concurrentCalls);
    
    Assert.IsTrue(isValid, message);
}
```

Internally, `AllowsConcurrent` tries all N! permutations of the concurrent calls. For each permutation, it simulates sequential execution using `Allows`. If any permutation succeeds — every response is allowed in that order — the concurrent execution is valid.

---

## Hand-Written Test Cases

You don't have to use auto-generated test cases. You can write test cases by hand and still get the benefits of conformance checking.

This is useful for:
- **Specific edge cases** you want to ensure are tested
- **Regression tests** for bugs you've found and fixed
- **Exploratory testing** where you want to try specific scenarios

The validation mechanism is identical. Whether a test case was generated by an algorithm or written by hand, Accordant validates it the same way: execute each operation, check if the response is allowed, update the state.

---

## Running Test Cases

The `Spec` class provides convenience methods for generating and running test cases:

```csharp
[TestMethod]
public async Task SequentialConformanceTests()
{
    var spec = new BankAccountSpec();
    var initialState = new BankState { Balance = 0 };
    var inputs = new InputSet { ... };
    
    // Generate and run sequential test cases
    var testCases = spec.GenerateTests(initialState, inputs);
    var results = await spec.RunTests(context, initialState, testCases);
    
    Assert.IsTrue(results.All(r => r.Passed));
}

[TestMethod]
public async Task ConcurrentConformanceTests()
{
    var spec = new BankAccountSpec();
    var initialState = new BankState { Balance = 0 };
    var inputs = new InputSet { ... };
    
    // Generate and run concurrent test cases
    var testCases = spec.GenerateConcurrentTests(initialState, inputs);
    var results = await spec.RunTests(context, initialState, testCases);
    
    Assert.IsTrue(results.All(r => r.Passed));
}
```

Internally, `RunTests` performs the same validation loop:
1. Start from initial state
2. For each operation call: execute against the system, validate response with `spec.Allows`, update state
3. Record pass/fail
4. Reset state before the next test case

For concurrent test cases, the framework additionally handles linearization — it tries all orderings of concurrent operations to find one where all responses are valid.

Under the hood, `RunTests` uses the same `spec.Allows` and `spec.AllowsConcurrent` methods shown earlier — the core validation logic is identical. The framework just handles additional concerns: when a request depends on a prior response (e.g., deleting a resource by ID from a create response), or when polling is needed for async operations. In hand-written test cases, you deal with these yourself. The framework provides structured support via `RequestDerivation` and polling configuration for these situations — we'll cover these concepts later.

---

## Summary

| Concept | Description |
|---------|-------------|
| **Conformance** | Implementation's behavior is a subset of spec-allowed behaviors |
| **State profile** | Set of possible states due to non-determinism |
| **Sequential validation** | Execute operation, check if response is in allowed set |
| **Concurrent validation** | Find a linearization where all responses are allowed |
| **Linearization** | An ordering of concurrent operations that explains observed results |
| **spec.Allows** | Checks if a response is allowed from the current state profile |
| **spec.AllowsConcurrent** | Checks if concurrent responses have a valid linearization |

---

## Next Steps

- [How Test Generation Works](how-test-generation-works.md) — how test cases are created from the state graph
- [Step Functions and Async Operations](step-functions-and-async.md) — modeling non-deterministic behavior
