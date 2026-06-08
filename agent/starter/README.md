# Starter Example

This folder contains a complete, minimal Accordant spec for a key-value store. It demonstrates all the pieces that need to come together:

- **State** — what the system tracks
- **Spec** — operations with expected outcomes
- **Test** — wiring it up and running

Use this as a reference when helping a user build their first spec. Adapt the patterns to their domain — don't copy this verbatim.

## Files

| File | Purpose |
|------|---------|
| `ExampleState.cs` | The `[State]` class — minimal, just what operations need |
| `ExampleSpec.cs` | A few related operations with error cases and success cases |
| `ExampleTests.cs` | Wiring up execution, state reset, inputs, and running tests |

## Key Patterns Shown

- `[State]` partial class with auto clone/equality/hash
- `spec.Operation<TReq, TResp>(...)` with conditional logic
- `Expect.That(...)` with predicates and explanations
- `.SameState()` for errors and reads
- `.ThenState(...)` for mutations
- Execution bindings with `spec.ExecuteWith<T>().BindAsync(...)`
- `InputSet` with labeled inputs
- `BeforeEachAsync` for state reset
- `spec.RunTests(...)` to generate and execute
