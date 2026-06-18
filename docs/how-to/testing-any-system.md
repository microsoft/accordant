# Testing Any System

> **TL;DR**: The spec is an oracle — it doesn't need to call your system, just see what happened. Record a trace of operations and responses from your system (any language), then validate it against the spec. You can also use the spec to generate test plans and execute them from any language.

---

## Quick Guide

| Your situation | What to do |
|---|---|
| Your system has an HTTP/gRPC API | Use Accordant normally — bind operations to a .NET client, it doesn't matter what language the server is in |
| You want to test from another language | Export test plans to JSON, execute from your runner, capture traces, validate with `spec.Allows()` — this guide covers all of this |
| You're calling native code (C/C++/Rust) from .NET | Use [P/Invoke](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke) for shared libraries (`.so`/`.dll`/`.dylib`), [COM interop](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cominterop) for COM components, or [C++/CLI](https://learn.microsoft.com/en-us/cpp/dotnet/native-and-dotnet-interoperability) for mixed-mode C++ |

If your system is callable from .NET (any network protocol, or native interop), you can use the full Accordant workflow directly — define operations, bind them to a client, generate and run tests. This is the approach in all the [samples](../../Samples/) and [tutorials](../tutorials/index.md).

The rest of this guide covers what you can do when calling from .NET isn't practical.

> ### The Two Things That Matter
>
> Accordant gives you two capabilities that are hard to replicate by hand:
>
> 1. **The spec as an oracle** — given any trace (a sequence of requests and responses), the spec tells you whether every response was correct, considering the full history of state transitions. You get a test oracle that grows automatically as your spec grows.
> 2. **State-graph-based test generation** — the spec simulates your system, explores reachable states, and algorithms extract high-coverage test sequences from the resulting graph.
>
> Everything else — the built-in test runner, polling, request derivation — is a small convenience layer. It saves a few lines of glue code, but the real value is in the two points above. Both work perfectly from any language via trace-based validation and JSON export.

### What You Need

1. A **.NET spec project** — defines state, operations, and behavioral rules
2. A **trace format** your system can emit (operation name + request + response as JSON)
3. A **validator** that reads traces and calls `spec.Allows()` (we'll build one below)

---

## Conformance Testing from Traces

Here's the key insight: the spec doesn't need to *run* your system. It just needs to *see* what happened.

Your system runs wherever it runs — a Go service, a Python app, a Rust binary — and produces a **trace**: a log of what operations were called, what requests were sent, and what responses came back. You then ask the spec: "Is this trace valid?" After every operation, the spec checks whether the observed response is allowed given the current state. The working rule is simple: after every step, ask *"was this response OK given where we think the system was?"*

### The Spec (No Execute Bindings Needed)

You define state and operations as usual. Since you're not running the system from .NET, you don't need Execute bindings — only the Apply logic (the behavioral rules) matters:

```csharp
// Request/response types — these must match the JSON fields in your trace
public record CreateAccountRequest(string AccountId);
public record DepositRequest(string AccountId, decimal Amount);
public record WithdrawRequest(string AccountId, decimal Amount);

public record BankResponse(int StatusCode, decimal? Balance = null)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotFound => StatusCode == 404;
    public bool IsConflict => StatusCode == 409;
    public bool IsBadRequest => StatusCode == 400;
}

// State: just what's needed to determine correct behavior
[State]
public partial class BankState
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}

var spec = new Spec<BankState>();

spec.Operation<CreateAccountRequest, BankResponse>("CreateAccount", (request, state) =>
{
    if (state.Accounts.ContainsKey(request.AccountId))
        return Expect.That<BankResponse>(r => r.IsConflict,
                   $"Account '{request.AccountId}' already exists → expect 409")
               .SameState();

    return Expect.That<BankResponse>(r => r.IsSuccess && r.Balance == 0,
               "New account → expect 2xx with balance 0")
           .ThenState<BankState>(s => s.Accounts[request.AccountId] = 0);
});

spec.Operation<DepositRequest, BankResponse>("Deposit", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
        return Expect.That<BankResponse>(r => r.IsNotFound,
                   $"Account '{request.AccountId}' doesn't exist → expect 404")
               .SameState();

    var newBalance = balance + request.Amount;
    return Expect.That<BankResponse>(r => r.IsSuccess && r.Balance == newBalance,
               $"Deposit succeeds → expect 2xx with balance {newBalance}")
           .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
});

spec.Operation<WithdrawRequest, BankResponse>("Withdraw", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
        return Expect.That<BankResponse>(r => r.IsNotFound,
                   $"Account '{request.AccountId}' doesn't exist → expect 404")
               .SameState();

    if (balance < request.Amount)
        return Expect.That<BankResponse>(r => r.IsBadRequest,
                   $"Insufficient funds: {balance} < {request.Amount} → expect 400")
               .SameState();

    var newBalance = balance - request.Amount;
    return Expect.That<BankResponse>(r => r.IsSuccess && r.Balance == newBalance,
               $"Withdrawal succeeds → expect 2xx with balance {newBalance}")
           .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
});
```

> **Tip**: Always include explanation strings in `Expect.That()`. They appear in violation messages and make debugging much easier when validating traces from another system.

### The Trace Format

Accordant doesn't prescribe a specific trace format — this is JSON you produce however works for you. Here's a simple format that works well:

```json
[
  {
    "operation": "CreateAccount",
    "request": { "AccountId": "alice" },
    "response": { "StatusCode": 201, "Balance": 0 }
  },
  {
    "operation": "Deposit",
    "request": { "AccountId": "alice", "Amount": 100 },
    "response": { "StatusCode": 200, "Balance": 100 }
  },
  {
    "operation": "Withdraw",
    "request": { "AccountId": "alice", "Amount": 30 },
    "response": { "StatusCode": 200, "Balance": 70 }
  }
]
```

The requirements are:

- Each entry has an **operation name** that matches a spec operation exactly
- The **request** JSON must deserialize into the C# request type (field names and types must match)
- The **response** JSON must deserialize into the C# response type
- The trace starts from a **known initial state** — the system must actually be in `new BankState()` when the first operation runs

To load this in C#, define a simple model:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public record TraceEntry(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("request")] JsonElement Request,
    [property: JsonPropertyName("response")] JsonElement Response);
