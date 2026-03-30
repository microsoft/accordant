# Operations and Expect

> **TL;DR**: Operations define what your system can do. The Apply method describes what *should* happen. The Execute method makes it *actually* happen. Keeping these separate is what enables test generation, model checking, state exploration, and conformance testing.

---

## What is an Operation?

An **operation** represents a single, atomic action your system can perform — from the perspective of an external observer. It might be an API endpoint, a method call, a command — anything that takes input and produces output, possibly changing state along the way. The key is that it's a unit of change: before the operation, the system is in one state; after, it's in another (or the same, for read-only operations). There's no observable "in-between."

Every operation has a name, a typed request, and a typed response. The key design decision: *specification* and *execution* are separate.

```csharp
spec.Operation<(string AccountId, decimal Amount), ApiResult<decimal>>("Withdraw", 
    (request, state) => { ... })   // Apply: what SHOULD happen
    .WithExecution(async (request, ctx) => { ... });  // Execute: what ACTUALLY happens
```

The **Apply** method describes valid behavior. Given this request and this state, what should the response look like? How should the state change? Apply doesn't touch the real system — it's pure logic, a specification of correctness.

The **Execute** method actually calls your system. It makes HTTP requests, invokes methods, or does whatever is needed to trigger the real operation on your system under test.

This separation is the foundation of everything else. Because Apply is pure, Accordant can explore thousands of operation sequences without executing anything. It can build state graphs, generate test cases, and check properties — all by reasoning about the spec alone. Then, when you run tests, Execute calls the real system and Apply validates the results.

---

## The Apply Method

The Apply method receives the request and current state, and returns what Accordant calls `ExpectedOutcomes` — a description of what the response should look like and what the next state should be.

Here's a deposit operation:

```csharp
spec.Operation<(string AccountId, decimal Amount), ApiResult<decimal>>("Deposit", (request, state) =>
{
    if (!state.Accounts.ContainsKey(request.AccountId))
    {
        return Expect.That<ApiResult<decimal>>(r => r.IsNotFound)
               .SameState();
    }

    var newBalance = state.Accounts[request.AccountId] + request.Amount;
    return Expect.That<ApiResult<decimal>>(r => r.IsSuccess && r.Data == newBalance)
           .ThenState<BankState>(nextState => nextState.Accounts[request.AccountId] = newBalance);
});
```

The pattern is straightforward: check preconditions first, handle error cases, then describe the success case. Guard clauses at the top return early with error expectations. The happy path comes last.

Notice how Apply doesn't touch the request or state — it doesn't call APIs, it doesn't have side effects. It simply specifies what the response should satisfy and what the next state(s) should be.

---

## Validating Responses with Expect.That

The `Expect.That<T>()` method is how you describe what a valid response looks like. At its simplest, it's just a predicate:

```csharp
Expect.That<int>(r => r == 42)
```

If the predicate returns true, the response is valid. If it returns false, the test fails.

### Adding Context for Debugging

When tests fail, you want to know *why*. Adding an explanation string makes failures much easier to diagnose:

```csharp
Expect.That<decimal>(r => r == expectedBalance, 
    $"Balance should be {expectedBalance}")
```

The explanation appears in test output. Instead of "predicate returned false," you see "Balance should be 150.00" — immediately actionable.

### Rich Error Messages with ValidationResult

Sometimes a boolean isn't enough. You want to say exactly *what* was wrong:

```csharp
Expect.That<User>(response =>
{
    if (response.Name != expectedName)
        return ValidationResult.Invalid($"Name was '{response.Name}', expected '{expectedName}'");
    if (response.Email == null)
        return ValidationResult.Invalid("Email was null");
    return ValidationResult.Valid();
})
```

This gives you full control over the error message. When something's wrong, you'll know precisely what.

### Using FluentAssertions for Complex Objects

For complex response validation — like checking that an object matches expected values except for certain server-controlled fields — FluentAssertions integrates nicely:

```csharp
Expect.That<User>(response =>
{
    var expected = new User 
    { 
        Id = expectedId, 
        Name = "Alice", 
        Email = "alice@example.com" 
    };
    
    try
    {
        response.Should().BeEquivalentTo(expected, options => options
            .Excluding(x => x.CreatedAt)
            .Excluding(x => x.UpdatedAt));
        
        return ValidationResult.Valid();
    }
    catch (Exception ex)
    {
        return ValidationResult.Invalid(ex.Message);
    }
})
```

When validation fails, you get detailed output:

```
Expected member Name to be "Alice", but "Bob" differs near "Bob".
Expected member Email to be "alice@example.com", but "bob@example.com" differs near "bob".
```

This is far more useful than hunting through a failed assertion to figure out what went wrong.

---

## State Transitions

After describing what the response should look like, you need to say what happens to state.

### When State Doesn't Change

Many operations leave state unchanged. Error cases don't modify anything — returning 404 for a missing resource, 409 for a conflict, 400 for bad input. Read-only operations like GET queries also leave state alone.

For these, use `.SameState()`:

```csharp
return Expect.That<ApiResult<User>>(r => r.IsNotFound)
       .SameState();
```

### When State Changes

For operations that modify state, use `.ThenState()`. The lambda receives a clone of the current state, which you can modify directly:

```csharp
return Expect.That<decimal>(r => r == newBalance)
       .ThenState<BankState>(nextState => nextState.Accounts[accountId] = newBalance);
```

The `nextState` parameter is already a clone — you're not touching the original. Modify it however you need to describe the post-operation state.

> **Convention**: Always name the lambda parameter `nextState` for clarity.

### When State Depends on the Response

Sometimes you don't know what the next state will be until you see the response. The server might assign an ID, generate an ETag, or set a timestamp. You can't predict these values — but you need them in your state.

