namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// Reports a malformed witness definition: an invalid possible-value domain,
/// a contradictory lifecycle change, a missing pending operation, or a
/// resolution that would leave an original concrete transition uncovered.
/// These errors are distinct from a refinement mapping or fairness failure.
/// </summary>
public sealed class WitnessDefinitionException : InvalidOperationException
{
    internal WitnessDefinitionException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The lifecycle change kinds a witness definition can request.
/// </summary>
internal enum WitnessChangeKind
{
    Introduce,
    Resolve,
    Cancel
}

internal sealed class WitnessChange
{
    internal WitnessChange(
        WitnessChangeKind kind,
        string operationId,
        IReadOnlyList<State> values)
    {
        Kind = kind;
        OperationId = operationId;
        Values = values;
    }

    public WitnessChangeKind Kind { get; }
    public string OperationId { get; }

    /// <summary>
    /// The canonically ordered introduced domain, or the single resolving
    /// value. Empty for cancellation.
    /// </summary>
    public IReadOnlyList<State> Values { get; }
}

/// <summary>
/// A set of witness lifecycle changes requested for one refinement position:
/// the concrete root, or one concrete transition.
/// </summary>
public sealed class WitnessChanges
{
    private static readonly WitnessChanges NoChanges =
        new WitnessChanges(Array.Empty<WitnessChange>());

    private WitnessChanges(IReadOnlyList<WitnessChange> changes)
    {
        Changes = changes;
    }

    internal IReadOnlyList<WitnessChange> Changes { get; }

    /// <summary>No witness lifecycle change.</summary>
    public static WitnessChanges None => NoChanges;

    /// <summary>
    /// Introduces a pending operation with a finite, nonempty, duplicate-free
    /// set of possible future values. The refinement proof branches into one
    /// copy per possible value.
    /// </summary>
    public static WitnessChanges Introduce(
        string operationId,
        params State[] possibleValues)
        => Introduce(operationId, (IEnumerable<State>)possibleValues);

    /// <summary>
    /// Introduces a pending operation with a finite, nonempty, duplicate-free
    /// set of possible future values.
    /// </summary>
    public static WitnessChanges Introduce(
        string operationId,
        IEnumerable<State> possibleValues)
    {
        var id = ValidateOperationId(operationId);
        if (possibleValues == null)
        {
            throw new WitnessDefinitionException(
                $"The witness domain introduced for operation '{id}' is null. " +
                "Introduce a finite, nonempty set of possible values.");
        }

        var values = possibleValues.ToArray();
        if (values.Length == 0)
        {
            throw new WitnessDefinitionException(
                $"The witness domain introduced for operation '{id}' is empty. " +
                "No witness value could cover the operation.");
        }

        var canonical = new List<State>();
        var seen = new Dictionary<string, State>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (ReferenceEquals(value, null))
            {
                throw new WitnessDefinitionException(
                    $"The witness domain introduced for operation '{id}' " +
                    "contains a null value.");
            }

            var identity = WitnessValueIdentity(value);
            if (seen.ContainsKey(identity))
            {
                throw new WitnessDefinitionException(
                    $"The witness domain introduced for operation '{id}' " +
                    $"contains the duplicate value '{value}'. Possible " +
                    "values must be semantically distinct.");
            }
            seen[identity] = value;
            canonical.Add(value);
        }

        canonical.Sort((left, right) => StringComparer.Ordinal.Compare(
            WitnessValueIdentity(left),
            WitnessValueIdentity(right)));
        return new WitnessChanges(new[]
        {
            new WitnessChange(
                WitnessChangeKind.Introduce,
                id,
                canonical.AsReadOnly())
        });
    }

    /// <summary>
    /// Resolves a pending operation with the value the concrete transition
    /// reveals. Only the witness copies that predicted this value continue;
    /// copies that predicted a different value are wrong predictions and die.
    /// </summary>
    public static WitnessChanges Resolve(string operationId, State value)
    {
        var id = ValidateOperationId(operationId);
        if (ReferenceEquals(value, null))
        {
            throw new WitnessDefinitionException(
                $"The resolving witness value for operation '{id}' is null.");
        }

        WitnessValueIdentity(value);
        return new WitnessChanges(new[]
        {
            new WitnessChange(
                WitnessChangeKind.Resolve,
                id,
                new[] { value })
        });
    }

