namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Builds and validates the conservative witness extension of the concrete
/// graph: it introduces, retains, resolves, and cancels future-validated
/// witness values along concrete transitions.
/// </summary>
internal sealed class WitnessRuntime<TConcrete>
    where TConcrete : IState
{
    private readonly Func<TConcrete, WitnessChanges> initial;
    private readonly Func<
        PendingWitnesses,
        RefinementTransition<TConcrete>,
        WitnessChanges> next;
    private readonly FrozenStateRegistry registry =
        new FrozenStateRegistry(
            value => $"witness value of type '{value.GetType().Name}'",
            "The witness value was not introduced by this check.");

    public WitnessRuntime(
        StateGraphNode concreteRoot,
        Func<TConcrete, WitnessChanges> initial,
        Func<PendingWitnesses, RefinementTransition<TConcrete>, WitnessChanges> next)
    {
        this.initial = initial;
        this.next = next;
        InitialCollections = Apply(
            WitnessCollection.Empty,
            Invoke(
                () => this.initial(GetConcrete(concreteRoot)),
                "initializer"),
            "the concrete root state");
    }

    public IReadOnlyList<WitnessCollection> InitialCollections { get; }

    /// <summary>
    /// Returns the witness collections that survive a concrete transition.
    /// An empty result means every prediction in this proof copy was refuted
    /// by the transition, so the copy is a dead wrong prediction.
    /// </summary>
    public IReadOnlyList<WitnessCollection> Advance(
        WitnessCollection current,
        StateGraphNode source,
        StateGraphEdge edge)
    {
        Validate(current);
        var transition = new RefinementTransition<TConcrete>(
            GetConcrete(source),
            edge.StepFunction,
            edge.Metadata,
            GetConcrete(edge.Target));
        var changes = Invoke(
            () => next(new PendingWitnesses(current), transition),
            "update");
        Validate(current);
        return Apply(
            current,
            changes,
            $"the concrete transition " +
            $"'{edge.StepFunction?.StepFunctionId ?? "<none>"}' from " +
            $"'{source.State}'");
    }

    /// <summary>Validates that no witness value was mutated.</summary>
    public void Validate(WitnessCollection collection)
    {
        if (collection == null)
        {
            return;
        }

        foreach (var entry in collection.Entries)
        {
            registry.Validate(entry.Value);
            foreach (var value in entry.Domain)
            {
                registry.Validate(value);
            }
        }
    }

    private static WitnessChanges Invoke(
        Func<WitnessChanges> callback,
        string role)
    {
        var changes = callback();
        if (changes == null)
        {
            throw new WitnessDefinitionException(
                $"The witness {role} returned null. Return " +
                "WitnessChanges.None when nothing changes.");
        }
        return changes;
    }

    private IReadOnlyList<WitnessCollection> Apply(
        WitnessCollection current,
        WitnessChanges changes,
        string context)
    {
        var removals = new Dictionary<string, WitnessChange>(
            StringComparer.Ordinal);
        var introductions = new List<WitnessChange>();
        var introduced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var change in changes.Changes)
        {
            var id = change.OperationId;
            if (change.Kind == WitnessChangeKind.Introduce)
            {
                if (!introduced.Add(id))
                {
                    throw new WitnessDefinitionException(
                        $"Operation '{id}' is introduced more than once at " +
                        $"{context}.");
                }
                introductions.Add(change);
                continue;
            }

            if (introduced.Contains(id))
            {
                throw new WitnessDefinitionException(
                    $"Operation '{id}' is introduced and then " +
                    $"{(change.Kind == WitnessChangeKind.Resolve ? "resolved" : "cancelled")} " +
                    $"at {context}. Resolution or cancellation must precede " +
                    "reintroduction.");
            }
            if (removals.ContainsKey(id))
            {
                throw new WitnessDefinitionException(
                    $"Operation '{id}' is resolved or cancelled more than " +
                    $"once at {context}.");
            }
            removals[id] = change;
        }

        foreach (var removal in removals.Values)
        {
            if (!current.IsPending(removal.OperationId))
            {
                throw new WitnessDefinitionException(
                    $"Operation '{removal.OperationId}' is " +
                    $"{(removal.Kind == WitnessChangeKind.Resolve ? "resolved" : "cancelled")} " +
                    $"at {context}, but no operation with that identity is " +
                    "pending. Pending operations: " +
                    $"[{string.Join(", ", current.PendingOperations)}].");
            }
        }

        foreach (var introduction in introductions)
        {
            if (current.IsPending(introduction.OperationId) &&
                !removals.ContainsKey(introduction.OperationId))
            {
                throw new WitnessDefinitionException(
                    $"Operation '{introduction.OperationId}' is introduced " +
                    $"at {context} while it is already pending. Resolve or " +
                    "cancel it first.");
            }
        }

        var retained = new List<WitnessEntry>();
        foreach (var entry in current.Entries)
        {
            if (!removals.TryGetValue(entry.OperationId, out var removal))
            {
                retained.Add(entry);
                continue;
            }

            if (removal.Kind == WitnessChangeKind.Cancel)
            {
                continue;
            }

            var actual = removal.Values[0];
            registry.Prepare(actual);
            var actualIdentity = WitnessChanges.WitnessValueIdentity(actual);
            if (!entry.Domain.Any(value =>
                StringComparer.Ordinal.Equals(
                    WitnessChanges.WitnessValueIdentity(value),
                    actualIdentity)))
            {
                throw new WitnessDefinitionException(
                    $"Operation '{entry.OperationId}' is resolved with " +
                    $"'{actual}' at {context}, but that value is not in the " +
                    "introduced domain " +
                    $"[{string.Join(", ", entry.Domain.Select(value => value.ToString()))}]. " +
                    "The concrete transition would have no witness " +
                    "extension.");
            }

            if (!StringComparer.Ordinal.Equals(
                entry.ValueIdentity,
                actualIdentity))
            {
                return Array.Empty<WitnessCollection>();
            }
        }

        var collections = new List<IReadOnlyList<WitnessEntry>> { retained };
        foreach (var introduction in introductions)
        {
            foreach (var value in introduction.Values)
            {
                registry.Prepare(value);
            }

            var domainIdentity = string.Join(
                ",",
                introduction.Values.Select(
                    WitnessChanges.WitnessValueIdentity));
            var expanded = new List<IReadOnlyList<WitnessEntry>>();
            foreach (var prefix in collections)
            {
                foreach (var value in introduction.Values)
                {
                    var entries = new List<WitnessEntry>(prefix)
                    {
                        new WitnessEntry(
                            introduction.OperationId,
                            introduction.Values,
                            domainIdentity,
                            value,
                            WitnessChanges.WitnessValueIdentity(value))
                    };
                    expanded.Add(entries);
                }
            }
            collections = expanded;
        }

        return collections
            .Select(entries => new WitnessCollection(
                entries
                    .OrderBy(
                        entry => entry.OperationId,
                        StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static TConcrete GetConcrete(StateGraphNode node)
    {
        if (node.State is TConcrete concrete)
        {
            return concrete;
        }

        throw new InvalidOperationException(
            $"Concrete graph node state '{node.State?.GetType().FullName}' " +
            $"is not a {typeof(TConcrete).FullName}.");
    }
}