```

### Validating a Sequential Trace

Walk the trace through the spec:

```csharp
var trace = JsonSerializer.Deserialize<List<TraceEntry>>(traceJson);
var stateProfile = new StateProfile(new BankState());
var allValid = true;

for (int i = 0; i < trace.Count; i++)
{
    var entry = trace[i];
    var operation = spec.GetOperation(entry.Operation);

    // Deserialize the JSON elements into the C# types the spec expects
    var request = JsonSerializer.Deserialize(entry.Request.GetRawText(), operation.RequestType);
    var response = JsonSerializer.Deserialize(entry.Response.GetRawText(), operation.ResponseType);

    var (isValid, message, nextProfile) = spec.Allows(operation, request, response, stateProfile);

    if (!isValid)
    {
        Console.WriteLine($"VIOLATION at step {i} ({entry.Operation}): {message}");
        allValid = false;
        break;
    }

    stateProfile = nextProfile;
}

if (allValid)
    Console.WriteLine("Trace is valid — all responses conform to the spec.");
```

Each call to `spec.Allows()` checks whether the observed response is consistent with what the spec expects, then advances the state profile so the next step is checked against the updated state.

### Catching a Bug

Here's the payoff. Say the system has a bug — it allows withdrawals that exceed the balance:

```json
[
  { "operation": "CreateAccount", "request": { "AccountId": "alice" },               "response": { "StatusCode": 201, "Balance": 0 } },
  { "operation": "Deposit",       "request": { "AccountId": "alice", "Amount": 50 }, "response": { "StatusCode": 200, "Balance": 50 } },
  { "operation": "Withdraw",      "request": { "AccountId": "alice", "Amount": 100 },"response": { "StatusCode": 200, "Balance": -50 } }
]
```

The spec says: balance is 50, withdrawing 100 → should return 400 Bad Request, state unchanged. But the trace shows 200 OK with balance -50. `spec.Allows()` reports:

```
VIOLATION at step 2 (Withdraw): Insufficient funds: balance 50 < requested 100 → expect 400
```

The spec caught the bug by analyzing the trace — without ever running the system.

### Validating Concurrent Traces

When your trace includes operations that ran simultaneously — say, two clients hitting the API at the same time — you need to check whether the *combined* results make sense. This is what `spec.AllowsConcurrent()` does.

Accordant doesn't try to guess the real thread schedule. It asks a simpler question: *could these results have happened in some valid sequential order?* If yes, the results are **linearizable** and everything is fine. If no ordering can explain the results, you've found a race condition.

Here's a concrete example. Alice's account has balance 100, and two withdrawals of 60 arrive concurrently. A convenient trace format is an array of **segments** — a segment with one entry is sequential, a segment with multiple entries means those operations overlapped:

```json
[
  [{ "operation": "CreateAccount", "request": { "AccountId": "alice" }, "response": { "StatusCode": 201, "Balance": 0 } }],
  [{ "operation": "Deposit", "request": { "AccountId": "alice", "Amount": 100 }, "response": { "StatusCode": 200, "Balance": 100 } }],
  [
    { "operation": "Withdraw", "request": { "AccountId": "alice", "Amount": 60 }, "response": { "StatusCode": 200, "Balance": 40 } },
    { "operation": "Withdraw", "request": { "AccountId": "alice", "Amount": 60 }, "response": { "StatusCode": 400 } }
  ]
]
```

This is valid! If the first withdrawal ran first (100 → 40), then the second saw balance 40 < 60 and correctly returned 400. One valid ordering exists, so the results are linearizable.

But if *both* withdrawals returned 200 OK — that's a bug. No sequential ordering can explain both succeeding when the account only has 100.

> **How to group concurrent operations**: Only batch operations whose execution windows actually overlapped in time. If you have timestamps, operations that don't overlap should remain in sequential order. Over-grouping (putting non-overlapping operations in a concurrent batch) can mask real ordering violations. Also keep groups small — linearizability checking tries all possible orderings, so cost grows factorially (N!) with group size.

Here's the validation code. Each segment is either a single sequential step or a concurrent group:

```csharp
var stateProfile = new StateProfile(new BankState());

