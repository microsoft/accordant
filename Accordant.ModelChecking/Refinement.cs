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

    /// <summary>
    /// Adds future-validated witness values for pending concrete operations.
    /// Each introduction branches the refinement proof over a finite set of
    /// possible future values; a resolving concrete transition retains only
    /// the copies that predicted the revealed value.
    /// </summary>
    public WitnessRefinementBuilder<TConcrete, TAbstract> WithWitness(
        Func<TConcrete, WitnessChanges> initial,
        Func<
            PendingWitnesses,
            RefinementTransition<TConcrete>,
            WitnessChanges> next)
        => new WitnessRefinementBuilder<TConcrete, TAbstract>(
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
    /// Adds future-validated witness values on top of deterministic
    /// augmentation. Augmentation and witnesses are computed independently
    /// from the same concrete transition; neither update callback reads the
    /// other.
    /// </summary>
    public AugmentedWitnessRefinementBuilder<
        TConcrete,
        TAbstract,
        TAuxiliary> WithWitness(
            Func<TConcrete, WitnessChanges> initial,
            Func<
                PendingWitnesses,
                RefinementTransition<TConcrete>,
                WitnessChanges> next)
        => new AugmentedWitnessRefinementBuilder<
            TConcrete,
            TAbstract,
            TAuxiliary>(
                concreteRoot,
                abstractRoot,
                this.initial,
                this.next,
                initial ?? throw new ArgumentNullException(nameof(initial)),
                next ?? throw new ArgumentNullException(nameof(next)));
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
    private readonly Func<
        RefinementTransition<TConcrete>,
        TAuxiliary,
        AbstractResponse> transitionMapping;

    internal AugmentedFunctionalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next,
        Func<TConcrete, TAuxiliary, TAbstract> mapping,
        Func<
            RefinementTransition<TConcrete>,
            TAuxiliary,
            AbstractResponse> transitionMapping = null)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.initial = initial;
        this.next = next;
        this.mapping = mapping;
        this.transitionMapping = transitionMapping;
    }

    /// <summary>
    /// Declares which abstract response a concrete transition represents. The
    /// declaration reads the concrete transition and the augmentation state
    /// the transition departs from. It narrows the state-consistent abstract
    /// responses; it never admits a response the mapping already excluded.
    /// </summary>
    public AugmentedFunctionalRefinementCheck<TConcrete, TAbstract, TAuxiliary>
        MapTransition(
            Func<
                RefinementTransition<TConcrete>,
                TAuxiliary,
                AbstractResponse> response)
    {
        RefinementTransitionMapping.EnsureNotDeclared(transitionMapping);
        return new AugmentedFunctionalRefinementCheck<
            TConcrete,
            TAbstract,
            TAuxiliary>(
                concreteRoot,
                abstractRoot,
                initial,
                next,
                mapping,
                response ?? throw new ArgumentNullException(nameof(response)));
    }

    /// <summary>
    /// Declares which abstract response a concrete transition represents,
    /// reading the concrete transition alone. Use this overload for
    /// declarations the augmentation state does not influence, such as naming
    /// the internal actions the abstraction hides with
    /// <see cref="AbstractResponse.Hidden"/>.
    /// </summary>
    public AugmentedFunctionalRefinementCheck<TConcrete, TAbstract, TAuxiliary>
        MapTransition(
            Func<RefinementTransition<TConcrete>, AbstractResponse> response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        return MapTransition((transition, _) => response(transition));
    }

    /// <summary>Checks augmented functional safety refinement.</summary>
    public RefinementCheckingResult Check()
    {
        var runtime = BuildRuntime(out var map);
        return FunctionalSafetyRefinement
            .CheckWithProofState<TConcrete, TAbstract>(
                concreteRoot,
                abstractRoot,
                runtime,
                map,
                BuildTransitionMapping(runtime));
    }

    /// <summary>
    /// Checks augmented functional temporal refinement under concrete and
    /// abstract fairness.
    /// </summary>
    public RefinementCheckingResult CheckTemporal(
        Fairness concreteFairness = null,
        Fairness abstractFairness = null)
    {
        var runtime = BuildRuntime(out var map);
        return FunctionalTemporalRefinement
            .CheckWithProofState<TConcrete, TAbstract>(
                concreteRoot,
                abstractRoot,
                runtime,
                map,
                concreteFairness,
                abstractFairness,
                BuildTransitionMapping(runtime));
    }

    private TransitionMapping BuildTransitionMapping(
        RefinementProofRuntime<TConcrete> runtime)
    {
        var local = transitionMapping;
        return local == null
            ? null
            : RefinementTransitionMapping.Create<TConcrete>(
                (transition, proofState) => local(
                    transition,
                    (TAuxiliary)proofState.Auxiliary),
                runtime.Validate);
    }

    private RefinementProofRuntime<TConcrete> BuildRuntime(
        out Func<TConcrete, RefinementProofState, TAbstract> map)
    {
        var augmentation = new AugmentationRuntime<TConcrete, TAuxiliary>(
            concreteRoot,
            initial,
            next);
        var localMapping = mapping;
        map = (concrete, proofState) => localMapping(
            concrete,
            (TAuxiliary)proofState.Auxiliary);
        return new RefinementProofRuntime<TConcrete>(
            augmentation,
            witnesses: null);
    }
}

