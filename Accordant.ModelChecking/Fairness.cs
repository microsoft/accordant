namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// Fairness constraints over changing model transitions.
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
            var nodes = new HashSet<string>(
                scc.Nodes.Select(node => node.GetNodeFingerprint()));

            IEnumerable<FairnessEdge> EnabledAt(StateGraphNode node)
                => node.Edges.Select(edge =>
                    new FairnessEdge(node, edge.StepFunction, edge.Metadata, edge.Target));

            IEnumerable<FairnessEdge> Taken()
            {
                foreach (var node in scc.Nodes)
                    foreach (var edge in node.Edges)
                        if (nodes.Contains(edge.Target.GetNodeFingerprint()))
                            yield return new FairnessEdge(
                                node, edge.StepFunction, edge.Metadata, edge.Target);
            }

            return CycleFairness.IsFair(
                CycleFairness.Compute(scc.Nodes, EnabledAt, Taken()),
                this);
        }

        private static Fairness ForEdge(bool isStrong, Func<FairnessEdge, bool> matches)
            => new Fairness
            {
                EdgeConstraints = new[] { new EdgeConstraint(isStrong, matches) }
            };

        /// <summary>
        /// Builds an edge-level constraint from the shared action-matching
        /// abstraction also used by the <c>ENABLED</c> proposition, so the two
        /// agree on which changing edges an action predicate selects.
        /// </summary>
        private static Fairness ForAction(
            bool isStrong, Func<TransitionContext, bool> action)
            => ForEdge(isStrong, ActionPredicate.ToEdgePredicate(action));

        private static Fairness ForStep(
            bool isStrong,
            Func<IStepFunction, bool> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            return isStrong
                ? new Fairness { StrongStepPredicate = selector }
                : new Fairness { WeakStepPredicate = selector };
        }

        internal sealed class EdgeConstraint
        {
            public bool IsStrong { get; }
            public Func<FairnessEdge, bool> Matches { get; }

            public EdgeConstraint(bool isStrong, Func<FairnessEdge, bool> matches)
            {
                IsStrong = isStrong;
                Matches = matches;
            }
        }
    }
}