foreach (var segment in trace)
{
    if (segment.Count == 1)
    {
        // Sequential step — validate with spec.Allows()
        var entry = segment[0];
        var op = spec.GetOperation(entry.Operation);
        var req = JsonSerializer.Deserialize(entry.Request.GetRawText(), op.RequestType);
        var resp = JsonSerializer.Deserialize(entry.Response.GetRawText(), op.ResponseType);

        var (isValid, message, next) = spec.Allows(op, req, resp, stateProfile);
        if (!isValid)
            throw new Exception($"Sequential step failed: {message}");
        stateProfile = next;
    }
    else
    {
        // Concurrent group — Accordant tries all possible orderings
        var concurrentCalls = segment.Select(entry =>
        {
            var op = spec.GetOperation(entry.Operation);
            var req = JsonSerializer.Deserialize(entry.Request.GetRawText(), op.RequestType);
            var resp = JsonSerializer.Deserialize(entry.Response.GetRawText(), op.ResponseType);
            return ((IOperation)op, req, resp);
        }).ToList();

        var (concValid, concMessage, nextProfile) = spec.AllowsConcurrent(
            stateProfile, concurrentCalls);

        if (!concValid)
            throw new Exception($"RACE CONDITION: {concMessage}");
        stateProfile = nextProfile;
    }
}
```

The `StateProfile` threads through all segments — sequential parts advance it one step at a time, concurrent parts check all orderings and advance to the set of possible next states.

> **Note on initial state**: Trace validation only works from a known starting point. The system must actually be in the state matching your `StateProfile` when the trace begins. In tests, reset the system before each trace. For production traces, you'd need to start from a snapshot or include all state-changing operations from the beginning.

---

## Generating Test Sequences

So far we've talked about validating traces that already exist. But where do the sequences come from in the first place?

You have several options, from manual to fully automated.

### Writing Sequences Yourself

The simplest approach: you know your system's edge cases. Write the sequences you care about, execute them, capture the trace, and validate with `spec.Allows()`. The spec handles all the assertion logic — you just pick interesting scenarios.

### Using the State Graph

Here's something more powerful. Because the spec defines how each operation changes state, Accordant can *simulate* your system — without running it. Starting from an initial state, it tries every operation with every sample input, sees which ones change state, and recurses from each new state. This builds a **state graph**: all the states your system can reach (bounded by `MaxDepth` and your sample inputs), connected by the operations that transition between them.

```csharp
var context = spec.CreateTestingContext();

var inputs = new InputSet
{
    spec.GetOperation<CreateAccountRequest, BankResponse>("CreateAccount")
        .With(new CreateAccountRequest("alice"), "Create alice"),
    spec.GetOperation<DepositRequest, BankResponse>("Deposit")
        .With(new DepositRequest("alice", 100m), "Deposit 100"),
    spec.GetOperation<WithdrawRequest, BankResponse>("Withdraw")
        .With(new WithdrawRequest("alice", 30m), "Withdraw 30"),
};

