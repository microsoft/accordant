---
name: Accordant Design State
description: How to design state classes using the [State] attribute for Accordant models - the abstract representation of system state
---

# Designing State Classes

The state class is the foundation of every Accordant model. It represents the abstract state of the system you're testing — NOT a copy of the real database schema.

## The [State] Attribute

Always mark your state class with the `[State]` attribute and inherit from `State`. The source generator provides automatic:
- **Cloning** via JSON serialization (deep copy)
- **Fingerprinting** for state equality/hashing
- **Mutation detection** (catches accidental mutation in `Apply`)
- **String representation** for debugging

```csharp
using Microsoft.Accordant;
d
[State]
public partial class MyState : State
{
    // Properties go here — plain C# types
}
```

## Design Principles

### 1. Model the ABSTRACT state, not the implementation
Your state should capture what the system "knows" at a conceptual level. If your real system uses SQL tables with IDs, timestamps, audit logs — only model what matters for behavior.

**Real system**: Users table with Id, Name, Email, CreatedAt, UpdatedAt, PasswordHash, LastLoginAt
**Model state**: `Dictionary<string, UserState>` with just Name and relevant fields

### 2. Use the simplest data structures
- `Dictionary<string, T>` for keyed collections (entities by ID)
- `List<T>` for ordered collections
- Primitive types for simple values
- Nested `[State]` classes for complex nested state

### 3. State must be serializable
All properties must use supported types. Avoid:
- Circular references (unless using nested `[State]` classes which handle them)
- Non-serializable types (streams, HTTP clients, etc.)
- Computed properties that depend on external state

## Patterns

### Simple State (e.g., Stack)
```csharp
[State]
public partial class StackState<T> : State
{
    public List<T> Items { get; set; } = new List<T>();
}
```

### Entity-Based State (e.g., User + Todos)
```csharp
[State]
public partial class AppState : State
{
    public Dictionary<string, UserState> Users { get; set; } = new();

    public class UserState
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, TodoState> Todos { get; set; } = new();
    }

    public class TodoState
    {
        public string Title { get; set; } = string.Empty;
        public bool Completed { get; set; } = false;
    }
}
```

### Multi-Resource State (e.g., Accounts + Images)
```csharp
[State]
public partial class PetImagesState : State
{
    public Dictionary<string, AccountState> Accounts { get; set; } = new();
}

[State]
public partial class AccountState : State
{
    public string Name { get; set; }
    public string Tier { get; set; }
    public int? NumberOfUsers { get; set; }
    public Dictionary<string, ImageState> Images { get; set; } = new();
}

[State]
public partial class ImageState : State
{
    public string Name { get; set; }
    public string ContentType { get; set; }
    public string State { get; set; }  // "Creating", "Created", "Failed"

    [JsonAtomic(nameof(ContentFingerprint))]
    public List<byte> Content { get; set; }

    public string ContentFingerprint => Content == null
        ? null
        : string.Join(string.Empty, Content.Select(b => b.ToString("x2")));
}
```

## [JsonAtomic] Attribute

For large binary data or expensive-to-clone values, use `[JsonAtomic]`:
- Performs **shallow copy** (reference preservation) instead of deep copy during Clone
- Requires a companion **fingerprint property** for state equality/hashing
- Syntax: `[JsonAtomic(nameof(FingerprintPropertyName))]`

```csharp
[JsonAtomic(nameof(ContentFingerprint))]
public List<byte> Content { get; set; }

public string ContentFingerprint => Content == null ? null
    : string.Join("", Content.Select(b => b.ToString("x2")));
```

## Nested State Classes

Inner state classes can also use the `[State]` attribute for independent cloning/fingerprinting. Non-`[State]` nested classes work fine too — they'll be handled by the source generator.

## Collection Initialization

Initializing collection properties can be convenient to avoid null checks in `Apply`:

```csharp
// Initialized - can use directly in Apply without null checks
public Dictionary<string, UserState> Users { get; set; } = new();
public List<string> Tags { get; set; } = new();

// Uninitialized - works fine, just check for null if needed
public Dictionary<string, UserState> Users { get; set; }
```

## State for Async/Non-Deterministic Systems

When modeling systems with background processing, include state fields that track the processing status:

```csharp
[State]
public partial class ImageState : State
{
    public string State { get; set; }  // "Creating" → "Created" or "Failed"
    // The State field is used by TerminatingStepFunction's IsTerminalState
}
```

## Dictionary Key Types

The `[State]` source generator supports sorted dictionary keys for the following types: `string`, `int`, `long`, `Guid`. Keys are sorted for deterministic state fingerprinting.

## Anti-Patterns

1. **Don't put business logic in state classes** — state is pure data
2. **Don't model implementation details** — model observable behavior only
3. **Don't use properties that can't round-trip through JSON** — avoid `DateTime` precision issues, use strings or nullable types
4. **Don't create deeply nested state hierarchies** — keep it flat when possible
