namespace Microsoft.Accordant.ModelChecking;

using System;

/// <summary>
/// Indicates that functional temporal refinement encountered more than one
/// known abstract response for a concrete transition.
/// </summary>
public sealed class AmbiguousTemporalRefinementException : Exception
{
    internal AmbiguousTemporalRefinementException(
        StateGraphNode concreteNode,
        StateGraphNode abstractNode,
        IStepFunction concreteStep,
        int matchCount)
        : base(
            $"Functional temporal refinement requires deterministic alignment. " +
            $"Concrete step '{concreteStep?.StepFunctionId}' from " +
            $"'{concreteNode?.State}' has {matchCount} matching abstract " +
            $"responses from '{abstractNode?.State}'.")
    {
        ConcreteNode = concreteNode;
        AbstractNode = abstractNode;
        ConcreteStep = concreteStep;
        MatchCount = matchCount;
    }

    /// <summary>The concrete configuration where ambiguity was found.</summary>
    public StateGraphNode ConcreteNode { get; }

    /// <summary>The abstract configuration where ambiguity was found.</summary>
    public StateGraphNode AbstractNode { get; }

    /// <summary>The concrete step being aligned.</summary>
    public IStepFunction ConcreteStep { get; }

    /// <summary>The number of matching abstract responses.</summary>
    public int MatchCount { get; }
}
