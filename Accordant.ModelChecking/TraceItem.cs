// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking
{
    /// <summary>
    /// A trace consists of a sequence of trace items. Each trace item
    /// indicates the action that lead to the state in the trace item.
    /// </summary>
    public class TraceItem
    {
        /// <summary>
        /// The step function that when applied to the state in the previous
        /// trace item in the trace lead to the state graph node in this trace item.
        /// </summary>
        public IStepFunction StepFunction { get; set; }

        /// <summary>
        /// The state graph node at this point in the trace.
        /// </summary>
        public StateGraphNode StateGraphNode { get; set; }

        /// <summary>
        /// The parent state graph node, at which applying the action leads to
        /// to the state graph node in this trace item.
        /// </summary>
        public StateGraphNode ParentStateGraphNode { get; set; }

        /// <summary>
        /// Constructs an instance of this class.
        /// </summary>
        public TraceItem(
            IStepFunction stepFunction,
            StateGraphNode stateGraphNode,
            StateGraphNode parentStateGraphNode)
        {
            StepFunction = stepFunction;
            StateGraphNode = stateGraphNode;
            ParentStateGraphNode = parentStateGraphNode;
        }
    }

}
