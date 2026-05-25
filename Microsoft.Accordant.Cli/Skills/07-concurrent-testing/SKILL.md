---
name: Accordant Concurrent Testing
description: How to test for concurrency correctness using linearizability checking, concurrent test generation, and AllowsConcurrent
---

# Concurrent Testing with Accordant

Accordant can verify that your system handles concurrent operations correctly by checking **linearizability** — the gold standard for concurrent correctness.

## What is Linearizability?

A set of concurrent operations is linearizable if their results can be explained by SOME sequential ordering. Accordant checks this by trying all possible linearizations of the concurrent operations against the spec.

Example: Two concurrent `CreateUser("bob")` calls → one should succeed (200), one should fail (409). Accordant verifies this by checking both orderings.

## Auto-Generated Concurrent Tests

### Generate and Run

```csharp
var spec = new MySpec();
var initialState = new MyState();

var inputs = new InputSet()
{
    spec.CreateUser.With(new User("alice", "Alice"), "Create Alice"),
    spec.CreateTodo.With(new Todo("alice", "todo-1", "Task"), "Create todo"),
    spec.CompleteTodo.With(("alice", "todo-1"), "Complete todo"),
    spec.DeleteTodo.With(("alice", "todo-1"), "Delete todo"),
};

// Generate concurrent test cases
var testCases = spec.GenerateConcurrentTests(
    initialState,
    inputs,
    new TestGenerationOptions
    {
        MaxDepth = 3,
        MaxConcurrencyLevel = 3  // Max operations to run concurrently
    });

var context = spec.CreateTestingContext();
context.Register(client);

// Execute concurrent tests
var results = await spec.RunTests(
    context,
    initialState,
    testCases,
    new TestExecutionOptions
    {
        BeforeEachAsync = async info =>
        {
            // Reset system before each test
            await CleanupAsync(info.Context);
        }
    });

Assert.IsTrue(results.All(r => r.Success));
```

### Concurrent Test Case Structure

Each `ConcurrentTestCase` has:
- **SequentialOperationCalls**: A prefix of operations run sequentially to set up state
- **ConcurrentOperationCalls**: Operations run in parallel after the prefix

Example test case:
1. Sequential: `CreateUser("alice")` → `CreateTodo("alice", "todo-1")`
2. Concurrent: `CompleteTodo("alice", "todo-1")` ‖ `DeleteTodo("alice", "todo-1")`

### MaxConcurrencyLevel

Controls how many operations run concurrently:

```csharp
new TestGenerationOptions
{
    MaxConcurrencyLevel = 2,  // Pairs of operations
    // MaxConcurrencyLevel = 3,  // Triples
    // MaxConcurrencyLevel = -1, // Unlimited (caution: combinatorial explosion)
}
```

## Manual Concurrent Testing: AllowsConcurrent

Validate specific concurrent scenarios without auto-generation:

```csharp
var spec = new MySpec();
var initialState = new MyState();
var context = spec.CreateTestingContext();
context.Register(client);

var stateProfile = new StateProfile(initialState);

// Fire concurrent requests
var task1 = Task.Run(() => createUser.ExecuteAsync(context, new User("bob", "Bob One")));
var task2 = Task.Run(() => createUser.ExecuteAsync(context, new User("bob", "Bob Two")));
await Task.WhenAll(task1, task2);

// Validate responses against spec
var (isValid, message, nextProfile) = spec.AllowsConcurrent(
    stateProfile,
    new List<(IOperation, object, object)>
    {
        (createUser, new User("bob", "Bob One"), await task1),
        (createUser, new User("bob", "Bob Two"), await task2),
    });

Assert.IsTrue(isValid, $"Responses should be linearizable: {message}");
```

### How AllowsConcurrent Works

1. Takes a `StateProfile` and a list of `(operation, request, response)` tuples
2. Tries ALL possible orderings (permutations) of the operations
3. For each ordering, checks if applying operations sequentially produces valid responses
4. If **any** ordering produces all valid responses → linearizable ✓
5. If **no** ordering works → linearizability violation ✗

## Async Operations in Concurrent Tests

When operations trigger `TerminatingStepFunction`s (background async work), the test framework:

1. Runs the concurrent operations
2. For each possible linearization, checks if step functions eventually reach terminal states
3. Ensures responses are valid regardless of async completion order

### Controlling Step Function Unwinding

```csharp
new TestGenerationOptions
{
    // Don't unwind step functions during generation (faster but less thorough)
    ShouldUnwindStepFunction = ctx => false,

    // Only unwind specific step functions
    ShouldUnwindStepFunction = ctx =>
        ctx.StepFunction is TerminatingStepFunction &&
        ctx.Operation.Name == "PUT Image"
};
```

## Patterns for Concurrent Testing

### Pattern 1: Same Resource, Same Operation (Race Condition)
Two clients try to create the same entity:
```csharp
// Both try to create user "bob"
(createUser, new User("bob", "A"), response1),
(createUser, new User("bob", "B"), response2)
// Expected: one 200, one 409 (or equivalent error)
```

### Pattern 2: Same Resource, Different Operations (Read-Write Race)
One client writes while another reads:
```csharp
// Prefix: CreateUser("alice"), CreateTodo("alice", "t1")
// Concurrent:
(completeTodo, ("alice", "t1"), response1),
(getTodo, ("alice", "t1"), response2)
// Expected: GetTodo may see completed=true or completed=false
```

### Pattern 3: Dependent Resources (Cascade Race)
One client deletes a parent while another operates on child:
```csharp
// Prefix: CreateUser("alice"), CreateTodo("alice", "t1")
// Concurrent:
(deleteUser, "alice", response1),     // Cascades to delete todos
(getTodo, ("alice", "t1"), response2) // May see 200 or 404
```

## Stack Concurrent Test Example

```csharp
[Test]
public async Task ConcurrentStackTests()
{
    var spec = new StackSpec();
    var inputs = new InputSet()
    {
        spec.Push.With(1, "Push 1"),
        spec.Push.With(2, "Push 2"),
        spec.Pop.With("Pop"),
        spec.Count.With("Count")
    };

    var testCases = spec.GenerateConcurrentTests(
        new StackState<int>(),
        inputs,
        new TestGenerationOptions
        {
            StateConstraint = s => ((StackState<int>)s).Items.Count < 3
        });

    var context = spec.CreateTestingContext();

    var results = await spec.RunTests(
        context,
        new StackState<int>(),
        testCases,
        new TestExecutionOptions
        {
            BeforeEach = _ => context.Register(new Stack<int>())
        });

    Assert.IsTrue(results.All(r => r.Success));
}
```

## Key Points

1. **Linearizability is the correctness criterion** — responses must be explainable by some serial ordering
2. **MaxConcurrencyLevel controls test explosion** — start with 2, increase carefully
3. **Reset system state completely** in BeforeEach — concurrent tests are especially sensitive to leftover state
4. **Same spec, different test mode** — the same `Apply` method works for both sequential and concurrent validation
5. **Non-deterministic specs work naturally** — `Expect.OneOf()` and async operations with multiple outcomes are handled correctly during linearization
6. **Use `Task.Run` for true concurrency** — avoid `await` between concurrent operations to ensure they actually overlap
