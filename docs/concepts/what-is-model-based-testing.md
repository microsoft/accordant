# What is Model-Based Testing?

> **TL;DR**: Instead of scattering assertions across many tests, write a **model** that defines what correct behavior means. The model becomes your single source of truth — all tests, whether generated or hand-written, validate against it.

---

## The Problem: Scattered Semantics

If you've written tests before, you know the pattern: set up some state, call a method, check the result. Repeat for every scenario you can think of.

### In Traditional Testing

Each test carries its own assertions — its own local opinion about what "correct" means:

```csharp
// Test 1
account.Deposit(100);
Assert.Equal(100, account.Balance);  // assertion here

// Test 2  
account.Deposit(100);
account.Withdraw(30);
Assert.Equal(70, account.Balance);   // similar assertion here

// Test 3, 4, 5... more assertions scattered across the codebase
```

This works until it doesn't. What happens when the business logic changes? Maybe withdrawals should now return the new balance, or maybe insufficient funds should throw a different exception. You update the code — but now you have to hunt down every test that made assumptions about that behavior. Update 20 assertions. Miss one, and you have **silent drift**: tests that pass but are actually wrong.

The fundamental problem is that the *truth* about your system's behavior is smeared across dozens of test files. There's no single place that says "this is how withdrawals work."

### In Spec-Driven Development

The rise of AI-assisted coding has made this problem more acute. You write a spec in English — maybe a README, an ADR, or comments describing the expected behavior. Then AI generates the implementation.

But how do you know the generated code actually matches your spec?

English is ambiguous. "Should return an error if the balance is insufficient" — but what kind of error? What's the message? Does it throw or return a result? You end up manually reviewing every line, comparing code to prose, hoping you catch the discrepancies.

### The Core Issue

Whether you're writing traditional tests or working with AI-generated code, the problem is the same: the **semantics** of your system — what it *should* do — lives in the wrong place. It's either scattered (across test assertions) or informal (in English documentation).

There's no single, precise, executable source of truth.

---

## The Idea: A Formal Model of Semantics

What if, instead of encoding behavior expectations in scattered assertions, you wrote them down once in a **model**?

A model (also called a **spec**) answers one question precisely:

> "Given state X and operation Y, what response is valid? What's the new state?"

An **operation** is anything that changes or observes state — an API endpoint, a method call, a command, an event handler. The model describes the *semantics*: what each operation should do. Here's what a model looks like in Accordant:

```csharp
// The state: just a dictionary of account balances
[State]
public partial class BankState
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}

// The model for Withdraw
spec.Operation<WithdrawRequest, WithdrawResponse>("Withdraw", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
    {
        return Expect.That<WithdrawResponse>(r => r.IsNotFound)
                     .SameState();
    }

    if (balance < request.Amount)
    {
        return Expect.That<WithdrawResponse>(r => r.IsBadRequest)
                     .SameState();
    }

    var newBalance = balance - request.Amount;
    return Expect.That<WithdrawResponse>(r => r.IsSuccess && r.Balance == newBalance)
                 .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
});
```

Read this as a truth table. Given a withdraw request and the current state:

- **Account doesn't exist?** Expect a 404 response. State doesn't change.
- **Insufficient balance?** Expect a 400 response. State doesn't change.
- **Otherwise?** Expect success with the new balance. State updates.

That's the complete truth about withdrawals, written once. The model is:

- **Precise** — no ambiguity about what counts as "correct"
- **Executable** — the machine can check real responses against these expectations
- **Single source of truth** — when behavior changes, update it here, not in dozens of test files

Every test — whether you write it by hand or let the framework generate hundreds of them — validates against this one model.

### "Wait, This Looks Like a Mock..."

If you've used mocking frameworks, this code might look familiar. But models and mocks serve fundamentally different purposes:

| | Mock/Fake/Simulator | Model/Spec |
|---|---------------------|------------|
| **Purpose** | Replace a dependency you can't or don't want to call | Validate that the real system behaves correctly |
| **What it does** | Returns canned or simulated data | Judges whether a response is valid |
| **Used when** | Isolating a unit from its dependencies | Verifying the integrated system works |

A **mock** says: "Pretend the database returned this."  
A **model** says: "The real system returned this — is that correct?"

The model above doesn't *replace* your bank service. It *validates* that when you call the real service, the actual response matches what the model says should happen. You run both the real system and the model, and the model acts as the oracle — the judge of correctness.

