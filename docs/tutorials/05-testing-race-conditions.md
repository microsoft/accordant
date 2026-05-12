# Tutorial 5: Testing Race Conditions

Concurrency bugs are notoriously hard to find. A system might work perfectly in sequential tests but fail when two requests arrive simultaneously. This tutorial shows how Accordant automatically tests **all interleavings** to find race conditions.

**Time:** 20 minutes

**What you'll learn:**
- Using `RunConcurrentTests` to test interleavings
- Understanding linearizability (the correctness criterion)
- The "double-booking" pattern for finding race conditions

**Prerequisites:**
- Completed [Tutorial 1](01-your-first-spec.md)
- Basic understanding of concurrent programming

---

## The Problem: Double-Booking

Consider a booking system with this requirement:

> Each time slot can only be booked by one customer.

Your API has a `BookSlot` endpoint. It works great in sequential tests. But what happens when Alice and Bob both try to book the same slot **at the exact same moment**?

Without proper synchronization, both might succeed—a **double-booking** bug.

---

## The Scenario

```
Slot "9am" exists and is available.

Concurrent requests:
  - Alice: POST /api/slots/9am/book { customer: "Alice" }
  - Bob: POST /api/slots/9am/book { customer: "Bob" }

Valid outcomes (linearizable):
  ✓ Alice succeeds (200), Bob fails (409 Conflict)
  ✓ Bob succeeds (200), Alice fails (409 Conflict)

Invalid outcome (BUG!):
  ✗ Both succeed - slot is double-booked
```

---

## Setting Up the Spec

Let's define a booking spec:

```csharp
[State]
public partial class BookingState : State
{
    /// <summary>
    /// Key = slotId, Value = customer name (null if available)
    /// </summary>
    public Dictionary<string, string?> Slots { get; set; } = new();
}

private static Spec<BookingState> CreateSpec()
{
    var spec = new Spec<BookingState>().WithJsonPrinters();

    // CREATE SLOT
    spec.Operation<string, ApiResult<Slot>>("CreateSlot", (slotId, state) =>
    {
        if (state.Slots.ContainsKey(slotId))
            return Expect.That<ApiResult<Slot>>(r => r.IsConflict,
                       "Slot already exists")
                   .SameState();

        var newState = (BookingState)state.Clone();
        newState.Slots[slotId] = null;  // null = available

        return Expect.That<ApiResult<Slot>>(
                   r => r.IsSuccess && r.Data.IsAvailable,
                   "Should create available slot")
               .ThenState(newState);
    });

    // BOOK SLOT - The critical concurrent operation!
    spec.Operation<(string SlotId, string Customer), ApiResult<Slot>>("BookSlot", (request, state) =>
    {
        var (slotId, customer) = request;

        if (!state.Slots.TryGetValue(slotId, out var currentBookedBy))
            return Expect.That<ApiResult<Slot>>(r => r.IsNotFound,
                       "Slot doesn't exist")
                   .SameState();

        if (currentBookedBy != null)
            return Expect.That<ApiResult<Slot>>(r => r.IsConflict,
                       $"Slot already booked by '{currentBookedBy}'")
                   .SameState();

        // Available - book it!
        var newState = (BookingState)state.Clone();
        newState.Slots[slotId] = customer;

        return Expect.That<ApiResult<Slot>>(
                   r => r.IsSuccess && 
                        r.Data.BookedBy == customer &&
                        !r.Data.IsAvailable,
                   $"Should book slot for '{customer}'")
               .ThenState(newState);
    });

    // ... bind to API ...
    
    return spec;
}
```

---

## Running Concurrent Tests

The magic is in `RunConcurrentTests`:

```csharp
[Test]
public async Task ConcurrentTests_DoubleBookingPrevented()
{
    using var factory = new BookingServiceFactory();
    var spec = CreateSpec();

    spec.ProvideTargetAndInitialState(() => (
        new BookingApiClient(factory.CreateTestClient()),
        new BookingState()));

    var createSlot = spec.GetOperation<string, ApiResult<Slot>>("CreateSlot");
    var bookSlot = spec.GetOperation<(string, string), ApiResult<Slot>>("BookSlot");
    var getSlot = spec.GetOperation<string, ApiResult<Slot>>("GetSlot");

    var inputs = new InputSet()
    {
        // Setup: create a slot
        createSlot.With("9am", "Create 9am slot"),
        
        // THE RACE: Alice and Bob both try to book!
        bookSlot.With(("9am", "Alice"), "Alice books 9am"),
        bookSlot.With(("9am", "Bob"), "Bob books 9am"),
        
        // Verify final state
        getSlot.With("9am", "Check who got the slot"),
    };

    var results = await spec.RunConcurrentTests(  // <-- Note: RunConcurrentTests
        inputs,
        generationOptions: new TestGenerationOptions { MaxDepth = 4 },
        executionOptions: new TestExecutionOptions
        {
            BeforeEachAsync = async ctx =>
            {
                var client = ctx.Context.Get<BookingApiClient>();
                await client.DeleteSlotAsync("9am");
            }
        });

    var failures = results.Where(r => !r.Success).ToList();
    Assert.IsEmpty(failures, 
        $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
}
```

