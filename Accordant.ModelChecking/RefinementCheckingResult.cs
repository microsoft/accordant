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
        IReadOnlyList<AbstractTransition> stateConsistentAbstractTransitions = null,
        AbstractProjectionKind projectionKind = AbstractProjectionKind.Start)
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
        ProjectionKind = projectionKind;
    }

    /// <summary>
    /// Classifies an aligned abstract response, which the temporal check knows
    /// exactly.
    /// </summary>
    internal static AbstractProjectionKind Classify(
        AbstractTransition aligned,
        bool hasIncomingConcreteStep,
        bool isCheckerCompletion)
        => !hasIncomingConcreteStep
            ? AbstractProjectionKind.Start
            : isCheckerCompletion
                ? AbstractProjectionKind.CheckerCompletion
            : aligned == null
                ? AbstractProjectionKind.Unaligned
                : aligned.IsStutter
                    ? AbstractProjectionKind.HiddenAction
                    : aligned.ChangesState
                        ? AbstractProjectionKind.AbstractStep
                        : AbstractProjectionKind.StateNeutralStep;

    /// <summary>
    /// Classifies a position of a safety-refinement trace, which carries a set
    /// of coherent abstract configurations instead of one aligned response. A
    /// changing mapped abstract state can only have come from a changing
    /// abstract edge; an unchanged one is reported as hidden only where the
    /// model declared it hidden.
    /// </summary>
    internal static AbstractProjectionKind Classify(
        IState previousMappedAbstractState,
        IState mappedAbstractState,
        bool hasIncomingConcreteStep,
        bool hasAbstractCandidates,
        AbstractResponse declaredAbstractResponse)
    {
        if (!hasIncomingConcreteStep)
        {
            return AbstractProjectionKind.Start;
        }

        if (!hasAbstractCandidates)
        {
            return AbstractProjectionKind.Unaligned;
        }

        if (previousMappedAbstractState == null ||
            mappedAbstractState == null ||
            !StateSemantics.Equal(
                previousMappedAbstractState,
                mappedAbstractState))
        {
            return AbstractProjectionKind.AbstractStep;
        }

        return declaredAbstractResponse != null &&
            declaredAbstractResponse.HidesConcreteAction
                ? AbstractProjectionKind.HiddenAction
                : AbstractProjectionKind.AbstractUnchanged;
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

    /// <summary>
    /// How the concrete step that entered this position appears after the
    /// mapping projects it onto the abstract model: a hidden action, a
    /// state-neutral abstract edge, an ordinary abstract step, or no abstract
    /// projection at all.
    /// </summary>
    public AbstractProjectionKind ProjectionKind { get; }
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
        AppendOutcome(sb);

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

        AppendHiddenDivergenceHint(sb);
        AppendCheckedHidingHint(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the diagnostic trace as the projection the refinement mapping
    /// defines: each concrete position, the abstract state it maps to, and how
    /// the concrete step that entered it appears abstractly — a hidden action,
    /// a state-neutral abstract step, or an ordinary abstract step.
    /// </summary>
    public string GetProjectionString()
    {
        if (Trace == null)
        {
            return GetTraceString();
        }

        var sb = new StringBuilder();
        AppendOutcome(sb);
        sb.AppendLine(
            "Concrete behavior projected onto the abstract model by the mapping:");

        foreach (var item in Trace)
        {
            sb.Append("  ")
                .Append(item.IsInCycle ? "[cycle] " : string.Empty)
                .Append(item.ConcreteStepFunction == null
                    ? "start"
                    : "--" +
                        PropertyCheckingResult.FormatStep(
                            item.ConcreteStepFunction) +
                        "-->")
                .Append(' ')
                .Append(item.ConcreteNode.State)
                .Append("  =>  ")
                .Append(item.MappedAbstractState == null
                    ? "<unmapped>"
                    : item.MappedAbstractState.ToString());

            var projection = DescribeProjection(item);
            if (projection != null)
            {
                sb.Append("   ").Append(projection);
            }
            sb.AppendLine();
        }

        AppendHiddenDivergenceHint(sb);
        AppendCheckedHidingHint(sb);
        return sb.ToString();
    }

    private string DescribeProjection(RefinementTraceItem item)
    {
        var aligned = item.AlignedAbstractTransition;

        switch (item.ProjectionKind)
        {
            case AbstractProjectionKind.Start:
                return null;

            case AbstractProjectionKind.CheckerCompletion:
                return "checker completion of a terminal concrete behavior";

            case AbstractProjectionKind.HiddenAction:
                return "hidden action (abstract stutter)";

            case AbstractProjectionKind.StateNeutralStep:
                return "state-neutral abstract step " +
                    aligned.StepFunction.StepFunctionId;

            case AbstractProjectionKind.AbstractStep:
                return aligned == null
                    ? "abstract step"
                    : "abstract step " + aligned.StepFunction.StepFunctionId;

            case AbstractProjectionKind.AbstractUnchanged:
                return "abstract state unchanged " +
                    "(abstract stutter or a state-neutral abstract step)";

            default:
                return Status == RefinementCheckingStatus.InconclusiveBound
                    ? "abstract projection unknown at depth bound"
                    : "no abstract projection";
        }
    }

    private void AppendOutcome(StringBuilder sb)
    {
        if (Status == RefinementCheckingStatus.InconclusiveBound)
        {
            sb.AppendLine(
                "Refinement is inconclusive because exploration reached a depth bound.");
            return;
        }

        if (Status == RefinementCheckingStatus.DoesNotRefine)
        {
            sb.AppendLine(
                FailureKind == RefinementFailureKind.InitialStateMismatch
                    ? "Refinement failed: the concrete and abstract initial states do not correspond."
                    : FailureKind == RefinementFailureKind.TemporalFairnessMismatch
                        ? "Refinement failed: a fair concrete behavior has no fair aligned abstract behavior."
                        : "Refinement failed: a concrete transition has no coherent abstract match.");
        }
    }

    /// <summary>
    /// Explains a fairness counterexample whose repeating part is entirely
    /// hidden. Hiding a concrete action declares that the abstract model does
    /// not move; it never removes the concrete behavior, so an infinite loop
    /// of hidden actions still has to be excluded by concrete fairness.
    /// </summary>
    private void AppendHiddenDivergenceHint(StringBuilder sb)
    {
        if (FailureKind != RefinementFailureKind.TemporalFairnessMismatch ||
            Trace == null)
        {
            return;
        }

        // The first in-cycle position is the lasso entry, reported with the
        // prefix transition that reached it. The repeating transitions are
        // the ones after it.
        var cycle = Trace
            .Where(item => item.IsInCycle)
            .Skip(1)
            .Where(item => item.ConcreteStepFunction != null)
            .ToArray();
        if (cycle.Length == 0 ||
            cycle.Any(item =>
                item.ProjectionKind != AbstractProjectionKind.HiddenAction))
        {
            return;
        }

        sb.AppendLine(
            "  Every concrete transition in the repeating part is hidden, so " +
            "the abstract model stutters forever there.");
        sb.AppendLine(
            "  Hidden actions never discharge an abstract fairness " +
            "obligation. This concrete divergence is a real behavior: " +
            "exclude it with concrete fairness if the implementation cannot " +
            "actually run it forever.");
    }

    /// <summary>
    /// Explains a mismatch where the model declared a concrete transition
    /// hidden but the mapping moves the abstract state across it. Hiding is a
    /// checked declaration of abstract stutter, not a way to suppress a
    /// transition.
    /// </summary>
    private void AppendCheckedHidingHint(StringBuilder sb)
    {
        if (FailureKind != RefinementFailureKind.TransitionMismatch ||
            Trace == null ||
            Trace.Count == 0)
        {
            return;
        }

        var mismatch = Trace.FirstOrDefault(item =>
            item.ProjectionKind == AbstractProjectionKind.Unaligned &&
            item.DeclaredAbstractResponse != null &&
            item.DeclaredAbstractResponse.HidesConcreteAction);
        if (mismatch == null)
        {
            return;
        }

        sb.AppendLine(
            "  The declaration hides this concrete transition, but the " +
            "mapping does not: the mapped abstract state is not the one the " +
            "abstract model stays at.");
        sb.AppendLine(
            "  Hiding a concrete action is the checked claim that the " +
            "abstract model stutters. Map the transition to the abstract " +
            "action it really performs, or hide the concrete detail in the " +
            "state mapping so the abstract state does not change.");
    }
}
