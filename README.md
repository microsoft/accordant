<img src="docs/images/accordant-wordmark-adaptive.svg" alt="Accordant" height="48">

**Executable behavioral specifications for .NET**

You can describe your system's rules: *"Withdraw succeeds if there's enough balance, fails otherwise, and the balance updates accordingly."* With Accordant, that description becomes **code** — a spec that's precise, testable, and never drifts out of sync.

Use the spec to generate test cases automatically, or write tests by hand and let the spec validate each one. Either way, the logic that decides "is this behavior correct?" lives in one place.

---

## The Problem With Testing Today

You write tests one by one. Each test is a story: set up state, call a method, check the result.

But how many stories do you write? There are thousands of ways your system can be called — operations in different orders, edge cases compounding, concurrent requests interleaving. You can't write them all.

And when you *do* write tests, the validation logic gets scattered. Each test repeats what's valid. Change the behavior? Update dozens of assertions. Miss one? Silent drift.

## The Solution

Accordant lets you write that spec as code — and then **execute** it.

```csharp
[State]
public partial class BankState : State
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}

spec.Operation<WithdrawRequest, ApiResult<decimal>>("Withdraw", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
        return Expect.That<ApiResult<decimal>>(r => r.IsNotFound).SameState();

    if (balance < request.Amount)
        return Expect.That<ApiResult<decimal>>(r => r.IsBadRequest).SameState();

    var newBalance = balance - request.Amount;
    return Expect.That<ApiResult<decimal>>(r => r.IsSuccess && r.Data == newBalance)
                 .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
});
```

This says everything about Withdraw: when it succeeds, when it fails, and how state changes. The spec tracks what the system remembers between calls. Given any starting state and any operation, Accordant knows exactly what should happen next.

From this, Accordant generates **50+ test sequences** automatically — every meaningful path through the state space, validated against your spec.

## Why Specs Matter

**One source of truth.** The spec defines what "correct" means. Whether you generate a thousand tests or write ten by hand, they all validate against the same model. Change the behavior? Update one place.

**Machine-checkable contracts.** The spec isn't prose that drifts out of sync. It's executable code that answers: *"Given this state and this operation, is this response valid?"*

**AI-ready development.** When AI generates code faster than you can review it, you need something that checks the work. The spec is that check — a formal definition of correctness that validates every line, whether written by you or by a model.

**Find bugs you'd never think to test.** Accordant explores combinations systematically: operations in unexpected orders, race conditions with concurrent access, edge cases hiding in the state space. The spec catches them all.

## What You Can Specify

| Scenario | What Accordant Does |
|----------|---------------------|
| **CRUD operations** | Tracks entities in state, validates responses, catches "not found" when you delete twice |
| **Error conditions** | Knows when to expect success vs failure — validates both |
| **Concurrent access** | Generates interleaved executions, finds race conditions and lost updates |
| **Async workflows** | Models jobs, polling, and multi-step processes with step functions |
| **Distributed systems** | Captures retries, network errors, eventual consistency |

The same approach scales from a Stack class to a REST API to a distributed system.

## Installation

```bash
dotnet add package Microsoft.Accordant
```

## Quick Start

**5 minutes** — Run the BankAccount sample and see "50 tests ✓":

```bash
git clone https://github.com/microsoft/accordant.git
cd accordant/Samples/BankAccount/BankAccount.Api.Tests
dotnet test
```

→ [Quick Start Guide](docs/quickstart.md)

## Documentation

- **[Quick Start](docs/quickstart.md)** — See it work in 5 minutes
- **[Your First Spec](docs/tutorials/01-your-first-spec.md)** — Build a spec step by step
- **[Understanding State](docs/concepts/understanding-state.md)** — How specs track system state
- **[Operations & Expectations](docs/concepts/operations-and-expect.md)** — Defining behavior
- **[Testing Race Conditions](docs/tutorials/05-testing-race-conditions.md)** — Concurrent access
- **[Step Functions & Async](docs/concepts/step-functions-and-async.md)** — Background work and workflows

**[Full Documentation](docs/index.md)** · **[Samples](Samples/)**

## Samples

| Sample | Description |
|--------|-------------|
| [BankAccount](Samples/BankAccount/) | REST API with CRUD, deposits, withdrawals, error handling |
| [Stack](Samples/Stack/) | Classic data structure — push, pop, peek |
| [TodoList](Samples/TodoList/) | Create, complete, delete tasks |
| [Booking](Samples/Booking/) | Reservation system with conflicts |
| [JobQueue](Samples/JobQueue/) | Async job processing with polling |
| [TerminationDetection](Samples/TerminationDetection/) | Distributed algorithm verification |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and contribution guidelines.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

## License

[MIT](LICENSE)