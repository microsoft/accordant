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

**What you write** — just the definite outcomes:

```csharp
public class CreateUserOperation : TodoApiOperation<User, User, AppState>
{
    protected override ExpectedOutcomes ApplyInternal(User request, AppState state)
    {
        // 409 Conflict if user already exists
        if (state.Users.ContainsKey(request.UserId))
        {
            return Expect.That((ApiResult<User> r) => r.IsConflict)
                .SameState();
        }

        // 200 OK - create the user
        return Expect.That((ApiResult<User> r) => r.IsSuccess && r.Data?.UserId == request.UserId)
            .ThenState((response, next) =>
            {
                next.Users[request.UserId] = new UserState
                {
                    Name = request.Name,
                    CreatedAt = response?.Data?.CreatedAt,
                    ModifiedAt = response?.Data?.ModifiedAt
                };
            });
    }
}
```

**What the base class adds** — indefinite failure outcomes:

```csharp
public sealed override ExpectedOutcomes Apply(TRequest request, TState state)
{
    var baseOutcomes = ApplyInternal(request, state);
    
    // Add: indefinite failure with NO state change (request lost before save)
    newOutcomes.Add(new ExpectedOutcome(
        IndefiniteFailureValidator,
        state));  // same state

    // Add: indefinite failure WITH state change (saved but response lost)
    // Reuses the same state transition, but with IndefiniteFailureValidator
    foreach (var outcome in baseOutcomes.PossibleOutcomes)
    {
        newOutcomes.Add(new ExpectedOutcome(
            IndefiniteFailureValidator,
            outcome.NextStateGenerator));  // same state change as success
    }
    
    return new ExpectedOutcomes(newOutcomes);
}
```

The result: CreateUser now has **three** possible outcomes when `IndefiniteFailureSemantics.Enabled`:
1. Success (200) → user exists with known timestamps
2. Indefinite failure → no state change
3. Indefinite failure → user exists with `null` timestamps

### Part 2: Fault Injection (Making Failures Actually Happen)

Modeling is one thing. Actually *causing* failures is another. This sample demonstrates one approach — use whatever works best for your project (chaos engineering tools, test doubles, network proxies, etc.).

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

**CreateUser("alice", "Alice Smith")**

Model produces three possible outcomes:
1. ✅ Success → alice exists with timestamps from response
2. ⚠️ Indefinite failure → alice doesn't exist
3. ⚠️ Indefinite failure → alice exists, timestamps = null

Server decides to return 500 (via fault injection). Response matches #2 or #3 — both expect `IsIndefiniteFailure`. Model now tracks **two possible states**.

**GetUser("alice")**

Server returns `200 OK, alice exists`.

- State where alice doesn't exist → **pruned** (expected 404, got 200)
- State where alice exists → **matches!** Timestamps learned from response

Testing continues with the remaining state(s).

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
