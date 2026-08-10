namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;

internal static class FunctionalSafetyRefinement
{
    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAbstract> mapping)
        where TConcrete : IState
        where TAbstract : IState
    {
        var mappedStates = new Dictionary<string, TAbstract>(StringComparer.Ordinal);

        TAbstract Map(StateGraphNode concreteNode)
        {
            if (!(concreteNode.State is TConcrete concreteState))
            {
                throw new InvalidOperationException(
                    $"Concrete graph node state '{concreteNode.State?.GetType().FullName}' " +
                    $"is not a {typeof(TConcrete).FullName}.");
            }

            var fingerprint = concreteNode.GetNodeFingerprint();
            if (!mappedStates.TryGetValue(fingerprint, out var mapped))
            {
                mapped = mapping(concreteState);
                if (ReferenceEquals(mapped, null))
                {
                    throw new InvalidOperationException(
                        $"The refinement mapping returned null for concrete state " +
                        $"'{concreteNode.State}'.");
                }
                mappedStates[fingerprint] = mapped;
            }
            return mapped;
        }

        return SafetyRefinementCore.Check<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            (concrete, abstractNode) =>
                StateSemantics.Equal(Map(concrete), abstractNode.State),
            concrete => Map(concrete));
    }

    /// <summary>
    /// Checks functional safety refinement where the mapping additionally
    /// reads deterministic augmentation and future witness values.
    /// </summary>
    internal static RefinementCheckingResult CheckWithProofState<
        TConcrete,
        TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        RefinementProofRuntime<TConcrete> runtime,
        Func<TConcrete, RefinementProofState, TAbstract> mapping)
        where TConcrete : IState
        where TAbstract : IState
    {
        var map = ProofStateMapping.Memoize(runtime, mapping);
        return SafetyRefinementCore.Check<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            runtime.InitialStates,
            runtime.Advance,
            (concrete, proofState, abstraction) =>
                StateSemantics.Equal(
                    map(concrete, proofState),
                    abstraction.State),
            (concrete, proofState) => map(concrete, proofState));
    }
}

internal static class ProofStateMapping
{
    /// <summary>
    /// Memoizes a proof-state-aware mapping. The cache key includes the proof
    /// identity so that sibling witness copies at one concrete graph node
    /// never share a mapped abstract state.
    /// </summary>
    internal static Func<StateGraphNode, RefinementProofState, TAbstract>
        Memoize<TConcrete, TAbstract>(
            RefinementProofRuntime<TConcrete> runtime,
            Func<TConcrete, RefinementProofState, TAbstract> mapping)
        where TConcrete : IState
        where TAbstract : IState
    {
        var mappedStates = new Dictionary<string, TAbstract>(StringComparer.Ordinal);

        return (concreteNode, proofState) =>
        {
            if (!(concreteNode.State is TConcrete concreteState))
            {
                throw new InvalidOperationException(
                    $"Concrete graph node state " +
                    $"'{concreteNode.State?.GetType().FullName}' is not a " +
                    $"{typeof(TConcrete).FullName}.");
            }

            var key = concreteNode.GetNodeFingerprint() + "|" +
                proofState.Identity;
            if (!mappedStates.TryGetValue(key, out var mapped))
            {
                mapped = mapping(concreteState, proofState);
                runtime.Validate(proofState);
                if (ReferenceEquals(mapped, null))
                {
                    throw new InvalidOperationException(
                        "The refinement mapping returned null.");
                }
                mappedStates[key] = mapped;
            }
            return mapped;
        };
    }
}
