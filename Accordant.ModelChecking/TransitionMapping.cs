namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;

/// <summary>
/// Selects the abstract response one concrete transition represents. The
/// selector reads the concrete transition and the refinement proof state the
/// transition <em>departs from</em>; it never receives a graph node, so it
/// cannot inspect or expand either graph.
/// </summary>
internal delegate AbstractResponse AbstractResponseSelector(
    StateGraphNode concreteSource,
    RefinementProofState proofState,
    StateGraphEdge concreteEdge);

/// <summary>
/// An explicit transition mapping. It narrows the state-consistent abstract
/// responses of a concrete transition; it can never admit a response the
/// functional state mapping already excluded.
/// </summary>
internal sealed class TransitionMapping
{
    private readonly AbstractResponseSelector selector;
    private readonly Action<RefinementProofState> validate;
    private readonly Dictionary<
        StateGraphEdge,
        Dictionary<string, AbstractResponse>> cache =
            new Dictionary<StateGraphEdge, Dictionary<string, AbstractResponse>>();

    internal TransitionMapping(
        AbstractResponseSelector selector,
        Action<RefinementProofState> validate = null)
    {
        this.selector = selector;
        this.validate = validate;
    }

    /// <summary>
    /// The response declared for a concrete edge leaving a concrete node in a
    /// given proof state. Memoized per concrete edge and proof identity: the
    /// same concrete edge is aligned from many abstract configurations, and
    /// the declaration never depends on the abstract side.
    /// </summary>
    internal AbstractResponse Declared(
        StateGraphNode concreteSource,
        RefinementProofState proofState,
        StateGraphEdge concreteEdge)
    {
        if (!cache.TryGetValue(concreteEdge, out var byProofState))
        {
            byProofState = new Dictionary<string, AbstractResponse>(
                StringComparer.Ordinal);
            cache[concreteEdge] = byProofState;
        }

        if (!byProofState.TryGetValue(proofState.Identity, out var response))
        {
            response = selector(concreteSource, proofState, concreteEdge);
            validate?.Invoke(proofState);
            if (response == null)
            {
                throw new InvalidOperationException(
                    $"The transition mapping returned null for concrete step " +
                    $"'{concreteEdge.StepFunction?.StepFunctionId}' from " +
                    $"'{concreteSource.State}'. Return " +
                    $"{nameof(AbstractResponse)}." +
                    $"{nameof(AbstractResponse.Unconstrained)} for transitions " +
                    $"the mapping does not constrain.");
            }
            byProofState[proofState.Identity] = response;
        }

        return response;
    }

    internal void Validate(RefinementProofState proofState)
        => validate?.Invoke(proofState);

    /// <summary>
    /// Whether a declared response admits one abstract response. A null
    /// declaration admits every response, so the checker behaves exactly as
    /// it does without a transition mapping.
    /// </summary>
    internal static bool Admits(
        AbstractResponse declared,
        StateGraphNode abstractSource,
        StateGraphEdge abstractEdge)
        => declared == null ||
            declared.Admits(View(abstractSource, abstractEdge));

    /// <summary>
    /// The typed view of one abstract response. A null
    /// <paramref name="abstractEdge"/> is abstract stutter.
    /// </summary>
    internal static AbstractTransition View(
        StateGraphNode abstractSource,
        StateGraphEdge abstractEdge)
        => abstractEdge == null
            ? new AbstractTransition(
                abstractSource.State,
                stepFunction: null,
                metadata: null,
                abstractSource.State)
            : new AbstractTransition(
                abstractSource.State,
                abstractEdge.StepFunction,
                abstractEdge.Metadata,
                abstractEdge.Target.State);
}
