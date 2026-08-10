namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

internal sealed class AugmentationRuntime<TConcrete, TAuxiliary>
    where TConcrete : IState
    where TAuxiliary : State
{
    private readonly Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next;
    private readonly Dictionary<TAuxiliary, string> frozenFingerprints =
        new Dictionary<TAuxiliary, string>(new ReferenceComparer());

    public AugmentationRuntime(
        StateGraphNode concreteRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next)
    {
        this.next = next;
        InitialState = Prepare(
            initial(GetConcrete(concreteRoot)),
            "The augmentation initializer returned null.");
    }

    public object InitialState { get; }

    public object Advance(
        object auxiliaryState,
        StateGraphNode source,
        StateGraphEdge edge)
    {
        var current = (TAuxiliary)auxiliaryState;
        var updated = next(
            current,
            new RefinementTransition<TConcrete>(
                GetConcrete(source),
                edge.StepFunction,
                edge.Metadata,
                GetConcrete(edge.Target)));
        Validate(current);
        return Prepare(
            updated,
            "The augmentation update returned null.");
    }

    public string GetIdentity(object auxiliaryState)
    {
        var value = (TAuxiliary)auxiliaryState;
        Validate(value);
        return value
            .GetStateHash()
            .ToString("X16", CultureInfo.InvariantCulture);
    }

    public TAuxiliary GetValue(object auxiliaryState)
        => (TAuxiliary)auxiliaryState;

    public void Validate(object auxiliaryState)
        => Validate((TAuxiliary)auxiliaryState);

    private TAuxiliary Prepare(TAuxiliary value, string nullMessage)
    {
        if (ReferenceEquals(value, null))
        {
            throw new InvalidOperationException(nullMessage);
        }
        if (frozenFingerprints.TryGetValue(value, out var expected))
        {
            Validate(value, expected);
        }
        else
        {
            if (value.IsFrozen)
            {
                value.ValidateNotMutated();
            }
            else
            {
                value.Freeze();
            }
            frozenFingerprints[value] =
                value.StringRepresentation(forceRecompute: true);
        }
        return value;
    }

    private void Validate(TAuxiliary value)
    {
        if (!frozenFingerprints.TryGetValue(value, out var expected))
        {
            throw new InvalidOperationException(
                "The augmentation state was not initialized by this check.");
        }
        Validate(value, expected);
    }

    private static void Validate(TAuxiliary value, string expected)
    {
        var current = value.StringRepresentation(forceRecompute: true);
        if (!StringComparer.Ordinal.Equals(expected, current))
        {
            throw new StateFrozenException(
                $"Frozen augmentation state of type " +
                $"'{typeof(TAuxiliary).Name}' was mutated after freezing.");
        }
        value.ValidateNotMutated();
    }

    private sealed class ReferenceComparer : IEqualityComparer<TAuxiliary>
    {
        public bool Equals(TAuxiliary left, TAuxiliary right)
            => ReferenceEquals(left, right);

        public int GetHashCode(TAuxiliary value)
            => RuntimeHelpers.GetHashCode(value);
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

internal static class AugmentedSafetyRefinement
{
    internal static RefinementCheckingResult CheckFunctional<
        TConcrete,
        TAbstract,
        TAuxiliary>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next,
        Func<TConcrete, TAuxiliary, TAbstract> mapping)
        where TConcrete : IState
        where TAbstract : IState
        where TAuxiliary : State
    {
        var runtime = new AugmentationRuntime<TConcrete, TAuxiliary>(
            concreteRoot,
            initial,
            next);
        var mappedStates = new Dictionary<string, TAbstract>(StringComparer.Ordinal);

        TAbstract Map(StateGraphNode concrete, object auxiliary)
        {
            var key = concrete.GetNodeFingerprint() + "|" +
                runtime.GetIdentity(auxiliary);
            if (!mappedStates.TryGetValue(key, out var mapped))
            {
                mapped = mapping(
                    (TConcrete)concrete.State,
                    runtime.GetValue(auxiliary));
                runtime.Validate(auxiliary);
                if (ReferenceEquals(mapped, null))
                {
                    throw new InvalidOperationException(
                        "The augmented refinement mapping returned null.");
                }
                mappedStates[key] = mapped;
            }
            return mapped;
        }

        return SafetyRefinementCore.Check<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            runtime.InitialState,
            runtime.Advance,
            runtime.GetIdentity,
            (concrete, auxiliary, abstraction) =>
                StateSemantics.Equal(
                    Map(concrete, auxiliary),
                    abstraction.State),
            (concrete, auxiliary) => Map(concrete, auxiliary),
            includeAuxiliaryInTrace: true);
    }

    internal static RefinementCheckingResult CheckRelational<
        TConcrete,
        TAbstract,
        TAuxiliary>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next,
        Func<TConcrete, TAuxiliary, TAbstract, bool> correspondence)
        where TConcrete : IState
        where TAbstract : IState
        where TAuxiliary : State
    {
        var runtime = new AugmentationRuntime<TConcrete, TAuxiliary>(
            concreteRoot,
            initial,
            next);
        return SafetyRefinementCore.Check<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            runtime.InitialState,
            runtime.Advance,
            runtime.GetIdentity,
            (concrete, auxiliary, abstraction) =>
            {
                var result = correspondence(
                    (TConcrete)concrete.State,
                    runtime.GetValue(auxiliary),
                    (TAbstract)abstraction.State);
                runtime.Validate(auxiliary);
                return result;
            },
            abstractView: null,
            includeAuxiliaryInTrace: true);
    }
}
