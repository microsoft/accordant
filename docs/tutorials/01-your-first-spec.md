# Tutorial 1: Your First Spec

In this tutorial, you'll build a complete Accordant specification for a **TodoList REST API**. By the end, you'll understand the core concepts and have hundreds of automatically generated tests.

**Time:** 20-30 minutes

**What you'll learn:**
- How to define state
- How to define operations with `Expect`
- How to bind operations to a real HTTP API
- How to run auto-generated tests

**Prerequisites:**
- Familiar with basic C# (see the [Overview](../index.md) for an introduction)
- The TodoList sample project (we'll guide you through it)

---

## The System Under Test

We're testing a TodoList REST API with these endpoints:

| Endpoint | Description |
|----------|-------------|
| `PUT /api/users/{userId}` | Create a user |
| `GET /api/users/{userId}` | Get a user |
| `DELETE /api/users/{userId}` | Delete a user (cascades to todos) |
| `PUT /api/users/{userId}/todos/{todoId}` | Create a todo |
| `GET /api/users/{userId}/todos/{todoId}` | Get a todo |
| `POST /api/users/{userId}/todos/{todoId}/complete` | Mark todo as completed |
| `DELETE /api/users/{userId}/todos/{todoId}` | Delete a todo |

The implementation uses ASP.NET Core with Entity Framework and SQLite. It has controllers, entities, a DbContext, and thread-safety handling. **But our spec will be much simpler.**

---

## Step 1: Define the State

The first question: **What does our spec need to track?**

For a TodoList, we need to know:
- Which users exist
- Each user's name
- Which todos belong to each user
- Each todo's title and completion status

```csharp
[State]
public partial class AppState
{
    /// <summary>
    /// Dictionary of users. Key = userId, Value = user data with their todos.
    /// </summary>
    public Dictionary<string, UserState> Users { get; set; } = new();
}

[State]
public partial class UserState
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, TodoState> Todos { get; set; } = new();
}

[State]
public partial class TodoState
{
    public string Title { get; set; } = string.Empty;
    public bool Completed { get; set; } = false;
}
```

### Why [State]?

The `[State]` attribute triggers source generation that provides:
- **Automatic cloning** (deep copy of your state)
- **Automatic equality comparison** (for state deduplication)
- **Automatic hashing** (for state graph exploration)

You just define your data structure—Accordant handles the rest.

> **Key Insight:** The state is **not** the implementation's internal state. It's the spec's *view* of what the system should remember. It's typically much simpler than the real implementation.

---

## Step 2: Create the Spec and Define Operations

Now we define what should happen for each operation. Let's start with CreateUser:

```csharp
private static Spec<AppState> CreateSpec()
{
    var spec = new Spec<AppState>()
        .WithJsonPrinters();  // Nice formatting for logs

    // ---------------------------------------------------------
    // CREATE USER: PUT /api/users/{userId}
    // ---------------------------------------------------------
    spec.Operation<User, ApiResult<User>>("CreateUser", (request, state) =>
    {
        // Case 1: User already exists → Conflict
        if (state.Users.ContainsKey(request.UserId))
        {
            return Expect.That<ApiResult<User>>(r => r.IsConflict,
                       $"Should return 409 Conflict because user '{request.UserId}' already exists")
                   .SameState();  // State doesn't change on error
        }

        // Case 2: User doesn't exist → Create it
        var newState = (AppState)state.Clone();
        newState.Users[request.UserId] = new UserState
        {
            Name = request.Name,
            Todos = new()
        };

        return Expect.That<ApiResult<User>>(
                   r => r.IsSuccess &&
                        r.Data != null &&
                        r.Data.UserId == request.UserId &&
                        r.Data.Name == request.Name,
                   $"Should return 200 OK with created user '{request.UserId}'")
               .ThenState(newState);  // State changes on success
    });

    // ... more operations ...
    
    return spec;
}
```

### Breaking Down the Operation

1. **`spec.Operation<TRequest, TResponse>`** - Defines an operation with typed request/response
2. **The lambda `(request, state) =>`** - Receives the request and current state
3. **Conditional logic** - Check state to determine the expected outcome
4. **`Expect.That<T>(predicate, explanation)`** - Defines what the response should look like
5. **`.SameState()`** - The operation doesn't modify state (error case, or read-only)
6. **`.ThenState(newState)`** - The operation transitions to a new state

### The Pattern: Clone-Modify-Return

When state changes, always:
1. **Clone** the current state: `var newState = (AppState)state.Clone();`
2. **Modify** the clone: `newState.Users[...] = ...;`
3. **Return** with `.ThenState(newState)`

Never modify the original state directly!

---

## Step 3: Add More Operations

Let's add the remaining operations:

```csharp
// ---------------------------------------------------------
// GET USER: GET /api/users/{userId}
// ---------------------------------------------------------
spec.Operation<string, ApiResult<User>>("GetUser", (userId, state) =>
{
    if (!state.Users.TryGetValue(userId, out var user))
    {
        return Expect.That<ApiResult<User>>(r => r.IsNotFound,
                   $"Should return 404 Not Found because user '{userId}' doesn't exist")
               .SameState();
    }

    return Expect.That<ApiResult<User>>(
               r => r.IsSuccess &&
                    r.Data != null &&
                    r.Data.UserId == userId &&
                    r.Data.Name == user.Name,
               $"Should return 200 OK with user '{userId}'")
           .SameState();  // GET is read-only
});

// ---------------------------------------------------------
// DELETE USER: DELETE /api/users/{userId}
// Cascades to delete all user's todos
// ---------------------------------------------------------
spec.Operation<string, int>("DeleteUser", (userId, state) =>
{
    if (!state.Users.ContainsKey(userId))
    {
        return Expect.That<int>(s => s == 404, 
                   $"Should return 404 Not Found")
               .SameState();
    }

    var newState = (AppState)state.Clone();
    newState.Users.Remove(userId);  // Remove user and all their todos

    return Expect.That<int>(s => s == 204, 
               $"Should return 204 No Content")
           .ThenState(newState);
});

// ---------------------------------------------------------
// CREATE TODO: PUT /api/users/{userId}/todos/{todoId}
// ---------------------------------------------------------
spec.Operation<Todo, ApiResult<Todo>>("CreateTodo", (request, state) =>
{
    // Must check: does user exist?
    if (!state.Users.TryGetValue(request.UserId, out var user))
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                   $"Should return 404 Not Found because user '{request.UserId}' doesn't exist")
               .SameState();
    }

    // Check: does todo already exist?
    if (user.Todos.ContainsKey(request.TodoId))
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsConflict,
                   $"Should return 409 Conflict because todo '{request.TodoId}' already exists")
               .SameState();
    }

    var newState = (AppState)state.Clone();
    newState.Users[request.UserId].Todos[request.TodoId] = new TodoState
    {
        Title = request.Title,
        Completed = false
    };

    return Expect.That<ApiResult<Todo>>(
               r => r.IsSuccess &&
                    r.Data != null &&
                    r.Data.TodoId == request.TodoId &&
                    r.Data.Title == request.Title &&
                    r.Data.Completed == false,
               $"Should return 200 OK with created todo")
           .ThenState(newState);
});

// ---------------------------------------------------------
// GET TODO, COMPLETE TODO, DELETE TODO
// (Similar pattern - check existence, return appropriate response)
// ---------------------------------------------------------
```

---

## Step 4: Bind to the HTTP API

So far we've defined *what should happen*. Now we connect it to *how to make it happen*:

```csharp
spec.ExecuteWith<TodoApiClient>()
    // User operations
    .BindAsync<User, ApiResult<User>>("CreateUser", 
        (client, req) => client.CreateUserAsync(req.UserId, req.Name))
    .BindAsync<string, ApiResult<User>>("GetUser", 
        (client, userId) => client.GetUserAsync(userId))
    .BindAsync<string, int>("DeleteUser", 
        (client, userId) => client.DeleteUserAsync(userId))
    // Todo operations
    .BindAsync<Todo, ApiResult<Todo>>("CreateTodo", 
        (client, req) => client.CreateTodoAsync(req.UserId, req.TodoId, req.Title))
    .BindAsync<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo", 
        (client, req) => client.GetTodoAsync(req.UserId, req.TodoId))
    .BindAsync<(string UserId, string TodoId), ApiResult<Todo>>("CompleteTodo", 
        (client, req) => client.CompleteTodoAsync(req.UserId, req.TodoId))
    .BindAsync<(string UserId, string TodoId), int>("DeleteTodo", 
        (client, req) => client.DeleteTodoAsync(req.UserId, req.TodoId));
```

The `TodoApiClient` is a simple wrapper around `HttpClient` that makes REST calls. Accordant doesn't care how you call your system—just that you return the right response type.

---

## Step 5: Configure and Run Tests

Now the exciting part—running tests!

```csharp
[Test]
public async Task SequentialTests_UsersAndTodos()
{
    using var factory = new TodoServiceFactory();  // Starts the API
    var spec = CreateSpec();

    // Tell the spec how to get a client and initial state
    spec.ProvideTargetAndInitialState(() => (
        new TodoApiClient(factory.CreateTestClient()),
        new AppState()  // Empty state
    ));

    // Get operation references for building inputs
    var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
    var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");
    var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
    var getTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("GetTodo");

    // Define inputs - Accordant explores all sequences!
    var inputs = new InputSet()
    {
        createUser.With(new User("alice", "Alice Smith"), "Create Alice"),
        getUser.With("alice", "Get Alice"),
        getUser.With("unknown", "Get unknown user"),
        createTodo.With(new Todo("alice", "todo-1", "Buy milk"), "Create todo"),
        getTodo.With(("alice", "todo-1"), "Get todo"),
    };

    // Run the tests
    var results = await spec.RunTests(
        inputs,
        generationOptions: new TestGenerationOptions
        {
            MaxDepth = 4  // Sequences up to length 4
        },
        executionOptions: new TestExecutionOptions
        {
            BeforeEachAsync = async ctx =>
            {
                // Reset database before each test
                var client = ctx.Context.Get<TodoApiClient>();
                await client.DeleteUserAsync("alice");
                await client.DeleteUserAsync("unknown");
            }
        });

    // Verify all passed
    var failures = results.Where(r => !r.Success).ToList();
    Assert.IsEmpty(failures, 
        $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    
    TestContext.WriteLine($"Ran {results.Count} test cases - all passed!");
}
```

### What Gets Generated?

From 5 inputs, Accordant generates test cases like:

```
CreateUser("alice") → GetUser("alice")
CreateUser("alice") → GetUser("unknown")
CreateUser("alice") → CreateUser("alice")  // Tests duplicate
CreateUser("alice") → CreateTodo(...) → GetTodo(...)
GetUser("alice")  // User doesn't exist yet
GetUser("unknown") → CreateUser("alice") → GetUser("alice")
... hundreds more ...
```

Each sequence is:
1. **Meaningful** - Accordant avoids redundant combinations
2. **Complete** - Every response is validated against the spec
3. **Isolated** - Database resets between tests

---

## Step 6: Understanding the Output

When a test fails, Accordant tells you exactly what went wrong:

```
FAILURE in test case: CreateUser("alice") → CreateUser("alice")
  Step 2: CreateUser("alice")
    Expected: Should return 409 Conflict because user 'alice' already exists
    Actual: IsSuccess=True, StatusCode=200
    State before: { "Users": { "alice": { "Name": "Alice Smith", "Todos": {} } } }
```

This tells you:
- **Which sequence** failed
- **Which step** in the sequence
- **What was expected** vs **what happened**
- **The state** at the time of failure

---

## Summary

You've learned the core Accordant workflow:

1. **Define State** - `[State]` partial class tracking what matters
2. **Define Operations** - `spec.Operation<TReq, TResp>(...)` with `Expect.That(...)`
3. **Bind Execution** - `spec.ExecuteWith<T>().BindAsync(...)`
4. **Configure & Run** - `ProvideTargetAndInitialState`, `InputSet`, `RunTests`

### Key Concepts

| Concept | Purpose |
|---------|----------|
| `[State]` | Attribute for state classes with auto clone/compare/hash |
| `Expect.That<T>()` | Declare expected response |
| `.SameState()` | Operation doesn't change state |
| `.ThenState(newState)` | Operation transitions to new state |
| `InputSet` | Values to try—Accordant explores sequences |
| `MaxDepth` | Limit sequence length |
| `BeforeEachAsync` | Reset state before each test |

---

## What's Next?

- **[Tutorial 2: Handling Errors](02-handling-errors.md)** - Exception handling with `Expect.Throws<>`
- **[Tutorial 3: Response-Dependent State](03-response-dependent-state.md)** - When the server returns values you need to track
- **[Concept: Not Just HTTP](../concepts/not-just-http.md)** - Accordant works for any stateful class, not just REST APIs

---

## Full Code Reference

See the complete TodoList sample:
- [TodoListTests.cs](https://github.com/microsoft/accordant/blob/main/Samples/TodoList/TodoList.Tests/TodoListTests.cs) - Complete spec
- [TodoApiClient.cs](https://github.com/microsoft/accordant/blob/main/Samples/TodoList/TodoList.Tests/TodoApiClient.cs) - HTTP client
- [TodoServiceFactory.cs](https://github.com/microsoft/accordant/blob/main/Samples/TodoList/TodoList.Tests/TodoServiceFactory.cs) - Test server setup
