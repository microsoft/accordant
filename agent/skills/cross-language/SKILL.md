---
name: accordant-cross-language
description: Testing systems not written in .NET - trace-based conformance testing, JSON test plan export, and executing from any language. Use when the user's system is in Python, Go, Java, Rust, C++, or any non-.NET language, or when they ask about trace validation or exporting test cases.
---

# Testing Any System with Accordant

The spec is an oracle — it doesn't need to call your system, just see what happened. This skill covers how to use Accordant when the system under test isn't in .NET.

## Decision Guide

| Situation | Approach |
|---|---|
| System has an HTTP/gRPC API | Use Accordant normally — bind operations to a .NET client. Server language doesn't matter. |
| Want to test from another language | Export test plans to JSON, execute from your runner, capture traces, validate with `spec.Allows()` |
| Calling native code (C/C++/Rust) from .NET | Use [P/Invoke](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke) for shared libraries, [COM interop](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cominterop), or [C++/CLI](https://learn.microsoft.com/en-us/cpp/dotnet/native-and-dotnet-interoperability) |

## The Two Core Capabilities

Everything else (built-in test runner, polling, request derivation) is a small convenience layer. The real value:

1. **Spec as oracle** — given any trace (requests + responses), the spec tells you whether every response was correct given the state history
2. **State-graph-based test generation** — the spec simulates the system, explores reachable states, and algorithms extract high-coverage test sequences

Both work from any language via trace validation and JSON export.

## Trace-Based Conformance Testing

### The Pattern

1. System runs (any language) and produces a **trace**: operation name + request + response per step
2. A small .NET validator walks the trace through `spec.Allows()`
3. The spec checks each response against expected behavior given current state

### Trace Format

The spec doesn't prescribe a format. A simple JSON array works:

```json
[
  { "operation": "CreateAccount", "request": { "AccountId": "alice" }, "response": { "StatusCode": 201, "Balance": 0 } },
  { "operation": "Deposit", "request": { "AccountId": "alice", "Amount": 100 }, "response": { "StatusCode": 200, "Balance": 100 } }
]
```

Requirements:
- Operation name must match a spec operation exactly
- Request/response JSON must deserialize into the C# types
- Trace starts from a known initial state

### Sequential Validation

```csharp
public record TraceEntry(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("request")] JsonElement Request,
    [property: JsonPropertyName("response")] JsonElement Response);

var trace = JsonSerializer.Deserialize<List<TraceEntry>>(traceJson);
var stateProfile = new StateProfile(new InitialState());
var allValid = true;

for (int i = 0; i < trace.Count; i++)
{
    var entry = trace[i];
    var operation = spec.GetOperation(entry.Operation);
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
```

### Concurrent Validation

For overlapping operations, use `spec.AllowsConcurrent()`. Format trace as array of segments — single-element = sequential, multi-element = concurrent:

```csharp
foreach (var segment in trace)
{
    if (segment.Count == 1)
    {
        // Sequential — use spec.Allows()
        var (isValid, message, next) = spec.Allows(op, req, resp, stateProfile);
        stateProfile = next;
    }
    else
    {
        // Concurrent — tries all possible orderings (N! cost, keep groups small)
        var (concValid, concMessage, nextProfile) = spec.AllowsConcurrent(stateProfile, concurrentCalls);
        stateProfile = nextProfile;
    }
}
```

## Exporting Test Plans

### Generate and Save

```csharp
var testCases = spec.GenerateTests(new InitialState(), inputs,
    new TestGenerationOptions
    {
        MaxDepth = 4,
        StateConstraint = s => /* prune unwanted states */
    });

var context = spec.CreateTestingContext();
TestCaseGenerator.SaveSequentialTestCases(context, "test-cases.json", testCases);
TestCaseGenerator.SaveConcurrentTestCases(context, "concurrent-test-cases.json", concurrentTestCases);
```

### Exported JSON Shape

```json
[
  {
    "Description": "Create alice → Deposit 100",
    "OperationCalls": [
      {
        "Name": "Create alice",
        "Input": {
          "OperationName": "CreateAccount",
          "Name": "Create alice",
          "SerializedRequest": "{\"AccountId\":\"alice\"}"
        }
      }
    ]
  }
]
```

Key fields: `OperationName` = what to call, `SerializedRequest` = the JSON payload. Concurrent test cases use `Segments` instead of `OperationCalls`.

### Executing from Any Language (Python example)

```python
import json

with open("test-cases.json") as f:
    test_cases = json.load(f)

for test_case in test_cases:
    reset_system()
    trace = []
    for call in test_case["OperationCalls"]:
        op_name = call["Input"]["OperationName"]
        request = json.loads(call["Input"]["SerializedRequest"])
        response = execute_operation(op_name, request)
        trace.append({"operation": op_name, "request": request, "response": response})
    # Feed trace back to spec.Allows() for validation
```

### Polling and Derivations

These are minor convenience features — straightforward to replicate:

- **Polling**: Check `Input.Polling` field — loop calling the poll operation with wait time between retries
- **Derivations**: If `Input.DerivedFromOperationCalls` is set, build the request from prior responses instead of using `SerializedRequest`

## Event-Based Systems

For systems that emit events (not request/response), model each event as a request with an empty response:

```csharp
public record OrderShippedEvent(string OrderId, string TrackingNumber);
public record Unit;

spec.Operation<OrderShippedEvent, Unit>("OrderShipped", (evt, state) => { ... });
```

Trace entry: `{ "operation": "OrderShipped", "request": { ... }, "response": {} }`

## Key Points to Communicate

- The spec **doesn't run** the system — it validates what happened
- No Execute bindings needed for trace validation — only Apply logic matters
- Test generation is optional — trace validation works with any source of sequences
- The built-in test runner, polling, and derivation are small conveniences, not core features
- Always include explanation strings in `Expect.That()` — they appear in violation messages

## Documentation

See [Testing Any System](../../docs/how-to/testing-any-system.md) for the full guide with examples.
