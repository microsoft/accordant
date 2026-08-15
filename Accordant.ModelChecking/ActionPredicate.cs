namespace Microsoft.Accordant.ModelChecking;

using System;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Symbolic;

/// <summary>
/// The shared "does this action letter match?" abstraction. Both fairness
/// constraints (<see cref="Fairness"/>) and the node-level
/// <c>ENABLED</c> proposition select actions the same way, so the
/// conversions from the public predicate shapes — a step-function
/// selector, a step-function type, a state-pair relation, a full edge
/// predicate, edge metadata, and a <see cref="TransitionObservation"/> —
/// live here once and are used by both.
///
/// <para>A matcher is a predicate over a <see cref="TransitionContext"/>
/// letter, which is the common currency of proposition evaluation.
/// <see cref="ToEdgePredicate"/> lifts it to the
/// <see cref="FairnessEdge"/> view used by the cycle-fairness
/// engine.</para>
/// </summary>
internal static class ActionPredicate
{
    /// <summary>Matches any letter whose action satisfies <paramref name="selector"/>.</summary>
    internal static Func<TransitionContext, bool> ForStep(
        Func<IStepFunction, bool> selector)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        return context => context.Action != null && selector(context.Action);
    }

    /// <summary>Matches any letter produced by a <typeparamref name="TStep"/> action.</summary>
    internal static Func<TransitionContext, bool> ForStepType<TStep>()
        where TStep : IStepFunction
        => context => context.Action is TStep;

    /// <summary>Matches any letter whose source/target pair satisfies <paramref name="relation"/>.</summary>
    internal static Func<TransitionContext, bool> ForRelation<TState>(
        Func<TState, TState, bool> relation)
        where TState : State
    {
        if (relation == null) throw new ArgumentNullException(nameof(relation));
        return context => relation((TState)context.From, (TState)context.To);
    }

    /// <summary>Matches any letter satisfying the full edge <paramref name="predicate"/>.</summary>
    internal static Func<TransitionContext, bool> ForEdge<TState>(
        Func<TState, IStepFunction, TState, bool> predicate)
        where TState : State
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return context => predicate(
            (TState)context.From, context.Action, (TState)context.To);
    }

    /// <summary>
    /// Matches any letter whose public transition view satisfies
    /// <paramref name="predicate"/>.
    /// </summary>
    internal static Func<TransitionContext, bool> ForAction(
        Func<Transition, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return context => predicate(ToTransition(context));
    }

    /// <summary>
    /// Matches any letter whose source, public transition view, and target
    /// satisfy <paramref name="predicate"/>.
    /// </summary>
    internal static Func<TransitionContext, bool> ForAction<TState>(
        Func<TState, Transition, TState, bool> predicate)
        where TState : State
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return context => predicate(
            (TState)context.From,
            ToTransition(context),
            (TState)context.To);
    }

    /// <summary>
    /// Matches any letter whose metadata is a <typeparamref name="TMetadata"/>
    /// satisfying <paramref name="predicate"/>.
    /// </summary>
    internal static Func<TransitionContext, bool> ForMetadata<TMetadata>(
        Func<TMetadata, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return context =>
            context.Metadata is TMetadata metadata &&
            predicate(metadata);
    }

    /// <summary>
    /// Matches any letter whose source, typed metadata, and target satisfy
    /// <paramref name="predicate"/>.
    /// </summary>
    internal static Func<TransitionContext, bool> ForMetadata<TState, TMetadata>(
        Func<TState, TMetadata, TState, bool> predicate)
        where TState : State
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return context =>
            context.Metadata is TMetadata metadata &&
            predicate(
                (TState)context.From,
                metadata,
                (TState)context.To);
    }

    /// <summary>Matches any letter satisfying <paramref name="observation"/>.</summary>
    internal static Func<TransitionContext, bool> ForObservation(
        TransitionObservation observation)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return context => observation.PredicateCore.Eval(in context);
    }

    /// <summary>
    /// Views an action matcher as a predicate over a
    /// <see cref="FairnessEdge"/>, which is how the cycle-fairness engine
    /// consumes edge constraints.
    /// </summary>
    internal static Func<FairnessEdge, bool> ToEdgePredicate(
        Func<TransitionContext, bool> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return edge => action(edge.Context);
    }

    private static Transition ToTransition(TransitionContext context)
        => new Transition(context.Action, context.Metadata);
}
