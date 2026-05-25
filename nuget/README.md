# Accordant

**Executable behavioral specifications for .NET**

Accordant is a framework for model-based testing. Write a spec that defines what your system should do, and Accordant generates tests to verify the implementation behaves correctly.

## Install

```bash
dotnet add package Microsoft.Accordant
```

## Quick Example

```csharp
// Define state
[State]
public partial class BankState
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}

// Define an operation
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

## Learn More

- **[Documentation](https://microsoft.github.io/accordant)** — Full docs, tutorials, and concepts
- **[GitHub](https://github.com/microsoft/accordant)** — Source code and samples
