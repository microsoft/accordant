namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant.ModelChecking.Symbolic;

/// <summary>
/// Fairness constraints over model transitions.
///
/// <para>The original <see cref="Weak(Func{IStepFunction, bool})"/>,
/// <see cref="Strong(Func{IStepFunction, bool})"/>, relation, edge, and
/// <see cref="TransitionObservation"/> overloads retain their historical
/// changing-domain-state semantics. The explicit <c>Action</c> and
/// <c>Each</c> APIs instead select semantic model edges, including a
/// selected state-neutral edge. Domain effect, formula visibility, and
/// fairness identity are therefore independent choices. A selected
/// state-neutral or no-op edge is a real occurrence and discharges its
/// obligation; selectors intended to force domain progress must exclude
/// no-op occurrences explicitly.</para>
/// </summary>
public sealed class Fairness
{
    /// <summary>No fairness constraints.</summary>
    public static Fairness None { get; } = new Fairness();

    /// <summary>Weak fairness for every step function.</summary>
    public static Fairness WeakAll { get; } = Weak(_ => true);

    internal Func<IStepFunction, bool> WeakStepPredicate { get; private set; }
        = _ => false;

    internal Func<IStepFunction, bool> StrongStepPredicate { get; private set; }
        = _ => false;

    internal IReadOnlyList<EdgeConstraint> EdgeConstraints { get; private set; }
        = Array.Empty<EdgeConstraint>();

    /// <summary>Creates weak fairness for selected step functions.</summary>
    public static Fairness Weak(Func<IStepFunction, bool> selector)
        => ForStep(false, selector);

    /// <summary>Creates strong fairness for selected step functions.</summary>
    public static Fairness Strong(Func<IStepFunction, bool> selector)
        => ForStep(true, selector);

    /// <summary>Creates weak fairness for a step-function type.</summary>
    public static Fairness Weak<TStep>() where TStep : IStepFunction
        => Weak(step => step is TStep);

    /// <summary>Creates strong fairness for a step-function type.</summary>
    public static Fairness Strong<TStep>() where TStep : IStepFunction
        => Strong(step => step is TStep);

    /// <summary>Creates weak fairness for a state-pair relation.</summary>
    public static Fairness Weak<TState>(Func<TState, TState, bool> relation)
        where TState : State
        => ForAction(false, ActionPredicate.ForRelation<TState>(relation));

    /// <summary>Creates strong fairness for a state-pair relation.</summary>
    public static Fairness Strong<TState>(Func<TState, TState, bool> relation)
        where TState : State
        => ForAction(true, ActionPredicate.ForRelation<TState>(relation));

    /// <summary>Creates weak fairness for a full edge predicate.</summary>
    public static Fairness Weak<TState>(
        Func<TState, IStepFunction, TState, bool> predicate)
        where TState : State
        => ForAction(false, ActionPredicate.ForEdge<TState>(predicate));

    /// <summary>Creates strong fairness for a full edge predicate.</summary>
    public static Fairness Strong<TState>(
        Func<TState, IStepFunction, TState, bool> predicate)
        where TState : State
        => ForAction(true, ActionPredicate.ForEdge<TState>(predicate));

    /// <summary>Creates weak fairness for an observed transition relation.</summary>
    public static Fairness Weak(TransitionObservation observation)
        => ForAction(false, ActionPredicate.ForObservation(observation));

    /// <summary>Creates strong fairness for an observed transition relation.</summary>
    public static Fairness Strong(TransitionObservation observation)
        => ForAction(true, ActionPredicate.ForObservation(observation));

    /// <summary>
    /// Creates one collective weak-fairness obligation for the model edges
    /// whose action/metadata view satisfies <paramref name="selector"/>.
    ///
    /// <para>The obligation is enabled at an exact graph configuration
    /// when any selected outgoing model edge exists there. If it remains
    /// enabled from some point onward, a selected edge must eventually
    /// occur (and recur if enablement persists). Equivalently, every
    /// recurring SCC in which the family is enabled at every exact
    /// configuration must contain a selected taken edge. Selected
    /// state-neutral edges count; the synthetic terminal stutter does
    /// not.</para>
    /// </summary>
    public static Fairness WeakAction(Func<Transition, bool> selector)
        => ForSemanticAction(false, ActionPredicate.ForAction(selector));

    /// <summary>
    /// Creates one collective strong-fairness obligation for the model
    /// edges whose action/metadata view satisfies
    /// <paramref name="selector"/>. If selected edges are enabled at any
    /// configuration infinitely often, selected edges must occur
    /// infinitely often. Equivalently, a recurring SCC that enables the
    /// family anywhere must contain a selected taken edge.
    /// </summary>
    public static Fairness StrongAction(Func<Transition, bool> selector)
        => ForSemanticAction(true, ActionPredicate.ForAction(selector));

