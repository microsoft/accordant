namespace Microsoft.Accordant.ModelChecking;

using System;

/// <summary>
/// Entry point for checking refinement between two explored state graphs.
/// </summary>
public static class Refinement
{
    /// <summary>
    /// Creates a typed refinement builder for a concrete and abstract state graph.
    /// </summary>
    public static RefinementBuilder<TConcrete, TAbstract> Between<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot)
        where TConcrete : IState
        where TAbstract : IState
        => new RefinementBuilder<TConcrete, TAbstract>(concreteRoot, abstractRoot);
}

/// <summary>
/// Defines how concrete states map to abstract states.
/// </summary>
public sealed class RefinementBuilder<TConcrete, TAbstract>
    where TConcrete : IState
    where TAbstract : IState
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;

    internal RefinementBuilder(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot)
    {
        this.concreteRoot = concreteRoot
            ?? throw new ArgumentNullException(nameof(concreteRoot));
        this.abstractRoot = abstractRoot
            ?? throw new ArgumentNullException(nameof(abstractRoot));

        if (!(concreteRoot.State is TConcrete))
        {
            throw new ArgumentException(
                $"The concrete root state must be a {typeof(TConcrete).FullName}.",
                nameof(concreteRoot));
        }

        if (!(abstractRoot.State is TAbstract))
        {
            throw new ArgumentException(
                $"The abstract root state must be a {typeof(TAbstract).FullName}.",
                nameof(abstractRoot));
        }
    }

    /// <summary>
    /// Defines the functional mapping from each concrete state to its abstract
    /// state representation.
    /// </summary>
    public FunctionalRefinementCheck<TConcrete, TAbstract> Map(
        Func<TConcrete, TAbstract> mapping)
        => new FunctionalRefinementCheck<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            mapping ?? throw new ArgumentNullException(nameof(mapping)));

    /// <summary>
    /// Defines a relational correspondence between concrete and abstract
    /// states. The checker retains every path-coherent abstract candidate
    /// until a later concrete state rules it out.
    /// </summary>
    public RelationalRefinementCheck<TConcrete, TAbstract> Corresponds(
        Func<TConcrete, TAbstract, bool> correspondence)
        => new RelationalRefinementCheck<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            correspondence ?? throw new ArgumentNullException(nameof(correspondence)));

    /// <summary>
    /// Adds deterministic checker-local state derived from the concrete
    /// execution prefix.
    /// </summary>
    public AugmentedRefinementBuilder<TConcrete, TAbstract, TAuxiliary>
        Augment<TAuxiliary>(
            Func<TConcrete, TAuxiliary> initial,
            Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next)
        where TAuxiliary : State
        => new AugmentedRefinementBuilder<
            TConcrete,
            TAbstract,
            TAuxiliary>(
                concreteRoot,
                abstractRoot,
                initial ?? throw new ArgumentNullException(nameof(initial)),
                next ?? throw new ArgumentNullException(nameof(next)));
}

/// <summary>
/// Defines refinement using deterministic checker-local augmentation state.
/// </summary>
public sealed class AugmentedRefinementBuilder<
    TConcrete,
    TAbstract,
    TAuxiliary>
    where TConcrete : IState
    where TAbstract : IState
    where TAuxiliary : State
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, TAuxiliary> initial;
    private readonly Func<
        TAuxiliary,
        RefinementTransition<TConcrete>,
        TAuxiliary> next;

    internal AugmentedRefinementBuilder(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.initial = initial;
        this.next = next;
    }

    /// <summary>
    /// Defines a functional abstract-state mapping that can read the current
    /// concrete and augmentation states.
    /// </summary>
    public AugmentedFunctionalRefinementCheck<
        TConcrete,
        TAbstract,
        TAuxiliary> Map(
            Func<TConcrete, TAuxiliary, TAbstract> mapping)
        => new AugmentedFunctionalRefinementCheck<
            TConcrete,
            TAbstract,
            TAuxiliary>(
                concreteRoot,
                abstractRoot,
                initial,
                next,
                mapping ?? throw new ArgumentNullException(nameof(mapping)));

    /// <summary>
    /// Defines a relational correspondence that can read the current
    /// concrete and augmentation states.
    /// </summary>
    public AugmentedRelationalRefinementCheck<
        TConcrete,
        TAbstract,
        TAuxiliary> Corresponds(
            Func<TConcrete, TAuxiliary, TAbstract, bool> correspondence)
        => new AugmentedRelationalRefinementCheck<
            TConcrete,
            TAbstract,
            TAuxiliary>(
                concreteRoot,
                abstractRoot,
                initial,
                next,
                correspondence ?? throw new ArgumentNullException(
                    nameof(correspondence)));
}

