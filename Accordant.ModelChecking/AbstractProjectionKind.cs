namespace Microsoft.Accordant.ModelChecking;

/// <summary>
/// How one concrete transition appears after it is projected onto the abstract
/// model by the functional refinement mapping. A refinement mapping is a
/// projection: it hides everything the abstract state does not record, so each
/// concrete transition either moves the abstract model or does not.
/// </summary>
public enum AbstractProjectionKind
{
    /// <summary>
    /// The first position of a behavior. No concrete transition entered it.
    /// </summary>
    Start,

    /// <summary>
    /// The checker completed a genuinely terminal concrete behavior with its
    /// synthetic infinite stutter. This is not a concrete action and must not
    /// be interpreted as hidden implementation divergence.
    /// </summary>
    CheckerCompletion,

    /// <summary>
    /// The concrete transition was aligned with abstract stutter: the abstract
    /// model did not move, so the abstraction hides this concrete action.
    /// Hiding is a checked claim, not a suppression — the mapped abstract
    /// state had to be unchanged for the alignment to exist. A hidden action
    /// raises and discharges no abstract fairness obligation.
    /// </summary>
    HiddenAction,

    /// <summary>
    /// The concrete transition was aligned with a real abstract edge that
    /// leaves the abstract state unchanged. This moves the abstract graph
    /// configuration — and therefore the abstract steps enabled afterwards —
    /// without changing the abstract state. Accordant fairness is defined over
    /// changing edges, so it neither raises nor discharges an obligation.
    /// </summary>
    StateNeutralStep,

    /// <summary>
    /// The concrete transition was aligned with an abstract edge that changes
    /// the abstract state: an ordinary abstract step.
    /// </summary>
    AbstractStep,

    /// <summary>
    /// The mapped abstract state did not change, but the check did not pin the
    /// response down to abstract stutter or to a state-neutral abstract edge.
    /// Safety refinement carries a set of coherent abstract configurations
    /// rather than one aligned response, so it reports this instead of
    /// guessing.
    /// </summary>
    AbstractUnchanged,

    /// <summary>
    /// No known abstract response survived at this position. This is either
    /// the position of a transition mismatch or a bounded frontier where the
    /// abstract projection remains unknown.
    /// </summary>
    Unaligned
}
