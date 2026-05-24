# Booking Sample

A slot booking system demonstrating **concurrency testing**.

## What It Demonstrates

### The Double-Booking Problem
- Two customers try to book the same slot simultaneously
- Only one should succeed (the other gets a conflict)
- Accordant automatically tests all interleavings

### Concurrency Testing
- Parallel test generation explores race conditions
- Validates linearizability — system behaves as if operations were sequential
- Catches bugs that only appear under specific timing

## Key Scenario

```
Customer A: BookSlot("9am", "Alice")  ─┐
                                       ├─► Only ONE succeeds
Customer B: BookSlot("9am", "Bob")   ─┘
```

Accordant generates test cases covering:
- A succeeds, then B fails (conflict)
- B succeeds, then A fails (conflict)  
- Various interleavings with other operations

## Running

```bash
cd Booking.Tests
dotnet test
```

## See Also

- [Testing Race Conditions Tutorial](../../docs/tutorials/05-testing-race-conditions.md)