    /// <summary>
    /// Cancels a pending operation without constraining its value. Every
    /// witness copy survives and copies that become identical merge.
    /// </summary>
    public static WitnessChanges Cancel(string operationId)
        => new WitnessChanges(new[]
        {
            new WitnessChange(
                WitnessChangeKind.Cancel,
                ValidateOperationId(operationId),
                Array.Empty<State>())
        });

    /// <summary>
    /// Combines this change with another so that one atomic concrete
    /// transition can change several operations.
    /// </summary>
    public WitnessChanges And(WitnessChanges other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        if (other.Changes.Count == 0)
        {
            return this;
        }
        if (Changes.Count == 0)
        {
            return other;
        }
        return new WitnessChanges(
            Changes.Concat(other.Changes).ToArray());
    }

    /// <summary>Combines several changes in order.</summary>
    public static WitnessChanges Combine(params WitnessChanges[] changes)
    {
        if (changes == null)
        {
            throw new ArgumentNullException(nameof(changes));
        }

        var result = NoChanges;
        foreach (var change in changes)
        {
            if (change == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }
            result = result.And(change);
        }
        return result;
    }

    private static string ValidateOperationId(string operationId)
    {
        if (string.IsNullOrEmpty(operationId))
        {
            throw new WitnessDefinitionException(
                "A witness operation identity must be a nonempty string. " +
                "Use a deterministic model identity such as a request, " +
                "message, process, or queue-entry identifier.");
        }
        return operationId;
    }

    internal static string WitnessValueIdentity(State value)
    {
        if (!value.IsFrozen)
        {
            value.Freeze();
        }
        return value
            .GetStateHash()
            .ToString("X16", CultureInfo.InvariantCulture);
    }
}

internal sealed class WitnessEntry
{
    internal WitnessEntry(
        string operationId,
        IReadOnlyList<State> domain,
        string domainIdentity,
        State value,
        string valueIdentity)
    {
        OperationId = operationId;
        Domain = domain;
        DomainIdentity = domainIdentity;
        Value = value;
        ValueIdentity = valueIdentity;
    }

    public string OperationId { get; }
    public IReadOnlyList<State> Domain { get; }
    public string DomainIdentity { get; }
    public State Value { get; }
    public string ValueIdentity { get; }
}

/// <summary>
/// The immutable witness values selected at one refinement position. Each
/// pending operation has exactly one selected value here; sibling proof
/// copies carry the other possible values.
/// </summary>
public sealed class WitnessCollection
{
    private readonly IReadOnlyList<WitnessEntry> entries;
    private readonly Dictionary<string, WitnessEntry> byOperation;

    internal WitnessCollection(IReadOnlyList<WitnessEntry> entries)
    {
        this.entries = entries;
        byOperation = entries.ToDictionary(
            entry => entry.OperationId,
            StringComparer.Ordinal);
        Identity = string.Join(
            ";",
            entries.Select(entry =>
                entry.OperationId.Length.ToString(
                    CultureInfo.InvariantCulture) + ":" +
                entry.OperationId + "=" + entry.ValueIdentity +
                "@" + entry.DomainIdentity));
    }

    internal static WitnessCollection Empty { get; } =
        new WitnessCollection(Array.Empty<WitnessEntry>());

    internal IReadOnlyList<WitnessEntry> Entries => entries;

    internal string Identity { get; }

    /// <summary>The number of pending operations.</summary>
    public int Count => entries.Count;

    /// <summary>The pending operation identities, in ordinal order.</summary>
    public IReadOnlyList<string> PendingOperations
        => entries.Select(entry => entry.OperationId).ToArray();

    /// <summary>Whether the named operation currently has a witness.</summary>
    public bool IsPending(string operationId)
        => operationId != null && byOperation.ContainsKey(operationId);