For this, use the response-dependent form of `ThenState`:

```csharp
return Expect.That<CreateResponse>(r => r.Id != null)
       .ThenState<AppState>(
           (response, nextState) => nextState.KnownIds.Add(response.Id),
           mock: () => new CreateResponse { Id = Guid.NewGuid().ToString() });
```

The `mock:` parameter might seem odd at first. Here's why it exists: during **test generation**, there's no real system running. Accordant is exploring possible sequences, building state graphs, figuring out what tests to generate. But if the next state depends on a response, and there's no real system to call, how can it know what state to explore next?

The mock provides a *plausible* response — something that lets Accordant continue exploring. It doesn't need to be accurate; it just needs to be the right shape. At actual test execution time, the mock is ignored — the real response from your real system is used instead.

> See [How Test Generation Works](how-test-generation-works.md) for more on state graph exploration.

---

## Executing Operations

So far we've talked about Apply — the specification side. But at some point, you need to actually call your system. That's where Execute comes in.

### Class-Based Operations

When you define operations as classes, you override both Apply and ExecuteAsync:

```csharp
public class WithdrawOperation : Operation<WithdrawRequest, decimal>
{
    public override ExpectedOutcomes Apply(WithdrawRequest request, BankState state)
    {
        // ... spec logic ...
    }
    
    public override async Task<decimal> ExecuteAsync(
        WithdrawRequest request, 
        ITestContext context)
    {
        var client = context.Get<HttpClient>();
        var response = await client.PostAsync($"/accounts/{request.Id}/withdraw", ...);
        return await response.Content.ReadFromJsonAsync<decimal>();
    }
}
```

Apply says what *should* happen. ExecuteAsync makes it *actually* happen.

### Inline Operations

For simpler specs, you can define everything inline:

```csharp
spec.Operation<string, decimal>("GetBalance", (accountId, state) => { ... })
    .WithExecution(async (accountId, ctx) => 
        await ctx.Get<BankClient>().GetBalanceAsync(accountId));
```

Same separation, more compact syntax.

### Why the Separation Matters

The separation between Apply and Execute enables powerful capabilities:

**Test generation** — Accordant can explore operation sequences by running Apply repeatedly, without ever calling Execute. It builds up a picture of your system's behavior purely from the spec.

**State graph visualization** — You can visualize every reachable state and every possible transition, all computed from Apply alone.

**Model checking** — You can verify properties of the spec itself. "Is it always true that...?" "Can we ever reach a state where...?" These questions are answered by exploring Apply.

**Trace validation** — Given a recorded sequence of requests and responses, you can validate that every response was valid according to Apply. The real execution already happened; now you're checking correctness after the fact.

None of this would be possible if specification and execution were tangled together.

---

## Multiple Valid Outcomes

Sometimes more than one outcome is valid. The real world is messy, and your spec should reflect that.

The classic example is a **network timeout**. You call CreateAccount, and the network times out. What actually happened?

Maybe your request never reached the server. The account doesn't exist.

Or maybe the server processed your request successfully, but the response got lost on the way back. The account *does* exist — you just don't know it yet.

From your perspective as the client, **both states are possible**. You can't tell which until you check. A good spec expresses this uncertainty:

```csharp
spec.Operation<string, ApiResult<decimal>>("CreateAccount", (accountId, state) =>
{
    if (state.Accounts.ContainsKey(accountId))
    {
        return Expect.That<ApiResult<decimal>>(r => r.IsConflict)
               .SameState();
    }

    return Expect.OneOf(
        // Success: account created
        Expect.That<ApiResult<decimal>>(r => r.IsSuccess && r.Data == 0)
              .ThenState<BankState>(nextState => nextState.Accounts[accountId] = 0),
              
        // Timeout case 1: request never reached server
        Expect.That<ApiResult<decimal>>(r => r.IsTimeout)
              .SameState(),
              
        // Timeout case 2: server processed it, but response was lost
        Expect.That<ApiResult<decimal>>(r => r.IsTimeout)
              .ThenState<BankState>(nextState => nextState.Accounts[accountId] = 0)
    );
});
```

`Expect.OneOf()` says: if *any* of these outcomes match, the response is valid. This is essential for modeling real-world conditions — network instability, retries, eventual consistency. The spec doesn't pretend the world is simpler than it is.

---

## Writing Good Predicates

One final piece of guidance: **the stronger your predicates, the more bugs you'll catch.**

A loose predicate might pass when the response is subtly wrong:

```csharp
// Too loose — only checks status
Expect.That<ApiResult<User>>(r => r.IsSuccess)
```

This passes if the status is success — even if the returned user has the wrong name, wrong email, wrong everything. You'd never catch a bug where the wrong user was returned.

A stronger predicate validates the actual content:

```csharp
// Better — validates what matters
Expect.That<ApiResult<User>>(r => 
    r.IsSuccess && 
    r.Data.Id == expectedId && 
    r.Data.Name == expectedName)
```

Now you're actually checking that the response is correct, not just that it didn't error.

Make your predicates as strong as you reasonably can. If the response should have a specific ID, check the ID. If it should have exactly 3 items, check the count. If the balance should be 150.00, check that it's 150.00. Weak predicates let bugs slip through. Strong predicates catch them.

---

## Next Steps

- **Tutorial 1**: [Your First Spec](../tutorials/01-your-first-spec.md) — full walkthrough of building and testing a spec
- **Tutorial 3**: [Response-Dependent State](../tutorials/03-response-dependent-state.md) — ETags, server IDs, and the mock parameter in depth
- **Concept**: [How Test Generation Works](how-test-generation-works.md) — understanding state graph exploration
- **Concept**: [Understanding State](understanding-state.md) — designing minimal, effective state
