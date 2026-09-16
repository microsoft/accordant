namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Represents a Strongly Connected Component (SCC) in the state graph.
    /// An SCC is a maximal set of nodes where every node is reachable from every other node.
    /// </summary>
    public class StronglyConnectedComponent
    {
        /// <summary>
        /// The nodes in this SCC.
        /// </summary>
        public List<StateGraphNode> Nodes { get; } = new List<StateGraphNode>();

        /// <summary>
        /// Returns true if this SCC has a real cycle (more than one node, or a self-loop).
        /// </summary>
        public bool HasCycle { get; internal set; }

        /// <summary>
        /// Returns true if any node in this SCC satisfies the given predicate.
        /// </summary>
        public bool Any(Func<StateGraphNode, bool> predicate)
        {
            return Nodes.Any(predicate);
        }

        /// <summary>
        /// Returns true if all nodes in this SCC satisfy the given predicate.
        /// </summary>
        public bool All(Func<StateGraphNode, bool> predicate)
        {
            return Nodes.All(predicate);
        }

        /// <summary>
        /// Project an arbitrary collection of system <see cref="StateGraphNode"/>s
        /// (deduplicated by <see cref="StateGraphNode.GetNodeFingerprint"/>)
        /// into a <see cref="StronglyConnectedComponent"/>. Used by the
        /// symbolic backends (<see cref="Symbolic.SccProductCheck"/>,
        /// <see cref="Symbolic.NestedDfsCheck"/>) to attach a system-level
        /// SCC to <see cref="PropertyCheckingResult.BadCycle"/> so the
        /// enabled-but-not-taken fairness hint fires consistently with
        /// the explicit-LTL backend.
        /// </summary>
        internal static StronglyConnectedComponent FromSystemNodes(IEnumerable<StateGraphNode> nodes, bool hasCycle = true)
        {
            var scc = new StronglyConnectedComponent();
            var seen = new HashSet<string>();
            foreach (var n in nodes)
            {
                if (n == null) continue;
                var fp = n.GetNodeFingerprint();
                if (seen.Add(fp)) scc.Nodes.Add(n);
            }
            scc.HasCycle = hasCycle;
            return scc;
        }
    }

    /// <summary>
    /// Implements Tarjan's algorithm for finding Strongly Connected Components.
    /// Time complexity: O(V + E)
    /// </summary>
    public static class TarjanSCC
    {
        /// <summary>
        /// Finds all SCCs in the state graph reachable from the given root.
        /// SCCs are returned in reverse topological order (leaf SCCs first).
        /// </summary>
        public static List<StronglyConnectedComponent> FindSCCs(StateGraphNode root)
        {
            var result = new List<StronglyConnectedComponent>();
            var indexMap = new Dictionary<string, int>();
            var lowLinkMap = new Dictionary<string, int>();
            var onStack = new HashSet<string>();
            var stack = new Stack<StateGraphNode>();
            int index = 0;

            void StrongConnect(StateGraphNode node)
            {
                var nodeId = node.GetNodeFingerprint();
                indexMap[nodeId] = index;
                lowLinkMap[nodeId] = index;
                index++;
                stack.Push(node);
                onStack.Add(nodeId);

                foreach (var edge in node.Edges)
                {
                    var successor = edge.Target;
                    var successorId = successor.GetNodeFingerprint();

                    if (!indexMap.ContainsKey(successorId))
                    {
                        // Successor not yet visited
                        StrongConnect(successor);
                        lowLinkMap[nodeId] = Math.Min(lowLinkMap[nodeId], lowLinkMap[successorId]);
                    }
                    else if (onStack.Contains(successorId))
                    {
                        // Successor is on stack, hence in current SCC
                        lowLinkMap[nodeId] = Math.Min(lowLinkMap[nodeId], indexMap[successorId]);
                    }
                }

                // If node is a root node, pop the stack and generate an SCC
                if (lowLinkMap[nodeId] == indexMap[nodeId])
                {
                    var scc = new StronglyConnectedComponent();
                    StateGraphNode w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w.GetNodeFingerprint());
                        scc.Nodes.Add(w);
                    } while (w.GetNodeFingerprint() != nodeId);

                    // Determine if SCC has a real cycle
                    scc.HasCycle = DetermineHasCycle(scc);
                    result.Add(scc);
                }
            }

            StrongConnect(root);
            return result;
        }

        private static bool DetermineHasCycle(StronglyConnectedComponent scc)
        {
            // More than one node means there's definitely a cycle
            if (scc.Nodes.Count > 1)
            {
                return true;
            }

            // Single node - check for self-loop
            if (scc.Nodes.Count == 1)
            {
                var node = scc.Nodes[0];
                var nodeId = node.GetNodeFingerprint();
                return node.Edges.Any(e => e.Target.GetNodeFingerprint() == nodeId);
            }

            return false;
        }

        /// <summary>
        /// Gets all SCCs that are reachable from any node in the given starting set.
        /// </summary>
        public static HashSet<StronglyConnectedComponent> GetReachableSCCs(
            StateGraphNode startNode,
            List<StronglyConnectedComponent> allSCCs)
        {
            // Build a map from node fingerprint to SCC
            var nodeToSCC = new Dictionary<string, StronglyConnectedComponent>();
            foreach (var scc in allSCCs)
            {
                foreach (var node in scc.Nodes)
                {
                    nodeToSCC[node.GetNodeFingerprint()] = scc;
                }
            }

            // BFS/DFS to find all reachable SCCs
            var reachable = new HashSet<StronglyConnectedComponent>();
            var visited = new HashSet<string>();
            var queue = new Queue<StateGraphNode>();

            queue.Enqueue(startNode);
            visited.Add(startNode.GetNodeFingerprint());

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                var nodeId = node.GetNodeFingerprint();

                if (nodeToSCC.TryGetValue(nodeId, out var scc))
                {
                    reachable.Add(scc);
                }

                foreach (var edge in node.Edges)
                {
                    var successorId = edge.Target.GetNodeFingerprint();
                    if (!visited.Contains(successorId))
                    {
                        visited.Add(successorId);
                        queue.Enqueue(edge.Target);
                    }
                }
            }

            return reachable;
        }
    }
}
