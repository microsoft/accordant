namespace Microsoft.Accordant.ModelChecking;

using System;

internal static class RelationalSafetyRefinement
{
    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAbstract, bool> correspondence)
        where TConcrete : IState
        where TAbstract : IState
        => SafetyRefinementCore.Check<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            (concrete, abstractNode) => correspondence(
                (TConcrete)concrete.State,
                (TAbstract)abstractNode.State));
}
