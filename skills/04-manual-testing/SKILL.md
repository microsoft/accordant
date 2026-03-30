---
name: Accordant Manual Testing
description: How to manually test a system against a Accordant spec using Allows() and AllowsConcurrent() for response validation
---

# Manual Testing with Accordant

Manual testing lets you validate individual API calls against your spec without auto-generating test cases. Use this when:
- Exploring a system interactively
- Writing targeted integration tests
- Validating specific scenarios you care about

## Core API: spec.Allows()

`Allows()` checks whether an observed response is valid according to the spec, given the current state:

```csharp
var (isValid, message, nextStateProfile) = spec.Allows(
    operation,      // The operation being tested
    request,        // The request that was sent
    response,       // The actual response received
    stateProfile    // Current state profile (tracks possible states)
);
```

Returns:
- `isValid`: `true` if the response matches what the spec predicts
- `message`: Error explanation if invalid, null otherwise
- `nextStateProfile`: Updated state profile reflecting the transition

## StateProfile

A `StateProfile` tracks the possible states the system could be in. It starts with a single initial state but can branch when non-deterministic operations occur.

```csharp
// Start with a known initial state
var stateProfile = new StateProfile(new MyState());

// After each Allows() call, use the returned nextStateProfile
var (isValid, message, nextProfile) = spec.Allows(op, req, resp, stateProfile);
stateProfile = nextProfile;  // Chain for next call
```

## Complete Manual Test Pattern

```csharp
[Test]
public async Task ManualTest_Scenario()
{
    var spec = new MySpec();
    var initialState = new MyState();
    var context = spec.CreateTestingContext();

    // Register real system under test
    context.Register(new MyApiClient());

    var stateProfile = new StateProfile(initialState);

    // Helper to execute and validate
    async Task<TResp> Allows<TReq, TResp>(
        Operation<TReq, TResp, MyState> op, TReq request)
    {
        var response = await op.ExecuteAsync(context, request);
        var (isValid, message, nextProfile) = spec.Allows(op, request, response, stateProfile);
        Assert.IsTrue(isValid, message);
        stateProfile = nextProfile;
        return response;
    }

    // Execute scenario steps
    await Allows(spec.CreateUser, new User("alice", "Alice"));
    await Allows(spec.CreateTodo, new Todo("alice", "todo-1", "Buy milk"));
    await Allows(spec.GetTodo, ("alice", "todo-1"));
    await Allows(spec.CompleteTodo, ("alice", "todo-1"));
}
```

## Creating a TestingContext

```csharp
var context = spec.CreateTestingContext();

// Register services the Execute methods need
context.Register(new HttpClient());           // context.Get<HttpClient>()
context.Register(new MyApiClient());          // context.Get<MyApiClient>()
context.Register(new Stack<int>());           // context.Get<Stack<int>>()
```

`TestingContext` is a simple service locator — `Register<T>(instance)` stores, `Get<T>()` retrieves.

## Concurrent Validation: AllowsConcurrent()

Validates that a set of concurrent responses can be explained by SOME valid linearization:

```csharp
// Fire concurrent requests
var task1 = Task.Run(() => op.ExecuteAsync(context, req1));
var task2 = Task.Run(() => op.ExecuteAsync(context, req2));
await Task.WhenAll(task1, task2);

// Validate: responses must be explainable by some serial ordering
var (isValid, message, nextProfile) = spec.AllowsConcurrent(
    stateProfile,
    new List<(IOperation, object, object)>
    {
        (createUser, new User("bob", "Bob One"), await task1),
        (createUser, new User("bob", "Bob Two"), await task2),
    });

Assert.IsTrue(isValid, message);
```

`AllowsConcurrent` tries all possible orderings (linearizations) of the concurrent operations. If ANY ordering produces valid responses for ALL operations, the result is valid.

## Using Operations Directly

Operations can be used to execute calls outside of the test framework:

```csharp
// Execute a call
var response = await operation.ExecuteAsync(context, request);

// Or synchronously
var response = operation.Execute(context, request);
```

## ExplainInvalidResponse

When a response is invalid, get a detailed explanation:

```csharp
var (isValid, _, _) = spec.Allows(op, request, response, stateProfile);
if (!isValid)
{
    string explanation = op.ExplainInvalidResponse(request, stateProfile.GetState(), response);
    Console.WriteLine(explanation);
}
```

