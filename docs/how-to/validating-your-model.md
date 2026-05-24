# Validating Your Model

> **TL;DR**: The model validates your implementation — but what validates the model? Models can have runtime bugs (null refs, missing keys) or silent regressions (traces that used to pass now fail). Catch these with trace databases: record known-good request/response pairs and replay them after every model change.

---

## The Meta-Problem: Who Tests the Tests?

You write a model to check your implementation. The model is typically simple — a dictionary and some conditionals — which makes it easy to review and trust. But over time, models grow. More operations, more edge cases, more conditional logic. Eventually, you have a non-trivial piece of code.

Two things can go wrong:

1. **Runtime errors** — A bug in the model causes an exception during `Apply`. Null reference, key not found, index out of bounds.

2. **Silent regressions** — The model doesn't crash, but a trace that *used to* be valid is now rejected. Maybe you changed the model intentionally. Maybe you broke something.

Both are problems. Let's look at how to detect and prevent them.

---

## Problem 1: Runtime Errors in the Model

### How They Manifest

A bug in your model's `Apply` method throws an exception. This can happen in two places:

**During test generation** — Before any tests run, while exploring the state graph, Accordant wraps the error with helpful context:

```
Microsoft.Accordant.TestCaseGenerationException: 
  Encountered an exception when exploring the state graph for test case generation.
  The path from the root node to the node at which the exception happened is: 
    Get alice balance
  The state of the node at which the exception happened is: 
    BankState {Accounts={}}
  
  ----> System.Collections.Generic.KeyNotFoundException: 
    The given key 'alice' was not present in the dictionary.
```

This tells you:
- **Which operation** triggered the crash (`Get alice balance`)
- **What state** the model was in (`{Accounts={}}` — empty)
- **The underlying exception** (`KeyNotFoundException`)

The model crashed while computing what states are reachable. No tests were generated at all.

**During test execution** — If the bug only appears in certain state paths that test generation happens to avoid, it might surface when validating a real response:

```
Test case 23 of 150 FAILED
  Step 4: GetTodo("alice", "task-1") → ERROR

System.KeyNotFoundException: The given key 'task-1' was not present in the dictionary.
   at TodoSpec.GetTodoOperation.Apply(...)
   at Microsoft.Accordant.ResponseValidator.Validate(...)
```

The implementation returned a response, but when Accordant tried to check it against the model, the model itself crashed.

### Common Causes

- **Forgetting to check existence** before accessing a dictionary key
- **Null properties** when the model expected them to be populated
- **Off-by-one errors** in list indexing
- **Missing state initialization** in `ThenState` lambdas

### Example: A Subtle Bug

```csharp
spec.Operation<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo", (request, state) =>
{
    var user = state.Users[request.UserId];  // BUG: What if user doesn't exist?
    var todo = user.Todos[request.TodoId];   // BUG: What if todo doesn't exist?

    return Expect.That<ApiResult<Todo>>(r => r.IsSuccess && r.Data.Title == todo.Title)
           .SameState();
});
```

This model crashes with `KeyNotFoundException` if you call GetTodo for a non-existent user or todo. The fix:

```csharp
spec.Operation<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo", (request, state) =>
{
    if (!state.Users.TryGetValue(request.UserId, out var user))
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsNotFound)
               .SameState();
    }

    if (!user.Todos.TryGetValue(request.TodoId, out var todo))
    {
        return Expect.That<ApiResult<Todo>>(r => r.IsNotFound)
               .SameState();
    }

    return Expect.That<ApiResult<Todo>>(r => r.IsSuccess && r.Data.Title == todo.Title)
           .SameState();
});
```

### Detection Strategy: Run Test Generation

The simplest check: **generate test cases without running them**. If the model has a runtime bug reachable from your inputs, test generation will hit it:

```csharp
[Test]
public void ModelDoesNotCrashDuringExploration()
{
    var spec = CreateSpec();
    var inputs = CreateInputs(spec);

    // This explores the state graph — if the model crashes, this fails
    var testCases = spec.GenerateTests(
        new AppState(),
        inputs,
        new TestGenerationOptions { MaxDepth = 5 });

    Assert.That(testCases.Count, Is.GreaterThan(0));
}
```

This test doesn't need a running system. It just exercises the model's `Apply` methods across all reachable states.

---

## Problem 2: Silent Regressions

### What They Look Like

You change the model — maybe to add a new feature, fix a bug, or refactor. Tests that used to pass now fail. But which changed: the implementation or the model?

```
Test case 47 FAILED
  CreateUser("alice") → Success ✓
  CreateTodo("alice", "task-1", "Buy milk") → Success ✓
  GetTodo("alice", "task-1") → MISMATCH

  Expected: Title = "Buy milk", Completed = false
  Actual: Title = "Buy milk", Completed = false, CreatedAt = "2026-05-24T10:30:00Z"

  Validation failed: response has unexpected field 'CreatedAt'
```

