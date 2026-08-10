namespace Microsoft.Accordant.ModelChecking;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Outcome of a safety-refinement check.
/// </summary>
public enum RefinementCheckingStatus
{
    /// <summary>Every explored concrete behavior is simulated abstractly.</summary>
    Refines,

    /// <summary>
    /// A concrete behavior cannot be simulated by the required abstract
    /// behavior.
    /// </summary>
    DoesNotRefine,

    /// <summary>
    /// No mismatch was found, but a concrete or abstract depth frontier
    /// prevents a definitive result.
    /// </summary>
    InconclusiveBound
}

/// <summary>
/// Kind of refinement failure.
/// </summary>
public enum RefinementFailureKind
{
    /// <summary>The concrete and abstract initial states do not correspond.</summary>
    InitialStateMismatch,

    /// <summary>A concrete transition has no coherent abstract match.</summary>
    TransitionMismatch,

    /// <summary>
    /// A fair concrete behavior has no fair aligned abstract behavior.
    /// </summary>
    TemporalFairnessMismatch
}

/// <summary>
/// One position in a refinement diagnostic trace.
/// </summary>
public sealed class RefinementTraceItem
{
    internal RefinementTraceItem(
        StateGraphNode concreteNode,
        IStepFunction concreteStepFunction,
        object concreteEdgeMetadata,
        IState mappedAbstractState,
        IReadOnlyList<StateGraphNode> abstractCandidates,
        bool isInCycle = false,
        State auxiliaryState = null,
        WitnessCollection witnesses = null,
        AbstractResponse declaredAbstractResponse = null,
        AbstractTransition alignedAbstractTransition = null,
        IReadOnlyList<AbstractTransition> stateConsistentAbstractTransitions = null)
    {
        ConcreteNode = concreteNode;
        ConcreteStepFunction = concreteStepFunction;
        ConcreteEdgeMetadata = concreteEdgeMetadata;
        MappedAbstractState = mappedAbstractState;
        AbstractCandidates = abstractCandidates;
        IsInCycle = isInCycle;
        AuxiliaryState = auxiliaryState;
        Witnesses = witnesses;
        DeclaredAbstractResponse = declaredAbstractResponse;
        AlignedAbstractTransition = alignedAbstractTransition;
        StateConsistentAbstractTransitions = stateConsistentAbstractTransitions;
    }

    /// <summary>The concrete graph node at this trace position.</summary>
    public StateGraphNode ConcreteNode { get; }

    /// <summary>The concrete step that entered this position, or null at the root.</summary>
    public IStepFunction ConcreteStepFunction { get; }

    /// <summary>Metadata on the incoming concrete edge, if any.</summary>
    public object ConcreteEdgeMetadata { get; }

    /// <summary>
    /// The abstract state produced by the functional refinement mapping at
    /// this position.
    /// </summary>
    public IState MappedAbstractState { get; }

    /// <summary>
    /// Coherent abstract graph configurations surviving at this position.
    /// Empty at the position where matching failed.
    /// </summary>
    public IReadOnlyList<StateGraphNode> AbstractCandidates { get; }

    /// <summary>
    /// Whether this position belongs to the repeating part of a temporal
    /// counterexample.
    /// </summary>
    public bool IsInCycle { get; }

    /// <summary>
    /// Deterministic checker-local augmentation state at this position, or
    /// null when the refinement check is not augmented.
    /// </summary>
    public State AuxiliaryState { get; }

    /// <summary>
    /// The future witness values predicted at this position, or null when the
    /// refinement check uses no witnesses. This is separate from
    /// <see cref="AuxiliaryState"/>: augmentation is determined by the
    /// concrete past, witnesses are validated by the concrete future.
    /// </summary>
    public WitnessCollection Witnesses { get; }

    /// <summary>
    /// The abstract response an explicit transition mapping declared for the
    /// concrete step that entered this position, or null when the check uses
    /// no transition mapping, when the position is the root, or when the
    /// transition was left unconstrained.
    /// </summary>
    public AbstractResponse DeclaredAbstractResponse { get; }

    /// <summary>
    /// The abstract response the temporal check aligned the incoming concrete
    /// step with. Null at the root, on a mismatch, and for safety refinement,
    /// which carries a set of coherent abstract configurations rather than one
    /// aligned response.
    /// </summary>
    public AbstractTransition AlignedAbstractTransition { get; }

    /// <summary>
    /// The abstract responses the functional state mapping made
    /// state-consistent at this position, reported where a declared response
    /// admitted none of them or several of them. Null elsewhere.
    /// </summary>
    public IReadOnlyList<AbstractTransition> StateConsistentAbstractTransitions { get; }
}

