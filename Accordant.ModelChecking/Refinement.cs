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