---

## How It Works: Linearizability

Accordant uses **linearizability** as its correctness criterion:

> Even though operations execute concurrently, the results must be explainable by **some** sequential ordering.

For our booking scenario:

**Test execution:**
```
T1: CreateSlot("9am") → Success
T2: [Alice BookSlot] and [Bob BookSlot] run concurrently
T3: GetSlot("9am")
```

**Accordant checks:** Is there a sequential order that explains the results?

- If Alice got 200 and Bob got 409 → **Valid** (as if Alice went first)
- If Bob got 200 and Alice got 409 → **Valid** (as if Bob went first)
- If both got 200 → **Invalid** (no sequential order explains this!)

---

## What Gets Generated

`RunConcurrentTests` generates test cases with concurrent operation pairs:

```
Test Case 1:
  Sequential: CreateSlot("9am")
  Concurrent: [BookSlot(Alice)] || [BookSlot(Bob)]
  Sequential: GetSlot("9am")
  
Test Case 2:
  Sequential: CreateSlot("9am") → BookSlot(Alice)
  Concurrent: [BookSlot(Bob)] || [GetSlot("9am")]
  
... and so on ...
```

Each test case is validated against all possible linearizations.

---

## Finding the Bug

If your API has a race condition, Accordant will catch it:

```
FAILURE in concurrent test case:
  Sequential prefix: CreateSlot("9am")
  Concurrent: BookSlot(Alice) || BookSlot(Bob)
  
  Observed results:
    BookSlot(Alice) → 200 OK, slot booked by Alice
    BookSlot(Bob) → 200 OK, slot booked by Bob
  
  ERROR: No linearization found!
    - If Alice first: Bob should get 409 Conflict
    - If Bob first: Alice should get 409 Conflict
    - Both succeeded → DOUBLE BOOKING BUG
```

---

## The Fix

The API needs proper synchronization. Common approaches:

```csharp
// Option 1: Database-level locking
using var transaction = await _db.Database.BeginTransactionAsync(
    IsolationLevel.Serializable);

// Option 2: Application-level lock
private static readonly SemaphoreSlim _bookingLock = new(1, 1);
await _bookingLock.WaitAsync();
try { /* booking logic */ }
finally { _bookingLock.Release(); }

// Option 3: Optimistic concurrency with row versioning
if (slot.RowVersion != originalRowVersion)
    return Conflict();
```

After fixing, run the concurrent tests again—they should pass.

---

## More Complex Scenarios

Test multiple resources with multiple actors:

```csharp
var inputs = new InputSet()
{
    createSlot.With("9am", "Create 9am"),
    createSlot.With("10am", "Create 10am"),
    
    // Multiple races
    bookSlot.With(("9am", "Alice"), "Alice books 9am"),
    bookSlot.With(("9am", "Bob"), "Bob books 9am"),
    bookSlot.With(("10am", "Alice"), "Alice books 10am"),
    bookSlot.With(("10am", "Bob"), "Bob books 10am"),
    
    // Cancellation adds another dimension
    cancelBooking.With("9am", "Cancel 9am"),
};
```

Accordant will test all meaningful interleavings.

---

## Summary

Concurrent testing finds race conditions:

| Concept | Meaning |
|---------|---------|
| `RunConcurrentTests` | Tests operations running in parallel |
| Linearizability | Results must match some sequential order |
| Double-booking | Classic race condition pattern |

### The Key Insight

You don't write concurrent test cases manually. Define the operations and inputs—Accordant explores the interleavings and validates each against linearizability.

---

## What's Next?

- **[Tutorial 6: Async Operations](06-async-operations-polling.md)** - Testing background processing
- **[Concept: Concurrent Test Validation](../concepts/concurrent-test-validation.md)** - How linearizability checking works

---

## Full Code Reference

See the complete Booking sample:
- [BookingTests.cs](../../Samples/Booking/Booking.Tests/BookingTests.cs) - Complete concurrent tests
- [BookingApiClient.cs](../../Samples/Booking/Booking.Tests/BookingApiClient.cs) - HTTP client
