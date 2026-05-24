# JobQueue Sample

An async job processing system demonstrating **step functions and polling**.

## What It Demonstrates

### Async Operations with Step Functions
- `CreateJob` returns immediately with `Pending` status
- Background processing eventually completes (or fails)
- `GetJob` polls for completion

### Server-Generated Results
- `ResultPath` is `null` while job is `Pending`
- Once `Completed`, `ResultPath` is set and stable
- Response-dependent state captures the server-generated path

### Temporal Properties
- Status transitions: `Pending` → `Completed` or `Failed`
- `ResultPath` stability: once set, never changes
- Polling with derivations from prior responses

## Key Pattern

```csharp
// Step function for async completion
spec.Operation<string, ApiResult<Job>>("GetJob", (jobId, state) =>
{
    var job = state.Jobs[jobId];
    
    if (job.Status == JobStatus.Pending)
    {
        return Expect.OneOf(
            // Still pending
            Expect.That<ApiResult<Job>>(r => r.Data.Status == JobStatus.Pending)
                .SameState(),
            // Completed - capture ResultPath
            Expect.That<ApiResult<Job>>(r => r.Data.Status == JobStatus.Completed)
                .ThenState((response, next) => {
                    next.Jobs[jobId].Status = JobStatus.Completed;
                    next.Jobs[jobId].ResultPath = response.Data.ResultPath;
                })
        );
    }
    // ...
});
```

## Running

```bash
cd JobQueue.Tests
dotnet test
```

## See Also

- [Async Operations & Polling Tutorial](../../docs/tutorials/06-async-operations-polling.md)
- [Step Functions Concept](../../docs/concepts/step-functions-and-async.md)
