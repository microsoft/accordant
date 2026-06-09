namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Represents fairness constraints for liveness checking.
    /// Fairness filters out "unfair" cycles where enabled actions are never taken.
    /// </summary>
    public class Fairness
    {
        /// <summary>
        /// No fairness - all cycles are considered valid counterexamples.
        /// This is the strictest setting.
        /// </summary>
        public static Fairness None { get; } = new Fairness
        {
            WeakFairPredicate = _ => false,
            StrongFairPredicate = _ => false
        };

        /// <summary>
        /// Weak fairness on all step functions.
        /// "If an action is continuously enabled, it will eventually be taken."
        /// This is the default.
        /// </summary>
        public static Fairness WeakFairAll { get; } = new Fairness
        {
            WeakFairPredicate = _ => true,
            StrongFairPredicate = _ => false
        };

        /// <summary>
        /// Predicate that returns true for step functions that should have weak fairness.
        /// </summary>
        public Func<IStepFunction, bool> WeakFairPredicate { get; set; } = _ => true;

        /// <summary>
        /// Predicate that returns true for step functions that should have strong fairness.
        /// </summary>
        public Func<IStepFunction, bool> StrongFairPredicate { get; set; } = _ => false;

        /// <summary>
        /// Creates weak fairness for step functions matching the predicate.
        /// </summary>
        public static Fairness WeakFair(Func<IStepFunction, bool> predicate)
        {
            return new Fairness
            {
                WeakFairPredicate = predicate,
                StrongFairPredicate = _ => false
            };
        }

        /// <summary>
        /// Creates weak fairness for a specific step function type.
        /// </summary>
        public static Fairness WeakFair<T>() where T : IStepFunction
        {
            return WeakFair(sf => sf is T);
        }

        /// <summary>
        /// Creates strong fairness for step functions matching the predicate.
        /// </summary>
        public static Fairness StrongFair(Func<IStepFunction, bool> predicate)
        {
            return new Fairness
            {
                WeakFairPredicate = _ => false,
                StrongFairPredicate = predicate
            };
        }

        /// <summary>
        /// Creates strong fairness for a specific step function type.
        /// </summary>
        public static Fairness StrongFair<T>() where T : IStepFunction
        {
            return StrongFair(sf => sf is T);
        }

        /// <summary>
        /// Combines two fairness constraints.
        /// </summary>
        public static Fairness operator +(Fairness a, Fairness b)
        {
            return new Fairness
            {
                WeakFairPredicate = sf => a.WeakFairPredicate(sf) || b.WeakFairPredicate(sf),
                StrongFairPredicate = sf => a.StrongFairPredicate(sf) || b.StrongFairPredicate(sf)
            };
        }

        /// <summary>
        /// Checks if a cycle (SCC) is fair according to these constraints.
        /// A cycle is unfair if there's a fair action that is enabled but
        /// never taken inside the SCC.
        /// </summary>
        /// <remarks>
        /// <para>
        /// "Enabled at a node" is derived from the node's outgoing edges:
        /// the state-graph explorer records an outgoing edge only for a
        /// step function whose <c>Apply</c> succeeded on that state, so
        /// the set of step-function ids appearing on outgoing edges is
        /// exactly the set actually enabled there. The static
        /// <see cref="StateGraphNode.StepFunctions"/> list is the model-
        /// wide step menu and is <em>not</em> a per-state "enabled" set —
        /// using it would over-approximate the enabled set and incorrectly
        /// reject genuinely fair cycles (e.g., deadlock self-loops where
        /// only the stutter step is actually enabled).
        /// </para>
        /// <para>
        /// Definitions:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <b>enabledInSCC</b> — union over SCC nodes of "enabled at this
        /// node"; the steps that fire infinitely often along any cyclic
        /// run through the SCC.
        /// </description></item>
        /// <item><description>
        /// <b>continuouslyEnabled</b> — enabled at <em>every</em> SCC node;
        /// the steps that are continuously enabled along any cyclic run.
        /// </description></item>
        /// <item><description>
        /// <b>takenInSCC</b> — labels of edges whose source and target are
        /// both in the SCC.
        /// </description></item>
        /// </list>
        /// </remarks>
        public bool IsFairCycle(StronglyConnectedComponent scc)
        {
            var nodesInSCC = new HashSet<string>(scc.Nodes.Select(n => n.GetNodeFingerprint()));

            // Per-node enabled = step functions on the node's outgoing edges.
            // Taken-in-cycle = step functions on edges whose source AND
            // target are both inside the SCC.
            IEnumerable<IStepFunction> EnabledAt(StateGraphNode n)
                => n.Edges.Select(e => e.StepFunction);

            IEnumerable<IStepFunction> Taken()
            {
                foreach (var n in scc.Nodes)
                    foreach (var e in n.Edges)
                        if (nodesInSCC.Contains(e.Target.GetNodeFingerprint()))
                            yield return e.StepFunction;
            }

            var analysis = CycleFairness.Compute(scc.Nodes, EnabledAt, Taken());
            return CycleFairness.IsFair(analysis, this);
        }
    }
}
