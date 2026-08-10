namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Runtime.CompilerServices;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// The single definition of <c>ENABLED A</c> — the node-level proposition
    /// "some action satisfying <c>A</c> is available here".
    ///
    /// <para><b>Semantics.</b> <c>ENABLED A</c> holds at a state-graph node
    /// <c>n</c> iff at least one outgoing model edge <c>n --(a)--&gt; n'</c>
    /// <em>changes the state</em> (<c>n'.State ≠ n.State</c> under
    /// <see cref="StateSemantics"/>) and its action letter satisfies
    /// <c>A</c>. State-neutral edges never count, exactly as in the fairness
    /// enabledness used by <see cref="CycleFairness.Compute"/>: an action that
    /// leaves the state unchanged is neither enabled nor taken. Consequently
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
    /// <para><b>Plumbing.</b> The only graph-aware addition required is
    /// <see cref="TransitionContext.SourceNode"/>, which all three product
    /// evaluators —
    /// <see cref="Symbolic.NestedDfsCheck"/> (nested-DFS emptiness),
    /// <see cref="Symbolic.SccProductCheck"/> (SCC emptiness, the engine used
    /// when a <see cref="Fairness"/> constraint is supplied) and
    /// <see cref="Symbolic.SymbolicLtlCheck"/>'s product exploration — attach
    /// to every letter they evaluate, so the backends stay at parity. The
    /// cycle-fairness engine itself is untouched: it already derives
    /// enabledness from the same changing outgoing edges.</para>
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
                    source => new Verdict(IsEnabledAt(source, action))).Value;
            };
        }

        /// <summary>
        /// Returns <c>true</c> iff <paramref name="node"/> has a changing
        /// outgoing edge whose letter satisfies <paramref name="action"/>.
        /// </summary>
        internal static bool IsEnabledAt(
            StateGraphNode node,
            Func<TransitionContext, bool> action)
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
                if (StateSemantics.Equal(node.State, edge.Target.State)) continue;

                var letter = TransitionContext.Edge(
                    node.State, edge.StepFunction, edge.Metadata, edge.Target.State, node);
                if (action(letter)) return true;
            }

            return false;
        }
    }
}