## Invoke (Pure Model Execution)

For spec-only exploration without calling any real system:

```csharp
// Get all possible (response, nextStateProfile) pairs from the model
IList<(TResponse, StateProfile)> results = operation.Invoke(request, currentState);

foreach (var (response, nextProfile) in results)
{
    Console.WriteLine($"Possible response: {response}, next state: {nextProfile}");
}
```

## Manual Polling for Async Operations

When an operation triggers background processing (via `AsyncOperation.Create` / `TerminatingStepFunction`), polling is handled automatically in generated tests. But in manual tests, **you must poll yourself**. Here's the pattern:

### The Problem

After calling an async operation (e.g., `CreateJob`), the system returns immediately with a "Pending" status. The background work completes asynchronously. You need to poll with a separate operation (e.g., `GetJob`) until the work finishes.

### The Pattern

```csharp
[Test]
public async Task ManualTest_AsyncJobFlow()
{
    var spec = new JobQueueSpec();
    var initialState = new JobQueueState();
    var context = spec.CreateTestingContext();
    context.Register(new JobQueueApiClient(factory.CreateTestClient()));
    var stateProfile = new StateProfile(initialState);

    // Helper: execute and validate
    async Task<TResp> Allows<TReq, TResp>(
        Operation<TReq, TResp, JobQueueState> op, TReq request)
    {
        var response = await op.ExecuteAsync(context, request);
        var (isValid, message, nextProfile) = spec.Allows(op, request, response, stateProfile);
        Assert.IsTrue(isValid, message);
        stateProfile = nextProfile;
        return response;
    }

    // Helper: check if async work is done in ALL possible states
    // (non-deterministic operations cause state branching)
    bool IsTerminal(string jobId) =>
        stateProfile.StatesAndStepFunctions
            .All(ssf => CreateJobOperation.IsTerminal((JobQueueState)ssf.State, jobId));

    // 1. Trigger async operation — returns Pending immediately
    await Allows(spec.CreateJob, "job1");

    // 2. Poll until terminal — use the operation's Polling config for bounds
    var polling = spec.CreateJob.Polling;
    for (int i = 0; i < polling.MaxRetryCount; i++)
    {
        await Allows(spec.GetJob, "job1");       // Each poll validates against spec
        if (IsTerminal("job1")) break;            // Check if done
        await Task.Delay(polling.WaitTimeInMs);   // Wait before next poll
    }

    // 3. Liveness check — job should not be stuck forever
    Assert.IsTrue(IsTerminal("job1"),
        $"Liveness violation: job still Pending after {polling.MaxRetryCount} retries");

    // 4. Continue with further operations — spec tracks state correctly
    await Allows(spec.GetJob, "job1");   // ResultPath should be stable now
    await Allows(spec.DeleteJob, "job1");
}
```

### Key Polling Concepts

1. **StateProfile branches on non-determinism**: After `CreateJob`, the `AsyncOperation.Create` with multiple transitions (Completed, Failed) means the state profile may track multiple possible states. Each `GetJob` poll narrows the possibilities based on the observed response.

2. **Use `stateProfile.StatesAndStepFunctions`**: Check terminal conditions across ALL possible states, not just one. The system is in exactly one state, but the spec doesn't know which one until it observes a distinguishing response.

3. **Reuse the operation's `Polling` config**: Access `operation.Polling.WaitTimeInMs` and `operation.Polling.MaxRetryCount` for consistent bounds.

4. **Response-dependent state**: When the server generates values (e.g., `ResultPath`), use `.ThenState((response, nextState) => ...)` in your operation's `Apply` to capture them on first observation, then enforce stability on subsequent observations.

## Key Points

1. **Always chain StateProfile**: Pass the returned `nextStateProfile` to the next `Allows()` call
2. **Register dependencies before testing**: All services needed by `Execute` methods must be registered
3. **Cleanup before each scenario**: Reset the real system to match the initial state
4. **AllowsConcurrent is for linearizability**: It checks if concurrent responses are consistent with SOME serial ordering, not a specific one
5. **Manual polling is your responsibility**: Auto-generated tests handle polling automatically; manual tests must poll explicitly
