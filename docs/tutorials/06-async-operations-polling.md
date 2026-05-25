# Tutorial 6: Async Operations & Polling

Some operations don't complete immediately. You call an API to start a job, it returns "Pending", and the job finishes later in the background.

This creates two challenges:
1. **Modeling**: How do you describe background work that happens outside of any API call?
2. **Testing**: How do you wait for that work to complete before checking the result?

This tutorial builds up the pattern piece by piece.

**Time:** 25 minutes

**What you'll learn:**
- Modeling background work with step functions
- How Accordant tracks "the system could be in one of several states"
- How observing a response narrows down which state we're actually in
- Setting up polling for test execution
- Liveness testing: ensuring background work eventually completes

**Prerequisites:**
- Completed [Tutorial 3: Response-Dependent State](03-response-dependent-state.md)

---

## Part 1: The Simplest Case

Let's start with a job queue where jobs always succeed. No failures, no complications.

### The API

```
PUT /api/jobs/job123
→ 200 OK { jobId: "job123", status: "Pending" }

// ... background processing happens ...

GET /api/jobs/job123  
→ 200 OK { jobId: "job123", status: "Completed" }
```

### The State

```csharp
[State]
public partial class JobQueueState
{
    public Dictionary<string, JobStatus> Jobs { get; set; } = new();
}

public enum JobStatus { Pending, Completed }
```

### The CreateJob Operation

When you create a job, two things happen:
1. The API returns immediately with `Pending`
2. Background work starts that will eventually complete

We can't directly observe the background work — it happens inside the system. But we can **model** it by saying: "after this operation, some background process starts that will eventually change the state."

In Accordant, this is called a **step function**. A step function describes how the system's state can evolve on its own, outside of any API call.

`AsyncOperation.Create<TState>()` is a convenient shorthand for creating a step function inline:

```csharp
spec.Operation<string, ApiResult<Job>>("CreateJob", (jobId, state) =>
{
    if (state.Jobs.ContainsKey(jobId))
    {
        return Expect.That(r => r.IsConflict, "Already exists")
               .SameState();
    }

    return Expect.That(
               r => r.IsSuccess && r.Data.Status == JobStatus.Pending,
               "Returns Pending")
           .ThenState(next => next.Jobs[jobId] = JobStatus.Pending)
           .Triggers(AsyncOperation.Create<JobQueueState>(
               isTerminal: s => s.Jobs[jobId] != JobStatus.Pending,
               transition: next => next.Jobs[jobId] = JobStatus.Completed
           ));
});
```

The `.Triggers(...)` says: "After this operation returns, a background process starts that can change the state."

- **isTerminal**: A predicate that returns true when the background work is done. Here, when the job is no longer `Pending`.
- **transition**: How the state changes when the background work completes. Here, the status becomes `Completed`.

---

## Part 2: The System Could Be In Either State

Here's the key insight. Right after `CreateJob` returns, we know:
- The API said `Pending`
- Background work has started

But we **don't know** if the background work has finished yet. Maybe it completed instantly. Maybe it's still running. We simply can't tell.

So the system could be in either of two states:

```
State A: Jobs = { "job123": Pending }     // Still processing
State B: Jobs = { "job123": Completed }   // Already done
```

Accordant doesn't guess. It tracks **both** possibilities. It keeps both states until we observe something that tells us which one is actually true.

Think of it like this: we're holding two "candidate" states in our head, waiting for evidence.

---

## Part 3: How Observing a Response Narrows It Down

Now we call `GetJob("job123")`. Here's the GetJob spec:

```csharp
spec.Operation<string, ApiResult<Job>>("GetJob", (jobId, state) =>
{
    if (!state.Jobs.TryGetValue(jobId, out var status))
    {
        return Expect.That(r => r.IsNotFound, "Not found")
               .SameState();
    }

    return Expect.That(
               r => r.IsSuccess && r.Data.Status == status,
               $"Returns job with status {status}")
           .SameState();
});
```

Notice what the spec does: it says the response should have `status` matching whatever's in the state. If the state says `Pending`, the response should say `Pending`. If the state says `Completed`, the response should say `Completed`.

### The Matching Process

When the real API returns a response, Accordant checks **each candidate state** to see if it can explain what we observed:

**Say the API returns `{ status: "Completed" }`:**

1. **Check State A (Pending)**  
   The framework runs the GetJob spec with State A.  
   The spec says: "expect response with status = Pending".  
   But we got `Completed`.  
   ❌ **Doesn't match.** State A is eliminated.

2. **Check State B (Completed)**  
   The framework runs the GetJob spec with State B.  
   The spec says: "expect response with status = Completed".  
   We got `Completed`.  
   ✓ **Matches!** State B survives.

**Result:** We now know the system is in State B. The uncertainty is resolved.

### What If We'd Gotten a Different Response?

If the API had returned `{ status: "Pending" }`:

1. **Check State A (Pending)** → Spec expects `Pending`, got `Pending`. ✓ Matches.
2. **Check State B (Completed)** → Spec expects `Completed`, got `Pending`. ❌ Doesn't match.

State A would survive, and we'd know the background work isn't done yet.

