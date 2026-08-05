namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Memory-efficient on-the-fly emptiness check for the product of a model
    /// program (<see cref="StateGraphNode"/>) and a symbolic NBW, implemented
    /// as Algorithm B (Nested Depth-First Search) from
    ///
    ///   Costas Courcoubetis, Moshe Y. Vardi, Pierre Wolper, Mihalis Yannakakis.
    ///   "Memory-Efficient Algorithms for the Verification of Temporal Properties."
    ///   Formal Methods in System Design 1 (1992), 275–288.
    ///   https://doi.org/10.1007/BF00121128
    ///
    /// Algorithm B uses two interleaved DFS passes:
    ///
    /// <list type="number">
    ///   <item>
    ///     <description><b>dfs1</b> (outer): a standard DFS from the initial
    ///     product states. When a product node <c>s</c> is about to be popped
    ///     (i.e. post-order) and <c>nbw.IsAccepting(s.NbwState)</c> holds,
    ///     dfs1 launches <b>dfs2</b> with <c>seed = s</c>.</description>
    ///   </item>
    ///   <item>
    ///     <description><b>dfs2</b> (inner): a DFS from <c>seed</c>. If it ever
    ///     reaches <c>seed</c> as a successor, an accepting cycle exists and
    ///     the search terminates.</description>
    ///   </item>
    /// </list>
    ///
    /// Both DFS passes share a single <c>visited2</c> set across all dfs2
    /// invocations — this is what makes the algorithm linear-time and
    /// linear-space (only two bits per product state are required, regardless
    /// of how many accepting states there are).
    ///
    /// Both passes are implemented iteratively (explicit frame stacks) so that
    /// they do not blow the CLR call stack on deep product graphs.
    /// </summary>
    public static class NestedDfsCheck
    {
        /// <summary>
        /// Check emptiness of the language of the product (System × NBW) using
        /// Algorithm B. Returns <see cref="PropertyCheckingResult.Failure"/>
        /// with a counterexample when an accepting cycle is found, and
        /// <see cref="PropertyCheckingResult.Success"/> otherwise.
        /// </summary>
        /// <typeparam name="TNbwState">NBW state type.</typeparam>
        /// <param name="root">Root system node.</param>
        /// <param name="nbw">Symbolic NBW over <see cref="IStatePredicate"/> /
        ///   <see cref="State"/>.</param>
        /// <param name="maxDepth">If positive: any product node first reached
        ///   at depth ≥ <paramref name="maxDepth"/> is treated as a frontier
        ///   node with only a stutter self-loop, matching the bounded-depth
        ///   semantics of <see cref="SymbolicLtlCheck.Check"/>.</param>
        /// <param name="nbwStateComparer">Equality comparer for NBW states.
        ///   Defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        public static PropertyCheckingResult Check<TNbwState>(
            StateGraphNode root,
            SymbolicNBW<IStatePredicate, State, TNbwState> nbw,
            int maxDepth = 0,
            IEqualityComparer<TNbwState> nbwStateComparer = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (nbw == null) throw new ArgumentNullException(nameof(nbw));

            var nbwCmp = nbwStateComparer ?? EqualityComparer<TNbwState>.Default;
            var registry = nbw.Registry;

            // Per-node info (parent pointers for counterexample reconstruction,
            // visited flags). Indexed by composite key (system fingerprint +
            // NBW state).
            var nodes = new Dictionary<string, Node<TNbwState>>(StringComparer.Ordinal);

            // Find or create a product node entry.
            Node<TNbwState> Intern(StateGraphNode sys, TNbwState q, int depth)
            {
                var key = MakeKey(sys, q);
                if (!nodes.TryGetValue(key, out var n))
                {
                    n = new Node<TNbwState>(sys, q, key, depth);
                    nodes[key] = n;
                }
                return n;
            }

            // Enumerate product successors on-the-fly.
            IEnumerable<Successor<TNbwState>> Successors(Node<TNbwState> p)
            {
                var nbwTrans = nbw.GetTransition(p.NbwState);
                var nbwSuccs = EvaluateNbwTransitions(nbwTrans, p.SystemNode.State, registry, nbwCmp);

                var edges = p.SystemNode.Edges;
                var atFrontier = (maxDepth > 0 && p.Depth >= maxDepth);
                var terminal = edges == null || edges.Count == 0;

                if (terminal || atFrontier)
                {
                    foreach (var q in nbwSuccs)
                        yield return new Successor<TNbwState>(p.SystemNode, q, null);
                    yield break;
                }

                foreach (var edge in edges)
                {
                    foreach (var q in nbwSuccs)
                        yield return new Successor<TNbwState>(edge.Target, q, edge.StepFunction);
                }
            }

            // -------- Outer DFS (dfs1) --------
            // For each initial product node, run dfs1 if not already visited.
            // dfs1 maintains an explicit frame stack with a successor
            // enumerator per frame. When a frame's enumerator is exhausted, we
            // post-process: if the frame's node is accepting, launch dfs2.

            var outerStack = new Stack<Frame<TNbwState>>();

            foreach (var nbwInit in nbw.InitialStates)
            {
                var init = Intern(root, nbwInit, 0);
                if (init.Visited1) continue;

                init.Visited1 = true;
                outerStack.Push(new Frame<TNbwState>(init, Successors(init).GetEnumerator()));

                while (outerStack.Count > 0)
                {
                    var top = outerStack.Peek();

                    if (top.Successors.MoveNext())
                    {
                        var s = top.Successors.Current;
                        var child = Intern(s.SystemNode, s.NbwState, top.Node.Depth + 1);
                        if (!child.Visited1)
                        {
                            child.Visited1 = true;
                            child.OuterParent = top.Node;
                            child.IncomingStep = s.StepFunction;
                            outerStack.Push(new Frame<TNbwState>(child, Successors(child).GetEnumerator()));
                        }
                    }
                    else
                    {
                        // Post-order: enumerator exhausted, ready to pop.
                        top.Successors.Dispose();
                        outerStack.Pop();

                        if (nbw.IsAccepting(top.Node.NbwState))
                        {
                            // Run dfs2 with seed = top.Node.
                            if (RunInnerDfs(top.Node, Successors, nodes, out var closingPredecessor))
                            {
                                // Found an accepting cycle.
                                var trace = BuildCounterexample(top.Node, closingPredecessor);
                                trace = TraceInstantiation.AttachValuations(trace, registry);
                                var badCycle = StronglyConnectedComponent.FromSystemNodes(
                                    CollectCycleSystemNodes(top.Node, closingPredecessor));
                                return PropertyCheckingResult.Failure(trace, badCycle);
                            }
                        }
                    }
                }
            }

            return PropertyCheckingResult.Success();
        }

        /// <summary>
        /// Inner DFS (dfs2): from <paramref name="seed"/>, search for a path
        /// back to <paramref name="seed"/>. Returns true if a cycle is found;
        /// in that case <paramref name="closingPredecessor"/> is the node from
        /// which <c>seed</c> was discovered as a successor (i.e. the last node
        /// on the cycle before it closes back onto <c>seed</c>).
        /// The shared <c>Visited2</c> flag is preserved across all dfs2 calls.
        /// </summary>
        private static bool RunInnerDfs<TNbwState>(
            Node<TNbwState> seed,
            Func<Node<TNbwState>, IEnumerable<Successor<TNbwState>>> successors,
            Dictionary<string, Node<TNbwState>> nodes,
            out Node<TNbwState> closingPredecessor)
        {
            closingPredecessor = null;

            // If seed already visited by a previous dfs2 (it cannot be — dfs2
            // only runs at most once per accepting node and shares visited2 —
            // but this guards against re-entry should that invariant change).
            if (seed.Visited2) return false;
            seed.Visited2 = true;
            seed.InnerParent = null;

            var stack = new Stack<Frame<TNbwState>>();
            stack.Push(new Frame<TNbwState>(seed, successors(seed).GetEnumerator()));

            while (stack.Count > 0)
            {
                var top = stack.Peek();

                if (top.Successors.MoveNext())
                {
                    var s = top.Successors.Current;
                    var key = MakeKey(s.SystemNode, s.NbwState);

                    // Algorithm B's defining check: did dfs2 reach the seed?
                    if (key.Equals(seed.Key, StringComparison.Ordinal))
                    {
                        closingPredecessor = top.Node;
                        // Drain frames so enumerators are disposed.
                        while (stack.Count > 0)
                            stack.Pop().Successors.Dispose();
                        return true;
                    }

                    if (!nodes.TryGetValue(key, out var child))
                    {
                        // dfs2 should only reach nodes already discovered by
                        // dfs1 in a single connected exploration, but the
                        // product is built on-the-fly, so just intern.
                        child = new Node<TNbwState>(s.SystemNode, s.NbwState, key, top.Node.Depth + 1);
                        nodes[key] = child;
                    }

                    if (!child.Visited2)
                    {
                        child.Visited2 = true;
                        child.InnerParent = top.Node;
                        child.InnerIncomingStep = s.StepFunction;
                        stack.Push(new Frame<TNbwState>(child, successors(child).GetEnumerator()));
                    }
                }
                else
                {
                    top.Successors.Dispose();
                    stack.Pop();
                }
            }

            return false;
        }

        /// <summary>
        /// Walks the inner-parent chain from <paramref name="closingPredecessor"/>
        /// back to <paramref name="seed"/> to collect the system nodes
        /// participating in the accepting cycle. Feeds the system-level
        /// projection used for <see cref="PropertyCheckingResult.BadCycle"/>
        /// so the enabled-but-not-taken fairness hint fires for
        /// nested-DFS-discovered counterexamples on parity with the
        /// explicit-LTL backend.
        /// </summary>
        private static IEnumerable<StateGraphNode> CollectCycleSystemNodes<TNbwState>(
            Node<TNbwState> seed,
            Node<TNbwState> closingPredecessor)
        {
            yield return seed.SystemNode;
            if (closingPredecessor == null) yield break;
            for (var n = closingPredecessor; n != null && n != seed; n = n.InnerParent)
                yield return n.SystemNode;
        }

        /// <summary>
        /// Builds a counterexample trace: prefix (initial → seed) followed by
        /// cycle (seed → … → seed). The closing edge is implied by the last
        /// trace item being the predecessor that discovered seed as a
        /// successor in dfs2.
        /// </summary>
        private static List<TraceItem> BuildCounterexample<TNbwState>(
            Node<TNbwState> seed,
            Node<TNbwState> closingPredecessor)
        {
            var trace = new List<TraceItem>();

            // Prefix: walk outer-parent pointers from seed back to its root,
            // then reverse. The root has OuterParent == null.
            var prefix = new List<Node<TNbwState>>();
            for (var n = seed; n != null; n = n.OuterParent)
                prefix.Add(n);
            prefix.Reverse();

            foreach (var n in prefix)
                trace.Add(new TraceItem(n.IncomingStep, n.SystemNode, isInCycle: false));

            // Cycle: walk inner-parent pointers from closingPredecessor back
            // to seed, reverse, then append the seed itself to close the
            // cycle.
            if (closingPredecessor != null)
            {
                var cycle = new List<Node<TNbwState>>();
                for (var n = closingPredecessor; n != null && n != seed; n = n.InnerParent)
                    cycle.Add(n);
                cycle.Reverse();

                foreach (var n in cycle)
                    trace.Add(new TraceItem(n.InnerIncomingStep, n.SystemNode, isInCycle: true));

                // Closing edge: from closingPredecessor (the last node added,
                // or seed itself if cycle is empty i.e. self-loop) back to
                // seed. The step function on this edge is the step function
                // that took closingPredecessor → seed in the product. We
                // don't track that here; emit the seed as the final cycle
                // item with a null step (the cycle interpretation is "return
                // to seed").
                trace.Add(new TraceItem(null, seed.SystemNode, isInCycle: true));
            }

            return trace;
        }

        /// <summary>
        /// Evaluate Antimirov-form transitions against a concrete system state
        /// to produce the set of successor NBW states.
        /// </summary>
        private static HashSet<TNbwState> EvaluateNbwTransitions<TNbwState>(
            IReadOnlyList<TransitionTerm<StateSet<TNbwState>>> transitions,
            IState systemState,
            ConditionRegistry<IStatePredicate> registry,
            IEqualityComparer<TNbwState> comparer)
        {
            var result = new HashSet<TNbwState>(comparer);
            foreach (var term in transitions)
            {
                var leaf = EvaluateTerm(term, systemState, registry);
                if (leaf != null)
                    foreach (var s in leaf)
                        result.Add(s);
            }
            return result;
        }

        /// <summary>
        /// Walks an ITE transition term against a concrete state, following
        /// the unique path to a leaf.
        /// </summary>
        private static StateSet<TNbwState> EvaluateTerm<TNbwState>(
            TransitionTerm<StateSet<TNbwState>> term,
            IState systemState,
            ConditionRegistry<IStatePredicate> registry)
        {
            while (true)
            {
                if (term is TransitionTermLeaf<StateSet<TNbwState>> leaf)
                    return leaf.Value;
                var ite = (TransitionTermIte<StateSet<TNbwState>>)term;
                var pred = registry.GetPredicate(ite.ConditionIndex);
                term = pred.Eval(systemState) ? ite.Hi : ite.Lo;
            }
        }

        private static string MakeKey<TNbwState>(StateGraphNode sys, TNbwState q)
            => $"{sys.GetNodeFingerprint()}|{q?.GetHashCode():X8}|{q}";

        #region Internal types

        private sealed class Node<TNbwState>
        {
            public StateGraphNode SystemNode { get; }
            public TNbwState NbwState { get; }
            public string Key { get; }
            public int Depth { get; }

            public bool Visited1 { get; set; }
            public bool Visited2 { get; set; }

            // Outer-DFS spanning-tree info (for prefix reconstruction).
            public Node<TNbwState> OuterParent { get; set; }
            public IStepFunction IncomingStep { get; set; }

            // Inner-DFS spanning-tree info (for cycle reconstruction).
            public Node<TNbwState> InnerParent { get; set; }
            public IStepFunction InnerIncomingStep { get; set; }

            public Node(StateGraphNode sys, TNbwState q, string key, int depth)
            {
                SystemNode = sys;
                NbwState = q;
                Key = key;
                Depth = depth;
            }
        }

        private readonly struct Successor<TNbwState>
        {
            public StateGraphNode SystemNode { get; }
            public TNbwState NbwState { get; }
            public IStepFunction StepFunction { get; }

            public Successor(StateGraphNode sys, TNbwState q, IStepFunction sf)
            {
                SystemNode = sys;
                NbwState = q;
                StepFunction = sf;
            }
        }

        private sealed class Frame<TNbwState>
        {
            public Node<TNbwState> Node { get; }
            public IEnumerator<Successor<TNbwState>> Successors { get; }

            public Frame(Node<TNbwState> node, IEnumerator<Successor<TNbwState>> succs)
            {
                Node = node;
                Successors = succs;
            }
        }

        #endregion
    }
}