/// <summary>
/// Defines refinement using future-validated witness values.
/// </summary>
public sealed class WitnessRefinementBuilder<TConcrete, TAbstract>
    where TConcrete : IState
    where TAbstract : IState
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, WitnessChanges> initial;
    private readonly Func<
        PendingWitnesses,
        RefinementTransition<TConcrete>,
        WitnessChanges> next;

    internal WitnessRefinementBuilder(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, WitnessChanges> initial,
        Func<PendingWitnesses, RefinementTransition<TConcrete>, WitnessChanges> next)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.initial = initial;
        this.next = next;
    }

    /// <summary>
    /// Defines a functional abstract-state mapping that can read the current
    /// concrete state and the immutable witness collection.
    /// </summary>
    public WitnessFunctionalRefinementCheck<TConcrete, TAbstract> Map(
        Func<TConcrete, WitnessCollection, TAbstract> mapping)
        => new WitnessFunctionalRefinementCheck<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            initial,
            next,
            mapping ?? throw new ArgumentNullException(nameof(mapping)));
}

/// <summary>
/// A configured witness-extended functional refinement check.
/// </summary>
public sealed class WitnessFunctionalRefinementCheck<TConcrete, TAbstract>
    where TConcrete : IState
    where TAbstract : IState
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, WitnessChanges> initial;
    private readonly Func<
        PendingWitnesses,
        RefinementTransition<TConcrete>,
        WitnessChanges> next;
    private readonly Func<TConcrete, WitnessCollection, TAbstract> mapping;
    private readonly Func<
        RefinementTransition<TConcrete>,
        WitnessCollection,
        AbstractResponse> transitionMapping;

    internal WitnessFunctionalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, WitnessChanges> initial,
        Func<PendingWitnesses, RefinementTransition<TConcrete>, WitnessChanges> next,
        Func<TConcrete, WitnessCollection, TAbstract> mapping,
        Func<
            RefinementTransition<TConcrete>,
            WitnessCollection,
            AbstractResponse> transitionMapping = null)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.initial = initial;
        this.next = next;
        this.mapping = mapping;
        this.transitionMapping = transitionMapping;
    }

    /// <summary>
    /// Declares which abstract response a concrete transition represents. The
    /// declaration reads the concrete transition and the witness collection
    /// the transition departs from, so a prediction the transition is about to
    /// resolve is still visible.
    /// </summary>
    public WitnessFunctionalRefinementCheck<TConcrete, TAbstract> MapTransition(
        Func<
            RefinementTransition<TConcrete>,
            WitnessCollection,
            AbstractResponse> response)
    {
        RefinementTransitionMapping.EnsureNotDeclared(transitionMapping);
        return new WitnessFunctionalRefinementCheck<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            initial,
            next,
            mapping,
            response ?? throw new ArgumentNullException(nameof(response)));
    }

    /// <summary>
    /// Declares which abstract response a concrete transition represents,
    /// reading the concrete transition alone. Use this overload for
    /// declarations no prediction influences, such as naming the internal
    /// actions the abstraction hides with
    /// <see cref="AbstractResponse.Hidden"/>.
    /// </summary>
    public WitnessFunctionalRefinementCheck<TConcrete, TAbstract> MapTransition(
        Func<RefinementTransition<TConcrete>, AbstractResponse> response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        return MapTransition((transition, _) => response(transition));
    }

    /// <summary>
    /// Checks functional safety refinement over every valid witness-extended
    /// behavior.
    /// </summary>
    public RefinementCheckingResult Check()
    {
        var runtime = BuildRuntime(out var map);
        return FunctionalSafetyRefinement
            .CheckWithProofState<TConcrete, TAbstract>(
                concreteRoot,
                abstractRoot,
                runtime,
                map,
                BuildTransitionMapping(runtime));
    }

    /// <summary>
    /// Checks functional temporal refinement over every valid witness-extended
    /// behavior under concrete and abstract fairness.
    /// </summary>
    public RefinementCheckingResult CheckTemporal(
        Fairness concreteFairness = null,
        Fairness abstractFairness = null)
    {
        var runtime = BuildRuntime(out var map);
        return FunctionalTemporalRefinement
            .CheckWithProofState<TConcrete, TAbstract>(
                concreteRoot,
                abstractRoot,
                runtime,
                map,
                concreteFairness,
                abstractFairness,
                BuildTransitionMapping(runtime));
    }

    private TransitionMapping BuildTransitionMapping(
        RefinementProofRuntime<TConcrete> runtime)
    {
        var local = transitionMapping;
        return local == null
            ? null
            : RefinementTransitionMapping.Create<TConcrete>(
                (transition, proofState) => local(
                    transition,
                    proofState.Witnesses),
                runtime.Validate);
    }

    private RefinementProofRuntime<TConcrete> BuildRuntime(
        out Func<TConcrete, RefinementProofState, TAbstract> map)
    {
        var witnesses = new WitnessRuntime<TConcrete>(
            concreteRoot,
            initial,
            next);
        var localMapping = mapping;
        map = (concrete, proofState) => localMapping(
            concrete,
            proofState.Witnesses);
        return new RefinementProofRuntime<TConcrete>(
            augmentation: null,
            witnesses);
    }
}