For a deeper exploration of how models relate to fakes and simulators — including where they genuinely differ and how you can derive one from the other — see [Models and Fakes](models-vs-fakes.md).

---

## It's a State Machine

At its core, a model is a state machine.

**State** is what the system remembers between operations. For a bank account, it's the balances. For a shopping cart, it's the items. For a job queue, it's which jobs exist and their status.

**Operations** cause transitions. Each operation takes the system from one state to another (or leaves it unchanged, for queries or failed mutations).

Every valid behavior of your system is a *path* through this state graph:

```
             Deposit(100)
    [0] ─────────────────────► [100]
     │                           │
     │ Withdraw(50)              │ Withdraw(30)
     │ (fails: insufficient)     │
     ▼                           ▼
    [0]                        [70]
```

Starting from an empty account (balance 0), depositing makes the balance 100. From there, withdrawing 30 takes you to 70. But trying to withdraw 50 from an empty account fails — you stay at 0.

The model defines all these transitions explicitly. Accordant uses this structure to explore paths through your system, generating test sequences that cover the meaningful combinations.

---

## Model ≠ Implementation

Here's something crucial: **the model is intentionally simpler than the implementation.**

Think about a real banking system. The implementation deals with HTTP routing, Entity Framework, database connections, transaction isolation, retry policies, logging, metrics, authentication, and a hundred other concerns. It might be thousands of lines of code spread across dozens of files.

The model? A dictionary and some conditionals:

| System | Implementation | Model |
|--------|----------------|-------|
| Bank account | EF + SQL + HTTP + retry logic + auth | `Dictionary<string, decimal>` |
| Job queue | Background workers, message queues, polling | `Dictionary<string, JobStatus>` |
| Shopping cart | Microservices, caching, inventory checks | `Dictionary<string, List<Item>>` |

The model captures **what** should happen — the semantics, the business rules. The implementation handles **how** — the mechanics, the infrastructure, the edge cases of real systems.

This asymmetry is a feature, not a bug. You *want* the model to be simple. Simple means readable. Simple means reviewable. Simple means you can look at 60 lines of model code and be confident it's correct — then use that confidence to validate 600 lines of implementation.

You might even have multiple models for the same system. Consider a distributed database: one model might focus on **consistency** — tracking which writes have been acknowledged and asserting that reads return the latest value. Another model might focus on **availability** — tracking response times and asserting that operations complete within SLAs. Same system, different properties to verify.

---

## Related: Property-Based Testing

**Property-based testing** combines two ideas: you define *properties* (invariants that should always hold), and the framework *fuzzes* inputs — generating many random values to try to find violations.

For example: "sorting a list twice gives the same result as sorting once." The framework generates thousands of random lists and checks that this property holds for all of them.

**Model-based testing is property-based testing over state.**

Instead of stateless properties like "sorting is idempotent," you're defining properties over *sequences of operations* and the *state* that evolves as those operations execute. The model tracks state; each operation's expected outcome depends on the current state. When you say "withdraw should fail if balance is insufficient," that's a state-dependent property — a property that only makes sense in the context of what happened before.

Accordant focuses on making it easy to express models concisely — capturing the patterns and situations that arise in real services: error handling, async operations, non-determinism, concurrent access. It intentionally doesn't package a fuzzer. Instead, it explores the state graph to generate meaningful operation sequences from sample inputs, using the model as an oracle to judge correctness. If you want fuzzed inputs, you can combine Accordant with an external fuzzer — the fuzzer generates inputs, Accordant handles sequences and validation.

---

## What You Get

When you write a model and use Accordant, you get several things:

**1. A single source of truth.** The model defines what "correct" means. If behavior changes, you update the model once — not assertions scattered across dozens of tests. Hand-written tests validate against the model. Generated tests validate against the model. Everyone agrees on what's correct.

**2. Automatic test generation.** You provide sample inputs — "here are some account IDs to try, here are some deposit amounts." Accordant explores the *state graph*, trying operations in different orders, finding paths through your system that you wouldn't think to test manually. Six sample inputs might expand to hundreds of test sequences.

**3. Concurrency testing.** Accordant can run operations concurrently and check all possible interleavings. Race conditions, double-booking bugs, lost updates — the kinds of bugs that hide in timing — become visible.

