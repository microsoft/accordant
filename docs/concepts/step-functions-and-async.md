# Step Functions and Async Operations

> **TL;DR**: Step functions model state changes that happen outside of API calls — background processing that eventually completes. Accordant handles both the modeling (`.Triggers()`) and the testing (polling until completion).

---

## The Problem: Background Work

Some operations don't finish when the API call returns. You call an endpoint to create a job, it immediately returns "Pending," and then — somewhere in the background — work happens that eventually moves the job to "Completed" or "Failed."

This presents a challenge. Operations model atomic transitions: you call an API, the state changes, you get a response. One request, one transition. But background processing isn't atomic — the system independently moves through a series of transitions over time, without any client involvement. And none of these transitions are triggered by an API call. The state just... changes on its own.

How do you model that?

---

## A Concrete Example

Consider a job queue API:

```
PUT /api/jobs/job123
→ 200 OK { jobId: "job123", status: "Pending" }

// ... time passes, background processing happens ...

GET /api/jobs/job123  
→ 200 OK { jobId: "job123", status: "Completed" }
```

The transition from `Pending` to `Completed` happened without any client action. The server did it in the background.

In Accordant, you model this with a **step function** — but the most common pattern has a convenient shorthand. Here's what it looks like:

```csharp
return Expect.That(r => r.Data.Status == JobStatus.Pending)
       .ThenState(next => next.Jobs[jobId] = JobStatus.Pending)
       .Triggers(AsyncOperation.Create<JobQueueState>(
           isTerminal: s => s.Jobs[jobId] != JobStatus.Pending,
           transitions: new Action<JobQueueState>[] {
               next => next.Jobs[jobId] = JobStatus.Completed,
               next => next.Jobs[jobId] = JobStatus.Failed
           }
       ));
```

The `.Triggers(...)` says: "After this operation returns, background work starts that can change the state."

Let's break down what `AsyncOperation.Create` does.

---

## AsyncOperation.Create: The Two Key Parameters

The `isTerminal` parameter is a predicate that returns true when the background work is done. In our example, that's when the job status is no longer `Pending`. The job could be `Completed`, it could be `Failed` — either way, the background work has finished.

The `transitions` parameter is an array of possible outcomes. Each element describes one way the state could change. Here we have two: the job could complete successfully, or it could fail. These represent **non-determinism** — we don't know which will happen until we observe the actual system behavior.

When you later call `GetJob` and see status `Completed`, the spec learns which branch actually occurred. Until then, the spec tracks both possibilities.

---

## This Is Just Modeling

It's worth emphasizing: `AsyncOperation.Create` is purely declarative. You're describing what *can* happen — not executing anything. The spec says: "after this operation, background work starts, and it could end in any of these states."

No background threads are running. No async work is happening. You're just telling Accordant about the system's behavior so it knows what to expect during testing.

---

## Testing: What About Polling?

When you actually run tests, the framework needs to wait for background work to complete. You can't validate the final state until the system gets there.

If you're using `spec.RunTests()`, configure polling on the operation:

```csharp
public class CreateJobOperation : Operation<string, ApiResult<Job>, JobQueueState>
{
    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "GetJob",      // Which operation to poll
        WaitTimeInMs = 100,        // Wait between polls
        MaxRetryCount = 100        // Maximum attempts
    };
    
    // ...
}
```

The framework handles the rest. It calls the polling operation repeatedly, validates each response against the spec, and checks whether all possible states have reached their terminal condition. Once they have, it moves on.

See [Tutorial 6: Async Operations & Polling](../tutorials/06-async-operations-polling.md) for a complete walkthrough of the polling pattern.

---

## Writing Polling by Hand

If you're writing tests manually instead of using `spec.RunTests()`, you write your own polling loop. The logic is the same — you're just doing it explicitly:

```csharp
var stateProfile = new StateProfile(initialState);

// Execute CreateJob
var createResponse = await system.CreateJob(jobId);
(bool isValid, string message, stateProfile) = 
    spec.Allows(spec.CreateJob, jobId, createResponse, stateProfile);
Assert.IsTrue(isValid, message);

// Poll until complete
for (int i = 0; i < 100; i++)
{
    await Task.Delay(100);
    var getResponse = await system.GetJob(jobId);
    (isValid, message, stateProfile) = 
        spec.Allows(spec.GetJob, jobId, getResponse, stateProfile);
    Assert.IsTrue(isValid, message);
    
    // Check if all possible states have reached terminal
    bool isTerminal = stateProfile.StatesAndStepFunctions
        .All(ssf => CreateJobOperation.IsTerminal((JobQueueState)ssf.State, jobId));
    if (isTerminal)
        break;
}
```

Notice the terminal check: we look at *all* possible states in the state profile. Because of non-determinism, there might be multiple candidate states. We only stop polling when every candidate has reached a terminal condition.

The framework just automates this loop for you.

---

## Liveness: Does the System Make Progress?

Conformance testing checks **safety**: the system never does something wrong. Each response must match what the spec allows.

But there's another kind of correctness: **liveness**. Does the system eventually make progress? A job that stays `Pending` forever is wrong, even if no individual response violates the spec.

The `MaxRetryCount` in PollingSetup acts as a liveness check. If the framework polls 100 times and the background work still hasn't finished, the test fails:

```
Liveness violation: job still Pending after 100 retries
```

This catches "stuck" systems — a real bug that conformance testing alone wouldn't find. The system isn't returning wrong responses; it's just not making progress.

---

## The General Concept: Step Functions

`AsyncOperation.Create` is a convenient shorthand. Internally, it creates something called a **step function**.

