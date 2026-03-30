---
name: Accordant Overview
description: Top-level guide to the Accordant model-based testing framework — what it is, which skill to use when, and the end-to-end workflow
---

# Accordant — A Model-Based Testing Framework

Accordant is a **model-based testing framework** for C#. You write a spec — a simplified, executable model of your system's expected behavior — and the framework uses it to:
- **Validate responses** — check any API response against the model
- **Auto-generate test cases** — systematically explore behavior to produce sequential and concurrent tests
- **Verify concurrency correctness** — confirm linearizability under parallel operations

The core idea: write the model once, get comprehensive testing for free.

## When to Use Which Skill

| Phase | Skill | What it covers |
|-------|-------|----------------|
| **Understand concepts** | `foundational` | Core mental model, architecture, namespaces, `Unit` type, conventions |
| **Define state** | `design-state` | `JsonState`, collections, `[JsonAtomic]`, nested state, initialization |
| **Write operations** | `write-operations` | `Operation<TReq,TResp,TState>`, `Apply`, `Execute`, `Expect` API, derivations, polling, `AsyncOperation`, creating the `Spec` class |
| **Manual testing** | `manual-testing` | `Allows()`, `AllowsConcurrent()`, `StateProfile`, `TestingContext`, manual polling for async operations |
| **Generate tests** | `generate-tests` | `InputSet`, `With()`, `GenerateTests()`, `GenerateConcurrentTests()`, `TestGenerationOptions`, `StateConstraint` |
| **Execute tests** | `execute-tests` | `RunTests()`, `TestExecutionOptions`, lifecycle hooks (`BeforeEach`, `AfterEach`, etc.), `OnStepExecuted` |
| **Concurrency** | `concurrent-testing` | Linearizability, concurrent test generation, `AllowsConcurrent()`, race condition patterns |
| **HTTP APIs** | `http-services` | `ApiResult<T>`, `HttpRequest`, `HttpExecutable`, REST API testing patterns |

## End-to-End Workflow

### Step 1: Design your state (`design-state`)
Model the abstract state of your system as a `JsonState` subclass. Keep it minimal — only what matters for observable behavior.

```csharp
public class MyState : JsonState
{
    public Dictionary<string, ItemState> Items { get; set; } = new();
}
```

### Step 2: Write operations (`write-operations`)
For each API endpoint or action, create a class-based operation:
- `Apply(request, state) → ExpectedOutcomes` — what SHOULD happen (pure function)
- `Execute(context, request) → response` — how to call the real system

### Step 3: Create the spec (`write-operations`)
Define a `Spec<TState>` subclass that registers all operations via `RegisterOperationProperties()`.

### Step 4: Validate manually (`manual-testing`)
Write targeted tests using `spec.Allows()` to verify specific scenarios. Use manual polling for async operations.

### Step 5: Auto-generate tests (`generate-tests`)
Define an `InputSet` with concrete requests, configure `TestGenerationOptions`, and call `spec.GenerateTests()` or `spec.GenerateConcurrentTests()`.

### Step 6: Execute tests (`execute-tests`)
Run generated tests against the real system with `spec.RunTests()`, using lifecycle hooks to manage setup/teardown.

### Step 7: Test concurrency (`concurrent-testing`)
Generate and run concurrent tests to verify linearizability under parallel load.

## Key Design Decisions

- **Always use class-based operations** — never inline lambdas via `spec.Operation<>()`
- **State is pure data** — no methods, no computed properties (except fingerprint helpers)
- **Apply is a pure function** — never mutate the input state; use `ThenState` (auto-clones)
- **Start simple** — begin with a basic state and a few operations, then grow incrementally

## Common Patterns Reference

| Pattern | Where to look |
|---------|---------------|
| Simple CRUD operations | `write-operations` → Stack example |
| Error handling (not found, conflict) | `write-operations` → conditional Apply |
| Async background processing | `write-operations` → AsyncOperation, Polling |
| Response-dependent state | `write-operations` → ThenState with response |
| Request derivation (GET from POST) | `write-operations` → DerivedFrom property |
| Manual polling loop | `manual-testing` → Manual Polling section |
| State explosion prevention | `generate-tests` → StateConstraint |
| Race condition testing | `concurrent-testing` → Patterns section |
| HTTP API testing | `execute-tests` → HTTP API example |

