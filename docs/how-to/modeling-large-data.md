# Modeling Large Data

> **TL;DR**: When state includes large data like images or binary blobs, cloning becomes expensive. Use `[SharedState]` to share large immutable data by reference across clones, or use seeded random data (store seed + length, regenerate bytes on demand) when content doesn't need to be meaningful.

---

## The Problem: Cloning Copies Everything

Accordant clones state on every transition. When you call `.ThenState(...)`, you get a fresh copy of the state object. This keeps the original state immutable and makes reasoning about transitions straightforward.

But here's the issue: **everything gets copied**. If your state includes large data — images, binary blobs, documents — each clone duplicates all of it.

```csharp
[State]
public partial class ImageStoreState
{
    public Dictionary<string, ImageState> Images { get; set; } = new();
}

[State]
public partial class ImageState
{
    public string Name { get; set; }
    public List<byte> Content { get; set; }  // Could be megabytes!
}
```

Every time an operation transitions state, that entire `Content` list gets deep-cloned. With a few images, you're copying megabytes on every transition. The old copies eventually get garbage collected, but you're still churning through memory and slowing things down.

---

## Solution 1: The `[SharedState]` Pattern

For large, immutable data, mark the property with `[SharedState]`. This tells Accordant: "Don't deep-clone this — just copy the reference."

```csharp
using System.IO.Hashing;

[State]
public partial class ImageState
{
    public string Name { get; set; }
    
    [SharedState(Fingerprint = nameof(ContentFingerprint))]
    public List<byte> Content { get; set; }
    
    private string ContentFingerprint(List<byte> content) =>
        content == null ? "" : XxHash64.HashToUInt64(content.ToArray()).ToString();
}
```

### How It Works

1. **Cloning by reference**: When the state is cloned, the `Content` property is copied by reference, not deep-cloned. Both the old and new state point to the same `List<byte>` in memory.

2. **Fingerprinting for equality**: Accordant still needs to know when two states are "the same" for test generation. The `Fingerprint` method computes a hash that represents the property's value. States with the same fingerprint are considered equal.

3. **Immutability assumption**: This only works if the shared data is treated as immutable. If you modify the content in one state, all states sharing that reference will see the change — which breaks the model.

### When to Use `[SharedState]`

- **Large binary data**: Images, files, documents, serialized blobs
- **Immutable reference types**: Data that's set once and never modified
- **Expensive to clone**: Anything where deep-copying is a performance problem

### The Fingerprint Method

The `Fingerprint` parameter is required for types that Accordant can't automatically hash (like `List<byte>`). The method must:

- Take the property type as its single parameter
- Return a `string`
- Be deterministic (same input → same output)

Use `State.ComputeHash64()` — the same XxHash64-based function Accordant uses internally:

```csharp
using System.IO.Hashing;

private string ContentFingerprint(byte[] content)
{
    if (content == null) return "";
    return XxHash64.HashToUInt64(content).ToString();
}

// Or use the State helper method for string-based hashing:
private string ContentFingerprint(List<byte> content)
{
    if (content == null) return "";
    return State.ComputeHash64(Convert.ToBase64String(content.ToArray())).ToString();
}
```

XxHash64 is fast and consistent with how Accordant computes state equality internally.

---

## Solution 2: Seeded Random Data

When you control the data generation, you can avoid storing large data entirely. Instead, store just a **seed** and **length** — enough information to regenerate the exact same bytes on demand.

### The Idea

A seeded random number generator produces deterministic output: given the same seed, you get the same sequence of bytes every time. So instead of storing megabytes of image data, store two numbers:

```csharp
[State]
public partial class ImageState
{
    public string Name { get; set; }
    public int ContentSeed { get; set; }   // Seed for random generator
    public int ContentLength { get; set; } // How many bytes to generate
}
```

When you need the actual bytes — to send in a request or validate a response — regenerate them:

```csharp
public static byte[] GenerateContent(int seed, int length)
{
    var random = new Random(seed);
    var bytes = new byte[length];
    random.NextBytes(bytes);
    return bytes;
}
```

### Full Example

