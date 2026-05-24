# TodoList-FaultInjection Sample

Demonstrates handling **indefinite failures** — when you can't tell if an operation succeeded or failed.

## What Are Indefinite Failures?

When you call `CreateUser("alice")` and get a **500 error** or **network timeout**, you don't know:
- Did the server crash *before* saving? → Alice doesn't exist
- Did the server crash *after* saving? → Alice exists, but you lost the response

Both are valid possibilities. Your tests must handle both.

## The Challenge

Without proper modeling, your tests would fail randomly:
```
CreateUser("alice") → 500 error
GetUser("alice")    → 200 OK with alice  ← "Unexpected! We thought she didn't exist!"
```

The test expected alice to not exist (because Create "failed"), but she actually does exist (because the failure was *after* the save).

## The Solution: Model Both Possibilities

When an indefinite failure occurs, the model tracks **both** possible states:
- **Branch 1**: User doesn't exist (request lost before save)
- **Branch 2**: User exists but timestamps are unknown (response lost after save)

Later operations succeed as long as the response matches **at least one** possible state. Branches that don't match are pruned.

---

## How This Sample Works

### Part 1: Modeling (The Base Class Pattern)

The `TodoOperation` base class **automatically wraps** every operation with indefinite failure outcomes:

```csharp
// You write this (happy path only):
protected override ExpectedOutcomes ApplyInternal(User request, AppState state)
{
    return Expect.That<ApiResult<User>>(r => r.IsSuccess)
        .ThenState((response, next) => {
            next.Users[request.UserId] = new UserState {
                Name = request.Name,
                CreatedAt = response?.Data?.CreatedAt,   // null if no response
                ModifiedAt = response?.Data?.ModifiedAt
            };
        });
}

// Base class automatically adds these outcomes:
// 1. Indefinite failure, no state change (request never reached server)
// 2. Indefinite failure, user created with unknown timestamps (response lost)
```

**Key insight**: `response?.Data?.CreatedAt` — when the base class simulates an indefinite failure, it passes `null` as the response. The `?.` operator naturally produces `null` for server-generated fields.

### Part 2: Fault Injection (Making Failures Actually Happen)

Modeling is one thing. Actually *causing* failures is another.

**Server-side faults** (in the API):
```csharp
_context.SaveChanges();

// FAULT POINT: If this throws, user WAS created but client sees 500
_context.MaybeInjectPostSaveFault("CreateUser");

return Ok(user);  // Never reached if fault injected
```

**Client-side faults** (network layer):
```csharp
// Before request: simulate network failure
if (_random.NextDouble() < _config.PreRequestFaultProbability)
    throw new SocketException(ConnectionReset);

var result = await operation();

// After response: simulate losing the response
if (result.IsSuccess && _random.NextDouble() < _config.PostResponseFaultProbability)
    throw new SocketException(ConnectionReset);
```

---

## Concrete Example: The Full Flow

### Scenario: CreateUser followed by GetUser

**Step 1: CreateUser("alice", "Alice Smith")**

The base class produces these outcomes:
1. ✅ Success → alice exists with timestamps from response
2. ⚠️ Indefinite failure, no change → alice doesn't exist
3. ⚠️ Indefinite failure, user created → alice exists, timestamps = null

**Step 2: Fault is injected (PostSave)**

Server saves alice, then throws. Client sees 500 error.
- Response matches outcome #2 or #3 (both expect `IsIndefiniteFailure`)
- Model now tracks **two possible states**

**Step 3: GetUser("alice")**

Response: `200 OK, alice exists`
- Branch 1 (alice doesn't exist) → **pruned** (expected 404, got 200)
- Branch 2 (alice exists, timestamps unknown) → **matches!** Learn timestamps from response

The non-matching branch is pruned. Testing continues with the remaining state(s).

---

## Key Files

| File | Purpose |
|------|---------|
| [TodoOperationBase.cs](TodoList-FaultInjection.Tests/Operations/TodoOperationBase.cs) | Base class that wraps outcomes |
| [UserOperations.cs](TodoList-FaultInjection.Tests/Operations/UserOperations.cs) | CreateUser, GetUser implementations |
| [FaultInjectingApiClient.cs](TodoList-FaultInjection.Tests/FaultInjectingApiClient.cs) | Client-side fault injection |
| [FaultInjectingDbContext.cs](TodoList-FaultInjection.Api/Data/FaultInjectingDbContext.cs) | Server-side fault injection |

## Running

```bash
cd TodoList-FaultInjection.Tests
dotnet test
```

## See Also

- [Indefinite Failures Concept](../../docs/concepts/indefinite-failures.md)
- [Fault Injection Testing How-To](../../docs/how-to/fault-injection-testing.md)