### The Key Idea

Every time you observe a response, Accordant asks: *"Which of my candidate states can explain this?"* The framework runs the spec (the `Apply` method) against each candidate state and checks if the observed response satisfies the expected outcome.

States that match survive. States that don't are eliminated. This is how uncertainty gets resolved over time.

### A Subtle Point

Sometimes **more than one** candidate state can explain the same response. In that case, both survive — you haven't fully resolved the uncertainty yet. The next operation might narrow it down further.

This is perfectly normal. You're not always trying to get down to exactly one state; you're just maintaining a consistent picture of what the system *could* be.

---

## Part 4: Polling Until Done

In practice, you want to wait for the job to complete before continuing with your test.

### Modeling vs Testing

It's important to understand the distinction:

- **`.Triggers(...)`** is part of the **model**. It describes what the system does — background work starts after CreateJob returns.
- **`PollingSetup`** is a **test-time construct**. It tells the test runner how to wait for that background work to complete.

If you're writing your own test loop manually, you wouldn't need `PollingSetup` — you'd just call GetJob in a loop yourself. But if you're using Accordant's test execution (`spec.RunTests(...)`), you configure polling so the framework handles it for you.

### Configuring Polling

Tell the framework: "After CreateJob triggers background work, keep calling GetJob until the background work finishes."

```csharp
public class CreateJobOperation : Operation<string, ApiResult<Job>, JobQueueState>
{
    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "GetJob",     // Which operation to call while waiting
        WaitTimeInMs = 100,       // How often to poll
        MaxRetryCount = 100       // Give up after this many attempts
    };
    
    // ... Apply method with step function ...
}
```

The framework will call GetJob repeatedly. After each call, it checks: did the response narrow us down to a state where the background work is finished (`isTerminal` returns true)? If so, stop polling. If not, wait and try again.

### The Problem: What Request Should Polling Use?

CreateJob takes a `jobId`. GetJob also takes a `jobId`. When polling, the framework needs to know: *what `jobId` should I pass to GetJob?*

The answer seems obvious — use the same `jobId` we passed to CreateJob. But the framework doesn't automatically know that. CreateJob and GetJob are separate operations with separate request types. We need to tell the framework how they relate.

### The Solution: Derivations

A **derivation** tells the framework how to build the polling request from the original operation:

```csharp
public class GetJobOperation : Operation<string, ApiResult<Job>, JobQueueState>
{
    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        Derive.From<string, ApiResult<Job>, string>("CreateJob")
              .As((createRequest, createResponse) => createRequest)
    };
    
    // ... Apply method ...
}
```

Let's break this down:

- `Derive.From<...>("CreateJob")` — "When polling after a CreateJob call..."
- `.As((createRequest, createResponse) => createRequest)` — "...use the same request that was passed to CreateJob."

The derivation function receives both the original request and response, so you can derive from either. For example, if CreateJob returned an auto-generated ID:

```csharp
// Derive from the response instead
.As((req, resp) => resp.Data.JobId)
```

---

## Part 5: What If Jobs Can Fail?

So far, jobs always succeed. Let's add reality: jobs might succeed or fail. We don't know ahead of time which will happen.

### Multiple Possible Outcomes

Update the step function to describe both possibilities:

```csharp
.Triggers(AsyncOperation.Create<JobQueueState>(
    isTerminal: s => s.Jobs[jobId] != JobStatus.Pending,
    transitions: new Action<JobQueueState>[]
    {
        next => next.Jobs[jobId] = JobStatus.Completed,
        next => next.Jobs[jobId] = JobStatus.Failed
    }
))
```

Instead of one `transition`, we provide an array of `transitions` — each one describing a possible outcome.

### More Candidate States to Track

