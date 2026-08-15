namespace Microsoft.Accordant.ModelChecking;

using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Symbolic;

/// <summary>
/// The transition (action) presented to a proposition or semantic-action
/// fairness predicate of the form <c>p(s, a, metadata, s')</c>. It exposes
/// the <see cref="IStepFunction"/> that produced the transition together
/// with any edge <see cref="Metadata"/>.
///
/// <para>For the stutter self-loop emitted at terminal nodes,
/// <see cref="IsStutter"/> is <c>true</c> and <see cref="Action"/>
/// is the reserved <see cref="StutterAction"/> singleton.</para>
/// </summary>
public sealed class Transition
{
    internal Transition(IStepFunction action, object metadata)
    {
        Action = action;
        Metadata = metadata;
    }

    /// <summary>
    /// The step function that produced this transition, or the reserved
    /// stutter action for a stutter self-loop. May be <c>null</c> only in
    /// contexts where no proposition inspects the action.
    /// </summary>
    public IStepFunction Action { get; }

    /// <summary>The edge metadata associated with <see cref="Action"/>, if any.</summary>
    public object Metadata { get; }

    /// <summary>
    /// The identifier of the underlying step function, or <c>null</c> when
    /// <see cref="Action"/> is <c>null</c>.
    /// </summary>
    public string ActionId => Action?.StepFunctionId;

    /// <summary>
    /// <c>true</c> when this is the checker's synthetic terminal stutter.
    /// Ordinary model edges that leave domain state unchanged are not
    /// synthetic stutter and return <c>false</c>.
    /// </summary>
    public bool IsStutter => Action is StutterAction;
}
