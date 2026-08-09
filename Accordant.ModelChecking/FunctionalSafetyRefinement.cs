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
        var mappedStates = new Dictionary<string, TAbstract>();

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
}