    /// <summary>
    /// Creates one collective weak-fairness obligation for selected
    /// semantic edges, with access to source and target domain state.
    /// Selected state-neutral model edges count.
    /// </summary>
    public static Fairness WeakAction<TState>(
        Func<TState, Transition, TState, bool> selector)
        where TState : State
        => ForSemanticAction(false, ActionPredicate.ForAction(selector));

    /// <summary>
    /// Creates one collective strong-fairness obligation for selected
    /// semantic edges, with access to source and target domain state.
    /// Selected state-neutral model edges count.
    /// </summary>
    public static Fairness StrongAction<TState>(
        Func<TState, Transition, TState, bool> selector)
        where TState : State
        => ForSemanticAction(true, ActionPredicate.ForAction(selector));

    /// <summary>
    /// Creates one collective weak-fairness obligation for edges carrying
    /// typed metadata that satisfies <paramref name="selector"/>.
    /// </summary>
    public static Fairness WeakAction<TMetadata>(
        Func<TMetadata, bool> selector)
        => ForSemanticAction(false, ActionPredicate.ForMetadata(selector));

    /// <summary>
    /// Creates one collective strong-fairness obligation for edges
    /// carrying typed metadata that satisfies <paramref name="selector"/>.
    /// </summary>
    public static Fairness StrongAction<TMetadata>(
        Func<TMetadata, bool> selector)
        => ForSemanticAction(true, ActionPredicate.ForMetadata(selector));

    /// <summary>
    /// Creates one collective weak-fairness obligation for selected
    /// typed-metadata edges, with access to source and target domain state.
    /// </summary>
    public static Fairness WeakAction<TState, TMetadata>(
        Func<TState, TMetadata, TState, bool> selector)
        where TState : State
        => ForSemanticAction(false, ActionPredicate.ForMetadata(selector));

    /// <summary>
    /// Creates one collective strong-fairness obligation for selected
    /// typed-metadata edges, with access to source and target domain state.
    /// </summary>
    public static Fairness StrongAction<TState, TMetadata>(
        Func<TState, TMetadata, TState, bool> selector)
        where TState : State
        => ForSemanticAction(true, ActionPredicate.ForMetadata(selector));

