namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Tracks the frozen fingerprint of every checker-local <see cref="State"/>
/// value so that later mutation is detected even if the value is refrozen.
/// </summary>
internal sealed class FrozenStateRegistry
{
    private readonly ConditionalWeakTable<State, Fingerprint> fingerprints =
        new ConditionalWeakTable<State, Fingerprint>();
    private readonly Func<State, string> describe;
    private readonly string unknownValueMessage;

    public FrozenStateRegistry(
        Func<State, string> describe,
        string unknownValueMessage)
    {
        this.describe = describe;
        this.unknownValueMessage = unknownValueMessage;
    }

    /// <summary>
    /// Freezes a newly produced value, or validates a previously seen value,
    /// and records the fingerprint used for later mutation detection.
    /// </summary>
    public void Prepare(State value)
    {
        if (fingerprints.TryGetValue(value, out var expected))
        {
            Validate(value, expected.Value);
            return;
        }

        if (value.IsFrozen)
        {
            value.ValidateNotMutated();
        }
        else
        {
            value.Freeze();
        }
        fingerprints.Add(
            value,
            new Fingerprint(
                value.StringRepresentation(forceRecompute: true)));
    }

    /// <summary>Validates that a registered value was not mutated.</summary>
    public void Validate(State value)
    {
        if (!fingerprints.TryGetValue(value, out var expected))
        {
            throw new InvalidOperationException(unknownValueMessage);
        }
        Validate(value, expected.Value);
    }

    private void Validate(State value, string expected)
    {
        var current = value.StringRepresentation(forceRecompute: true);
        if (!StringComparer.Ordinal.Equals(expected, current))
        {
            throw new StateFrozenException(
                $"Frozen {describe(value)} was mutated after freezing.");
        }
    }

    private sealed class Fingerprint
    {
        public Fingerprint(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }
}