Wait — did the implementation add a new field, or did you accidentally tighten the model's expectations? If you recently edited the model, you might have introduced the regression.

### The Core Challenge

When a test fails, there are two possibilities:

1. **The implementation is wrong** — This is a real bug. The model is correct.
2. **The model regressed** — The model changed in a way that rejects previously-valid behavior.

You can't tell which without additional information.

---

## Solution: Trace Databases

A **trace database** is a collection of recorded request/response pairs from known-good test runs. After every model change, you replay these traces through the model. If a previously-accepted trace is now rejected, you have a regression.

### The Pattern

1. **Record traces** from successful test runs
2. **Store them** in a file or database
3. **Replay them** against the model after changes
4. **Investigate** any traces that fail

### Recording Traces

A trace is a starting state plus a sequence of (operation, request, response) tuples:

```csharp
public class Trace
{
    public string InitialStateJson { get; set; }
    public List<TraceStep> Steps { get; set; } = new();
}

public class TraceStep
{
    public string OperationName { get; set; }
    public string RequestJson { get; set; }
    public string ResponseJson { get; set; }
}
```

During test execution, capture traces using `AfterEach`:

```csharp
var allTraces = new List<Trace>();
Trace currentTrace = null;

var results = await spec.RunTests(context, initialState, testCases, new TestExecutionOptions
{
    BeforeEach = (info) =>
    {
        // Start a new trace with the initial state
        currentTrace = new Trace
        {
            InitialStateJson = JsonSerializer.Serialize(initialState),
            Steps = new()
        };
    },
    OnStepExecuted = (stepInfo) =>
    {
        if (stepInfo.IsSingleOperation)
        {
            currentTrace.Steps.Add(new TraceStep
            {
                OperationName = stepInfo.Operation.Name,
                RequestJson = JsonSerializer.Serialize(stepInfo.Request),
                ResponseJson = JsonSerializer.Serialize(stepInfo.Response)
            });
        }
    },
    AfterEach = (info) =>
    {
        if (info.Success)
        {
            allTraces.Add(currentTrace);
        }
    }
});

// Save all successful traces
var json = JsonSerializer.Serialize(allTraces, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText("traces/golden-traces.json", json);
```

### Replaying Traces

Replay each trace from its starting state, validating each step:

```csharp
[Test]
public void ModelAcceptsPreviouslyValidTraces()
{
    var spec = CreateSpec();
    var tracesJson = File.ReadAllText("traces/golden-traces.json");
    var traces = JsonSerializer.Deserialize<List<Trace>>(tracesJson);

    var failures = new List<string>();

    foreach (var trace in traces)
    {
        // Start from the trace's initial state
        var state = JsonSerializer.Deserialize<AppState>(trace.InitialStateJson);
        var stateProfile = new StateProfile(state);

        foreach (var step in trace.Steps)
        {
            var operation = spec.GetOperation(step.OperationName);
            var request = DeserializeRequest(step.OperationName, step.RequestJson);
            var response = DeserializeResponse(step.OperationName, step.ResponseJson);

            try
            {
                var (isValid, message, updatedProfile) = spec.Allows(
                    operation, request, response, stateProfile);

                if (!isValid)
                {
                    failures.Add($"{step.OperationName}: Response no longer accepted. {message}");
                    break;  // Stop this trace on first failure
                }

                stateProfile = updatedProfile;  // Continue with updated state
            }
            catch (Exception ex)
            {
                failures.Add($"{step.OperationName}: Model threw {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }
    }

    Assert.IsEmpty(failures, $"Model regressions detected:\n{string.Join("\n", failures)}");
}
```

The key insight: `spec.Allows()` returns the updated `StateProfile`, so you can chain through the whole trace — just like the test executor does.

### When Failures Are Expected

Sometimes you *intend* to change what the model accepts. In that case:

1. Run the replay test — it fails
2. Review the failures — confirm they're expected
3. Update the trace database with new golden traces
4. Commit both the model change and the updated traces

The trace database becomes a form of **approval testing** for your model.

---

## Practical Tips

### Start Small

You don't need to record every trace from every test run. Start with a representative sample:

- A few traces per operation
- Cover success and error paths
- Include edge cases you've debugged before

### Version Your Traces

Store traces in version control alongside your model. When you change the model, the diff shows both the code change and which traces were updated.

### Automate in CI

Run trace replay as part of your CI pipeline. Any model change that breaks existing traces requires explicit acknowledgment (updating the trace files).

---

## Summary

| Problem | Detection | Prevention |
|---------|-----------|------------|
| Runtime errors in model | Run test generation without a live system | Defensive coding in `Apply` methods |
| Silent regressions | Replay trace database after changes | Review trace diffs in code review |

The model validates your implementation. The trace database validates your model.

> **Note**: The trace database also helps with Problem 1 — if your model crashes during replay, you've found a regression.

---

## See Also

- [Test Logs](test-logs.md) — Finding detailed output when things go wrong
