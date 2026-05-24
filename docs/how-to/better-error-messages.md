# Better Error Messages

> **TL;DR**: When a response doesn't match expectations, you want to know *why*. Use `ValidationResult` for custom messages (built-in), or integrate with FluentAssertions for rich diff output.

---

## The Problem: Uninformative Failures

A simple predicate with an explanation tells you *that* something failed, but not *which part* was wrong:

```csharp
Expect.That<User>(
    r => r.Name == expectedName && r.Email == expectedEmail,
    "User fields should match expected values")
```

When this fails, you see:

```
User fields should match expected values
```

Not helpful. Was the name wrong? The email? Both? You have to dig through logs or add debugging to figure out what actually differed.

---

## Option 1: ValidationResult (Built-In)

Accordant includes `ValidationResult` — no external dependencies required. Return `ValidationResult.Invalid(message)` with a clear explanation:

```csharp
Expect.That<User>(response =>
{
    if (response.Name != expectedName)
        return ValidationResult.Invalid(
            $"Name mismatch: expected '{expectedName}', got '{response.Name}'");
    
    if (response.Email != expectedEmail)
        return ValidationResult.Invalid(
            $"Email mismatch: expected '{expectedEmail}', got '{response.Email}'");
    
    if (response.Status != expectedStatus)
        return ValidationResult.Invalid(
            $"Status mismatch: expected '{expectedStatus}', got '{response.Status}'");
    
    return ValidationResult.Valid();
})
```

When validation fails, you see exactly what went wrong:

```
Email mismatch: expected 'alice@example.com', got 'alice@test.com'
```

### Checking Multiple Fields

For responses with many fields, check them all and collect errors:

```csharp
Expect.That<Order>(response =>
{
    var errors = new List<string>();
    
    if (response.OrderId != expectedOrder.OrderId)
        errors.Add($"OrderId: expected '{expectedOrder.OrderId}', got '{response.OrderId}'");
    
    if (response.Status != expectedOrder.Status)
        errors.Add($"Status: expected '{expectedOrder.Status}', got '{response.Status}'");
    
    if (response.Total != expectedOrder.Total)
        errors.Add($"Total: expected {expectedOrder.Total}, got {response.Total}");
    
    if (response.Items.Count != expectedOrder.Items.Count)
        errors.Add($"Items.Count: expected {expectedOrder.Items.Count}, got {response.Items.Count}");
    
    return errors.Count == 0
        ? ValidationResult.Valid()
        : ValidationResult.Invalid(string.Join("\n", errors));
})
```

Output when multiple fields differ:

```
Status: expected 'Shipped', got 'Pending'
Total: expected 150.00, got 145.00
```

---

## Option 2: FluentAssertions

[FluentAssertions](https://fluentassertions.com/) is a popular library that provides rich comparison output with detailed diffs. It's particularly good for comparing complex objects.

### Installation

```bash
dotnet add package FluentAssertions
```

### Basic Integration

Wrap FluentAssertions calls in a try-catch and convert exceptions to `ValidationResult`:

```csharp
using FluentAssertions;

Expect.That<User>(response =>
{
    try
    {
        response.Should().BeEquivalentTo(new User
        {
            Id = expectedId,
            Name = "Alice",
            Email = "alice@example.com"
        });
        
        return ValidationResult.Valid();
    }
    catch (Exception ex)
    {
        return ValidationResult.Invalid(ex.Message);
    }
})
```

When validation fails, FluentAssertions provides detailed output:

```
Expected member Name to be "Alice", but "Bob" differs near "Bob" (index 0).
Expected member Email to be "alice@example.com", but "bob@example.com" differs near "bob" (index 0).
```

### Excluding Server-Generated Fields

Real responses often include fields you can't predict — timestamps, ETags, version numbers. Use `Excluding` to skip them:

```csharp
Expect.That<Todo>(response =>
{
    try
    {
        response.Should().BeEquivalentTo(new Todo
        {
            TodoId = expectedTodoId,
            Title = expectedTitle,
            Completed = false
        }, options => options
            .Excluding(x => x.CreatedAt)      // Server-generated
            .Excluding(x => x.LastModified)   // Server-generated
            .Excluding(x => x.ETag));         // Server-generated
        
        return ValidationResult.Valid();
    }
    catch (Exception ex)
    {
        return ValidationResult.Invalid(ex.Message);
    }
})
```

### Comparing Collections

FluentAssertions shines when comparing lists:

```csharp
Expect.That<OrderResponse>(response =>
{
    try
    {
        response.Items.Should().BeEquivalentTo(expectedItems, options => options
            .WithStrictOrdering());  // Order matters
        
        return ValidationResult.Valid();
    }
    catch (Exception ex)
    {
        return ValidationResult.Invalid(ex.Message);
    }
})
```

Output when collections differ:

```
Expected collection to contain 3 items, but found 2.
Expected item[1].Quantity to be 5, but found 3.
```

### Helper Method

If you use FluentAssertions frequently, create a helper:

```csharp
public static class ExpectHelpers
{
    public static ValidationResult EquivalentTo<T>(T actual, T expected, 
        Func<EquivalencyAssertionOptions<T>, EquivalencyAssertionOptions<T>>? config = null)
    {
        try
        {
            if (config != null)
                actual.Should().BeEquivalentTo(expected, config);
            else
                actual.Should().BeEquivalentTo(expected);
            
            return ValidationResult.Valid();
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid(ex.Message);
        }
    }
}

// Usage becomes cleaner:
Expect.That<User>(response => ExpectHelpers.EquivalentTo(response, expectedUser, 
    opt => opt.Excluding(x => x.CreatedAt)))
```

---

## Choosing an Approach

| Approach | Pros | Cons |
|----------|------|------|
| **ValidationResult** | No dependencies, full control | Manual work for complex objects |
| **FluentAssertions** | Rich diffs, great for objects/collections | External dependency |

**Recommendations:**

- **Simple responses** (few fields) → `ValidationResult` is fine
- **Complex objects/collections** → FluentAssertions saves time

---

## Example: Full Validation Pattern

Here's a complete example combining approaches:

```csharp
spec.Operation<string, ApiResult<Order>>("GetOrder", (orderId, state) =>
{
    if (!state.Orders.TryGetValue(orderId, out var expectedOrder))
    {
        return Expect.That<ApiResult<Order>>(r => r.IsNotFound,
                   "Order should not exist")
               .SameState();
    }

    return Expect.That<ApiResult<Order>>(response =>
    {
        // First check status code
        if (!response.IsSuccess)
            return ValidationResult.Invalid(
                $"Expected success, got {response.StatusCode}");
        
        if (response.Data == null)
            return ValidationResult.Invalid("Response data was null");
        
        // Then check the payload with FluentAssertions
        try
        {
            response.Data.Should().BeEquivalentTo(new Order
            {
                OrderId = orderId,
                CustomerId = expectedOrder.CustomerId,
                Status = expectedOrder.Status,
                Items = expectedOrder.Items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    Price = i.Price
                }).ToList()
            }, options => options
                .Excluding(x => x.CreatedAt)
                .Excluding(x => x.LastModified));
            
            return ValidationResult.Valid();
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid(ex.Message);
        }
    }, $"Should return order {orderId}")
    .SameState();
});
```

---

## See Also

- [Operations and Expect](../concepts/operations-and-expect.md) — Full details on the Expect API
- [FluentAssertions Documentation](https://fluentassertions.com/introduction)