/// <summary>
/// Defines refinement using deterministic augmentation and future-validated
/// witness values together.
/// </summary>
public sealed class AugmentedWitnessRefinementBuilder<
    TConcrete,
    TAbstract,
    TAuxiliary>
    where TConcrete : IState
    where TAbstract : IState
    where TAuxiliary : State
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, TAuxiliary> augmentInitial;
    private readonly Func<
        TAuxiliary,
        RefinementTransition<TConcrete>,
        TAuxiliary> augmentNext;
    private readonly Func<TConcrete, WitnessChanges> witnessInitial;
    private readonly Func<
        PendingWitnesses,
        RefinementTransition<TConcrete>,
        WitnessChanges> witnessNext;

    internal AugmentedWitnessRefinementBuilder(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> augmentInitial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> augmentNext,
        Func<TConcrete, WitnessChanges> witnessInitial,
        Func<PendingWitnesses, RefinementTransition<TConcrete>, WitnessChanges> witnessNext)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.augmentInitial = augmentInitial;
        this.augmentNext = augmentNext;
        this.witnessInitial = witnessInitial;
        this.witnessNext = witnessNext;
    }

    /// <summary>
    /// Defines a functional abstract-state mapping that can read the current
    /// concrete state, the augmentation state, and the witness collection.
    /// </summary>
    public AugmentedWitnessFunctionalRefinementCheck<
        TConcrete,
        TAbstract,
        TAuxiliary> Map(
            Func<TConcrete, TAuxiliary, WitnessCollection, TAbstract> mapping)
        => new AugmentedWitnessFunctionalRefinementCheck<
            TConcrete,
            TAbstract,
            TAuxiliary>(
                concreteRoot,
                abstractRoot,
                augmentInitial,
                augmentNext,
                witnessInitial,
                witnessNext,
                mapping ?? throw new ArgumentNullException(nameof(mapping)));
}

