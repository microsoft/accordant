# TodoList-FaultInjection Sample

Demonstrates handling **indefinite failures** — when you don't know if an operation succeeded or failed.

## What It Demonstrates

### 1. Server-Side Fault Injection
- **PreSave faults**: Exception before `SaveChanges()` — operation definitely failed
- **PostSave faults**: Exception after `SaveChanges()` — operation succeeded but client sees 500

### 2. Client-Side Fault Injection  
- Network failures (SocketException) injected at client layer
- Simulates real-world network unreliability

### 3. Indefinite Failure Handling Pattern
- Generic `TodoOperation` base class that automatically wraps outcomes with indefinite failure variants
- Operations use `response?.Data?.Field` pattern — when response is null (indefinite failure), server-generated fields become unknown
- `IndefiniteFailureSemantics.Enabled` toggle to suppress during exploration, enable during execution

### 4. Server-Generated Timestamps
- `CreatedAt` and `ModifiedAt` are server-generated
- On indefinite failure, timestamps become `null` (unknown)
- Subsequent `Get` operations can disambiguate by learning actual values

## Key Pattern

```csharp
// Operations use response?.Data?.Field for graceful null handling
.ThenState((response, next) => {
    next.Users[userId] = new UserState {
        Name = request.Name,
        CreatedAt = response?.Data?.CreatedAt,   // null if response is null
        ModifiedAt = response?.Data?.ModifiedAt  // null if response is null
    };
});
```

## Running

```bash
cd TodoList-FaultInjection.Tests
dotnet test
```

## See Also

- [Indefinite Failures Concept](../../docs/concepts/indefinite-failures.md)
- [Fault Injection Testing How-To](../../docs/how-to/fault-injection-testing.md)
