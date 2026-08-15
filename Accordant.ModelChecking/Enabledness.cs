namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Runtime.CompilerServices;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Symbolic;

/// <summary>
/// The shared definition of node-level enabledness — "some selected model
/// edge is available here".
///
/// <para><b>Legacy semantics.</b> <c>ENABLED A</c> holds at a state-graph node
/// <c>n</c> iff at least one outgoing model edge <c>n --(a)--&gt; n'</c>
/// <em>changes the state</em> (<c>n'.State ≠ n.State</c> under
/// <see cref="StateSemantics"/>) and its action letter satisfies
/// <c>A</c>. State-neutral edges never count, exactly as in the
/// compatibility fairness enabledness used by
/// <see cref="CycleFairness.Compute"/>: an action that leaves the state
/// unchanged is neither enabled nor taken. Consequently
/// a terminal node — and a node whose only outgoing edges are
/// state-neutral — enables nothing.</para>
///
/// <para><b>Why node-level and not state-level.</b> A state-graph node is
/// a (state, active step-function set) pair, so two nodes carrying equal
/// states can offer different actions. Enabledness is therefore read from
/// the node the letter was consumed at
/// (<see cref="TransitionContext.SourceNode"/>), not from the state alone.</para>
///
/// <para><b>Frontiers.</b> Enabledness is a property of a node's
/// <em>complete</em> outgoing edge set, which an unexpanded or
/// depth-truncated frontier does not have. Propositions built here are
/// therefore transition-aware, which makes every product evaluator prune
/// the frontier and report
/// <see cref="PropertyCheckingStatus.InconclusiveBound"/> rather than
/// reading "no edges" as "nothing enabled".</para>
///
/// <para><b>Semantic-action semantics.</b> Metadata-aware
/// <c>ENABLED ACTION A</c> uses the same exact-node rule but lets the
/// selected predicate count a state-neutral model edge. This is opt-in:
/// unselected administrative edges do not count, and the synthetic
/// terminal stutter is not an outgoing model edge at all.</para>
///
/// <para><b>Plumbing.</b> The only graph-aware addition required is
/// <see cref="TransitionContext.SourceNode"/>, which all three product
/// evaluators —
/// <see cref="Symbolic.NestedDfsCheck"/> (nested-DFS emptiness),
/// <see cref="Symbolic.SccProductCheck"/> (SCC emptiness, the engine used
/// when a <see cref="Fairness"/> constraint is supplied) and
/// <see cref="Symbolic.SymbolicLtlCheck"/>'s product exploration — attach
/// to every letter they evaluate, so the backends stay at parity. The
/// cycle-fairness engine uses the same two projections: changing edges for
/// legacy constraints and all selected model edges for semantic-action
/// constraints.</para>
/// </summary>
internal static class Enabledness
{
    private sealed class Verdict
    {
        internal Verdict(bool value)
        {
            Value = value;
        }

        internal bool Value { get; }
    }

    /// <summary>
    /// Creates the evaluator for <c>ENABLED A</c>. Its result depends only
    /// on <see cref="TransitionContext.SourceNode"/>, so it is memoized per
    /// graph node even though transition-aware product evaluation may ask
    /// for it once per outgoing edge.
    /// </summary>
    internal static Func<TransitionContext, bool> Create(
        Func<TransitionContext, bool> action)
        => Create(action, includeStateNeutral: false);

    /// <summary>
    /// Creates the evaluator for metadata/action-aware enabledness. A
    /// selected state-neutral model edge counts as enabled.
    /// </summary>
    internal static Func<TransitionContext, bool> CreateAction(
        Func<TransitionContext, bool> action)
        => Create(action, includeStateNeutral: true);

    private static Func<TransitionContext, bool> Create(
        Func<TransitionContext, bool> action,
        bool includeStateNeutral)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        var cache = new ConditionalWeakTable<StateGraphNode, Verdict>();
        return context =>
        {
            var node = context.SourceNode ??
                throw new InvalidOperationException(
                    "ENABLED requires a state-graph node. Evaluate it " +
                    "through a model check, not against a state alone.");
            return cache.GetValue(
                node,
                source => new Verdict(
                    IsEnabledAt(source, action, includeStateNeutral))).Value;
        };
    }

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="node"/> has a changing
    /// outgoing edge whose letter satisfies <paramref name="action"/>.
    /// </summary>
    internal static bool IsEnabledAt(
        StateGraphNode node,
        Func<TransitionContext, bool> action)
        => IsEnabledAt(node, action, includeStateNeutral: false);

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="node"/> has an outgoing
    /// model edge whose letter satisfies <paramref name="action"/>,
    /// regardless of whether that selected edge changes domain state.
    /// </summary>
    internal static bool IsActionEnabledAt(
        StateGraphNode node,
        Func<TransitionContext, bool> action)
        => IsEnabledAt(node, action, includeStateNeutral: true);

    private static bool IsEnabledAt(
        StateGraphNode node,
        Func<TransitionContext, bool> action,
        bool includeStateNeutral)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (node == null) throw new ArgumentNullException(nameof(node));

        // Reading Edges drives lazy expansion of exactly this node — the
        // same edges the checker walks — so on-the-fly exploration is
        // neither forced wider nor skipped.
        var edges = node.Edges;
        if (edges == null) return false;

        foreach (var edge in edges)
        {
            if (edge?.Target == null) continue;
            if (!includeStateNeutral &&
                StateSemantics.Equal(node.State, edge.Target.State))
                continue;

            var letter = TransitionContext.Edge(
                node.State, edge.StepFunction, edge.Metadata, edge.Target.State, node);
            if (action(letter)) return true;
        }

        return false;
    }
}
