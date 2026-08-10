namespace Microsoft.Accordant.ModelChecking;

using System;

/// <summary>
/// One abstract response a concrete transition can be aligned with: a single
/// abstract graph edge, or abstract stutter, where the abstract model does
/// not move at all.
/// </summary>
public sealed class AbstractTransition
{
    internal AbstractTransition(
        IState source,
        IStepFunction stepFunction,
        object metadata,
        IState target)
    {
        Source = source;
        StepFunction = stepFunction;
        Metadata = metadata;
        Target = target;
    }

    /// <summary>The abstract state the response departs from.</summary>
    public IState Source { get; }

    /// <summary>
    /// The abstract step function, or null when the response is abstract
    /// stutter.
    /// </summary>
    public IStepFunction StepFunction { get; }

    /// <summary>
    /// Metadata on the abstract graph edge, or null when the response is
    /// abstract stutter.
    /// </summary>
    public object Metadata { get; }

    /// <summary>The abstract state the response arrives at.</summary>
    public IState Target { get; }

    /// <summary>
    /// Whether the abstract model does not move. Abstract stutter takes no
    /// abstract action and keeps the same abstract graph configuration. A
    /// concrete transition aligned with abstract stutter is exactly a concrete
    /// action the abstraction hides.
    /// </summary>
    public bool IsStutter => StepFunction == null;

    /// <summary>
    /// Whether the abstract state changes. A <em>state-neutral</em> abstract
    /// edge has <see cref="IsStutter"/> false and <see cref="ChangesState"/>
    /// false: it is a real abstract action that moves to another abstract
    /// configuration without changing the abstract state. Accordant fairness
    /// is defined over changing edges only, so a state-neutral edge can be
    /// aligned explicitly but never raises or discharges a fairness
    /// obligation.
    /// </summary>
    public bool ChangesState => !StateSemantics.Equal(Source, Target);

    /// <summary>Formats the response for diagnostics.</summary>
    public override string ToString()
        => IsStutter
            ? "stutter"
            : "step " + StepFunction.StepFunctionId;
}

/// <summary>
/// The abstract response a concrete transition represents. A response never
/// widens matching: it filters the abstract responses the functional state
/// mapping already made state-consistent.
/// </summary>
public sealed class AbstractResponse
{
    private readonly Func<AbstractTransition, bool> admits;

    private AbstractResponse(
        string description,
        Func<AbstractTransition, bool> admits,
        bool hidesConcreteAction = false)
    {
        Description = description;
        this.admits = admits;
        HidesConcreteAction = hidesConcreteAction;
    }

    /// <summary>
    /// The concrete transition does not constrain the abstract response.
    /// This is the default for every transition a transition mapping does not
    /// name, and it reproduces state-only matching exactly.
    /// </summary>
    public static AbstractResponse Unconstrained { get; } =
        new AbstractResponse("unconstrained", _ => true);

    /// <summary>
    /// The abstract model does not move. This admits abstract stutter only,
    /// never a state-neutral abstract edge.
    /// </summary>
    public static AbstractResponse Stutter { get; } =
        new AbstractResponse(
            "stutter",
            transition => transition.IsStutter,
            hidesConcreteAction: true);

    /// <summary>
    /// The concrete transition is an internal action the abstraction hides:
    /// the abstract model does not move at all.
    /// </summary>
    /// <remarks>
    /// This is <see cref="Stutter"/> under the name that says why it was
    /// declared. Hiding a concrete action in Accordant is exactly the checked
    /// claim that the action maps to abstract stutter, and it is checked like
    /// any other declaration: if the concrete transition changes the mapped
    /// abstract state, the response is not state-consistent and the check
    /// reports a <see cref="RefinementFailureKind.TransitionMismatch"/>.
    /// There is no unchecked way to suppress a concrete transition.
    /// <para>
    /// Hiding declares only that the abstract model stands still. It never
    /// removes the concrete transition, changes concrete enabledness, or
    /// discharges an abstract fairness obligation, so an infinite concrete
    /// loop of hidden actions remains a real concrete behavior unless
    /// concrete fairness excludes it.
    /// </para>
    /// </remarks>
    public static AbstractResponse Hidden { get; } =
        new AbstractResponse(
            "hidden (abstract stutter)",
            transition => transition.IsStutter,
            hidesConcreteAction: true);

    /// <summary>
    /// The abstract model takes an edge whose step function is
    /// <typeparamref name="TStep"/>. Abstract stutter is never admitted.
    /// </summary>
    public static AbstractResponse Step<TStep>()
        where TStep : IStepFunction
        => new AbstractResponse(
            "step " + typeof(TStep).Name,
            transition => transition.StepFunction is TStep);

    /// <summary>
    /// The abstract model takes an edge whose step function matches
    /// <paramref name="selector"/>. Abstract stutter is never admitted.
    /// </summary>
    public static AbstractResponse Step(Func<IStepFunction, bool> selector)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        return new AbstractResponse(
            "selected step",
            transition => !transition.IsStutter &&
                selector(transition.StepFunction));
    }

    /// <summary>
    /// The abstract response matches <paramref name="predicate"/>. The
    /// predicate also sees abstract stutter, where
    /// <see cref="AbstractTransition.StepFunction"/> is null.
    /// </summary>
    public static AbstractResponse Matching(
        Func<AbstractTransition, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return new AbstractResponse("matching response", predicate);
    }

    /// <summary>
    /// The abstract response matches a typed source, step and target
    /// predicate. Abstract stutter is never admitted.
    /// </summary>
    public static AbstractResponse Matching<TAbstract>(
        Func<TAbstract, IStepFunction, TAbstract, bool> predicate)
        where TAbstract : State
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return new AbstractResponse(
            "matching " + typeof(TAbstract).Name + " response",
            transition => !transition.IsStutter &&
                transition.Source is TAbstract source &&
                transition.Target is TAbstract target &&
                predicate(source, transition.StepFunction, target));
    }

    /// <summary>A human-readable description used in diagnostics.</summary>
    public string Description { get; }

    /// <summary>
    /// Whether this declaration admits abstract stutter only, and therefore
    /// declares the concrete transition to be hidden by the abstraction.
    /// True for <see cref="Stutter"/> and <see cref="Hidden"/>.
    /// </summary>
    public bool HidesConcreteAction { get; }

    /// <summary>Formats the declared response for diagnostics.</summary>
    public override string ToString() => Description;

    internal bool Admits(AbstractTransition transition) => admits(transition);
}