/// <summary>
/// A configured augmented functional refinement check.
/// </summary>
public sealed class AugmentedFunctionalRefinementCheck<
    TConcrete,
    TAbstract,
    TAuxiliary>
    where TConcrete : IState
    where TAbstract : IState
    where TAuxiliary : State
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, TAuxiliary> initial;
    private readonly Func<
        TAuxiliary,
        RefinementTransition<TConcrete>,
        TAuxiliary> next;
    private readonly Func<TConcrete, TAuxiliary, TAbstract> mapping;

    internal AugmentedFunctionalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next,
        Func<TConcrete, TAuxiliary, TAbstract> mapping)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.initial = initial;
        this.next = next;
        this.mapping = mapping;
    }

    /// <summary>Checks augmented functional safety refinement.</summary>
    public RefinementCheckingResult Check()
        => AugmentedSafetyRefinement.CheckFunctional(
            concreteRoot,
            abstractRoot,
            initial,
            next,
            mapping);

    /// <summary>
    /// Checks augmented functional temporal refinement under concrete and
    /// abstract fairness.
    /// </summary>
    public RefinementCheckingResult CheckTemporal(
        Fairness concreteFairness = null,
        Fairness abstractFairness = null)
        => FunctionalTemporalRefinement.CheckAugmented(
            concreteRoot,
            abstractRoot,
            initial,
            next,
            mapping,
            concreteFairness,
            abstractFairness);
}

/// <summary>
/// A configured augmented relational safety-refinement check.
/// </summary>
public sealed class AugmentedRelationalRefinementCheck<
    TConcrete,
    TAbstract,
    TAuxiliary>
    where TConcrete : IState
    where TAbstract : IState
    where TAuxiliary : State
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, TAuxiliary> initial;
    private readonly Func<
        TAuxiliary,
        RefinementTransition<TConcrete>,
        TAuxiliary> next;
    private readonly Func<TConcrete, TAuxiliary, TAbstract, bool> correspondence;

    internal AugmentedRelationalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next,
        Func<TConcrete, TAuxiliary, TAbstract, bool> correspondence)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.initial = initial;
        this.next = next;
        this.correspondence = correspondence;
    }

    /// <summary>Checks augmented relational safety refinement.</summary>
    public RefinementCheckingResult Check()
        => AugmentedSafetyRefinement.CheckRelational(
            concreteRoot,
            abstractRoot,
            initial,
            next,
            correspondence);
}

/// <summary>
/// A configured relational safety-refinement check.
/// </summary>
public sealed class RelationalRefinementCheck<TConcrete, TAbstract>
    where TConcrete : IState
    where TAbstract : IState
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, TAbstract, bool> correspondence;

    internal RelationalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAbstract, bool> correspondence)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.correspondence = correspondence;
    }

    /// <summary>
    /// Checks strict step-aligned relational safety refinement. Candidate
    /// abstract configurations are retained and pruned path-coherently.
    /// </summary>
    public RefinementCheckingResult Check()
        => RelationalSafetyRefinement.Check(
            concreteRoot,
            abstractRoot,
            correspondence);
}

/// <summary>
/// A configured functional safety-refinement check.
/// </summary>
public sealed class FunctionalRefinementCheck<TConcrete, TAbstract>
    where TConcrete : IState
    where TAbstract : IState
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, TAbstract> mapping;

    internal FunctionalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAbstract> mapping)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.mapping = mapping;
    }

    /// <summary>
    /// Checks strict step-aligned safety refinement. Every concrete edge must
    /// map to one abstract edge or to abstract stutter.
    /// </summary>
    public RefinementCheckingResult Check()
        => FunctionalSafetyRefinement.Check(
            concreteRoot,
            abstractRoot,
            mapping);

    /// <summary>
    /// Checks strict step-aligned temporal refinement over infinite fair
    /// behaviors. The current exact mode requires each concrete transition
    /// to have at most one known abstract response.
    /// </summary>
    public RefinementCheckingResult CheckTemporal(
        Fairness concreteFairness = null,
        Fairness abstractFairness = null)
        => FunctionalTemporalRefinement.Check(
            concreteRoot,
            abstractRoot,
            mapping,
            concreteFairness,
            abstractFairness);
}
