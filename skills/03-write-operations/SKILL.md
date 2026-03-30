---
name: Accordant Write Operations
description: How to write class-based operations with Apply, Execute, Expect API, request derivation, polling, and async operations
---

# Writing Class-Based Operations

Operations are the heart of a Accordant model. Each operation models one API endpoint or action in your system.

## Operation Class Structure

```csharp
using Microsoft.Accordant;

public class MyOperation : Operation<TRequest, TResponse, TState>
{
    public MyOperation() : base("OperationName") { }

    // REQUIRED: What SHOULD happen? (pure function, no side effects)
    public override ExpectedOutcomes Apply(TRequest request, TState state)
    {
        // Return expected outcomes using Expect API
    }

    // REQUIRED for execution: How to call the real system
    public override TResponse Execute(TestingContext context, TRequest request)
    {
        // Call real system and return response
    }

    // OR for async execution:
    public override async Task<TResponse> ExecuteAsync(TestingContext context, TRequest request)
    {
        // Call real system asynchronously
    }
}
```

## The Apply Method

`Apply` is a **pure function** that defines expected behavior. Given a request and current state, it returns:
- What responses are valid
- How the state should change

**CRITICAL**: Never mutate the `state` parameter. The framework auto-clones state in `ThenState`.

### Basic Patterns

#### Return a specific value, state unchanged
```csharp
public override ExpectedOutcomes Apply(Unit request, StackState<int> state)
{
    return Expect.That(r => r == state.Items.Count, $"should equal {state.Items.Count}")
                 .SameState();
}
```

#### Return a value, update state
```csharp
public override ExpectedOutcomes Apply(int request, StackState<int> state)
{
    return Expect.Unit()  // void return
                 .ThenState(nextState => nextState.Items.Add(request));
}
```

#### Conditional behavior based on state
```csharp
public override ExpectedOutcomes Apply(Unit request, StackState<int> state)
{
    if (state.Items.Count > 0)
    {
        var top = state.Items[state.Items.Count - 1];
        return Expect.That(r => r.Equals(top), $"should return {top}")
                     .ThenState(nextState => nextState.Items.RemoveAt(nextState.Items.Count - 1));
    }
    else
    {
        return Expect.Throws<EmptyStackException>()
                     .SameState();
    }
}
```

## The Expect API

### Creating Expected Outcomes

| Method | Usage |
|--------|-------|
| `Expect.That<T>(predicate, explanation)` | Response matches predicate |
| `Expect.That<T>(Func<T, ValidationResult>)` | Response matches with detailed validation |
| `Expect.Throws<TException>()` | Operation should throw this exception type |
| `Expect.Unit()` | No meaningful response (void operations) |
| `Expect.OneOf(outcome1, outcome2, ...)` | Non-deterministic: any one outcome is valid |

### Inside Operation Classes: The `Expect` Property

Inside an `Operation<TReq, TResp, TState>` class, you have access to `this.Expect` which provides **type-inferred** methods. You don't need to specify generic type parameters:

```csharp
// Using the instance Expect property (preferred inside operations):
return Expect.That(r => r.Name == "test", "name should be test")
             .ThenState(next => next.Count++);

// Equivalent using static Expect class (explicit generics required):
return Accordant.Expect.That<MyResponse>(r => r.Name == "test", "name should be test")
             .ThenState<MyState>(next => next.Count++);
```

### Building Expected Outcomes

The fluent API chain:

```
Expect.That(...) → ExpectedOutcomeBuilder
    .ThenState(nextState => ...)     // State changes (auto-cloned)
    .SameState()                      // No state change
    .Triggers(stepFunction)           // Triggers background work
    .WithNextState(stateEnum)         // Set next state enum value
```

### ThenState Overloads

```csharp
// Basic: mutate cloned state
.ThenState(nextState => nextState.Items.Add(item))

// Response-dependent: use response to update state (requires mock response)
.ThenState((response, nextState) => {
    nextState.Id = response.Id;
}, mockResponse: () => new MyResponse { Id = "mock" })

// With clone map: access cloned state references
.ThenState((nextState, cloneMap) => {
    var clonedItem = cloneMap[originalItem];
    clonedItem.Status = "updated";
})
```

### Non-Deterministic Outcomes

When an operation can produce different valid responses:

```csharp
return Expect.OneOf(
    Expect.That(r => r.Status == "Success", "succeeds")
          .ThenState(next => next.Count++),
    Expect.That(r => r.Status == "AlreadyExists", "already exists")
          .SameState()
);
```

## The Execute Method

`Execute` or `ExecuteAsync` calls the real system. Access registered services via `TestingContext`:

```csharp
public override int Execute(TestingContext context, Unit request)
{
    return context.Get<Stack<int>>().Count();
}

// Async version for HTTP/IO operations:
public override async Task<HttpResponse> ExecuteAsync(TestingContext context, MyRequest request)
{
    var client = context.Get<HttpClient>();
    return await client.GetAsync($"/api/items/{request.Id}");
}
```

## The With() Helper

Operations have a `With()` method to create `OperationInput` for test generation:

```csharp
// With request and label:
spec.Push.With(42, "Push 42")

// For Unit request operations (no request needed):
spec.Pop.With("Pop")

// These are added to InputSet for test generation
```

## Request Derivation

