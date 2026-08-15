namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Indicates that functional temporal refinement encountered more than one
/// known abstract response for a concrete transition. An explicit transition
/// mapping resolves the ambiguity when the responses differ by step function,
/// edge metadata, or abstract state; responses identical in all three are
/// irreducibly ambiguous and remain an error.
/// </summary>
public sealed class AmbiguousTemporalRefinementException : Exception
{
    internal AmbiguousTemporalRefinementException(
        StateGraphNode concreteNode,
        StateGraphNode abstractNode,
        IStepFunction concreteStep,
        int matchCount,
        IReadOnlyList<AbstractTransition> responses = null,
        AbstractResponse declaredResponse = null)
        : base(
            $"Functional temporal refinement requires deterministic alignment. " +
            $"Concrete step '{concreteStep?.StepFunctionId}' from " +
            $"'{concreteNode?.State}' has {matchCount} matching abstract " +
            $"responses from '{abstractNode?.State}'" +
            Describe(responses) +
            (declaredResponse == null
                ? ". Declare the intended response with .MapTransition(...)."
                : $", and the declared response '{declaredResponse.Description}' " +
                    $"admits all of them. Narrow the declared response."))
    {
        ConcreteNode = concreteNode;
        AbstractNode = abstractNode;
        ConcreteStep = concreteStep;
        MatchCount = matchCount;
        Responses = responses ?? Array.Empty<AbstractTransition>();
        DeclaredResponse = declaredResponse;
    }

    /// <summary>The concrete configuration where ambiguity was found.</summary>
    public StateGraphNode ConcreteNode { get; }

    /// <summary>The abstract configuration where ambiguity was found.</summary>
    public StateGraphNode AbstractNode { get; }

    /// <summary>The concrete step being aligned.</summary>
    public IStepFunction ConcreteStep { get; }

    /// <summary>The number of matching abstract responses.</summary>
    public int MatchCount { get; }

    /// <summary>
    /// The matching abstract responses, so a transition mapping can name the
    /// intended one.
    /// </summary>
    public IReadOnlyList<AbstractTransition> Responses { get; }

    /// <summary>
    /// The response an explicit transition mapping declared, or null when the
    /// transition was left unconstrained.
    /// </summary>
    public AbstractResponse DeclaredResponse { get; }

    private static string Describe(IReadOnlyList<AbstractTransition> responses)
        => responses == null || responses.Count == 0
            ? string.Empty
            : " (" + string.Join(
                ", ",
                responses.Select(response => response.ToString())) + ")";
}
