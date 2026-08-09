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

    /// <summary>A finite concrete behavior cannot be simulated abstractly.</summary>
    DoesNotRefine,

    /// <summary>
    /// No mismatch was found, but a concrete or abstract depth frontier
    /// prevents a definitive result.
    /// </summary>
    InconclusiveBound
}

/// <summary>
/// Kind of finite safety-refinement failure.
/// </summary>
public enum RefinementFailureKind
{
    /// <summary>The mapped concrete initial state is not the abstract initial state.</summary>
    InitialStateMismatch,

    /// <summary>A concrete transition has no coherent abstract match.</summary>
    TransitionMismatch
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
        IReadOnlyList<StateGraphNode> abstractCandidates)
    {
        ConcreteNode = concreteNode;
        ConcreteStepFunction = concreteStepFunction;
        ConcreteEdgeMetadata = concreteEdgeMetadata;
        MappedAbstractState = mappedAbstractState;
        AbstractCandidates = abstractCandidates;
    }

    /// <summary>The concrete graph node at this trace position.</summary>
    public StateGraphNode ConcreteNode { get; }

    /// <summary>The concrete step that entered this position, or null at the root.</summary>
    public IStepFunction ConcreteStepFunction { get; }

    /// <summary>Metadata on the incoming concrete edge, if any.</summary>
    public object ConcreteEdgeMetadata { get; }

    /// <summary>The abstract state produced by the refinement mapping.</summary>
    public IState MappedAbstractState { get; }

    /// <summary>
    /// Coherent abstract graph configurations surviving at this position.
    /// Empty at the position where matching failed.
    /// </summary>
    public IReadOnlyList<StateGraphNode> AbstractCandidates { get; }
}

/// <summary>
/// Result of checking functional safety refinement.
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
            sb.AppendLine(FailureKind == RefinementFailureKind.InitialStateMismatch
                ? "Refinement failed: the mapped concrete initial state does not match the abstract initial state."
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
                .Append("--> concrete ")
                .Append(item.ConcreteNode.State)
                .Append("; mapped abstract ")
                .Append(item.MappedAbstractState)
                .Append("; candidates ")
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
        }

        return sb.ToString();
    }
}