    /// <summary>
    /// Returns the witness value predicted for a pending operation. Throws
    /// when the operation is not pending or its value has a different type;
    /// use <see cref="TryGet{T}"/> or <see cref="IsPending"/> for mappings
    /// that also span positions where the operation is absent.
    /// </summary>
    public T Get<T>(string operationId)
        where T : State
    {
        if (operationId == null)
        {
            throw new ArgumentNullException(nameof(operationId));
        }
        if (!byOperation.TryGetValue(operationId, out var entry))
        {
            throw new InvalidOperationException(
                $"No witness is pending for operation '{operationId}'. " +
                $"Pending operations: [{string.Join(", ", PendingOperations)}]. " +
                "Use TryGet<T> or IsPending for positions where the " +
                "operation is not pending.");
        }
        if (!(entry.Value is T typed))
        {
            throw new InvalidOperationException(
                $"The witness for operation '{operationId}' is a " +
                $"'{entry.Value.GetType().FullName}', not a " +
                $"'{typeof(T).FullName}'.");
        }
        return typed;
    }

    /// <summary>
    /// Returns the witness value predicted for a pending operation, or false
    /// when the operation is not pending or has a different value type.
    /// </summary>
    public bool TryGet<T>(string operationId, out T value)
        where T : State
    {
        value = null;
        if (operationId == null ||
            !byOperation.TryGetValue(operationId, out var entry))
        {
            return false;
        }
        value = entry.Value as T;
        return value != null;
    }

    /// <summary>Formats the pending operations and their predicted values.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        for (var index = 0; index < entries.Count; index++)
        {
            if (index > 0)
            {
                sb.Append(", ");
            }
            sb.Append(entries[index].OperationId)
                .Append("=")
                .Append(entries[index].Value);
        }
        sb.Append('}');
        return sb.ToString();
    }
}

/// <summary>
/// The read-only view of pending operations supplied to witness lifecycle
/// callbacks. It exposes operation identities and their declared possible
/// values, never the selected prediction, so that the lifecycle stays a pure
/// function of the concrete transition.
/// </summary>
public sealed class PendingWitnesses
{
    private readonly WitnessCollection collection;

    internal PendingWitnesses(WitnessCollection collection)
    {
        this.collection = collection;
    }

    /// <summary>The number of pending operations.</summary>
    public int Count => collection.Count;

    /// <summary>The pending operation identities, in ordinal order.</summary>
    public IReadOnlyList<string> PendingOperations
        => collection.PendingOperations;

    /// <summary>Whether the named operation is currently pending.</summary>
    public bool IsPending(string operationId)
        => collection.IsPending(operationId);

    /// <summary>
    /// The possible values declared when the operation was introduced, in
    /// canonical order. Throws when the operation is not pending.
    /// </summary>
    public IReadOnlyList<State> GetDomain(string operationId)
    {
        if (!TryGetDomain(operationId, out var domain))
        {
            throw new InvalidOperationException(
                $"No witness is pending for operation '{operationId}'. " +
                $"Pending operations: [{string.Join(", ", PendingOperations)}].");
        }
        return domain;
    }

    /// <summary>
    /// The possible values declared when the operation was introduced, or
    /// false when the operation is not pending.
    /// </summary>
    public bool TryGetDomain(
        string operationId,
        out IReadOnlyList<State> domain)
    {
        domain = null;
        if (operationId == null)
        {
            return false;
        }

        foreach (var entry in collection.Entries)
        {
            if (StringComparer.Ordinal.Equals(entry.OperationId, operationId))
            {
                domain = entry.Domain;
                return true;
            }
        }
        return false;
    }

    /// <summary>Formats the pending operations and their declared domains.</summary>
    public override string ToString()
        => "{" + string.Join(
            ", ",
            collection.Entries.Select(entry =>
                entry.OperationId + " in [" +
                string.Join(", ", entry.Domain.Select(value => value.ToString())) +
                "]")) + "}";
}
