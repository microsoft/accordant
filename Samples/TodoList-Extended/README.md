# TodoList-Extended Sample

Extended todo list demonstrating response-dependent state and request derivations.

## What It Demonstrates

### 1. Response-Dependent State (Timestamps)
- Server generates `CreatedAt` and `ModifiedAt` timestamps
- Use `ThenState` with lambda + mock to capture server-generated values
- Model validates timestamps are consistent

### 2. Server-Generated IDs + Request Derivations
- Server generates `TodoId` (client doesn't know it ahead of time)
- Use `ConfigureDerivations` to generate GetTodo/CompleteTodo/DeleteTodo requests from CreateTodo responses
- Enables testing operations that depend on server-generated identifiers

## Key Code

```csharp
// Response-dependent state: capture server timestamps
return Expect.That<ApiResult<User>>(r => r.IsSuccess && ...)
    .ThenState(
        (response, next) => {
            next.Users[userId] = new UserState {
                CreatedAt = response.Data.CreatedAt,  // From server
                ModifiedAt = response.Data.ModifiedAt
            };
        },
        mock: () => new ApiResult<User> { ... });

// Derivations: generate requests from prior responses  
spec.ConfigureDerivations("GetTodo",
    DeriveFrom<ApiResult<Todo>, (string, string)>.Response("CreateTodo",
        response => (response.Data.UserId, response.Data.TodoId)));
```

## Running

```bash
cd TodoList-Extended.Tests
dotnet test
```

## See Also

- [Response-Dependent State Tutorial](../../docs/tutorials/03-response-dependent-state.md)
- [Request Derivations Concept](../../docs/concepts/request-derivations.md)