/// <summary>
/// A configured augmented, witness-extended functional refinement check.
/// </summary>
public sealed class AugmentedWitnessFunctionalRefinementCheck<
    TConcrete,
    TAbstract,
    TAuxiliary>
    where TConcrete : IState
    where TAbstract : IState
    where TAuxiliary : State
{
    private readonly StateGraphNode concreteRoot;
    private readonly StateGraphNode abstractRoot;
    private readonly Func<TConcrete, TAuxiliary> augmentInitial;
    private readonly Func<
        TAuxiliary,
        RefinementTransition<TConcrete>,
        TAuxiliary> augmentNext;
    private readonly Func<TConcrete, WitnessChanges> witnessInitial;
    private readonly Func<
        PendingWitnesses,
        RefinementTransition<TConcrete>,
        WitnessChanges> witnessNext;
    private readonly Func<
        TConcrete,
        TAuxiliary,
        WitnessCollection,
        TAbstract> mapping;
    private readonly Func<
        RefinementTransition<TConcrete>,
        TAuxiliary,
        WitnessCollection,
        AbstractResponse> transitionMapping;

    internal AugmentedWitnessFunctionalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> augmentInitial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> augmentNext,
        Func<TConcrete, WitnessChanges> witnessInitial,
        Func<PendingWitnesses, RefinementTransition<TConcrete>, WitnessChanges> witnessNext,
        Func<TConcrete, TAuxiliary, WitnessCollection, TAbstract> mapping,
        Func<
            RefinementTransition<TConcrete>,
            TAuxiliary,
            WitnessCollection,
            AbstractResponse> transitionMapping = null)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.augmentInitial = augmentInitial;
        this.augmentNext = augmentNext;
        this.witnessInitial = witnessInitial;
        this.witnessNext = witnessNext;
        this.mapping = mapping;
        this.transitionMapping = transitionMapping;
    }

    /// <summary>
    /// Declares which abstract response a concrete transition represents. The
    /// declaration reads the concrete transition together with the
    /// augmentation state and the witness collection the transition departs
    /// from.
    /// </summary>
    public AugmentedWitnessFunctionalRefinementCheck<
        TConcrete,
        TAbstract,
        TAuxiliary> MapTransition(
            Func<
                RefinementTransition<TConcrete>,
                TAuxiliary,
                WitnessCollection,
                AbstractResponse> response)
    {
        RefinementTransitionMapping.EnsureNotDeclared(transitionMapping);
        return new AugmentedWitnessFunctionalRefinementCheck<
            TConcrete,
            TAbstract,
            TAuxiliary>(
                concreteRoot,
                abstractRoot,
                augmentInitial,
                augmentNext,
                witnessInitial,
                witnessNext,
                mapping,
                response ?? throw new ArgumentNullException(nameof(response)));
    }

    /// <summary>
    /// Declares which abstract response a concrete transition represents,
    /// reading the concrete transition alone. Use this overload for
    /// declarations neither the augmentation state nor a prediction
    /// influences, such as naming the internal actions the abstraction hides
    /// with <see cref="AbstractResponse.Hidden"/>.
    /// </summary>
    public AugmentedWitnessFunctionalRefinementCheck<
        TConcrete,
        TAbstract,
        TAuxiliary> MapTransition(
            Func<RefinementTransition<TConcrete>, AbstractResponse> response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        return MapTransition((transition, _, _) => response(transition));
    }

    /// <summary>Checks augmented, witness-extended safety refinement.</summary>
    public RefinementCheckingResult Check()
    {
        var runtime = BuildRuntime(out var map);
        return FunctionalSafetyRefinement
            .CheckWithProofState<TConcrete, TAbstract>(
                concreteRoot,
                abstractRoot,
                runtime,
                map,
                BuildTransitionMapping(runtime));
    }

    /// <summary>
    /// Checks augmented, witness-extended temporal refinement under concrete
    /// and abstract fairness.
    /// </summary>
    public RefinementCheckingResult CheckTemporal(
        Fairness concreteFairness = null,
        Fairness abstractFairness = null)
    {
        var runtime = BuildRuntime(out var map);
        return FunctionalTemporalRefinement
            .CheckWithProofState<TConcrete, TAbstract>(
                concreteRoot,
                abstractRoot,
                runtime,
                map,
                concreteFairness,
                abstractFairness,
                BuildTransitionMapping(runtime));
    }

    private TransitionMapping BuildTransitionMapping(
        RefinementProofRuntime<TConcrete> runtime)
    {
        var local = transitionMapping;
        return local == null
            ? null
            : RefinementTransitionMapping.Create<TConcrete>(
                (transition, proofState) => local(
                    transition,
                    (TAuxiliary)proofState.Auxiliary,
                    proofState.Witnesses),
                runtime.Validate);
    }

    private RefinementProofRuntime<TConcrete> BuildRuntime(
        out Func<TConcrete, RefinementProofState, TAbstract> map)
    {
        var augmentation = new AugmentationRuntime<TConcrete, TAuxiliary>(
            concreteRoot,
            augmentInitial,
            augmentNext);
        var witnesses = new WitnessRuntime<TConcrete>(
            concreteRoot,
            witnessInitial,
            witnessNext);
        var localMapping = mapping;
        map = (concrete, proofState) => localMapping(
            concrete,
            (TAuxiliary)proofState.Auxiliary,
            proofState.Witnesses);
        return new RefinementProofRuntime<TConcrete>(augmentation, witnesses);
    }
}

