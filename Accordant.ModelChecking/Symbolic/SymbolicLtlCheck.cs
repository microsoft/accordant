namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Symbolic LTL model checking via product construction with the NBW
    /// from alternation elimination. Uses the standard approach:
    /// negate the property, build NBW(¬φ), find accepting cycle = violation.
    /// 
    /// Supports bounded depth exploration for infinite-state model programs.
    /// </summary>
    public static class SymbolicLtlCheck
    {
        /// <summary>
        /// Check an LTL property against a model program.
        /// 
        /// Algorithm:
        /// 1. Negate the property: ¬φ
        /// 2. Construct ABW for ¬φ via symbolic derivatives
        /// 3. Run incremental Æ to get lazy NBW
        /// 4. Product construction: explore (SystemNode × NBWState) pairs
        /// 5. Detect accepting cycles via Tarjan's SCC
        /// 6. If found: return violation with counterexample
        /// </summary>
        /// <param name="root">Root of the state graph (model program).</param>
        /// <param name="property">The LTL property φ to check (should hold).</param>
        /// <param name="maxDepth">Maximum exploration depth (0 = unlimited).</param>
        /// <param name="fairness">Optional fairness constraint. When supplied
        ///   (non-<c>null</c> and not <see cref="Fairness.None"/>), emptiness
        ///   is checked with <see cref="SccProductCheck"/> so only fair
        ///   accepting cycles count as counterexamples.</param>
        /// <returns>Result with counterexample if property is violated.</returns>
        public static PropertyCheckingResult Check(
            StateGraphNode root,
            Ltl<IStatePredicate> property,
            int maxDepth = 0,
            Fairness fairness = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (property == null) throw new ArgumentNullException(nameof(property));

            // 1. Negate the property
            var negPhi = LtlAlgebra.Default.Not(property);

            // 2. Build ABW for ¬φ
            var eba = StatePropEbaProvider.Default;
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);
            var derivative = new LtlDerivative<IStatePredicate, State>(eba, registry);
            var abw = derivative.ToABW(negPhi);

            // 3. Incremental Æ → lazy NBW
            var incAE = new IncrementalAE<IStatePredicate, State, Ltl<IStatePredicate>>(abw);
            var nbw = incAE.ToNBW();

            // 4. Pick the emptiness check. With fairness we need an SCC-level
            //    analysis to evaluate enabled/taken; without it we keep the
            //    existing inline product+SCC path for backwards compatibility.
            if (fairness != null && !ReferenceEquals(fairness, Fairness.None))
            {
                var bpComparer = BreakpointState<Ltl<IStatePredicate>>.GetEqualityComparer();
                return SccProductCheck.Check(root, nbw, maxDepth, bpComparer, fairness);
            }

            return ExploreProduct(root, nbw, registry, maxDepth);
        }

        /// <summary>
        /// Check an LTL property against a model program using Nested DFS
        /// (Algorithm B from Courcoubetis–Vardi–Wolper–Yannakakis, FMSD 1992).
        ///
        /// Equivalent semantics to <see cref="Check"/> but uses on-the-fly
        /// nested depth-first cycle detection instead of building the full
        /// product graph and running Tarjan's SCC algorithm. Suitable for
        /// large or implicit product graphs where memory is a concern.
        /// </summary>
        /// <param name="root">Root of the state graph (model program).</param>
        /// <param name="property">The LTL property φ to check (should hold).</param>
        /// <param name="maxDepth">Maximum exploration depth (0 = unlimited).</param>
        public static PropertyCheckingResult CheckNDFS(
            StateGraphNode root,
            Ltl<IStatePredicate> property,
            int maxDepth = 0)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (property == null) throw new ArgumentNullException(nameof(property));

            var negPhi = LtlAlgebra.Default.Not(property);

            var eba = StatePropEbaProvider.Default;
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);
            var derivative = new LtlDerivative<IStatePredicate, State>(eba, registry);
            var abw = derivative.ToABW(negPhi);

            var incAE = new IncrementalAE<IStatePredicate, State, Ltl<IStatePredicate>>(abw);
            var nbw = incAE.ToNBW();

            var bpComparer = BreakpointState<Ltl<IStatePredicate>>.GetEqualityComparer();
            return NestedDfsCheck.Check(root, nbw, maxDepth, bpComparer);
        }

        /// <summary>
        /// Explores the product graph (System × NBW) looking for accepting cycles.
        /// Uses iterative Tarjan's SCC algorithm on the product graph.
        /// </summary>
        private static PropertyCheckingResult ExploreProduct(
            StateGraphNode root,
            SymbolicNBW<IStatePredicate, State,
                BreakpointState<Ltl<IStatePredicate>>> nbw,
            ConditionRegistry<IStatePredicate> registry,
            int maxDepth)
        {
            // Product state: (system fingerprint, NBW state)
            var bpComparer = BreakpointState<Ltl<IStatePredicate>>.GetEqualityComparer();

            // Product node tracking
            var productNodes = new Dictionary<string, ProductNodeInfo>();
            var worklist = new Queue<ProductNodeInfo>();

            // Initialize: system root × each NBW initial state
            foreach (var nbwInit in nbw.InitialStates)
            {
                var key = MakeProductKey(root, nbwInit);
                if (!productNodes.ContainsKey(key))
                {
                    var info = new ProductNodeInfo(root, nbwInit, key, 0, null, null);
                    productNodes[key] = info;
                    worklist.Enqueue(info);
                }
            }

            // BFS exploration of product graph, building adjacency
            while (worklist.Count > 0)
            {
                var current = worklist.Dequeue();

                if (maxDepth > 0 && current.Depth >= maxDepth)
                {
                    // At the depth frontier we admit only a system stutter
                    // (sys stays put); the NBW must make a real transition
                    // on the current state's label rather than a fake
                    // unconditional self-loop. The previous unconditional
                    // self-loop fabricated accepting cycles for properties
                    // the NBW could not actually satisfy at the frontier
                    // state (e.g. <c>F p</c> when <c>p</c> is true at the
                    // frontier and the NBW for <c>G ¬p</c> has no
                    // outgoing transition there). Now consistent with the
                    // <see cref="NestedDfsCheck"/> frontier handling.
                    var nbwTransitionsFr = nbw.GetTransition(current.NbwState);
                    var frontierSuccs = EvaluateNbwTransitions(
                        nbwTransitionsFr, current.SystemNode.State, registry);
                    foreach (var succNbwState in frontierSuccs)
                    {
                        var succKey = MakeProductKey(current.SystemNode, succNbwState);
                        if (!productNodes.TryGetValue(succKey, out var succInfo))
                        {
                            succInfo = new ProductNodeInfo(
                                current.SystemNode, succNbwState, succKey,
                                current.Depth + 1, null, current);
                            productNodes[succKey] = succInfo;
                            worklist.Enqueue(succInfo);
                        }
                        current.Successors.Add(succInfo);
                    }
                    continue;
                }

                var sysNode = current.SystemNode;
                var nbwState = current.NbwState;

                // Get NBW transitions for current NBW state
                var nbwTransitions = nbw.GetTransition(nbwState);

                // If system node is terminal (no outgoing edges): stutter self-loop
                var sysEdges = sysNode.Edges;
                if (sysEdges == null || sysEdges.Count == 0)
                {
                    // Stutter: stay in same system state, advance NBW
                    var successorNbwStates = EvaluateNbwTransitions(
                        nbwTransitions, sysNode.State, registry);

                    foreach (var succNbwState in successorNbwStates)
                    {
                        var succKey = MakeProductKey(sysNode, succNbwState);
                        if (!productNodes.TryGetValue(succKey, out var succInfo))
                        {
                            succInfo = new ProductNodeInfo(
                                sysNode, succNbwState, succKey, current.Depth + 1,
                                null, current);
                            productNodes[succKey] = succInfo;
                            worklist.Enqueue(succInfo);
                        }
                        current.Successors.Add(succInfo);
                    }
                    continue;
                }

                // Normal transitions
                foreach (var edge in sysEdges)
                {
                    var nextSysNode = edge.Target;

                    // Evaluate NBW transitions against the CURRENT system state
                    // (the label is consumed at the source)
                    var successorNbwStates = EvaluateNbwTransitions(
                        nbwTransitions, sysNode.State, registry);

                    foreach (var succNbwState in successorNbwStates)
                    {
                        var succKey = MakeProductKey(nextSysNode, succNbwState);
                        if (!productNodes.TryGetValue(succKey, out var succInfo))
                        {
                            succInfo = new ProductNodeInfo(
                                nextSysNode, succNbwState, succKey, current.Depth + 1,
                                edge.StepFunction, current);
                            productNodes[succKey] = succInfo;
                            worklist.Enqueue(succInfo);
                        }
                        current.Successors.Add(succInfo);
                    }
                }
            }

            // 5. Find SCCs in product graph using Tarjan's
            var sccs = FindProductSCCs(productNodes.Values);

            // 6. Check for accepting cycles
            foreach (var scc in sccs)
            {
                if (!scc.HasCycle) continue;

                // An SCC is accepting if it contains at least one product node
                // whose NBW state is accepting (obligation = ∅)
                bool hasAccepting = scc.Nodes.Any(n => nbw.IsAccepting(n.NbwState));
                if (!hasAccepting) continue;

                // Found a violation! Build counterexample trace.
                var trace = BuildCounterexample(scc, productNodes, root, nbw);
                trace = TraceInstantiation.AttachValuations(trace, registry);
                return PropertyCheckingResult.Failure(trace);
            }

            return PropertyCheckingResult.Success();
        }

        /// <summary>
        /// Evaluates NBW transitions (Antimirov form) against a concrete system state.
        /// Returns the set of successor NBW states.
        /// </summary>
        private static HashSet<BreakpointState<Ltl<IStatePredicate>>> EvaluateNbwTransitions(
            IReadOnlyList<TransitionTerm<StateSet<BreakpointState<Ltl<IStatePredicate>>>>> transitions,
            IState systemState,
            ConditionRegistry<IStatePredicate> registry)
        {
            var result = new HashSet<BreakpointState<Ltl<IStatePredicate>>>(
                BreakpointState<Ltl<IStatePredicate>>.GetEqualityComparer());

            foreach (var term in transitions)
            {
                var successorSet = EvaluateTerm(term, systemState, registry);
                if (successorSet != null)
                {
                    foreach (var s in successorSet)
                        result.Add(s);
                }
            }

            return result;
        }

        /// <summary>
        /// Evaluates a single transition term (ADD) against a concrete state,
        /// following the unique path through the ITE tree.
        /// </summary>
        private static StateSet<BreakpointState<Ltl<IStatePredicate>>> EvaluateTerm(
            TransitionTerm<StateSet<BreakpointState<Ltl<IStatePredicate>>>> term,
            IState systemState,
            ConditionRegistry<IStatePredicate> registry)
        {
            while (true)
            {
                if (term is TransitionTermLeaf<StateSet<BreakpointState<Ltl<IStatePredicate>>>> leaf)
                    return leaf.Value;

                var ite = (TransitionTermIte<StateSet<BreakpointState<Ltl<IStatePredicate>>>>)term;
                var pred = registry.GetPredicate(ite.ConditionIndex);
                term = pred.Eval(systemState) ? ite.Hi : ite.Lo;
            }
        }

        private static string MakeProductKey(
            StateGraphNode sysNode,
            BreakpointState<Ltl<IStatePredicate>> nbwState)
        {
            return $"{sysNode.GetNodeFingerprint()}|{nbwState.GetHashCode():X8}|{nbwState}";
        }

        #region Tarjan SCC on Product Graph

        private static List<ProductSCC> FindProductSCCs(
            IEnumerable<ProductNodeInfo> nodes)
        {
            var result = new List<ProductSCC>();
            var indexMap = new Dictionary<string, int>();
            var lowLinkMap = new Dictionary<string, int>();
            var onStack = new HashSet<string>();
            var stack = new Stack<ProductNodeInfo>();
            int index = 0;

            foreach (var node in nodes)
            {
                if (!indexMap.ContainsKey(node.Key))
                    StrongConnect(node);
            }

            void StrongConnect(ProductNodeInfo node)
            {
                indexMap[node.Key] = index;
                lowLinkMap[node.Key] = index;
                index++;
                stack.Push(node);
                onStack.Add(node.Key);

                foreach (var succ in node.Successors)
                {
                    if (!indexMap.ContainsKey(succ.Key))
                    {
                        StrongConnect(succ);
                        lowLinkMap[node.Key] = Math.Min(
                            lowLinkMap[node.Key], lowLinkMap[succ.Key]);
                    }
                    else if (onStack.Contains(succ.Key))
                    {
                        lowLinkMap[node.Key] = Math.Min(
                            lowLinkMap[node.Key], indexMap[succ.Key]);
                    }
                }

                if (lowLinkMap[node.Key] == indexMap[node.Key])
                {
                    var scc = new ProductSCC();
                    ProductNodeInfo w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w.Key);
                        scc.Nodes.Add(w);
                    } while (!w.Key.Equals(node.Key));

                    // SCC has a real cycle if it has >1 node, or a self-loop
                    scc.HasCycle = scc.Nodes.Count > 1 ||
                        scc.Nodes[0].Successors.Any(s => s.Key == scc.Nodes[0].Key);

                    result.Add(scc);
                }
            }

            return result;
        }

        #endregion

        #region Counterexample Construction

        private static List<TraceItem> BuildCounterexample(
            ProductSCC scc,
            Dictionary<string, ProductNodeInfo> allNodes,
            StateGraphNode systemRoot,
            SymbolicNBW<IStatePredicate, State,
                BreakpointState<Ltl<IStatePredicate>>> nbw)
        {
            var trace = new List<TraceItem>();

            // Find a node in the SCC to serve as the cycle entry
            var cycleEntry = scc.Nodes.First(n => nbw.IsAccepting(n.NbwState))
                ?? scc.Nodes[0];

            // BFS from initial nodes to the cycle entry (prefix)
            var path = BfsToNode(allNodes, systemRoot, nbw, cycleEntry);

            // Add prefix (non-cycle portion)
            foreach (var node in path)
            {
                trace.Add(new TraceItem(
                    node.IncomingStepFunction,
                    node.SystemNode,
                    isInCycle: false));
            }

            // Add cycle portion (walk around the SCC)
            var sccKeys = new HashSet<string>(scc.Nodes.Select(n => n.Key));
            var visited = new HashSet<string>();
            var cycleNode = cycleEntry;
            visited.Add(cycleNode.Key);

            // Walk one step around the cycle
            for (int i = 0; i < scc.Nodes.Count && i < 10; i++)
            {
                var next = cycleNode.Successors
                    .FirstOrDefault(s => sccKeys.Contains(s.Key) && !visited.Contains(s.Key));
                if (next == null)
                {
                    // Try to close the cycle
                    next = cycleNode.Successors
                        .FirstOrDefault(s => s.Key == cycleEntry.Key);
                    if (next != null)
                    {
                        trace.Add(new TraceItem(
                            next.IncomingStepFunction,
                            next.SystemNode,
                            isInCycle: true));
                    }
                    break;
                }

                visited.Add(next.Key);
                trace.Add(new TraceItem(
                    next.IncomingStepFunction,
                    next.SystemNode,
                    isInCycle: true));
                cycleNode = next;
            }

            return trace;
        }

        private static List<ProductNodeInfo> BfsToNode(
            Dictionary<string, ProductNodeInfo> allNodes,
            StateGraphNode systemRoot,
            SymbolicNBW<IStatePredicate, State,
                BreakpointState<Ltl<IStatePredicate>>> nbw,
            ProductNodeInfo target)
        {
            // BFS from initial product nodes to target
            var visited = new Dictionary<string, ProductNodeInfo>();
            var parent = new Dictionary<string, ProductNodeInfo>();
            var queue = new Queue<ProductNodeInfo>();

            // Find initial product nodes
            foreach (var kvp in allNodes)
            {
                var node = kvp.Value;
                if (node.Depth == 0)
                {
                    visited[node.Key] = node;
                    parent[node.Key] = null;
                    queue.Enqueue(node);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Key == target.Key)
                {
                    // Reconstruct path
                    var path = new List<ProductNodeInfo>();
                    var n = current;
                    while (n != null)
                    {
                        path.Add(n);
                        parent.TryGetValue(n.Key, out n);
                    }
                    path.Reverse();
                    return path;
                }

                foreach (var succ in current.Successors)
                {
                    if (!visited.ContainsKey(succ.Key))
                    {
                        visited[succ.Key] = succ;
                        parent[succ.Key] = current;
                        queue.Enqueue(succ);
                    }
                }
            }

            // Fallback: return just the target
            return new List<ProductNodeInfo> { target };
        }

        #endregion

        #region Internal Types

        private sealed class ProductNodeInfo
        {
            public StateGraphNode SystemNode { get; }
            public BreakpointState<Ltl<IStatePredicate>> NbwState { get; }
            public string Key { get; }
            public int Depth { get; }
            public IStepFunction IncomingStepFunction { get; }
            public ProductNodeInfo Parent { get; }
            public List<ProductNodeInfo> Successors { get; } = new List<ProductNodeInfo>();

            public ProductNodeInfo(
                StateGraphNode systemNode,
                BreakpointState<Ltl<IStatePredicate>> nbwState,
                string key,
                int depth,
                IStepFunction incomingStepFunction,
                ProductNodeInfo parent)
            {
                SystemNode = systemNode;
                NbwState = nbwState;
                Key = key;
                Depth = depth;
                IncomingStepFunction = incomingStepFunction;
                Parent = parent;
            }
        }

        private sealed class ProductSCC
        {
            public List<ProductNodeInfo> Nodes { get; } = new List<ProductNodeInfo>();
            public bool HasCycle { get; set; }
        }

        #endregion
    }
}