```csharp
[State]
public partial class ImageStoreState
{
    public Dictionary<string, ImageState> Images { get; set; } = new();
}

[State]
public partial class ImageState
{
    public string Name { get; set; }
    public int ContentSeed { get; set; }
    public int ContentLength { get; set; }
    
    public byte[] GetContent() => GenerateContent(ContentSeed, ContentLength);
    
    public static byte[] GenerateContent(int seed, int length)
    {
        var random = new Random(seed);
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }
}
```

### Upload Operation

```csharp
spec.Operation<UploadRequest, ApiResult>("Upload", (request, state) =>
{
    return Expect.That<ApiResult>(r => r.IsSuccess)
        .ThenState<ImageStoreState>(s => s.Images[request.Name] = new ImageState
        {
            Name = request.Name,
            ContentSeed = request.Seed,
            ContentLength = request.Length
        });
});
```

### Download Operation

```csharp
spec.Operation<string, ApiResult<byte[]>>("Download", (name, state) =>
{
    if (!state.Images.TryGetValue(name, out var image))
        return Expect.That<ApiResult<byte[]>>(r => r.IsNotFound).SameState();

    var expectedContent = image.GetContent();
    return Expect.That<ApiResult<byte[]>>(r =>
            r.IsSuccess &&
            r.Data.SequenceEqual(expectedContent))
        .SameState();
});
```

### Test Inputs

```csharp
var inputs = new InputSet
{
    // Upload with seed=42, 1KB of data
    spec.GetOperation("Upload").With(new UploadRequest("img1", Seed: 42, Length: 1024)),
    // Upload with seed=123, 10KB of data  
    spec.GetOperation("Upload").With(new UploadRequest("img2", Seed: 123, Length: 10240)),
    spec.GetOperation("Download").With("img1"),
    spec.GetOperation("Download").With("img2"),
    spec.GetOperation("Download").With("nonexistent"),
};
```

### When to Use Seeded Data

- **Testing with arbitrary binary data**: When the specific bytes don't matter, just that they're consistent
- **Large payloads**: When you want to test with megabytes of data without storing megabytes in state
- **Reproducibility**: Same seed always generates the same content — tests are deterministic

---

## Comparing the Approaches

| Approach | When to Use | Trade-offs |
|----------|-------------|------------|
| `[SharedState]` | Real binary data from external sources; data must persist in state | Requires immutability discipline; fingerprint method needed |
| Seeded Random Data | Arbitrary test data; content doesn't need to be meaningful | Only works when you control data generation |

### Example: Uploading Real Images

If you're testing with actual image files (PNG, JPEG) where the content matters, `[SharedState]` is the right choice:

```csharp
using System.IO.Hashing;

[State]
public partial class ImageState
{
    public string Name { get; set; }
    
    [SharedState(Fingerprint = nameof(ContentFingerprint))]
    public byte[] Content { get; set; }
    
    private string ContentFingerprint(byte[] content) =>
        content == null ? "" : XxHash64.HashToUInt64(content).ToString();
}
```

### Example: Testing with Arbitrary Binary Data

If you just need "some bytes" to test upload/download behavior, seeded data keeps state tiny:

```csharp
[State]
public partial class ImageState
{
    public string Name { get; set; }
    public int ContentSeed { get; set; }    // 4 bytes instead of megabytes
    public int ContentLength { get; set; }  // 4 bytes
    
    public byte[] GetContent()
    {
        var random = new Random(ContentSeed);
        var bytes = new byte[ContentLength];
        random.NextBytes(bytes);
        return bytes;
    }
}
```

---

## Summary

When your spec needs to track large data:

1. **First, ask if you need the actual bytes.** If you just need "some data" for testing, use seeded random data — store `(seed, length)` and regenerate bytes on demand.

2. **If you must store real content, use `[SharedState]`.** Mark large, immutable properties so they're shared by reference instead of deep-cloned.

3. **Provide a fingerprint method using XxHash64.** Accordant needs to know when two states are "equal" for test generation — use the same hash function it uses internally.

4. **Treat shared data as immutable.** Never modify shared state in place — always create new instances.
