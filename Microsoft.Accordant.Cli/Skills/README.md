# Accordant Skills

Skills are focused guides for AI coding assistants (and humans) working with the Accordant model-based testing framework.

## Skill Order

Skills are numbered to reflect the typical workflow progression:

| # | Folder | Skill | Description |
|---|--------|-------|-------------|
| 00 | `00-overview` | Overview | Top-level guide, when to use which skill |
| 01 | `01-foundational` | Foundational Concepts | Core mental model, architecture, namespaces |
| 02 | `02-design-state` | Design State | `[State]` attribute, collections, `[SharedState]`, nested state |
| 03 | `03-write-operations` | Write Operations | `Operation<>`, `Apply`, `Execute`, `Expect` API, derivations, polling |
| 04 | `04-manual-testing` | Manual Testing | `Allows()`, `AllowsConcurrent()`, `StateProfile` |
| 05 | `05-generate-tests` | Generate Tests | `InputSet`, `GenerateTests()`, `TestGenerationOptions` |
| 06 | `06-execute-tests` | Execute Tests | `RunTests()`, lifecycle hooks, `TestExecutionOptions` |
| 07 | `07-concurrent-testing` | Concurrent Testing | Linearizability, `GenerateConcurrentTests()`, race conditions |
| 08 | `08-http-services` | HTTP Services | `ApiResult`, `HttpRequest`, `HttpExecutable`, REST API testing |

## How to Use

1. **Start with `00-overview`** to understand the end-to-end workflow
2. **Learn fundamentals in `01-foundational`** for core concepts
3. **Follow the numbered order** when building a new spec from scratch
4. **Jump to specific skills** when working on a particular phase

## Conventions

- Each skill folder contains a `SKILL.md` file with the full guide
- Skills with code samples include an `examples/` subfolder
- All examples use **class-based operations** (not inline lambdas)
- State classes use `[State]` attribute with `partial class` inheriting from `State`
- Specs inherit from `Spec<TState>` and use `RegisterOperationProperties()`

## Quick Start

```csharp
// 1. Define state (02-design-state)
[State]
public partial class StackState<T>
{
    public List<T> Items { get; set; } = new();
}

// 2. Define operations (03-write-operations)
public class PushOperation<T> : Operation<T, Unit, StackState<T>>
{
    public PushOperation() : base("Push") { }

    public override ExpectedOutcomes Apply(T request, StackState<T> state)
        => Expect.Unit().ThenState(next => next.Items.Add(request));

    public override Unit Execute(TestingContext context, T request)
    {
        context.Get<Stack<T>>().Push(request);
        return Unit.Value;
    }
}

// 3. Create spec (03-write-operations)
public class StackSpec : Spec<StackState<int>>
{
    public PushOperation<int> Push { get; } = new();
    public PopOperation<int> Pop { get; } = new();

    public StackSpec() { RegisterOperationProperties(); }
}

// 4. Generate and run tests (05-generate-tests, 06-execute-tests)
var spec = new StackSpec();
var inputs = new InputSet { spec.Push.With(1, "Push 1"), spec.Pop.With("Pop") };
var testCases = spec.GenerateTests(new StackState<int>(), inputs);
var results = await spec.RunTests(context, new StackState<int>(), testCases, options);
```
