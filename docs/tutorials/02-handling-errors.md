# Tutorial 2: Handling Errors

In Tutorial 1, you learned to handle error cases like "user not found" by returning different expected outcomes. But what about operations that **throw exceptions**? This tutorial shows you how to specify and test exception-throwing behavior.

**Time:** 10-15 minutes

**What you'll learn:**
- Using `Expect.Throws<TException>()` for exception expectations
- Structuring operations with multiple error paths
- Testing that errors are handled correctly

**Prerequisites:**
- Completed [Tutorial 1: Your First Spec](01-your-first-spec.md)

---

## The Scenario

Our TodoList API has a business rule: **Deleting a user cascades to their todos.** But what if we want to add a rule that **you can't delete a user who has incomplete todos**?

Let's add this validation to demonstrate exception handling.

---

## Adding Validation That Throws

First, let's imagine the API throws a `BusinessRuleException` when you try to delete a user with incomplete todos:

```csharp
// In the API implementation
public async Task DeleteUserAsync(string userId)
{
    var user = await _db.Users.Include(u => u.Todos).FirstOrDefaultAsync(u => u.UserId == userId);
    if (user == null) throw new NotFoundException($"User '{userId}' not found");
    
    if (user.Todos.Any(t => !t.Completed))
        throw new BusinessRuleException($"Cannot delete user '{userId}' with incomplete todos");
    
    _db.Users.Remove(user);
    await _db.SaveChangesAsync();
}
```

---

## Specifying Exception Expectations

In your spec, use `Expect.Throws<TException>()`:

```csharp
spec.Operation<string, Unit>("DeleteUser", (userId, state) =>
{
    // Case 1: User doesn't exist
    if (!state.Users.TryGetValue(userId, out var user))
    {
        return Expect.Throws<NotFoundException>(
                   $"Should throw NotFoundException because user '{userId}' doesn't exist")
               .SameState();
    }

    // Case 2: User has incomplete todos
    var hasIncompleteTodos = user.Todos.Values.Any(t => !t.Completed);
    if (hasIncompleteTodos)
    {
        return Expect.Throws<BusinessRuleException>(
                   $"Should throw BusinessRuleException because user '{userId}' has incomplete todos")
               .SameState();
    }

    // Case 3: Can delete
    return Expect.That<Unit>(r => true, "Should succeed")
           .ThenState<AppState>(nextState =>
               nextState.Users.Remove(userId));
});
```

### Key Points

1. **`Expect.Throws<T>(explanation)`** - Expects the operation to throw exception type `T`
2. **Exceptions don't change state** - Use `.SameState()` (the operation failed)
3. **Order matters** - Check conditions in the same order as the implementation

---

## The Unit Type

Notice we used `Unit` as the response type. When an operation returns `void` or `Task` (no value), use `Unit`:

```csharp
spec.Operation<string, Unit>("DeleteUser", (userId, state) => { ... });

// In binding:
spec.ExecuteWith<TodoApiClient>()
    .BindAsync<string, Unit>("DeleteUser", async (client, userId) =>
    {
        await client.DeleteUserAsync(userId);
        return Unit.Value;  // Explicit return for void operations
    });
```

---

## Testing the Error Cases

Your test inputs should now cover these paths:

```csharp
var inputs = new InputSet()
{
    // Setup
    createUser.With(new User("alice", "Alice"), "Create Alice"),
    createTodo.With(new Todo("alice", "task-1", "Incomplete task"), "Create todo"),
    
    // This should throw - user has incomplete todo!
    deleteUser.With("alice", "Try to delete Alice (has incomplete todo)"),
    
    // After completing the todo, delete should work
    completeTodo.With(("alice", "task-1"), "Complete the todo"),
    deleteUser.With("alice", "Delete Alice (now allowed)"),
    
    // And of course, deleting non-existent user
    deleteUser.With("bob", "Delete non-existent user"),
};
```

Accordant will generate sequences that test all these paths:

```
CreateUser → CreateTodo → DeleteUser  (throws BusinessRuleException ✓)
CreateUser → CreateTodo → CompleteTodo → DeleteUser  (succeeds ✓)
DeleteUser  (throws NotFoundException ✓)
CreateUser → DeleteUser  (succeeds - no todos ✓)
```

---

## Multiple Exception Types

An operation can have multiple error paths with different exceptions:

```csharp
spec.Operation<TransferRequest, Unit>("TransferMoney", (request, state) =>
{
    // Different errors, different exceptions
    if (!state.Accounts.ContainsKey(request.FromAccount))
        return Expect.Throws<AccountNotFoundException>("Source account not found")
               .SameState();

    if (!state.Accounts.ContainsKey(request.ToAccount))
        return Expect.Throws<AccountNotFoundException>("Target account not found")
               .SameState();

    var balance = state.Accounts[request.FromAccount].Balance;
    if (request.Amount > balance)
        return Expect.Throws<InsufficientFundsException>(
                   $"Cannot transfer {request.Amount} - only {balance} available")
               .SameState();

    if (request.Amount <= 0)
        return Expect.Throws<ArgumentException>("Amount must be positive")
               .SameState();

    // Success case
    return Expect.That<Unit>(r => true, "Transfer succeeded")
           .ThenState<AppState>(nextState =>
           {
               nextState.Accounts[request.FromAccount].Balance -= request.Amount;
               nextState.Accounts[request.ToAccount].Balance += request.Amount;
           });
});
```

---

## Combining Exceptions and Status Codes

In REST APIs, you might have both:
- HTTP status codes (404, 409, etc.)
- Exceptions for truly exceptional cases

Your spec can handle both:

```csharp
spec.Operation<string, ApiResult<User>>("GetUser", (userId, state) =>
{
    if (!state.Users.ContainsKey(userId))
    {
        // API returns 404 status code (not an exception)
        return Expect.That<ApiResult<User>>(r => r.IsNotFound,
                   "Should return 404 Not Found")
               .SameState();
    }

    // Success
    return Expect.That<ApiResult<User>>(r => r.IsSuccess && r.Data != null,
               "Should return user")
           .SameState();
});

spec.Operation<Todo, ApiResult<Todo>>("CreateTodo", (request, state) =>
{
    // Validation errors might throw in some APIs
    if (string.IsNullOrEmpty(request.Title))
    {
        return Expect.Throws<ArgumentException>("Title is required")
               .SameState();
    }
    
    // Business logic uses status codes
    if (!state.Users.ContainsKey(request.UserId))
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                   "User doesn't exist")
               .SameState();
    }

    // ... rest of operation
});
```

---

## Summary

You've learned how to handle exceptions in Accordant:

| Pattern | When to Use |
|---------|-------------|
| `Expect.That<T>(predicate)` | Normal response validation |
| `Expect.Throws<TException>()` | Operation should throw |
| `Unit` response type | Operations returning void |

### The Key Insight

Exceptions are just another expected outcome. The spec describes **all possible outcomes**—success, error status codes, and exceptions. Accordant generates tests that exercise all paths.

---

## What's Next?

- **[Tutorial 3: Response-Dependent State](03-response-dependent-state.md)** - Handle server-generated values like timestamps and IDs
- **[Tutorial 4: Visualizing State Space](04-visualizing-state-space.md)** - See what Accordant is actually testing

---

## Full Code Reference

See the complete handling in:
- [TodoListTests.cs](https://github.com/microsoft/accordant/blob/main/Samples/TodoList/TodoList.Tests/TodoListTests.cs) - Error handling examples