    /// <summary>
    /// Creates one weak-fairness obligation per stable semantic key.
    /// Matching edges with equal keys (under ordinary
    /// <see cref="object.Equals(object, object)"/>) belong to the same
    /// obligation. A key is continuously enabled only when a matching edge
    /// for that key exists at every exact configuration in the cycle; an
    /// occurrence under another key does not discharge it.
    /// </summary>
    public static Fairness WeakEach<TKey>(
        Func<Transition, bool> selector,
        Func<Transition, TKey> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            false,
            ActionPredicate.ForAction(selector),
            edge => keySelector(ToTransition(edge)));
    }

    /// <summary>
    /// Creates one strong-fairness obligation per stable semantic key.
    /// A key enabled at any exact configuration in a cycle must be taken by
    /// a matching edge with the same key in that cycle; an occurrence under
    /// another key does not discharge it.
    /// </summary>
    public static Fairness StrongEach<TKey>(
        Func<Transition, bool> selector,
        Func<Transition, TKey> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            true,
            ActionPredicate.ForAction(selector),
            edge => keySelector(ToTransition(edge)));
    }

    /// <summary>
    /// Creates one weak-fairness obligation per key selected from typed
    /// edge metadata.
    /// </summary>
    public static Fairness WeakEach<TMetadata, TKey>(
        Func<TMetadata, bool> selector,
        Func<TMetadata, TKey> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            false,
            ActionPredicate.ForMetadata(selector),
            edge => keySelector((TMetadata)edge.Metadata));
    }

    /// <summary>
    /// Creates one strong-fairness obligation per key selected from typed
    /// edge metadata.
    /// </summary>
    public static Fairness StrongEach<TMetadata, TKey>(
        Func<TMetadata, bool> selector,
        Func<TMetadata, TKey> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            true,
            ActionPredicate.ForMetadata(selector),
            edge => keySelector((TMetadata)edge.Metadata));
    }

    /// <summary>
    /// Creates one weak-fairness obligation per key, with access to source
    /// and target domain state and the generic action/metadata view.
    /// </summary>
    public static Fairness WeakEach<TState, TKey>(
        Func<TState, Transition, TState, bool> selector,
        Func<TState, Transition, TState, TKey> keySelector)
        where TState : State
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            false,
            ActionPredicate.ForAction(selector),
            edge => keySelector(
                (TState)edge.Source.State,
                ToTransition(edge),
                (TState)edge.Target.State));
    }

    /// <summary>
    /// Creates one strong-fairness obligation per key, with access to source
    /// and target domain state and the generic action/metadata view.
    /// </summary>
    public static Fairness StrongEach<TState, TKey>(
        Func<TState, Transition, TState, bool> selector,
        Func<TState, Transition, TState, TKey> keySelector)
        where TState : State
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            true,
            ActionPredicate.ForAction(selector),
            edge => keySelector(
                (TState)edge.Source.State,
                ToTransition(edge),
                (TState)edge.Target.State));
    }

    /// <summary>
    /// Creates one weak-fairness obligation per key selected from source
    /// state, typed edge metadata, and target state.
    /// </summary>
    public static Fairness WeakEach<TState, TMetadata, TKey>(
        Func<TState, TMetadata, TState, bool> selector,
        Func<TState, TMetadata, TState, TKey> keySelector)
        where TState : State
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            false,
            ActionPredicate.ForMetadata(selector),
            edge => keySelector(
                (TState)edge.Source.State,
                (TMetadata)edge.Metadata,
                (TState)edge.Target.State));
    }

    /// <summary>
    /// Creates one strong-fairness obligation per key selected from source
    /// state, typed edge metadata, and target state.
    /// </summary>
    public static Fairness StrongEach<TState, TMetadata, TKey>(
        Func<TState, TMetadata, TState, bool> selector,
        Func<TState, TMetadata, TState, TKey> keySelector)
        where TState : State
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEach(
            true,
            ActionPredicate.ForMetadata(selector),
            edge => keySelector(
                (TState)edge.Source.State,
                (TMetadata)edge.Metadata,
                (TState)edge.Target.State));
    }

    /// <summary>Combines two sets of fairness constraints.</summary>
    public static Fairness operator +(Fairness left, Fairness right)
    {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));

        return new Fairness
        {
            WeakStepPredicate =
                step => left.WeakStepPredicate(step) || right.WeakStepPredicate(step),
            StrongStepPredicate =
                step => left.StrongStepPredicate(step) || right.StrongStepPredicate(step),
            EdgeConstraints = left.EdgeConstraints.Concat(right.EdgeConstraints).ToArray()
        };
    }

    /// <summary>Checks whether a system SCC satisfies these constraints.</summary>
    public bool IsFairCycle(StronglyConnectedComponent scc)
    {
        if (scc == null) throw new ArgumentNullException(nameof(scc));
        var nodes = new HashSet<StateGraphNode>(scc.Nodes);

        IEnumerable<FairnessEdge> EnabledAt(StateGraphNode node)
            => node.Edges.Select(edge =>
                new FairnessEdge(node, edge.StepFunction, edge.Metadata, edge.Target));

        IEnumerable<FairnessEdge> Taken()
        {
            foreach (var node in scc.Nodes)
                foreach (var edge in node.Edges)
                    if (nodes.Contains(edge.Target))
                        yield return new FairnessEdge(
                            node, edge.StepFunction, edge.Metadata, edge.Target);
        }

        return CycleFairness.IsFair(
            CycleFairness.Compute(scc.Nodes, EnabledAt, Taken()),
            this);
    }

    private static Fairness ForEdge(
        bool isStrong,
        Func<FairnessEdge, bool> matches,
        bool includesStateNeutral = false,
        Func<FairnessEdge, object> keySelector = null)
        => new Fairness
        {
            EdgeConstraints = new[]
            {
                new EdgeConstraint(
                    isStrong,
                    matches,
                    includesStateNeutral,
                    keySelector)
            }
        };

    /// <summary>
    /// Builds an edge-level constraint from the shared action-matching
    /// abstraction also used by the <c>ENABLED</c> proposition, so the two
    /// agree on which changing edges an action predicate selects.
    /// </summary>
    private static Fairness ForAction(
        bool isStrong, Func<TransitionContext, bool> action)
        => ForEdge(isStrong, ActionPredicate.ToEdgePredicate(action));

    private static Fairness ForSemanticAction(
        bool isStrong, Func<TransitionContext, bool> action)
        => ForEdge(
            isStrong,
            ActionPredicate.ToEdgePredicate(action),
            includesStateNeutral: true);

    private static Fairness ForEach(
        bool isStrong,
        Func<TransitionContext, bool> action,
        Func<FairnessEdge, object> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return ForEdge(
            isStrong,
            ActionPredicate.ToEdgePredicate(action),
            includesStateNeutral: true,
            keySelector: keySelector);
    }

    private static Fairness ForStep(
        bool isStrong,
        Func<IStepFunction, bool> selector)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        return isStrong
            ? new Fairness { StrongStepPredicate = selector }
            : new Fairness { WeakStepPredicate = selector };
    }

    private static Transition ToTransition(FairnessEdge edge)
        => new Transition(edge.StepFunction, edge.Metadata);

    internal sealed class EdgeConstraint
    {
        public bool IsStrong { get; }
        public Func<FairnessEdge, bool> Matches { get; }
        public bool IncludesStateNeutral { get; }
        public Func<FairnessEdge, object> KeySelector { get; }

        public EdgeConstraint(
            bool isStrong,
            Func<FairnessEdge, bool> matches,
            bool includesStateNeutral,
            Func<FairnessEdge, object> keySelector)
        {
            IsStrong = isStrong;
            Matches = matches ?? throw new ArgumentNullException(nameof(matches));
            IncludesStateNeutral = includesStateNeutral;
            KeySelector = keySelector;
        }
    }
}