**4. An oracle for AI-generated code.** When AI writes your implementation, the model answers: "Is this correct?" The model is formal and precise — not ambiguous English. It's a machine-readable spec that can validate any implementation, whether written by you or generated by an AI.

---

## The Model Can Be Partial

One of the most practical aspects of model-based testing is that **you don't have to model everything**. A model can be partial, incomplete, or deliberately simplified — and it's still valuable.

This might feel strange at first. If the model is supposed to be the "source of truth," shouldn't it be complete? Not necessarily. An **under-specified model** gives fewer guarantees, but those guarantees still hold. It's better to have a simple model that captures the core behavior than no model at all.

### Start Simple, Add Detail Over Time

You might start by modeling just the happy paths — the main success scenarios. Error handling, edge cases, and corner conditions can come later. The model grows with your understanding and your needs.

### Ignore Details That Don't Matter Yet

A fully-specified model might validate exact error messages, specific error codes, and detailed response structures:

```csharp
// Fully specified - validates everything
if (balance < amount)
    return Expect.That<ApiResult>(r => 
        r.StatusCode == 400 && 
        r.Error == "Insufficient funds" &&
        r.ErrorCode == "BALANCE_TOO_LOW");
```

But maybe you don't care about the exact error message right now. Maybe you just want to make sure the system fails gracefully:

```csharp
// Under-specified - still catches the important thing
if (balance < amount)
    return Expect.That<ApiResult>(r => r.StatusCode == 400);
```

This under-specified model will catch bugs where the system incorrectly allows overdrafts. It won't catch bugs in error message formatting — but maybe that's fine for now.

### Model Only the Operations You Care About

If your system has 20 endpoints, you don't have to model all 20 before you start testing. Model `CreateAccount`, `Deposit`, and `GetBalance`. Skip `TransferBetweenAccounts` and `CloseAccount` for now.

Accordant will only explore the operations you've modeled. You get value immediately from the operations you've specified, and you can expand coverage over time.

### Allow Multiple Valid Outcomes

Sometimes the system can legitimately end up in one of several states, and you won't know which until you observe more. This isn't a flaw — real distributed systems genuinely have this property.

**Classic example: network timeout.** You call `CreateOrder`, then the network times out. What actually happened?

- Maybe your request never reached the server. The order doesn't exist.
- Maybe the server processed your request successfully, but the response got lost on the way back. The order *does* exist.

From your perspective as the client, **both states are possible**. You can't tell which until you check. A good model expresses this uncertainty:

```csharp
// CreateOrder can succeed, fail, or timeout
// On timeout, we don't know if it happened or not — both states are valid
return Expect.OneOf(
    Expect.That<ApiResult>(r => r.IsSuccess)
          .ThenState(s => s.WithOrder(orderId)),
          
    // Timeout case 1: request never reached server
    Expect.That<ApiResult>(r => r.IsTimeout)
          .SameState(),
          
    // Timeout case 2: server processed it, but response was lost
    Expect.That<ApiResult>(r => r.IsTimeout)
          .ThenState(s => s.WithOrder(orderId))
);
```

**How Accordant handles this:** After the timeout, the framework tracks *multiple possible states* simultaneously. When you call the next operation, the framework validates the response against *all* candidate states. The outcome narrows down (or sometimes widens) the possibilities.

Call `GetOrder(orderId)` and it returns the order? Now we know it was created — the model narrows to that state. It returns 404? Now we know it wasn't created — the model narrows to the other state.

This ability to track uncertainty, then resolve it through observation, is what makes Accordant powerful for testing real distributed systems.

**Other scenarios where multiple states arise:**
- **Async background work** — a job is processing; has it finished yet? → [Step Functions & Async Behavior](step-functions-async.md)
- **Race conditions** — two concurrent requests compete; either could "win" → [Testing Race Conditions](../tutorials/05-testing-race-conditions.md)
- **Server-generated values** — IDs and timestamps that you capture from responses → [Response-Dependent State](../tutorials/03-response-dependent-state.md)

These are advanced topics for when you need them. The core idea is simpler: **a model doesn't have to be perfect to be useful.**

---

## Next Steps

You've learned what model-based testing is and why it matters. Ready to try it?

- **See it in action** → [Overview](../index.md#get-started) — get started in minutes
- **Build your first spec** → [Tutorial 1: Your First Spec](../tutorials/01-your-first-spec.md) — learn the fundamentals
- **Understand state deeply** → [Understanding State](understanding-state.md) — the foundation of everything