/// <summary>
/// Adapts the typed <c>MapTransition</c> declarations onto the internal
/// selector the refinement cores use.
/// </summary>
internal static class RefinementTransitionMapping
{
    internal static void EnsureNotDeclared(object existing)
    {
        if (existing != null)
        {
            throw new InvalidOperationException(
                "A refinement check declares its transition mapping once. " +
                "Combine the cases in one MapTransition callback.");
        }
    }

    internal static TransitionMapping Create<TConcrete>(
        Func<
            RefinementTransition<TConcrete>,
            RefinementProofState,
            AbstractResponse> response,
        Action<RefinementProofState> validate = null)
        where TConcrete : IState
        => new TransitionMapping(
            (concreteSource, proofState, concreteEdge) =>
                response(
                    new RefinementTransition<TConcrete>(
                        GetConcrete<TConcrete>(concreteSource),
                        concreteEdge.StepFunction,
                        concreteEdge.Metadata,
                        GetConcrete<TConcrete>(concreteEdge.Target)),
                    proofState),
            validate);

    private static TConcrete GetConcrete<TConcrete>(StateGraphNode node)
        where TConcrete : IState
    {
        if (node.State is TConcrete concrete)
        {
            return concrete;
        }

        throw new InvalidOperationException(
            $"Concrete graph node state '{node.State?.GetType().FullName}' " +
            $"is not a {typeof(TConcrete).FullName}.");
    }
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
    private readonly Func<
        RefinementTransition<TConcrete>,
        AbstractResponse> transitionMapping;

    internal FunctionalRefinementCheck(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAbstract> mapping,
        Func<
            RefinementTransition<TConcrete>,
            AbstractResponse> transitionMapping = null)
    {
        this.concreteRoot = concreteRoot;
        this.abstractRoot = abstractRoot;
        this.mapping = mapping;
        this.transitionMapping = transitionMapping;
    }

    /// <summary>
    /// Declares which abstract response a concrete transition represents,
    /// resolving cases where one mapped abstract state is reachable by
    /// abstract stutter and by an abstract edge, or by several abstract edges.
    /// The declaration narrows the state-consistent abstract responses; it
    /// never admits a response the state mapping already excluded.
    /// </summary>
    public FunctionalRefinementCheck<TConcrete, TAbstract> MapTransition(
        Func<RefinementTransition<TConcrete>, AbstractResponse> response)
    {
        RefinementTransitionMapping.EnsureNotDeclared(transitionMapping);
        return new FunctionalRefinementCheck<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            mapping,
            response ?? throw new ArgumentNullException(nameof(response)));
    }

    /// <summary>
    /// Checks strict step-aligned safety refinement. Every concrete edge must
    /// map to one abstract edge or to abstract stutter.
    /// </summary>
    public RefinementCheckingResult Check()
        => FunctionalSafetyRefinement.Check(
            concreteRoot,
            abstractRoot,
            mapping,
            BuildTransitionMapping());

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
            abstractFairness,
            BuildTransitionMapping());

    private TransitionMapping BuildTransitionMapping()
    {
        var local = transitionMapping;
        return local == null
            ? null
            : RefinementTransitionMapping.Create<TConcrete>(
                (transition, _) => local(transition));
    }
}