Now after CreateJob, the system could be in one of **three** states:
- Still `Pending` (background work hasn't finished yet)
- `Completed` (background work finished successfully)
- `Failed` (background work finished with an error)

The framework tracks all three. When GetJob returns, the response tells us which state is real — using the same matching process from Part 3.

---

## Part 6: Capturing Server-Generated Values

Real jobs often have results — a file path, a computed value, something the server generates. You don't know it ahead of time.

Add a `ResultPath` to the state:

```csharp
public class JobState
{
    public JobStatus Status { get; set; }
    public string? ResultPath { get; set; }  // Server-generated when completed
}
```

The step function sets `Status = Completed` but **can't** set `ResultPath` — we don't know what the server will generate.

### Learning from Responses

Normally, a GET operation doesn't change state — it just reads. But here's a subtle point: when we observe that a job is `Completed` for the first time, we **learn** the `ResultPath` from the response. Our model's knowledge of the system has changed, even though the real system's state hasn't.

So GetJob needs to transition to a new state that captures what we learned. This is **response-dependent state** (from Tutorial 3).

Note that even within "Completed", we have two situations:
- `Completed` with `ResultPath == null` — we know it's done, but haven't observed the result yet
- `Completed` with `ResultPath` captured — we've seen it and recorded the value

These are different candidate states, and GetJob handles them differently:

```csharp
if (job.Status == JobStatus.Completed && job.ResultPath == null)
{
    // First time seeing Completed - capture the ResultPath from the response
    return Expect.That(
               r => r.IsSuccess && 
                    r.Data.Status == JobStatus.Completed &&
                    !string.IsNullOrEmpty(r.Data.ResultPath),
               "Completed with a ResultPath")
           .ThenState(
               (ApiResult<Job> resp, JobQueueState next) => {
                   next.Jobs[jobId].ResultPath = resp.Data.ResultPath;
               },
               mock: () => new ApiResult<Job> {
                   Data = new Job(jobId, JobStatus.Completed, "/mock/path"),
                   StatusCode = 200
               });
}
```

After this, `ResultPath` is captured. Subsequent GetJob calls verify it stays the same:

```csharp
if (job.Status == JobStatus.Completed && job.ResultPath != null)
{
    // Already captured - verify stability
    return Expect.That(
               r => r.Data.ResultPath == job.ResultPath,
               $"ResultPath should still be {job.ResultPath}")
           .SameState();
}
```

---

## Part 7: Liveness Testing

What if a bug causes jobs to stay `Pending` forever? The background work might hang, or never start at all.

This is called a **liveness** problem. Unlike a *safety* bug ("the system did something wrong"), a liveness bug means "the system failed to make progress." The job should eventually complete, but it never does.

### Testing Liveness

The `MaxRetryCount` in PollingSetup acts as a liveness check:

```csharp
Polling = new PollingSetup
{
    MaxRetryCount = 100  // After 100 polls, if still Pending, fail the test
};
```

The framework polls GetJob up to 100 times. If after all those attempts the background work still hasn't finished (we're still in `Pending` candidate states), the test fails with a liveness error.

This catches bugs where:
- Background work hangs indefinitely
- Background work never starts
- A race condition prevents completion
- The `isTerminal` predicate is wrong and never returns true

### Liveness Without PollingSetup

If you're writing tests manually (not using `spec.RunTests()`), you'd implement liveness checking yourself — for example, with a timeout or retry limit in your own polling loop. The concept is the same: fail if the system doesn't make progress within some bound.

---

## Putting It All Together

Here's the complete CreateJob operation:

```csharp
public class CreateJobOperation : Operation<string, ApiResult<Job>, JobQueueState>
{
    public CreateJobOperation() : base("CreateJob") { }

    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "GetJob",
        WaitTimeInMs = 100,
        MaxRetryCount = 100
    };

    public override ExpectedOutcomes Apply(string jobId, JobQueueState state)
    {
        if (state.Jobs.ContainsKey(jobId))
        {
            return Expect.That(r => r.IsConflict, "Already exists")
                   .SameState();
        }

        return Expect.That(
                   r => r.IsSuccess && 
                        r.Data.Status == JobStatus.Pending &&
                        r.Data.ResultPath == null,
                   "Returns Pending with no result yet")
               .ThenState(next => next.Jobs[jobId] = new JobState 
               { 
                   Status = JobStatus.Pending 
               })
               .Triggers(AsyncOperation.Create<JobQueueState>(
                   isTerminal: s => s.Jobs[jobId].Status != JobStatus.Pending,
                   transitions: new Action<JobQueueState>[]
                   {
                       next => next.Jobs[jobId].Status = JobStatus.Completed,
                       next => next.Jobs[jobId].Status = JobStatus.Failed
                   }
               ));
    }
}
```

And the spec ties it together:

```csharp
public class JobQueueSpec : Spec<JobQueueState>
{
    public CreateJobOperation CreateJob { get; } = new();
    public GetJobOperation GetJob { get; } = new();

    public JobQueueSpec()
    {
        // Automatically registers all operation properties
        RegisterOperationProperties();
    }
}
```

(The `RegisterOperationProperties()` call automatically finds and registers all `Operation` properties on the spec class.)

---

## Summary

| Concept | What it does |
|---------|-------------|
| **Step function** | Models background work that can change state outside of any API call |
| **AsyncOperation.Create** | Shorthand for creating a step function inline |
| **isTerminal** | Predicate: is the background work done? |
| **transition(s)** | How state changes when background work finishes — provide multiple for multiple possible outcomes |
| **Candidate states** | After triggering background work, the framework tracks all states the system *could* be in |
| **Matching** | When you observe a response, the framework runs the spec against each candidate state; only states that can explain the response survive |
| **PollingSetup** | Test-time construct that configures automatic polling |
| **Derivation** | Tells the framework how to build the polling request from the original request/response |
| **Liveness testing** | Ensuring background work eventually completes; fails if the system doesn't make progress |

---

## What's Next?

- **[Concept: Step Functions](../concepts/step-functions.md)** - Deeper dive into the model
- **[How-To: Model Async Background Processing](../how-to/model-async-background-processing.md)** - More patterns

---

## Full Code Reference

See the complete JobQueue sample:
- [JobQueueTests.cs](https://github.com/microsoft/accordant/blob/main/Samples/JobQueue/JobQueue.Tests/JobQueueTests.cs) - Complete spec
- [JobsController.cs](https://github.com/microsoft/accordant/blob/main/Samples/JobQueue/JobQueue.Api/Controllers/JobsController.cs) - The API
