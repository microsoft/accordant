---
name: Accordant Foundational Concepts
description: Core concepts of Accordant model-based testing framework - what it is, how it works, and the mental model for writing specs
---

# Accordant Model-Based Testing Framework

Accordant is a **model-based testing framework** for C#. You write a **spec** — a simplified, executable model of your system's expected behavior — and Accordant can:
1. **Validate responses** against the model at runtime (manual testing)
2. **Auto-generate test cases** by systematically exploring behavior
3. **Execute generated tests** against a real implementation
4. **Test for concurrency correctness** via linearizability checking

## Core Mental Model

A Accordant spec consists of three pillars:

### 1. State
A plain C# class (using `[State]` attribute) that represents the abstract state of your system. This is NOT your real database — it's the simplest possible representation of what your system "knows."

### 2. Operations
Class-based operations (inheriting from `Operation<TRequest, TResponse, TState>`) that define:
- **Apply**: Given a request and current state, what responses are valid and how does the state change?
- **Execute**: How to actually call the real system under test

### 3. Spec
A class inheriting from `Spec<TState>` that registers all operations and serves as the entry point for testing.

## Architecture: Spec = State + Operations

```
Spec<TState>                          // Container
├── State ([State] partial class)      // Abstract model of system state
├── Operation<TReq, TResp, TState>    // Models one API/action
│   ├── Apply(request, state)         // Pure function: what SHOULD happen
│   └── Execute(context, request)     // Side effect: call real system
└── InputSet                          // Concrete inputs for test generation
```

## Key Principle: Class-Based Operations

**Always use class-based operations** (separate classes inheriting `Operation<TReq, TResp, TState>`), never inline lambdas via `spec.Operation<>()`.

Class-based operations:
- Are reusable and testable
- Support `DerivedFrom` property for request derivation
- Support `Polling` property for async operation polling
- Provide the `Expect` context property for type-inferred assertions
- Are registered via `RegisterOperationProperties()` on the spec

## Framework Namespaces

- `Accordant` — Core types: `State`, `[State]` attribute, `Spec<>`, `Operation<>`, `Expect`, `ExpectedOutcomes`
- `Accordant.Testing` — Test infrastructure: `TestingContext`, `InputSet`, `OperationInput`, `TestGenerationOptions`, `TestExecutionOptions`, `TestCaseGenerator`, `TestCaseExecutor`
- `Accordant.Http` — HTTP testing: `HttpRequest`, `HttpResponse`, `HttpExecutable`

## Workflow Phases

1. **Design State** → Define `[State]` partial class with properties modeling system state
2. **Write Operations** → Define `Operation<TReq, TResp, TState>` subclasses with `Apply` + `Execute`
3. **Create Spec** → Define `Spec<TState>` subclass registering all operations
4. **Manual Testing** → Use `spec.Allows()` to validate responses
5. **Generate Tests** → Use `spec.GenerateTests()` with `InputSet` and options
6. **Execute Tests** → Use `spec.RunTests()` against real implementation
7. **Concurrent Testing** → Use `spec.GenerateConcurrentTests()` and `spec.AllowsConcurrent()`

## Unit Type

When an operation has no request or no response, use the `Unit` type:
- `Unit.Value` — the singleton instance
- `Expect.Unit()` — shorthand for `Expect.That<Unit>(r => true, "void")`

## Important Conventions

- State classes should be pure data — no methods, no computed properties (except fingerprint helpers)
- `Apply` is a PURE function — it receives a state and returns expected outcomes, never mutates the input state
- `ThenState(nextState => ...)` receives an auto-cloned state — mutate it directly
- `SameState()` means the operation doesn't change the state
- Operation names must be unique within a spec
- `RegisterOperationProperties()` auto-discovers all public `IOperation` properties on the spec class
