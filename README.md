<!--
=============================================================================
OUTLINE / PLANNING NOTES
=============================================================================

## What is Accordant?
accordant is a library for model-based testing of stateful systems. you write 
executable behavioral specifications for the system-under-test, typically a 
service, over http, or whichever way. it can also be used for c# classes. its 
a c# framework so if you can invoke the system-under-test from c# you can use 
accordant. (maybe we don't have to say all this upfront, just dropping it here 
for now). (maybe we should also mention property-based testing someplace ...)

## Section 1: Familiar Territory
let's see how we typically test a service, using (integration) tests. say we 
have an account service that can allow you to deposit and withdraw balances 
from an account. and also create and delete accounts. you would write test 
cases like the following. we then show two or three short examples.

(challenge over here will be to not be too verbose, get to the point quickly, 
show enough examples but also not too much to be a wall of text?)

## Section 2: The Hidden Contract
the above are example-based tests. they typically follow the pattern where you `
call a set of apis/methods etc with some inputs, in some order and then do 
some assertions.

now, you can also ask, have we written _enough_ test cases? what about this 
case, and that one, and that one?

the other thing you also notice is that you learn something about the 
"contract" of the service through these examples. e.g. you cannot withdraw 
more than the balance has. if you do that, you get an error and the balance 
remains unchanged. and another property, etc.

## Section 3: Write the Contract Explicitly
let's write the contract (also known as spec, or a model) explicitly. here is 
what that looks like:

- show the state (maybe also a quick remark that while the underlying stuff is 
  stored in a database, it can conceptually be represented as a dictionary)
- show about two operation contracts (and maybe a link to the full one?)

## Section 4: What This Unlocks
what does this give us?

- the contract is stated pretty explicitly. it does not talk about where its 
  stored, or whether it is implemented over http/grpc, etc.
- instead of writing the assertions manually, we can instead use the model 
  instead. we can at this point show a couple of examples using spec.Allows
- if you have followed this far, you might realize that this actually unlocks 
  _mechanical test case generation_. you can use a fuzzer, and generate 
  arbitrary sequences!
- you can also _explore_ the state graph encoded by it and we can then do 
  walks in the graph to generate test cases, here is an example of how that 
  looks like.
- in addition to sequential, we can also check that operation works 
  concurrently. show an example using spec.AllowsConcurrent (mentions it 
  checks linearizability, perhaps pointer to a more detailed tutorial and/or 
  link over here)

at this point we can also mention that this is an excellent way of leveraging 
AI and use spec-driven development. you encode what _matters_, the contract 
and the AI can implement the details, and you can use the spec to generate 
test cases to ensure impl conforms to the spec. we can also weave in someplace 
that in addition to mechanical test case generation, if we also have a bunch 
of example-based tests, the assertions are encoded in one place, so they can 
be reviewed quickly and updated inplace. typical example-based tests _mix_ 
concerns that way.

## Section 5: Getting Started & What's Possible
after we have talked about the above, we have to talk about a couple of 
things, and we'll figure out in which order to talk them through:

- quickstart: how to install it, which set of tutorials to follow, guides, etc
- sneak preview of what's possible: concurrency testing, deterministic 
  simulation testing, asynchronous patterns, others, maybe also pointing to a 
  few tutorials/concepts/guides etc
- a quick mention of some common objections/concerns: aren't we re-implementing 
  the code? isn't this a mock? how is this related to property-based testing 
  (it _is_ prop-based testing?)? others? link to dedicated pages, as well as 
  some FAQs or something?

=============================================================================
-->

<img src="docs/images/accordant-wordmark-adaptive.svg" alt="Accordant" height="48">

**Executable behavioral specifications for .NET**

Accordant is a framework for model-based testing. You describe what your system should do, and Accordant tells you if the implementation actually does it.

You write a *spec* — executable code that captures the rules of your system. Given any state and any operation, the spec predicts what the response should be. Call the real system, check the response against the spec, and you know whether the observed behavior is correct or buggy. The spec is your oracle: a single source of truth about what "correct" means.

---

## How You Test Today

Say you're building a banking service — accounts, deposits, withdrawals. You write tests like these:

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

[Test]
public async Task Withdraw_WithInsufficientBalance_Fails()
{
    await client.CreateAccount("alice");
    await client.Deposit("alice", 50);
    
    var result = await client.Withdraw("alice", 100);
    
    Assert.True(result.IsBadRequest);
    
    var balance = await client.GetBalance("alice");
    Assert.Equal(50, balance.Data);  // unchanged
}

[Test]
public async Task Withdraw_FromNonexistentAccount_ReturnsNotFound()
{
    var result = await client.Withdraw("bob", 50);
    
    Assert.True(result.IsNotFound);
}
```

