namespace KeyValueStore.Tests;

using Microsoft.Accordant;

/// <summary>
/// State: what the system remembers between operations.
/// For a key-value store, that's just which keys exist and their values.
/// </summary>
[State]
public partial class KvState
{
    public Dictionary<string, string> Items { get; set; } = new();
}
