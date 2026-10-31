namespace Microsoft.Accordant.ModelChecking
{
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// A trace consists of a sequence of trace items. Each trace item
    /// indicates the action that led to the state in the trace item.
    /// </summary>
    public class TraceItem
    {
        /// <summary>
        /// The step function that when applied to the state in the previous
        /// trace item led to the state graph node in this trace item.
        /// </summary>
        public IStepFunction StepFunction { get; }

        /// <summary>
        /// The state graph node at this point in the trace.
        /// </summary>
        public StateGraphNode StateGraphNode { get; }

        /// <summary>
        /// Indicates whether this trace item is part of a cycle (for liveness counterexamples).
        /// </summary>
        public bool IsInCycle { get; }

        /// <summary>
        /// Concrete valuation of the symbolic predicates that participated
        /// in the property being checked, evaluated against this item's
        /// state. <c>null</c> for traces produced by the explicit (non-symbolic)
        /// checker. For traces produced by the symbolic LTL/RLTL backends, this
        /// is populated by <see cref="TraceInstantiation"/> with one entry per
        /// registered predicate (typically the atoms of the property, plus any
        /// derived predicates pulled in through regex closures).
        /// </summary>
        public IReadOnlyDictionary<IStatePredicate, bool> Valuation { get; }

        /// <summary>
        /// Constructs an instance of this class.
        /// </summary>
        public TraceItem(
            IStepFunction stepFunction,
            StateGraphNode stateGraphNode,
            bool isInCycle = false)
            : this(stepFunction, stateGraphNode, isInCycle, valuation: null)
        {
        }

        /// <summary>
        /// Constructs an instance of this class with a concrete predicate
        /// valuation attached.
        /// </summary>
        public TraceItem(
            IStepFunction stepFunction,
            StateGraphNode stateGraphNode,
            bool isInCycle,
            IReadOnlyDictionary<IStatePredicate, bool> valuation)
        {
            StepFunction = stepFunction;
            StateGraphNode = stateGraphNode;
            IsInCycle = isInCycle;
            Valuation = valuation;
        }
    }
}