This is the standard pattern. Each test tells a story: set up state, call a method, check the result.

These are just three tests. You could write many more. Positive cases: deposit then withdraw, multiple deposits, withdraw exactly the balance. Negative cases: withdraw from empty account, withdraw more than the balance, operate on a deleted account. Interesting sequences: create, deposit, delete, try to deposit again; create the same account twice; withdraw, deposit, withdraw, check the running total. The space of possible tests is large — these three barely scratch the surface. What if you could validate *arbitrary* operation sequences, opening the door to mechanical test generation? (More on that shortly.)

Now notice something else. Look at those assertions — not the setup, the assertions.

The first test says: withdraw with sufficient balance should succeed and return the new balance. The second says: withdraw with insufficient balance should fail and leave the balance unchanged. The third says: withdraw from a nonexistent account should return not found.

These aren't three unrelated facts. They're three pieces of the same *contract* — the rules for how Withdraw behaves.

And here's the thing: every test you write has to encode those rules. Each test asserts some aspect of the contract — what the response should be, what should happen to the state. Across all the tests you write, the same rules get repeated over and over, scattered across your test suite.

---

## Extract the Contract

What if you wrote those rules once, in one place?

Here's what that looks like:

```csharp
spec.Operation<WithdrawRequest, ApiResult<decimal>>("Withdraw", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
        return Expect.That(r => r.IsNotFound).SameState();

    if (balance < request.Amount)
        return Expect.That(r => r.IsBadRequest).SameState();

    var newBalance = balance - request.Amount;
    return Expect.That(r => r.IsSuccess && r.Data == newBalance)
                 .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
});
```

This is an `Operation<TRequest, TResponse>`. You're given a request and the current state, and you return the *expected* response — using `Expect.That(...)` — as well as what the next state should be.

But what's the state? In a stateful system, you can't predict the response from the request alone. Call Withdraw with the same request twice — the first might succeed, the second might fail because the balance changed. You need to know what the system is tracking.

For banking, that's just accounts and their balances:

```csharp
public class BankState : JsonState
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}
```

The state captures the minimal structure needed to say what correct behavior means. Nothing more complex is required. We treat the system as a black box — we don't need to know whether data is stored in a database, files, a cache, or anything else.

The spec doesn't query databases, route HTTP requests, or handle retries. It just encodes the semantics — what should happen, not how it's implemented.

→ [See the full BankAccount spec](Samples/BankAccount/)

---

## What This Unlocks

Once you have a spec — the semantics of your system encoded as executable code — a few things become possible.

### The Spec as Oracle

You can use the spec to validate any operation sequence. Compare this to the example-based tests from earlier:

```csharp
[Test]
public async Task Deposit_Then_Withdraw_Sequence()
{
    var state = new BankState();
    
    // Deposit 100
    var depositResult = await client.Deposit("alice", 100);
    var (isValid, message, nextState) = spec.Allows(depositOp, new DepositRequest("alice", 100), depositResult, state);
    Assert.True(isValid, message);
    state = nextState;
    
    // Withdraw 30
    var withdrawResult = await client.Withdraw("alice", 30);
    (isValid, message, nextState) = spec.Allows(withdrawOp, new WithdrawRequest("alice", 30), withdrawResult, state);
    Assert.True(isValid, message);
}
```

In the example above, you still chose the operations and the sequence — this is similar to example-based tests from earlier. But instead of writing `Assert.Equal(70, result.NewBalance)`, you write `spec.Allows(...)`. The scattered assertions are replaced by a single question: "Is this response correct given the state?" The spec answers.

This is already useful — assertions live in one place, reviewable and updateable. But it unlocks something bigger.

### Automatic Test Generation

If the spec can validate *any* response, you don't have to write every test by hand. You could hook up a fuzzer and generate random operation sequences:

```csharp
await ResetSystem();  // Start from known state
var state = new BankState();

for (int i = 0; i < 100; i++)
{
    // Pick a random operation (CreateAccount, Deposit, etc.) with a random request
    var (op, request) = PickRandomOperation(random);
    var response = await CallRealSystem(op, request);
    
    var (isValid, message, nextState) = spec.Allows(op, request, response, state);
    Assert.True(isValid, message);
    
    state = nextState;
}
```

The spec doesn't care where the sequence came from — it just validates each response.

Accordant does something more interesting. You provide sample inputs — a few account IDs, some amounts — and Accordant explores systematically:

```csharp
var inputs = new InputSet
{
    spec.GetOperation<string>("CreateAccount").With("alice", "Create(alice)"),
    spec.GetOperation<(string, decimal)>("Deposit").With(("alice", 50m), "Deposit(alice, 50)"),
    spec.GetOperation<(string, decimal)>("Deposit").With(("alice", 100m), "Deposit(alice, 100)"),
    spec.GetOperation<(string, decimal)>("Withdraw").With(("alice", 30m), "Withdraw(alice, 30)"),
    spec.GetOperation<(string, decimal)>("Withdraw").With(("alice", 70m), "Withdraw(alice, 70)"),
    spec.GetOperation<string>("DeleteAccount").With("alice", "Delete(alice)"),
};

var testCases = TestCaseGenerator.GenerateSequentialTestCases(
    context,
    initialState: new BankState(),
    inputs,
    new TestGenerationOptions { MaxDepth = 5 });
```

Because the spec defines what each operation does to state, Accordant can *simulate* the system — predicting what happens without running real code. Starting from an empty state, it applies each operation to the model, computes the expected next state, and repeats. This builds a *state graph*: nodes are states, edges are operations.

![State graph from BankAccount sample](docs/images/bank-account-state-graph.png)

This is the actual state graph from the BankAccount sample — generated by simulating the spec. Accordant then picks paths through this graph as test sequences:

1. `Create(alice)` → `Deposit(alice, 100)` → `Withdraw(alice, 30)` → `Withdraw(alice, 70)` ✓ balance now 0
2. `Create(alice)` → `Deposit(alice, 50)` → `Withdraw(alice, 70)` ✗ insufficient funds
3. `Create(alice)` → `Deposit(alice, 50)` → `Delete(alice)` → `Withdraw(alice, 30)` ✗ account not found

These aren't random — they're systematic walks designed to exercise different branches. Each sequence is then run against the real system, and the spec validates every response — checking that the real implementation matches the model.

```
Generated 31 test cases
Executed against BankAccount API
Results: 31 passed, 0 failed
```

→ [How Test Generation Works](docs/concepts/how-test-generation-works.md)

### And More

The same spec enables other kinds of testing:

- **Concurrency testing** — Run operations in parallel, check that results are *linearizable* (explainable by some sequential ordering). Catches race conditions, double bookings, lost updates. → [Testing Race Conditions](docs/tutorials/05-testing-race-conditions.md)

- **Indefinite failures** — A socket timeout is ambiguous: maybe the request never reached the server, or maybe it did but you never heard the response. Specs can encode this non-determinism — the operation either happened or didn't. Combined with deterministic simulation (injecting controlled failures), you can test retry logic, idempotency, and recovery paths. → [Modeling Indefinite Failures](docs/how-to/indefinite-failures.md)

- **Async workflows** — Model multi-step processes, background jobs, polling for completion. The spec tracks pending work and expected completions. → [Step Functions & Async](docs/concepts/step-functions-and-async.md)

### Spec-Driven Development

The spec becomes the source of truth for how your system should behave.

**Reviewable and self-documenting** — Business rules live in one place: 60 lines of clear logic, not 600 lines of scattered assertions. A product manager can read the spec and say "yes, that's what we want." And unlike markdown docs, the spec is always up to date — if it doesn't match reality, tests fail.

**Confidence through change** — Refactor the implementation freely. When requirements change, update the spec and test generation adapts automatically. No hunting through test files to find every assumption that needs updating.

**AI-assisted development** — This pairs exceptionally well with AI coding assistants. You write the spec — the contract, the thing that matters. AI implements the mechanics: database layer, HTTP endpoints, retry policies, logging. The spec validates the result. You review 60 lines of spec logic, not 2000 lines of generated code. Run the tests — if the spec accepts every response, the implementation is correct.

---

## Get Started

Install the package ([NuGet](https://nuget.org/packages/Microsoft.Accordant)):

```bash
dotnet add package Microsoft.Accordant
```

### Learning Paths

**Just want to see it work?**
Clone the repo and run the BankAccount sample — 31 generated tests against a real ASP.NET Core API:
```bash
git clone https://github.com/microsoft/accordant.git
cd accordant/Samples/BankAccount/BankAccount.Api.Tests
dotnet test
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
| **[Tutorials](docs/tutorials/)** | Step-by-step guides to learn Accordant |
| **[Concepts](docs/concepts/)** | Understand the theory — model-based testing, linearizability, state graphs |
| **[How-To Guides](docs/how-to/)** | Solve specific problems — "how do I test idempotency?" |
| **[Samples](Samples/)** | Working code — BankAccount, TodoList, and more |
| **[API Reference](api/)** | Complete API documentation |

---

## Common Questions

You might be wondering:

- Isn't this just a mock?
- How does this relate to property-based testing?
- How do I know the spec is correct?

→ [FAQ](docs/faq.md)

---

## Community

Have questions, ideas, or want to share what you're building? Join the conversation on [GitHub Discussions](https://github.com/microsoft/accordant/discussions).

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and contribution guidelines.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

## License

[MIT](LICENSE)