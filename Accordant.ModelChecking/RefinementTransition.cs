namespace Microsoft.Accordant.ModelChecking;

/// <summary>
/// A concrete transition observed while updating deterministic refinement
/// augmentation state.
/// </summary>
public sealed class RefinementTransition<TConcrete>
    where TConcrete : IState
{
    internal RefinementTransition(
        TConcrete source,
        IStepFunction stepFunction,
        object metadata,
        TConcrete target)
    {
        Source = source;
        StepFunction = stepFunction;
        Metadata = metadata;
        Target = target;
    }

    /// <summary>The concrete state before the transition.</summary>
    public TConcrete Source { get; }

    /// <summary>The concrete step that produced the transition.</summary>
    public IStepFunction StepFunction { get; }

    /// <summary>Metadata attached to the concrete graph edge.</summary>
    public object Metadata { get; }

    /// <summary>The concrete state after the transition.</summary>
    public TConcrete Target { get; }
}