// Build the state graph — returns the root node
var rootNode = TestCaseGenerator.ExploreStateSpace(
    context, new BankState(), inputs,
    new TestGenerationOptions { MaxDepth = 4 });
```

Each `StateGraphNode` has a `State` and a list of `Edges` leading to child nodes. You can traverse this graph however you want — find shortest paths to specific states, extract all paths of a certain length, or export the graph for visualization in another tool.

### Auto-Generating Test Cases

The built-in algorithms walk the state graph and extract test sequences automatically:

```csharp
var testCases = spec.GenerateTests(new BankState(), inputs,
    new TestGenerationOptions
    {
        MaxDepth = 4,
        StateConstraint = s => ((BankState)s).Accounts.Values.All(b => b <= 500)
    });

var concurrentTestCases = spec.GenerateConcurrentTests(new BankState(), inputs,
    new TestGenerationOptions { MaxDepth = 4 });
```

`MaxDepth` bounds how many steps deep the exploration goes. `StateConstraint` prunes the graph — states that don't satisfy the predicate are excluded, which keeps the graph manageable and lets you focus on the regions you care about.

Three built-in algorithms: `StateCoverage` (default — visits each unique state at least once), `TransitionCoverage` (exercises every edge), and `RandomWalk` (samples paths probabilistically). The algorithm is a delegate `(TestingContext, StateGraphNode) → IList<SequentialTestCase>`, so you can plug in your own graph-walking logic too. See [How Test Generation Works](../concepts/how-test-generation-works.md) for details.

Test sequence generation is an interesting and active area. These algorithms are a solid starting point, but the state graph is yours to traverse however you like — shortest paths to specific states, property-targeted searches, or coverage-guided exploration.

### Exporting and Understanding Test Cases

Save generated test cases to JSON:

```csharp
TestCaseGenerator.SaveSequentialTestCases(context, "test-cases.json", testCases);
TestCaseGenerator.SaveConcurrentTestCases(context, "concurrent-test-cases.json", concurrentTestCases);
```

Here's what the exported JSON looks like (simplified — actual output may include additional optional fields like `Comments`, `Polling`, `DerivedFromOperationCalls`). Each test case is a sequence of **operation calls** — an operation call is simply an operation name paired with a concrete request:

```json
[
  {
    "Description": "Create alice → Deposit 100 → Withdraw 30",
    "OperationCalls": [
      {
        "Name": "Create alice",
        "Input": {
          "OperationName": "CreateAccount",
          "Name": "Create alice",
          "SerializedRequest": "{\"AccountId\":\"alice\"}"
        }
      },
      {
        "Name": "Deposit 100",
        "Input": {
          "OperationName": "Deposit",
          "Name": "Deposit 100",
          "SerializedRequest": "{\"AccountId\":\"alice\",\"Amount\":100}"
        }
      },
      {
        "Name": "Withdraw 30",
        "Input": {
          "OperationName": "Withdraw",
          "Name": "Withdraw 30",
          "SerializedRequest": "{\"AccountId\":\"alice\",\"Amount\":30}"
        }
      }
    ]
  }
]
```

Concurrent test cases use `Segments` instead of `OperationCalls` — each segment contains a list of operation calls that should run either sequentially or concurrently.

The `OperationName` tells you what to call; the `SerializedRequest` is the JSON payload. This is language-agnostic — any system can parse this and execute the operations.

### Executing Test Cases Outside .NET

Your test runner (in any language) reads the exported JSON and executes each operation call. Here's what that looks like in Python:

```python
import json

with open("test-cases.json") as f:
    test_cases = json.load(f)

for test_case in test_cases:
    reset_system()  # Start from initial state
    trace = []

    for call in test_case["OperationCalls"]:
        op_name = call["Input"]["OperationName"]
        request = json.loads(call["Input"]["SerializedRequest"])

        response = execute_operation(op_name, request)  # Call your system

        trace.append({
            "operation": op_name,
            "request": request,
            "response": response,
        })

    # Feed the trace back to spec.Allows() for validation
    save_trace(test_case["Description"], trace)
