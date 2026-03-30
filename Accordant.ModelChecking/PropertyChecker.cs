// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// This class can be used to be check safety and liveness properties
    /// of a given state space graph.
    /// </summary>
    public class PropertyChecker
    {
        /// <summary>
        /// This field contains the last explored trace.
        /// </summary>
        internal Stack<List<TraceItem>> NestedTraces { get; set; } = new Stack<List<TraceItem>>();

        public PropertyChecker()
        {
            NestedTraces.Push(null);
        }

        /// <summary>
        /// This method checks whether the given predicate is always true
        /// i.e. it is true at each node in the state space graph reachable
        /// from the root.
        /// </summary>
        public bool Always(
            StateGraphNode root,
            Func<StateGraphNode, bool> predicate)
        {
            var result = AlwaysInternal(root, predicate);
            return result;
        }

        /// <summary>
        /// This method checks whether the given predicate is eventually true
        /// in some path starting from the root.
        /// </summary>
        public bool Eventually(
            StateGraphNode root,
            Func<StateGraphNode, bool> predicate)
        {
            var alwaysNotTrue = AlwaysInternal(root, predicate, negatePredicate: true);
            return !alwaysNotTrue;
        }

        /// <summary>
        /// This method checks whether its always the case that the given predicate
        /// is eventually true.
        /// </summary>
        public bool AlwaysEventually(
            StateGraphNode root,
            Func<StateGraphNode, bool> predicate)
        {
            return Always(root, nextN => Eventually(nextN, predicate));
        }

        private bool AlwaysInternal(
            StateGraphNode root,
            Func<StateGraphNode, bool> predicate,
            bool negatePredicate = false)
        {
            var predicateResultMap = new Dictionary<string, bool>();
            var nodesToProcess = new Stack<(StateGraphNode node, StateGraphEdge edge, StateGraphNode parent)>();

            nodesToProcess.Push((root, edge: null, parent: null));

            var trace = new List<TraceItem>();

            while (nodesToProcess.Count > 0)
            {
                var (currentNode, currentEdge, parentNode) = nodesToProcess.Pop();
                var fingerprint = currentNode.GetNodeFingerprint();

                while (
                    trace.Count > 0 &&
                    trace[trace.Count - 1].StateGraphNode != parentNode)
                {
                    trace.RemoveAt(trace.Count - 1);
                }

                var stepFunction = currentEdge?.StepFunction;

                trace.Add(new TraceItem(
                        stepFunction,
                        currentNode,
                        parentNode));

                if (predicateResultMap.ContainsKey(fingerprint))
                {
                    continue;
                }

                bool result;
                try
                {
                    NestedTraces.Push(null);
                    result = predicate(currentNode);
                }
                finally
                {
                    var nestedTrace = NestedTraces.Pop();
                    if (nestedTrace != null)
                    {
                        Invariant.Assert(nestedTrace.Count >= 1);

                        for (int i = 1; i < nestedTrace.Count; i++)
                        {
                            trace.Add(nestedTrace[i]);
                        }
                    }
                }

                predicateResultMap[fingerprint] = result;

                if (negatePredicate)
                {
                    result = !result;
                }

                if (!result)
                {
                    NestedTraces.Pop();
                    NestedTraces.Push(trace);

                    return false;
                }

                foreach (var edge in currentNode.Edges)
                {
                    nodesToProcess.Push((node: edge.Target, edge, parent: currentNode));
                }
            }

            NestedTraces.Pop();
            NestedTraces.Push(trace);

            return true;
        }
    }
}