When one operation's request depends on another's response (e.g., GET needs an ID returned by POST).

### Override DerivedFrom Property

```csharp
public class GetImageOperation : Operation<GetImageRequest, HttpResponse, MyState>
{
    public GetImageOperation() : base("GET Image") { }

    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        // Single derivation: GET derives from PUT
        Derive.From<PutImageRequest, HttpResponse, GetImageRequest>("PUT Image")
              .As((req, resp) => new GetImageRequest
              {
                  AccountName = req.AccountName,
                  Name = req.Name
              })
    };

    // ... Apply and Execute ...
}
```

### Derivation API

```csharp
// Simple: derive from one operation
Derive.From<TSourceReq, TSourceResp, TResult>("SourceOpName")
      .As((sourceReq, sourceResp) => derivedRequest)

// With filter: only derive when condition is met
Derive.From<TSourceReq, TSourceResp, TResult>("SourceOpName")
      .When((req, resp) => resp.StatusCode == 200)
      .As((req, resp) => derivedRequest)

// Multiple variants: produce multiple derived requests
Derive.From<TSourceReq, TSourceResp, TResult>("SourceOpName")
      .AsVariants((req, resp) => new Dictionary<string, TResult>
      {
          ["IfMatch"] = new TResult { ETag = resp.ETag },
          ["IfNoneMatch"] = new TResult { ETag = "wrong-etag" }
      })
```

## Polling Setup

For operations that trigger async background work (e.g., PUT triggers processing, GET polls for completion):

```csharp
public class PutImageOperation : Operation<PutImageRequest, HttpResponse, MyState>
{
    public PutImageOperation() : base("PUT Image") { }

    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "GET Image",      // Which operation to poll with
        WaitTimeInMs = 1000,           // Delay between polls (default: 1000)
        MaxRetryCount = 50             // Max poll attempts (default: 50)
    };

    // The polling operation must have DerivedFrom set up to derive
    // its request from this operation's request/response
}
```

## Async Operations (Step Functions)

Model background processes that happen after an operation completes:

```csharp
public override ExpectedOutcomes Apply(PutImageRequest request, MyState state)
{
    return Expect.That(r => r.StatusCode == 201, "created")
        .ThenState(next => next.Images[request.Name] = new ImageState { State = "Creating" })
        .Triggers(AsyncOperation.Create<MyState>(
            isTerminal: s => s.Images[request.Name].State != "Creating",
            transition: next => next.Images[request.Name].State = "Created"
        ));
}

// Non-deterministic async (can succeed or fail):
.Triggers(AsyncOperation.Create<MyState>(
    isTerminal: s => s.Images[name].State != "Creating",
    transitions: new Action<MyState>[]
    {
        next => next.Images[name].State = "Created",
        next => next.Images[name].State = "Failed"
    }
))
```

For complex step functions, subclass `TerminatingStepFunction`:

```csharp
public class ImageProcessingStep : TerminatingStepFunction
{
    private readonly string _imageName;
    public ImageProcessingStep(string imageName) { _imageName = imageName; }

    public override Func<State, bool> IsTerminalState =>
        s => ((MyState)s).Images[_imageName].State != "Creating";

    protected override IList<StepResult> GetStepResults(State state)
    {
        var next = (MyState)state.Clone();
        next.Images[_imageName].State = "Created";
        return new[] { new StepResult { State = next } };
    }
}
```

## Creating the Spec

```csharp
public class MySpec : Spec<MyState>
{
    // Declare operations as public properties
    public PushOperation Push { get; } = new();
    public PopOperation Pop { get; } = new();
    public PeekOperation Peek { get; } = new();

    public MySpec()
    {
        // Auto-register all public IOperation properties
        RegisterOperationProperties();

        // Optional: enable JSON formatting for request/response logs
        WithJsonPrinters();
    }
}
```

## Complete Example: Stack Operations

```csharp
// State
public class StackState<T> : JsonState
{
    public List<T> Items { get; set; } = new();
}

// Push Operation
public class PushOperation<T> : Operation<T, Unit, StackState<T>>
{
    public PushOperation() : base("Push") { }

    public override ExpectedOutcomes Apply(T request, StackState<T> state)
    {
        return Expect.Unit()
                     .ThenState(nextState => nextState.Items.Add(request));
    }

    public override Unit Execute(TestingContext context, T request)
    {
        context.Get<Stack<T>>().Push(request);
        return Unit.Value;
    }
}

// Pop Operation
public class PopOperation<T> : Operation<Unit, T, StackState<T>>
{
    public PopOperation() : base("Pop") { }

    public override ExpectedOutcomes Apply(Unit request, StackState<T> state)
    {
        if (state.Items.Count > 0)
        {
            var top = state.Items[state.Items.Count - 1];
            return Expect.That(r => r.Equals(top), $"should return {top}")
                         .ThenState(next => next.Items.RemoveAt(next.Items.Count - 1));
        }
        return Expect.Throws<EmptyStackException>().SameState();
    }

    public override T Execute(TestingContext context, Unit request)
    {
        return context.Get<Stack<T>>().Pop();
    }
}

// Spec
public class StackSpec : Spec<StackState<int>>
{
    public PushOperation<int> Push { get; } = new();
    public PopOperation<int> Pop { get; } = new();

    public StackSpec() { RegisterOperationProperties(); }
}
```