A step function is the general mechanism for modeling autonomous state evolution — changes that happen outside of any API call. The core idea is simple: from a given state, a step function can produce zero, one, or many next states.

```csharp
public interface IStepFunction
{
    IList<StepResult> Apply(State state, ...);
}
```

When `Apply` runs, it looks at the current state and returns a list of possible outcomes. Each outcome is a `StepResult` containing the next state and optionally more step functions to run.

Here's the key insight: a step function is **consumed** when it runs. But it can reproduce itself if it does not want to be effectively consumed. Each `StepResult` can include step functions that will run in that next state — including the same step function again.

Think of it like a recursive process. The step function runs, produces some next states, and those states might include the same step function to run again later. This continues until the step function decides not to include itself in the results.

For our job queue, the step function runs once and produces two possible outcomes (Completed or Failed). Neither includes the step function again, so the background work is modeled as "done."

For a daemon that runs continuously, the step function would include itself in every result, reproducing indefinitely.

---

## TerminatingStepFunction: Background Work That Finishes

Most async operations eventually complete. For these, inherit from `TerminatingStepFunction`:

```csharp
public abstract class TerminatingStepFunction : BaseStepFunction
{
    /// Returns true when the background work is complete.
    public abstract Func<State, bool> IsTerminalState { get; }
    
    /// Define the possible state transitions (only called when not terminal).
    protected abstract IList<StepResult> GetStepResults(State state);

    /// The framework calls this. It handles the terminal check automatically.
    protected sealed override IList<StepResult> ApplyInternal(State state)
    {
        // Already terminal? Nothing to do — stop evolving.
        if (IsTerminalState(state))
        {
            return null;
        }

        return GetStepResults(state);
    }
}
```

The key is `ApplyInternal`. When the framework applies this step function, it first checks `IsTerminalState`. If we've reached the terminal condition, it returns null — no more transitions, the step function is done. Otherwise, it calls `GetStepResults` to get the possible next states.

`IsTerminalState` is the predicate that returns true when the work is done. `GetStepResults` defines how the state transitions until we reach that terminal condition.

When you use `AsyncOperation.Create`, it builds a `TerminatingStepFunction` for you. The `isTerminal` parameter becomes `IsTerminalState`, and the `transitions` parameter defines `GetStepResults`.

Here's what `AsyncOperation.Create` actually produces — a thin wrapper around `TerminatingStepFunction`:

```csharp
internal sealed class AsyncOperation<TState> : TerminatingStepFunction where TState : State
{
    private readonly Func<TState, bool> _isTerminal;
    private readonly Action<TState>[] _transitions;

    public override Func<State, bool> IsTerminalState => 
        state => _isTerminal((TState)state);

    protected override IList<StepResult> GetStepResults(State state)
    {
        // Each transition gets its own cloned state
        var results = new List<StepResult>();
        foreach (var transition in _transitions)
        {
            var nextState = (TState)state.Clone();
            transition(nextState);
            results.Add(new StepResult { State = nextState });
        }
        return results;
    }
}
```

The `isTerminal` predicate you pass becomes `IsTerminalState`. Each action in `transitions` becomes one possible next state in `GetStepResults`. No magic — just a convenience wrapper.

The framework uses `IsTerminalState` in two places. During test execution, polling continues until `IsTerminalState` returns true for all candidate states. During test generation, the framework "unwinds" step functions by repeatedly applying them until they terminate, so test cases represent complete scenarios.

---

## Non-Terminating Step Functions

Not all background work finishes. Some processes run continuously — garbage collectors, event listeners, daemons.

For these, use `BaseStepFunction` directly:

```csharp
public class GarbageCollectorStepFunction : BaseStepFunction
{
    protected override IList<StepResult> ApplyInternal(State state)
    {
        var nextState = state.Clone();
        // Clean up some expired items
        nextState.ExpiredItems.Clear();
        
        return new List<StepResult> 
        {
            new StepResult 
            { 
                State = nextState,
                StepFunctions = new List<IStepFunction> { this } // Reproduce itself
            }
        };
    }
}
```

Notice how the step function includes itself in the result. It will run again in the next state, and again, indefinitely. This models ongoing background activity with no terminal condition.

## When to Use What

Most of the time, you'll use `AsyncOperation.Create`. It covers the common case: an operation triggers background work, that work eventually finishes, and you want to model the possible outcomes.

```csharp
.Triggers(AsyncOperation.Create<MyState>(
    isTerminal: s => s.Job.Status != "Pending",
    transitions: new Action<MyState>[] {
        next => next.Job.Status = "Completed",
        next => next.Job.Status = "Failed"
    }
))
```

If you need more control — custom logic, multiple step functions working together, or non-terminating background processes — inherit from `TerminatingStepFunction` or `BaseStepFunction` directly.

The pattern scales from simple "fire and eventually complete" async operations to complex distributed system behaviors. Start with `AsyncOperation.Create`, and reach for the base classes when you need them.

---

## Summary

| Concept | Description |
|---------|-------------|
| **Step function** | Models autonomous state evolution outside API calls |
| **AsyncOperation.Create** | Convenient shorthand for terminating async work |
| **isTerminal** | Predicate defining when background work is done |
| **transitions** | Possible outcomes (non-deterministic branches) |
| **PollingSetup** | Configuration for automatic polling during test execution |
| **Liveness** | Ensuring the system eventually makes progress |
| **TerminatingStepFunction** | Base class for background work that completes |

---

## Next Steps

- [Tutorial 6: Async Operations & Polling](../tutorials/06-async-operations-polling.md) — hands-on walkthrough with the JobQueue sample
- [Conformance Testing](conformance-testing.md) — how state profiles work in validation
