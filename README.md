<!-- Keep in sync with docs/index.md (adjust paths: docs/X -> X, Samples/ -> ../Samples/) -->

<img src="docs/images/accordant-wordmark-adaptive.svg" alt="Accordant" height="48">

**Executable behavioral specifications for .NET**

[Documentation](https://microsoft.github.io/accordant) · [NuGet](https://www.nuget.org/packages/Microsoft.Accordant) · [Samples](Samples/)

Accordant is a framework for model-based testing. You write a *spec* — executable code that captures the rules of your system. Given any state and any operation, the spec defines what the response should be and how the state should change. Accordant then generates hundreds of tests from this spec, runs them against your real implementation, and validates every response — telling you exactly where the implementation deviates from the rules.

---

## How You Test Today

Say you're building a banking service. You write tests like this:

```csharp
[Test]
public async Task Withdraw_WithSufficientBalance_Succeeds()
{
    await client.CreateAccount("alice");
    await client.Deposit("alice", 100);
    
    var result = await client.Withdraw("alice", 30);
    
    Assert.True(result.IsSuccess);
    Assert.Equal(70, result.Data);
}
```

Now imagine thirty more — insufficient balance, nonexistent account, duplicate creates, deposits after deletes, different orderings. Each test carries its own assertions, but they're all expressing pieces of the same *contract*: the rules for how your system behaves. Those rules end up scattered across your test suite, repeated in slightly different forms.

What if you wrote them once?

---

## Extract the Contract

Here's the complete contract for Withdraw, in one place:

```csharp
spec.Operation<WithdrawRequest, WithdrawResponse>("Withdraw", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
        return Expect.That(r => r.IsNotFound).SameState();

    if (balance < request.Amount)
        return Expect.That(r => r.IsBadRequest).SameState();

    var newBalance = balance - request.Amount;
    return Expect.That(r => r.IsSuccess && r.Balance == newBalance)
                 .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
});
```

`spec.Operation` registers an operation. The lambda receives the request and current state, and returns what the response should look like (`Expect.That(...)`) paired with how the state should change (`.SameState()` or `.ThenState(...)`). Read it as a truth table: account doesn't exist → not-found, state unchanged; insufficient balance → bad-request, state unchanged; otherwise → success with new balance, state updated.

The state is whatever information you need to define what correct behavior means. For banking, that's just accounts and their balances:

```csharp
[State]
public partial class BankState
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}
```

The `[State]` attribute triggers source generation for cloning and equality — you define the data structure, Accordant handles the rest. The state is intentionally simpler than the real implementation. We treat the system as a black box; we don't care whether data lives in SQL Server, Redis, or a flat file.

→ [See the full BankAccount spec](Samples/BankAccount/)

---

## What This Unlocks

Provide a few sample inputs — some account IDs, some amounts:

```csharp
// Operation handles obtained via spec.GetOperation<TRequest, TResponse>(name)
var inputs = new InputSet
{
    createAccount.With(new CreateAccountRequest("alice"), "Create(alice)"),
    deposit.With(new DepositRequest("alice", 50m), "Deposit(alice, 50)"),
    deposit.With(new DepositRequest("alice", 100m), "Deposit(alice, 100)"),
    withdraw.With(new WithdrawRequest("alice", 30m), "Withdraw(alice, 30)"),
    withdraw.With(new WithdrawRequest("alice", 70m), "Withdraw(alice, 70)"),
    deleteAccount.With(new DeleteAccountRequest("alice"), "Delete(alice)"),
};
```

Because the spec defines what each operation does to state, Accordant can *simulate* the system — predicting what happens without running real code. Starting from an empty state, it tries every operation with every input. Operations that change the state produce new nodes (e.g., creating an account that doesn't exist); operations that don't change state loop back to the same node (e.g., withdrawing from a nonexistent account). From each new node, it tries every operation again. The reachable states naturally unfold into a graph:

![State graph from BankAccount sample](docs/images/bank-account-state-graph.png)

Accordant then picks paths through this graph as test sequences:

| # | Sequence | Category |
|---|----------|----------|
| 1 | `Create(alice)` → `Deposit(alice, 100)` → `Withdraw(alice, 70)` | ✓ success path |
| 2 | `Create(alice)` → `Deposit(alice, 50)` → `Withdraw(alice, 70)` | ✗ insufficient funds |
| 3 | `Withdraw(alice, 30)` | ✗ 404 on non-existent account |
| 4 | `Create(alice)` → `Create(alice)` | ✗ 409 duplicate |
| 5 | `Create(alice)` → `Deposit(50)` → `Delete(alice)` | ↻ lifecycle |
| 6 | `Create(alice)` → `Deposit(100)` → `Deposit(50)` | ✓ accumulate balance |

These aren't random — they're systematic walks designed to exercise different branches. Each sequence is run against the real system, and the spec validates every response along the way:

```
Generated 31 test cases
Executed against BankAccount API
Results: 31 passed, 0 failed
```

From six sample inputs, Accordant generated thirty-one test cases that cover the reachable state space, with every response validated automatically.

→ [How Test Generation Works](docs/concepts/how-test-generation-works.md)

### It's Not Just Auto-Generation

The spec separates two concerns that traditional tests tangle together: **deciding what sequence to run** and **deciding whether the result is correct**. The spec handles the second part — it's an oracle. You bring whatever sequences you like.

That means you can pair the oracle with any source of test sequences:

- Hand-written scenarios that exercise specific edge cases
- Accordant's built-in state-graph exploration (what we just saw)
- Your own custom generation algorithms
- A fuzzer producing random operation streams
- Sequences replayed from production logs

In all cases, validation is the same single call:

```csharp
var (isValid, message, nextState) = spec.Allows(operation, request, response, currentState);
```

This also improves your existing hand-written tests. Compare — today each test carries bespoke assertions that duplicate business logic:

```csharp
var r1 = await client.CreateAccount("alice");
Assert.True(r1.IsSuccess);

var r2 = await client.Deposit("alice", 100);
Assert.True(r2.IsSuccess);
Assert.Equal(100, r2.Balance);         // ← business rule repeated here

var r3 = await client.Withdraw("alice", 30);
Assert.True(r3.IsSuccess);
Assert.Equal(70, r3.Balance);          // ← and here
```

With the spec as oracle, the same test becomes:

```csharp
var r1 = await client.CreateAccount("alice");
spec.Validate(createOp, request1, r1);  // ← one call, all rules checked

var r2 = await client.Deposit("alice", 100);
spec.Validate(depositOp, request2, r2); // ← same call, same rules

var r3 = await client.Withdraw("alice", 30);
spec.Validate(withdrawOp, request3, r3); // ← same call, same rules
```

The test is now *just a sequence* — it says what operations to run, and the spec says whether the responses are correct. When the contract changes, you update the spec once and every test that uses it automatically validates against the new rules.

→ [Conformance Testing](docs/concepts/conformance-testing.md)

---

## Why This Matters

**Reviewable** — Business rules live in one place: 60 lines of clear logic rather than scattered across hundreds of assertions. Reviewing a spec change is easier than reviewing changes to dozens of test files — you see the rule, not the repetition.

**Confidence through change** — Refactor the implementation freely. When requirements change, update the spec and test generation adapts automatically. No hunting through test files to find every assumption that needs updating.

**Natural fit with AI** — A spec lets you precisely state what you want and mechanically verify that you got it — through automatically generated test cases that check the implementation against your intent. AI can help write the spec, and the spec provides strong guardrails when AI generates the implementation.

---

## And More

The same spec enables other kinds of testing:

- **Concurrency testing** — Run operations in parallel, check that results are *linearizable* (explainable by some sequential ordering). Catches race conditions, double bookings, lost updates. → [Testing Race Conditions](docs/tutorials/05-testing-race-conditions.md)

- **Indefinite failures** — A socket timeout is ambiguous: maybe the request never reached the server, or maybe it did but you never heard the response. Specs can encode this non-determinism — the operation either happened or didn't. Combined with deterministic simulation (injecting controlled failures), you can test retry logic, idempotency, and recovery paths. → [Modeling Indefinite Failures](docs/how-to/indefinite-failures.md)

- **Async workflows** — Model multi-step processes, background jobs, polling for completion. The spec tracks pending work and expected completions. → [Step Functions & Async](docs/concepts/step-functions-and-async.md)

- **Language-independent testing** — Your system doesn't *have* to be in .NET. Export test plans to JSON, execute from any language, and validate traces against the spec. → [Testing Any System](docs/how-to/testing-any-system.md)

---

## Get Started

Install the package ([NuGet](https://www.nuget.org/packages/Microsoft.Accordant)):

```bash
dotnet add package Microsoft.Accordant
```

### Learning Paths

**Want an AI assistant to guide you?**
Point your AI coding agent (Copilot, Cursor, Claude Code, etc.) at [`agent/INSTALL.md`](agent/INSTALL.md) and it will walk you through setup and your first spec — interactively, using your actual project and domain.

**Just want to see it work?**
Clone the repo and run the BankAccount sample:
```bash
git clone https://github.com/microsoft/accordant.git
cd accordant/Samples/BankAccount/BankAccount.Api.Tests
dotnet test
```

You'll see output like:
```
Passed AutoGeneratedSequentialTests [2 s]
Passed HandWrittenScenarios [52 ms]
Passed VisualizeStateSpace [20 ms]
```

**Building your first spec?**
- [Your First Spec](docs/tutorials/01-your-first-spec.md) — Define state, operations, and expectations
- [Handling Errors](docs/tutorials/02-handling-errors.md) — Model error conditions
- [Response-Dependent State](docs/tutorials/03-response-dependent-state.md) — When state depends on responses
- [Visualizing State Space](docs/tutorials/04-visualizing-state-space.md) — See the state graph and generated tests

**Going deeper?**
- [Concurrency Testing](docs/tutorials/05-testing-race-conditions.md) — Find race conditions with linearizability checking
- [Async Workflows](docs/tutorials/06-async-operations-polling.md) — Model background jobs and polling

### Documentation

| Type | What it covers |
|------|----------------|
| **[Tutorials](docs/tutorials/index.md)** | Step-by-step guides to learn Accordant |
| **[Concepts](docs/concepts/index.md)** | Understand the theory — model-based testing, linearizability, state graphs |
| **[How-To Guides](docs/how-to/index.md)** | Solve specific problems — "how do I reset state between tests?" |
| **[Samples](Samples/)** | Working code — BankAccount, TodoList, and more |
| **[API Reference](https://microsoft.github.io/accordant/api/Microsoft.Accordant.html)** | Complete API documentation |

---

## Community

Have questions, ideas, or want to share what you're building? Join the conversation on [GitHub Discussions](https://github.com/microsoft/accordant/discussions).

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and contribution guidelines.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

## License

[MIT](LICENSE)