/// <summary>
/// Result of checking safety refinement.
/// </summary>
public sealed class RefinementCheckingResult
{
    private RefinementCheckingResult(
        RefinementCheckingStatus status,
        RefinementFailureKind? failureKind,
        IReadOnlyList<RefinementTraceItem> trace)
    {
        Status = status;
        FailureKind = failureKind;
        Trace = trace;
    }

    internal static RefinementCheckingResult Success() =>
        new RefinementCheckingResult(
            RefinementCheckingStatus.Refines,
            failureKind: null,
            trace: null);

    internal static RefinementCheckingResult Failure(
        RefinementFailureKind failureKind,
        IReadOnlyList<RefinementTraceItem> trace) =>
        new RefinementCheckingResult(
            RefinementCheckingStatus.DoesNotRefine,
            failureKind,
            trace);

    internal static RefinementCheckingResult Inconclusive(
        IReadOnlyList<RefinementTraceItem> trace) =>
        new RefinementCheckingResult(
            RefinementCheckingStatus.InconclusiveBound,
            failureKind: null,
            trace);

    /// <summary>The definitive or bounded-inconclusive outcome.</summary>
    public RefinementCheckingStatus Status { get; }

    /// <summary>
    /// True or false for conclusive checks; null when bounded exploration is
    /// inconclusive.
    /// </summary>
    public bool? Valid =>
        Status == RefinementCheckingStatus.Refines ? true :
        Status == RefinementCheckingStatus.DoesNotRefine ? false :
        (bool?)null;

    /// <summary>The failure category for a non-refinement result.</summary>
    public RefinementFailureKind? FailureKind { get; }

    /// <summary>
    /// Shortest discovered trace to the mismatch or uncertain frontier.
    /// Null when refinement is proved.
    /// </summary>
    public IReadOnlyList<RefinementTraceItem> Trace { get; }

    /// <summary>Formats the refinement outcome and its diagnostic trace.</summary>
    public string GetTraceString()
    {
        if (Status == RefinementCheckingStatus.Refines)
        {
            return "The concrete model refines the abstract model.";
        }

        var sb = new StringBuilder();
        if (Status == RefinementCheckingStatus.InconclusiveBound)
        {
            sb.AppendLine(
                "Refinement is inconclusive because exploration reached a depth bound.");
        }
        else
        {
            sb.AppendLine(
                FailureKind == RefinementFailureKind.InitialStateMismatch
                    ? "Refinement failed: the concrete and abstract initial states do not correspond."
                    : FailureKind == RefinementFailureKind.TemporalFairnessMismatch
                        ? "Refinement failed: a fair concrete behavior has no fair aligned abstract behavior."
                        : "Refinement failed: a concrete transition has no coherent abstract match.");
        }

        if (Trace == null)
        {
            return sb.ToString();
        }

        foreach (var item in Trace)
        {
            var step = item.ConcreteStepFunction == null
                ? "Start"
                : PropertyCheckingResult.FormatStep(item.ConcreteStepFunction);
            sb.Append("  --")
                .Append(step)
                .Append("--> ")
                .Append(item.IsInCycle ? "[cycle] " : string.Empty)
                .Append("concrete ")
                .Append(item.ConcreteNode.State);
            if (item.MappedAbstractState != null)
            {
                sb.Append("; mapped abstract ")
                    .Append(item.MappedAbstractState);
            }
            if (item.AuxiliaryState != null)
            {
                sb.Append("; auxiliary ")
                    .Append(item.AuxiliaryState);
            }
            if (item.Witnesses != null)
            {
                sb.Append("; witnesses ")
                    .Append(item.Witnesses);
            }
            if (item.DeclaredAbstractResponse != null)
            {
                sb.Append("; declared ")
                    .Append(item.DeclaredAbstractResponse.Description);
            }
            if (item.AlignedAbstractTransition != null)
            {
                sb.Append("; abstract ")
                    .Append(item.AlignedAbstractTransition);
            }
            sb.Append("; candidates ")
                .Append(item.AbstractCandidates.Count);

            if (item.AbstractCandidates.Count > 0)
            {
                sb.Append(" [")
                    .Append(string.Join(
                        ", ",
                        item.AbstractCandidates.Select(candidate =>
                            candidate.GetNodeFingerprint())))
                    .Append(']');
            }

            sb.AppendLine();

            if (item.StateConsistentAbstractTransitions != null)
            {
                sb.Append("      state-consistent abstract responses: ")
                    .AppendLine(
                        item.StateConsistentAbstractTransitions.Count == 0
                            ? "none"
                            : string.Join(
                                ", ",
                                item.StateConsistentAbstractTransitions
                                    .Select(response => response.ToString())
                                    .Distinct()));
            }
        }

        return sb.ToString();
    }
}
