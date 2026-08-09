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

        /// <summary>Compatibility name for <see cref="WeakAll"/>.</summary>
        public static Fairness WeakFairAll => WeakAll;

        /// <summary>
        /// Selects step functions subject to weak fairness.
        /// </summary>
        public Func<IStepFunction, bool> WeakFairPredicate { get; private set; } = _ => false;

        /// <summary>
        /// Selects step functions subject to strong fairness.
        /// </summary>
        public Func<IStepFunction, bool> StrongFairPredicate { get; private set; } = _ => false;

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
        {
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return ForEdge(
                false,
                edge => relation((TState)edge.Source.State, (TState)edge.Target.State));
        }

        /// <summary>Creates strong fairness for a state-pair relation.</summary>
        public static Fairness Strong<TState>(Func<TState, TState, bool> relation)
            where TState : State
        {
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return ForEdge(
                true,
                edge => relation((TState)edge.Source.State, (TState)edge.Target.State));
        }

        /// <summary>Creates weak fairness for a full edge predicate.</summary>
        public static Fairness Weak<TState>(
            Func<TState, IStepFunction, TState, bool> predicate)
            where TState : State
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return ForEdge(
                false,
                edge => predicate(
                    (TState)edge.Source.State,
                    edge.StepFunction,
                    (TState)edge.Target.State));
        }

        /// <summary>Creates strong fairness for a full edge predicate.</summary>
        public static Fairness Strong<TState>(
            Func<TState, IStepFunction, TState, bool> predicate)
            where TState : State
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return ForEdge(
                true,
                edge => predicate(
                    (TState)edge.Source.State,
                    edge.StepFunction,
                    (TState)edge.Target.State));
        }

        /// <summary>Creates weak fairness for an observed transition relation.</summary>
        public static Fairness Weak(TransitionObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            return ForEdge(false, edge => observation.PredicateCore.Eval(edge.Context));
        }

        /// <summary>Creates strong fairness for an observed transition relation.</summary>
        public static Fairness Strong(TransitionObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            return ForEdge(true, edge => observation.PredicateCore.Eval(edge.Context));
        }

        /// <summary>Compatibility name for weak step-function fairness.</summary>
        public static Fairness WeakFair(Func<IStepFunction, bool> predicate)
            => Weak(predicate);

        /// <summary>Compatibility name for weak fairness by step type.</summary>
        public static Fairness WeakFair<TStep>() where TStep : IStepFunction
            => Weak<TStep>();

        /// <summary>Compatibility name for strong step-function fairness.</summary>
        public static Fairness StrongFair(Func<IStepFunction, bool> predicate)
            => Strong(predicate);

        /// <summary>Compatibility name for strong fairness by step type.</summary>
        public static Fairness StrongFair<TStep>() where TStep : IStepFunction
            => Strong<TStep>();

        /// <summary>Combines two sets of fairness constraints.</summary>
        public static Fairness operator +(Fairness left, Fairness right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            return new Fairness
            {
                WeakFairPredicate =
                    step => left.WeakFairPredicate(step) || right.WeakFairPredicate(step),
                StrongFairPredicate =
                    step => left.StrongFairPredicate(step) || right.StrongFairPredicate(step),
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

        private static Fairness ForStep(
            bool isStrong,
            Func<IStepFunction, bool> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            return isStrong
                ? new Fairness { StrongFairPredicate = selector }
                : new Fairness { WeakFairPredicate = selector };
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