```

Then feed the captured traces back through `spec.Allows()` in .NET to validate them.

> **Execution vs validation**: When Accordant runs tests internally (via `spec.RunTests()`), execution and validation are interleaved — each response is validated before the next operation runs. When you're executing externally, you can run all operations first, capture the full trace, and validate afterwards. The result is the same.

### Polling and Request Derivations

> These are small convenience features of the built-in test executor — not core capabilities. They save a few lines of glue code, and are straightforward to replicate in any language.

When running test cases internally, Accordant handles two things automatically:

- **Polling**: If an operation triggers background work (modeled with `.Triggers()`), the executor polls a specified operation until the work completes. Polling parameters (wait time, max retries) may be defined at the operation level in the spec, or overridden per-input in the exported JSON.
- **Request derivations**: If one operation's request depends on a server-generated value from a previous response (like an auto-assigned ID), the executor extracts it automatically.

When executing outside .NET, you handle these yourself. It's a few lines of code:

- **Polling**: An input's `Polling` field (if present) tells you what to poll, how long to wait between retries, and when to stop. Your runner just loops until done.
- **Request derivations**: An input with `DerivedFromOperationCalls` has `SerializedRequest` set to `null`. You look up the response from the named prior call and extract the needed field — same mapping your spec's `ConfigureDerivations` defines.

Here's the pattern in Python:

```python
responses = {}  # Map of call Name → response

for call in test_case["OperationCalls"]:
    name = call["Name"]
    input_data = call["Input"]

    # Most inputs have SerializedRequest. If DerivedFromOperationCalls is set,
    # the request depends on prior responses — build it from those instead.
    if input_data.get("DerivedFromOperationCalls"):
        derived_from = input_data["DerivedFromOperationCalls"]
        request = derive_request(input_data["OperationName"], derived_from, responses)
    else:
        request = json.loads(input_data["SerializedRequest"])

    response = execute_operation(input_data["OperationName"], request)
    responses[name] = response

    # Handle polling if configured
    polling = input_data.get("Polling")
    if polling and not input_data.get("SkipPolling"):
        for _ in range(polling.get("MaxRetryCount", 10)):
            time.sleep(polling.get("WaitTimeInMs", 500) / 1000)
            poll_response = execute_operation(polling["Operation"], request)
            if is_terminal(poll_response):
                break
```

That's it — a dozen lines of code. The hard part isn't the glue; it's having a spec that can validate what your system did.

### The Full Round-Trip

Putting it all together:

1. **Write the spec** in .NET — state, operations, sample inputs
2. **Simulate the state graph** — Accordant explores reachable states using the spec
3. **Generate test plans** — algorithms extract paths through the graph as test sequences
4. **Export to JSON** — `SaveSequentialTestCases()` / `SaveConcurrentTestCases()`
5. **Execute** — your system (any language) reads the JSON, runs the operations, records a trace
6. **Validate** — feed the trace back through `spec.Allows()` / `spec.AllowsConcurrent()`

Steps 1–4 happen once (or whenever the spec changes). Steps 5–6 run as part of your CI pipeline.

That said, test generation (steps 2–4) is entirely optional. Trace validation works regardless of how the sequence was generated — hand-written scenarios, production traffic, fuzz testing, or any other source. As long as you have a trace, the spec can tell you whether the behavior was correct.

Specs can also express non-determinism — operations that may sometimes fail for reasons outside your control (timeouts, throttling, eventual consistency). The spec captures which responses are acceptable, and validation handles the rest. See [Indefinite Failures](indefinite-failures.md) for more.

---

## A Note on Event-Based Systems

Some systems don't follow a strict request/response pattern — they emit **events** (messages, webhooks, notifications). The same approach works: model each event as a request with an empty response.

```csharp
public record OrderShippedEvent(string OrderId, string TrackingNumber);
public record Unit;  // Empty response

spec.Operation<OrderShippedEvent, Unit>("OrderShipped", (evt, state) =>
{
    // Validate the event is expected given current state
    ...
});
```

Your trace then looks like `[{ "operation": "OrderShipped", "request": { ... }, "response": {} }]`. The spec validates whether each event was expected given the state at that point. Everything else — conformance checking, concurrent validation, test generation — works identically.

---

## See Also

- [Conformance Testing](../concepts/conformance-testing.md) — The theory behind spec-as-oracle validation
- [How Test Generation Works](../concepts/how-test-generation-works.md) — State graph exploration and test case algorithms
- [Testing Race Conditions](../tutorials/05-testing-race-conditions.md) — Linearizability checking for concurrent operations
- [Indefinite Failures](indefinite-failures.md) — Modeling non-deterministic responses and transient failures
- [Your First Spec](../tutorials/01-your-first-spec.md) — Getting started with state, operations, and expectations
- [Step Functions & Async](../concepts/step-functions-and-async.md) — How background work and polling are modeled
