# Tutorial 3: Response-Dependent State

Sometimes the server returns values that you need to track—like timestamps, ETags, or server-generated IDs. You can't know these values ahead of time, but subsequent operations depend on them. This tutorial shows you how to handle **response-dependent state**.

**Time:** 15-20 minutes

**What you'll learn:**
- Using `ThenState` with a response lambda
- Providing mock responses for state exploration
- Tracking server-generated values

**Prerequisites:**
- Completed [Tutorial 1](01-your-first-spec.md) and [Tutorial 2](02-handling-errors.md)

---

## The Problem

Let's add a `LastModified` timestamp to our todos. The server sets this automatically:

```json
{
  "todoId": "task-1",
  "title": "Buy milk",
  "completed": false,
  "lastModified": "2024-01-15T10:30:00Z"  // Server-generated!
}
```

When we create a todo, we don't know what timestamp the server will return. But:
1. We need to **capture** that timestamp
2. Subsequent `GetTodo` calls should **validate** the timestamp matches

---

## The Solution: Response Lambda + Mock

Accordant handles this with a two-part approach:

### Part 1: Capture with Response Lambda

When creating a todo, use a lambda that receives the response and builds the new state:

```csharp
spec.Operation<Todo, ApiResult<Todo>>("CreateTodo", (request, state) =>
{
    if (!state.Users.TryGetValue(request.UserId, out var user))
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                   "User not found")
               .SameState();
    }

    if (user.Todos.ContainsKey(request.TodoId))
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsConflict,
                   "Todo already exists")
               .SameState();
    }

    // Success - but we don't know LastModified yet!
    return Expect.That<ApiResult<Todo>>(
               r => r.IsSuccess &&
                    r.Data != null &&
                    r.Data.TodoId == request.TodoId &&
                    r.Data.LastModified != null,  // Just verify it exists
               "Should create todo with timestamp")
           .ThenState(
               // Lambda receives actual response, builds new state
               (ApiResult<Todo> response) =>
               {
                   var newState = (AppState)state.Clone();
                   newState.Users[request.UserId].Todos[request.TodoId] = new AppState.TodoState
                   {
                       Title = request.Title,
                       Completed = false,
                       LastModified = response.Data!.LastModified  // Capture it!
                   };
                   return newState;
               },
               // Mock for state space exploration (explained below)
               mock: () => new ApiResult<Todo>
               {
                   Data = new Todo(request.UserId, request.TodoId, request.Title)
                   {
                       LastModified = DateTime.UtcNow
                   },
                   StatusCode = 200
               });
});
```

### Part 2: Why the Mock?

Accordant does two things:
1. **State exploration** - Builds the state graph to generate test cases
2. **Test execution** - Runs against your real system

During **exploration**, there's no real server—so we need mock responses. The mock provides realistic values so the state graph is accurate.

During **execution**, the mock is ignored—real responses are used.

---

## Validating Captured Values

Now `GetTodo` can validate the captured timestamp:

```csharp
spec.Operation<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo", (request, state) =>
{
    var todo = state.Users.GetValueOrDefault(request.UserId)
                    ?.Todos.GetValueOrDefault(request.TodoId);

    if (todo == null)
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                   "Todo not found")
               .SameState();
    }

    // Now we can validate the captured LastModified!
    return Expect.That<ApiResult<Todo>>(
               r => r.IsSuccess &&
                    r.Data != null &&
                    r.Data.TodoId == request.TodoId &&
                    r.Data.Title == todo.Title &&
                    r.Data.LastModified == todo.LastModified,  // Must match!
               $"Should return todo with LastModified={todo.LastModified}")
           .SameState();
});
```

---

## Updated State Class

Add `LastModified` to your state:

```csharp
public class AppState : JsonState
{
    public Dictionary<string, UserState> Users { get; set; } = new();

    public class UserState
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, TodoState> Todos { get; set; } = new();
    }

    public class TodoState
    {
        public string Title { get; set; } = string.Empty;
        public bool Completed { get; set; } = false;
        public DateTime? LastModified { get; set; }  // Added!
    }
}
```

---

## Another Example: Server-Generated IDs

The same pattern works for server-generated IDs:

```csharp
// Request doesn't include ID - server generates it
public record CreateOrderRequest(string Product, int Quantity);

spec.Operation<CreateOrderRequest, ApiResult<Order>>("CreateOrder", (request, state) =>
{
    return Expect.That<ApiResult<Order>>(
               r => r.IsSuccess &&
                    r.Data != null &&
                    !string.IsNullOrEmpty(r.Data.OrderId) &&  // Server generates
                    r.Data.Product == request.Product,
               "Should create order with server-generated ID")
           .ThenState(
               (ApiResult<Order> response) =>
               {
                   var newState = (AppState)state.Clone();
                   var orderId = response.Data!.OrderId;  // Capture!
                   newState.Orders[orderId] = new OrderState
                   {
                       Product = request.Product,
                       Quantity = request.Quantity
                   };
                   return newState;
               },
               mock: () => new ApiResult<Order>
               {
                   Data = new Order
                   {
                       OrderId = Guid.NewGuid().ToString(),  // Mock generates ID
                       Product = request.Product,
                       Quantity = request.Quantity
                   },
                   StatusCode = 201
               });
});
```

---

## Temporal Properties: Stability

Some server-generated values should **never change** once set. For example, a result path for a completed job:

```csharp
// First observation - capture the value
if (job.ResultPath == null && response.Data.Status == JobStatus.Completed)
{
    return Expect.That<ApiResult<Job>>(
               r => r.Data.ResultPath != null,
               "Should have a ResultPath")
           .ThenState(
               (ApiResult<Job> resp) =>
               {
                   var newState = (JobQueueState)state.Clone();
                   newState.Jobs[jobId].ResultPath = resp.Data!.ResultPath;  // Capture
                   return newState;
               },
               mock: () => new ApiResult<Job> { /* ... */ });
}

// Subsequent observations - enforce stability
if (job.ResultPath != null)
{
    return Expect.That<ApiResult<Job>>(
               r => r.Data.ResultPath == job.ResultPath,  // Must match exactly!
               $"ResultPath must remain stable: {job.ResultPath}")
           .SameState();
}
```

This is a **temporal property**: once set, the value never changes. Accordant will generate tests that verify this stability.

---

## Summary

Response-dependent state handles values you can't predict:

| Pattern | Use Case |
|---------|----------|
| `ThenState(lambda, mock)` | Capture server-generated values |
| Mock responses | Enable state exploration without real server |
| Stability checks | Enforce values don't change unexpectedly |

### Key Insight

The spec captures reality: "I don't know what timestamp the server will return, but once I see it, I'll remember it and expect it to be consistent."

---

## What's Next?

- **[Tutorial 4: Visualizing State Space](04-visualizing-state-space.md)** - See the state graph Accordant explores
- **[Tutorial 5: Testing Race Conditions](05-testing-race-conditions.md)** - Find concurrency bugs

---

## Full Code Reference

See response-dependent state in:
- [JobQueueTests.cs](https://github.com/microsoft/accordant/blob/main/Samples/JobQueue/JobQueue.Tests/JobQueueTests.cs) - `ResultPath` capture pattern
