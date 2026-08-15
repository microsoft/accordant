namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One checker-local refinement proof position: the deterministic
/// augmentation value derived from the concrete past and the witness values
/// predicted for pending concrete operations. Both are carried separately so
/// diagnostics never conflate them.
/// </summary>
internal sealed class RefinementProofState
{
    private RefinementProofState(
        object auxiliary,
        WitnessCollection witnesses,
        string identity)
    {
        Auxiliary = auxiliary;
        Witnesses = witnesses;
        Identity = identity;
    }

    public static RefinementProofState Empty { get; } =
        new RefinementProofState(null, null, string.Empty);

    public static RefinementProofState Create(
        object auxiliary,
        string auxiliaryIdentity,
        WitnessCollection witnesses)
    {
        if (auxiliary == null && witnesses == null)
        {
            return Empty;
        }
        return new RefinementProofState(
            auxiliary,
            witnesses,
            (auxiliaryIdentity ?? string.Empty) + "|" +
                (witnesses == null ? string.Empty : witnesses.Identity));
    }

    /// <summary>The deterministic augmentation value, or null.</summary>
    public object Auxiliary { get; }

    /// <summary>The witness collection, or null when unused.</summary>
    public WitnessCollection Witnesses { get; }

    /// <summary>Semantic identity used for search and cache keys.</summary>
    public string Identity { get; }

    public State AuxiliaryState => Auxiliary as State;
}

/// <summary>
/// Deterministic checker-local augmentation, independent of the witness
/// mechanism.
/// </summary>
internal interface IAugmentationRuntime
{
    object InitialState { get; }

    object Advance(
        object auxiliaryState,
        StateGraphNode source,
        StateGraphEdge edge);

    string GetIdentity(object auxiliaryState);

    void Validate(object auxiliaryState);
}

/// <summary>
/// Combines optional deterministic augmentation with optional future
/// witnesses. On one concrete transition both are computed independently from
/// the same transition; neither reads the other.
/// </summary>
internal sealed class RefinementProofRuntime<TConcrete>
    where TConcrete : IState
{
    private static readonly IReadOnlyList<RefinementProofState> EmptyOnly =
        new[] { RefinementProofState.Empty };

    private readonly IAugmentationRuntime augmentation;
    private readonly WitnessRuntime<TConcrete> witnesses;

    public RefinementProofRuntime(
        IAugmentationRuntime augmentation,
        WitnessRuntime<TConcrete> witnesses)
    {
        this.augmentation = augmentation;
        this.witnesses = witnesses;

        if (augmentation == null && witnesses == null)
        {
            InitialStates = EmptyOnly;
            return;
        }

        var auxiliary = augmentation?.InitialState;
        var auxiliaryIdentity = augmentation == null
            ? string.Empty
            : augmentation.GetIdentity(auxiliary);
        InitialStates = witnesses == null
            ? new[]
            {
                RefinementProofState.Create(
                    auxiliary,
                    auxiliaryIdentity,
                    null)
            }
            : witnesses.InitialCollections
                .Select(collection => RefinementProofState.Create(
                    auxiliary,
                    auxiliaryIdentity,
                    collection))
                .ToArray();
    }

    public IReadOnlyList<RefinementProofState> InitialStates { get; }

    public IReadOnlyList<RefinementProofState> Advance(
        RefinementProofState state,
        StateGraphNode source,
        StateGraphEdge edge)
    {
        if (augmentation == null && witnesses == null)
        {
            return EmptyOnly;
        }

        object auxiliary = null;
        var auxiliaryIdentity = string.Empty;
        if (augmentation != null)
        {
            auxiliary = augmentation.Advance(state.Auxiliary, source, edge);
            auxiliaryIdentity = augmentation.GetIdentity(auxiliary);
        }

        if (witnesses == null)
        {
            return new[]
            {
                RefinementProofState.Create(
                    auxiliary,
                    auxiliaryIdentity,
                    null)
            };
        }

        var branches = witnesses.Advance(state.Witnesses, source, edge);
        if (branches.Count == 0)
        {
            return Array.Empty<RefinementProofState>();
        }

        var result = new RefinementProofState[branches.Count];
        for (var index = 0; index < branches.Count; index++)
        {
            result[index] = RefinementProofState.Create(
                auxiliary,
                auxiliaryIdentity,
                branches[index]);
        }
        return result;
    }

    /// <summary>
    /// Validates that user callbacks did not mutate checker-local values.
    /// </summary>
    public void Validate(RefinementProofState state)
    {
        if (augmentation != null)
        {
            augmentation.Validate(state.Auxiliary);
        }
        if (witnesses != null)
        {
            witnesses.Validate(state.Witnesses);
        }
    }
}
