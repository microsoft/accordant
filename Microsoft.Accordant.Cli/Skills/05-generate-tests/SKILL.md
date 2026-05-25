---
name: Accordant Generate Tests
description: How to auto-generate sequential and concurrent test cases using InputSet, TestGenerationOptions, and state space exploration
---

# Auto-Generating Test Cases

Accordant explores the state space of your spec to automatically generate test cases. You provide:
1. An initial state
2. An `InputSet` (concrete operation inputs)
3. `TestGenerationOptions` to control exploration

## InputSet: Defining Test Inputs

An `InputSet` is a collection of `OperationInput` — each binding an operation to a concrete request:

```csharp
var spec = new StackSpec();

var inputs = new InputSet()
{
    spec.Push.With(1, "Push 1"),         // Push with value 1
    spec.Push.With(2, "Push 2"),         // Push with value 2
    spec.Pop.With("Pop"),                // Pop (no request, just label)
    spec.Peek.With("Peek"),             // Peek
    spec.IsEmpty.With("IsEmpty"),       // IsEmpty
    spec.Count.With("Count"),           // Count
};
```

### The With() Method

Operations provide `With()` to create `OperationInput`:

```csharp
// Operation with a request:
operation.With(request, "label")    // Returns OperationInput

// Operation with Unit request (parameterless):
operation.With("label")             // Returns OperationInput with Unit.Value
```

### OperationInput Options

```csharp
// Disable polling for a specific input
spec.PutImage.With(imageReq, "Create Image").WithoutPolling()

// Override polling setup
spec.PutImage.With(imageReq, "Create Image").WithPolling(new PollingSetup
{
    Operation = "GET Image",
    WaitTimeInMs = 500,
    MaxRetryCount = 10
})
```

## Generating Sequential Tests

```csharp
var initialState = new MyState();

var testCases = spec.GenerateTests(
    initialState,
    inputs,
    new TestGenerationOptions { MaxDepth = 5 });

// testCases is IList<SequentialTestCase>
// Each test case is a sequence of operation calls
```

## Generating Concurrent Tests

```csharp
var testCases = spec.GenerateConcurrentTests(
    initialState,
    inputs,
    new TestGenerationOptions
    {
        MaxDepth = 3,
        MaxConcurrencyLevel = 3
    });

// testCases is IList<ConcurrentTestCase>
// Each has: SequentialOperationCalls (prefix) + ConcurrentOperationCalls (parallel calls)
```

## TestGenerationOptions

| Property | Default | Description |
|----------|---------|-------------|
| `MaxDepth` | 5 | Max depth of state space exploration. Set -1 for unlimited (caution!) |
| `StateConstraint` | null | Predicate to limit exploration. Return false to stop exploring from that state |
| `ShouldApply` | `(_, _) => true` | Filter which inputs are applied in which states |
| `MaxConcurrencyLevel` | 3 | Max concurrent operations in a concurrent test case |
| `MaxOperationApplicationCount` | (unlimited) | Max times any single input is applied on a path |
| `ApplyOperationsRepeatedly` | true | Whether to allow repeated application of the same input |
| `ShouldPreserveOperation` | `(_, _) => true` | Fine-grained control over which inputs remain available |
| `SimplifyOperationCallNames` | true | Shorten operation call names in generated tests |
| `SequentialTestCaseAlgorithm` | StateCoverage | Algorithm for sequential test generation |
| `ConcurrentTestCaseAlgorithm` | default(3) | Algorithm for concurrent test generation |
| `ShouldUnwindStepFunction` | All terminating | Whether to unwind async step functions during generation |
| `ShouldIncludeTransition` | null | Filter transitions in state graph |
| `DerivationSelectors` | null (all) | Filter which derivations to generate |
| `RequestTemplates` | empty | Templates for derivation-based requests |

### StateConstraint Example

Limit exploration to prevent state explosion:

```csharp
var options = new TestGenerationOptions
{
    StateConstraint = state =>
    {
        var s = (StackState<int>)state;
        // Only explore states with < 3 items and no duplicates
        return s.Items.Count < 3 &&
               s.Items.Distinct().Count() == s.Items.Count;
    }
};
```

### ShouldApply Example

Restrict when certain operations can be applied:

```csharp
var options = new TestGenerationOptions
{
    ShouldApply = (input, state) =>
    {
        // Only apply "Delete" when there are items
        if (input.Name == "Delete")
            return ((MyState)state).Items.Count > 0;
        return true;
    }
};
```

### MaxOperationApplicationCount Example

```csharp
var options = new TestGenerationOptions
{
    MaxOperationApplicationCount = 2  // Each input applied at most twice per test path
};
```

### Step Function Unwinding

By default, all `TerminatingStepFunction`s are unwound (applied until terminal). Control this:

```csharp
var options = new TestGenerationOptions
{
    // Don't unwind any step functions
    ShouldUnwindStepFunction = ctx => false,

    // Selectively unwind
    ShouldUnwindStepFunction = ctx =>
        ctx.StepFunction is TerminatingStepFunction &&
        ctx.Operation.Name != "BackgroundJob"
};
```

## Manual Test Case Construction

Create specific test sequences manually:

```csharp
var manualTest = TestCaseGenerator.CreateManualSequentialTestCase(
    context,
    inputs,
    "Push 1", "Push 2", "Pop", "Peek"  // Ordered list of input names
);
```

## Best Practices

1. **Start with small MaxDepth** (3-4) and increase as needed
2. **Use StateConstraint** to prevent state explosion with unbounded collections
3. **Name inputs descriptively** — names appear in test case descriptions
4. **Include error-path inputs** — e.g., requests for non-existent resources
5. **Include multiple values** for the same operation to test different code paths
6. **Use MaxOperationApplicationCount** to prevent combinatorial explosion
