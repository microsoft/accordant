<img src="docs/images/accordant-wordmark-adaptive.svg" alt="Accordant" height="56">

**Executable behavioral specifications for .NET**

You know your system's behavior. You can explain it: "Deposit adds to the balance. Withdraw subtracts — but only if there's enough. Otherwise it fails."

That's a **spec**. It's precise. It's complete. And with Accordant, it's executable.

Write that spec once. Accordant generates hundreds of tests from it — sequences you'd never think to write, edge cases hiding in the combinations, race conditions lurking in concurrent access.

The spec isn't documentation that drifts out of sync. It's the **definition** of what correct means. An oracle that answers: "Given this state and this operation, is this response valid?"

```bash
dotnet add package Microsoft.Accordant
```

> [!div class="nextstepaction"]
> [Get Started](docs/index.md#get-started)

---

## When AI Writes Your Code

In an age of AI-assisted coding, this matters more than ever. When code gets generated faster than you can review it, you need something that checks the work. 

The spec is that check — a machine-readable definition of correctness that validates every line, whether written by you or by an AI.

---

## The Problem With Testing Today

You write tests by hand. Each one is a story: set up state, call a method, check the result.

But how many stories do you write? Ten? Fifty? There are thousands of ways your system can be called. Operations in different orders. Edge cases compounding. Concurrent requests interleaving.

You can't write them all. So you write the obvious ones and hope for the best.

And here's the thing: even when you DO write test sequences by hand, the assertions are scattered. Each test repeats logic about what's valid. Change the behavior? Update dozens of tests. Miss one? Silent drift.

Accordant flips this. **The spec holds the truth.** Whether you generate a thousand test sequences or write ten by hand, they all validate against the same model. One place defines correct. Everything else follows.

---

## What This Looks Like

Here's a spec for a bank account:

```csharp
[State]
public partial class BankState
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}

spec.Operation<(string AccountId, decimal Amount), ApiResult<decimal>>("Withdraw", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
    {
        return Expect.That<ApiResult<decimal>>(r => r.IsNotFound)
                     .SameState();
    }

    if (balance < request.Amount)
    {
        return Expect.That<ApiResult<decimal>>(r => r.IsBadRequest)
                     .SameState();
    }

    var newBalance = balance - request.Amount;
    return Expect.That<ApiResult<decimal>>(r => r.IsSuccess && r.Data == newBalance)
                 .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
});
```

This says: "If the account doesn't exist, return 404. If there's not enough money, return 400. Otherwise, withdraw succeeds and balance drops."

That's the whole truth about Withdraw. The spec tracks **state** — what the system remembers between calls. Given any starting state and any operation, the spec knows exactly what should happen and what the next state should be.

Think of it as a state machine. The spec defines every valid transition. Accordant explores the machine — every path, every reachable state. When you test against real code, you're asking: "Does the implementation follow the same transitions?"

Now give Accordant a few sample inputs — create account, deposit $100, withdraw $30, delete account — and watch it generate **50 test sequences**. Every meaningful combination. Every path through the state space.

And every single one validated against your spec.

---

## What You Can Specify

| Scenario | What Accordant Does |
|----------|---------------------|
| **CRUD operations** | Tracks entities in state, validates responses, catches "not found" when you delete twice |
| **Error conditions** | Knows when to expect success vs failure — validates both |
| **Concurrent access** | Generates interleaved executions, finds race conditions and lost updates |
| **Async workflows** | Models jobs, polling, and multi-step processes with step functions |
| **Distributed systems** | Captures retries, network errors, eventual consistency |

---

## Next Steps

- **[Overview](docs/index.md)** — Full introduction to Accordant
- **[Your First Spec](docs/tutorials/01-your-first-spec.md)** — Build a spec step by step
- **[Concepts](docs/concepts/understanding-state.md)** — How test generation works
- **[API Reference](api/)** — Generated from XML doc comments
- **[Samples](https://github.com/microsoft/accordant/tree/main/Samples)** — BankAccount, Stack, JobQueue, and more
