# Quick Start

> **Time**: 5 minutes  
> **Goal**: See Accordant generate and run tests from a simple spec

---

## The Service We're Testing

We have a banking REST API — a real ASP.NET Core application with Entity Framework, a database, HTTP routing, the works. It handles accounts: you can create them, deposit money, withdraw money, check balances, and delete them.

The implementation lives in [BankAccount.Api](../Samples/BankAccount/BankAccount.Api/). Take a quick look if you want — you'll see a controller, an EF DbContext, entity classes, and all the infrastructure you'd expect. About 170 lines of code handling HTTP verbs, database operations, validation, and error responses.

Now here's the question: **how do you know it works correctly?**

You could write test after test. "Create an account, check the balance is zero." "Deposit $100, check the balance is 100." "Withdraw more than the balance, expect an error." Each test is its own story, and you have to think of all the stories.

Or you could write a **spec** — a single source of truth that says what the system should do, and let Accordant figure out the test cases.

---

## Clone and Run

```bash
git clone https://github.com/microsoft/Accordant.git
cd Accordant/Samples/BankAccount/BankAccount.Api.Tests
dotnet test
```

You'll see:

```
Generated and ran 50 test cases
Test summary: total: 1, failed: 0, succeeded: 1
```

Fifty test cases. From the spec, Accordant generated meaningful sequences and validated each one. Let's look at what that spec actually says.

---

## The Spec

Open [BankAccountTests.cs](../Samples/BankAccount/BankAccount.Api.Tests/BankAccountTests.cs). 

First, the state. The implementation has a database, entities, contexts. The spec has this:

```csharp
[State]
public partial class BankState : State
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}
```

That's the entire model. A dictionary mapping account IDs to balances. No database schema, no entity relationships — just the essential structure of "what does this system remember?"

Now the operations. Here's Withdraw:

```csharp
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

Read this as: "Given a withdraw request and the current state, what should happen?"

- Account doesn't exist? Return 404, state unchanged.
- Not enough balance? Return 400, state unchanged.
- Otherwise? Return the new balance, update state.

That's the complete truth about Withdraw. The spec doesn't know about HTTP verbs or database transactions — it just knows the rules.

The full spec has 5 operations (CreateAccount, GetBalance, Deposit, Withdraw, DeleteAccount) in about 60 lines. Compare that to the 170-line implementation. The spec captures the logic; the implementation handles the mechanics.

---

## What Accordant Did

When you ran `dotnet test`, here's what happened behind the scenes.

The test defined some sample inputs — a few account IDs, some deposit and withdraw amounts:

```csharp
var inputs = new InputSet
{
    spec.GetOperation("CreateAccount").With("alice"),
    spec.GetOperation("CreateAccount").With("bob"),
    spec.GetOperation("Deposit").With(("alice", 100m)),
    spec.GetOperation("Withdraw").With(("alice", 30m)),
    spec.GetOperation("Withdraw").With(("alice", 200m)),  // more than balance!
    spec.GetOperation("DeleteAccount").With("alice"),
};
```

Accordant took those inputs and explored sequences of operations, up to 4 steps deep. Not random sequences — meaningful ones, guided by the state space:

- Create alice → Deposit 100 → Withdraw 30 → Check balance
- Create alice → Withdraw 200 (fails — no balance yet!)
- Deposit to non-existent account (fails — not found!)
- Create alice → Delete alice → Deposit to alice (fails — she's gone!)

Each sequence ran against the real HTTP API. Each response was checked against what the spec predicted. Fifty sequences. Fifty validations.

You can also write test sequences by hand — and many teams do, for specific scenarios they care about. The point isn't that you *never* write tests manually. The point is that every test, whether generated or hand-written, validates against the same spec. One source of truth.

---

## The Key Ideas

**State** is what the system looks like from the outside — what an external observer would see. Here, it's "which accounts exist and what are their balances?" The spec doesn't care how the implementation stores this (database tables, in-memory cache, files). It only cares about the observable state.

**Operations** are actions with inputs and outputs. The spec says what each operation should return and how it changes state.

**Expect.That(...)** describes what the response should look like. **ThenState(...)** says how the state changes. **SameState()** means it doesn't change.

The spec is an **oracle** — given any state and any operation, it can answer: "What should happen?" Accordant uses this oracle to validate every generated test.

---

## Spec vs Implementation

The spec describes **what** should happen. The implementation handles **how**.

The spec is 60 lines with a dictionary and some conditionals. The implementation is 170 lines with HTTP routing, EF contexts, entity mappings, and error handling. They express the same behavior — but the spec is just the rules, while the implementation is the machinery.

When you change business logic, you update the spec. When you change infrastructure (swap databases, change serialization), the spec stays the same. The spec is stable because it only captures what matters.

---

## Next Steps

**Understand the concepts in depth:**  
→ [Tutorial: Your First Spec](tutorials/01-your-first-spec.md)

**Learn how test generation works:**  
→ [Concepts: How Test Generation Works](concepts/how-test-generation-works.md)
