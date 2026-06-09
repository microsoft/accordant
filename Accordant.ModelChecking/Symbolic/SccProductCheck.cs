namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Generic SCC-based emptiness check for the product of a model program
    /// (<see cref="StateGraphNode"/>) and a symbolic NBW. Tarjan's SCC
    /// algorithm on the on-the-fly product graph; an SCC is a counterexample
    /// when it has a real cycle and contains an accepting NBW state.
    ///
    /// <para>
    /// When a <see cref="Fairness"/> constraint is supplied, each candidate
    /// (cyclic, accepting) SCC is further filtered through
    /// <see cref="IsFairProductCycle"/>: only SCCs satisfying every required
    /// weak/strong fairness condition count as counterexamples. The check
    /// operates directly on the product SCC, reading <c>enabled</c> from
    /// <see cref="StateGraphNode.StepFunctions"/> and <c>taken</c> from the
    /// step-function annotation on product edges (<see cref="ProductEdge{T}.StepFunction"/>).
    /// </para>
    /// </summary>
    public static class SccProductCheck
    {
        /// <summary>
        /// Check emptiness of the product (System × NBW).
        /// </summary>
        public static PropertyCheckingResult Check<TNbwState>(
            StateGraphNode root,
            SymbolicNBW<IStatePredicate, State, TNbwState> nbw,
            int maxDepth = 0,
            IEqualityComparer<TNbwState> nbwStateComparer = null,
            Fairness fairness = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (nbw == null) throw new ArgumentNullException(nameof(nbw));

            nbwStateComparer ??= EqualityComparer<TNbwState>.Default;
            var registry = nbw.Registry;

            //
            // Build product graph: BFS over (system node, NBW state) pairs.
            // Each product node remembers (a) how it was reached (parent +
            // step function on the incoming edge) and (b) all of its
            // outgoing product edges annotated with the system step
            // function that produced them. The annotation is what makes a
            // sound fairness check possible later.
            //

            var nodesBySys = new Dictionary<string,
                Dictionary<TNbwState, ProductNode<TNbwState>>>();
            var allNodes = new List<ProductNode<TNbwState>>();
            var worklist = new Queue<ProductNode<TNbwState>>();

            ProductNode<TNbwState> GetOrCreate(
                StateGraphNode sysNode,
                TNbwState nbwState,
                int depth,
                IStepFunction incomingStep,
                ProductNode<TNbwState> parent)
            {
                var fp = sysNode.GetNodeFingerprint();
                if (!nodesBySys.TryGetValue(fp, out var byState))
                {
                    byState = new Dictionary<TNbwState, ProductNode<TNbwState>>(nbwStateComparer);
                    nodesBySys[fp] = byState;
                }
                if (byState.TryGetValue(nbwState, out var existing)) return existing;

                var info = new ProductNode<TNbwState>(
                    sysNode, nbwState, fp, depth, incomingStep, parent);
                byState[nbwState] = info;
                allNodes.Add(info);
                worklist.Enqueue(info);
                return info;
            }

            foreach (var nbwInit in nbw.InitialStates)
            {
                GetOrCreate(root, nbwInit, 0, null, null);
            }

            while (worklist.Count > 0)
            {
                var current = worklist.Dequeue();

                if (maxDepth > 0 && current.Depth >= maxDepth)
                {
                    // At the depth frontier we cannot explore further system
                    // edges, so the only continuation we admit is a system
                    // stutter (sys stays the same). The NBW, however, must
                    // make a real transition on the current state's label —
                    // pretending it can self-loop unconditionally would
                    // fabricate accepting cycles for properties the NBW
                    // cannot actually satisfy here. Aligns with the
                    // <see cref="NestedDfsCheck"/> frontier handling.
                    var frontierNbw = nbw.GetTransition(current.NbwState);
                    var frontierSuccs = EvaluateNbwTransitions(
                        frontierNbw, current.SystemNode.State, registry, nbwStateComparer);
                    foreach (var succNbw in frontierSuccs)
                    {
                        var succ = GetOrCreate(
                            current.SystemNode, succNbw, current.Depth + 1, null, current);
                        current.Successors.Add(new ProductEdge<TNbwState>(null, succ));
                    }
                    continue;
                }

                var nbwTrans = nbw.GetTransition(current.NbwState);
                var sysNode = current.SystemNode;
                var sysEdges = sysNode.Edges;

                if (sysEdges == null || sysEdges.Count == 0)
                {
                    var stutterSuccs = EvaluateNbwTransitions(
                        nbwTrans, sysNode.State, registry, nbwStateComparer);
                    foreach (var succNbw in stutterSuccs)
                    {
                        var succ = GetOrCreate(sysNode, succNbw, current.Depth + 1, null, current);
                        current.Successors.Add(new ProductEdge<TNbwState>(null, succ));
                    }
                    continue;
                }

                // NBW transitions depend only on the source system state,
                // so evaluate once and reuse for all outgoing edges.
                var nbwSuccsAll = EvaluateNbwTransitions(
                    nbwTrans, sysNode.State, registry, nbwStateComparer);

                foreach (var edge in sysEdges)
                {
                    foreach (var succNbw in nbwSuccsAll)
                    {
                        var succ = GetOrCreate(
                            edge.Target, succNbw, current.Depth + 1, edge.StepFunction, current);
                        current.Successors.Add(
                            new ProductEdge<TNbwState>(edge.StepFunction, succ));
                    }
                }
            }

            //
            // Tarjan SCC + accepting check + (optional) fairness filter.
            //
            var sccs = FindSCCs(allNodes);

            foreach (var scc in sccs)
            {
                if (!scc.HasCycle) continue;
                if (!scc.Nodes.Any(n => nbw.IsAccepting(n.NbwState))) continue;
                if (fairness != null && !ReferenceEquals(fairness, Fairness.None) &&
                    !IsFairProductCycle(scc, fairness))
                    continue;

                var trace = BuildCounterexample(scc, allNodes, nbw);
                trace = TraceInstantiation.AttachValuations(trace, registry);
                var badCycle = StronglyConnectedComponent.FromSystemNodes(
                    scc.Nodes.Select(pn => pn.SystemNode));
                return PropertyCheckingResult.Failure(trace, badCycle);
            }

            return PropertyCheckingResult.Success();
        }

        #region Transition evaluation

        private static HashSet<TNbwState> EvaluateNbwTransitions<TNbwState>(
            IReadOnlyList<TransitionTerm<StateSet<TNbwState>>> transitions,
            IState systemState,
            ConditionRegistry<IStatePredicate> registry,
            IEqualityComparer<TNbwState> cmp)
        {
            var result = new HashSet<TNbwState>(cmp);
            foreach (var term in transitions)
            {
                var leaf = EvaluateTerm(term, systemState, registry);
                if (leaf == null) continue;
                foreach (var s in leaf) result.Add(s);
            }
            return result;
        }

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

        #endregion

        #region Fairness on product SCCs

        /// <summary>
        /// Returns <c>true</c> iff the cycle inhabited by <paramref name="scc"/>
        /// satisfies every required fairness condition. Mirrors
        /// <see cref="Fairness.IsFairCycle"/> but operates on the product
        /// SCC: per-system-node "enabled" is derived from the system node's
        /// outgoing edges (the explorer records an edge only for step
        /// functions whose <c>Apply</c> succeeded), and <c>taken</c> comes
        /// from the step-function annotation on outgoing product edges
        /// that stay inside the SCC.
        /// </summary>
        private static bool IsFairProductCycle<TNbwState>(
            ProductScc<TNbwState> scc, Fairness fairness)
        {
            // Unique system nodes (a product SCC may contain multiple
            // NBW-variants of the same system state; collapse them).
            var systemNodes = new Dictionary<string, StateGraphNode>();
            foreach (var pn in scc.Nodes)
            {
                var fp = pn.SystemNode.GetNodeFingerprint();
                if (!systemNodes.ContainsKey(fp))
                    systemNodes[fp] = pn.SystemNode;
            }

            var sccNodes = new HashSet<ProductNode<TNbwState>>(scc.Nodes);

            IEnumerable<IStepFunction> EnabledAt(StateGraphNode sys)
                => sys.Edges.Select(e => e.StepFunction);

            IEnumerable<IStepFunction> Taken()
            {
                foreach (var n in scc.Nodes)
                    foreach (var pe in n.Successors)
                        if (sccNodes.Contains(pe.Target))
                            yield return pe.StepFunction;
            }

            var analysis = CycleFairness.Compute(systemNodes.Values, EnabledAt, Taken());
            return CycleFairness.IsFair(analysis, fairness);
        }

        #endregion

        #region Tarjan SCC on the product graph

        private static List<ProductScc<TNbwState>> FindSCCs<TNbwState>(
            List<ProductNode<TNbwState>> nodes)
        {
            var result = new List<ProductScc<TNbwState>>();
            var indexOf = new Dictionary<ProductNode<TNbwState>, int>();
            var lowOf = new Dictionary<ProductNode<TNbwState>, int>();
            var onStack = new HashSet<ProductNode<TNbwState>>();
            var stack = new Stack<ProductNode<TNbwState>>();
            int idx = 0;

            // Iterative Tarjan to avoid stack overflow on deep product graphs.
            foreach (var root in nodes)
            {
                if (indexOf.ContainsKey(root)) continue;

                var callStack = new Stack<(ProductNode<TNbwState> node, int i)>();
                callStack.Push((root, 0));
                indexOf[root] = idx;
                lowOf[root] = idx;
                idx++;
                stack.Push(root);
                onStack.Add(root);

                while (callStack.Count > 0)
                {
                    var (node, i) = callStack.Pop();
                    var succs = node.Successors;

                    if (i < succs.Count)
                    {
                        callStack.Push((node, i + 1));
                        var w = succs[i].Target;
                        if (!indexOf.ContainsKey(w))
                        {
                            indexOf[w] = idx;
                            lowOf[w] = idx;
                            idx++;
                            stack.Push(w);
                            onStack.Add(w);
                            callStack.Push((w, 0));
                        }
                        else if (onStack.Contains(w))
                        {
                            lowOf[node] = Math.Min(lowOf[node], indexOf[w]);
                        }
                    }
                    else
                    {
                        // Post-order: propagate lowlink to parent (if any).
                        if (callStack.Count > 0)
                        {
                            var parent = callStack.Peek().node;
                            lowOf[parent] = Math.Min(lowOf[parent], lowOf[node]);
                        }

                        if (lowOf[node] == indexOf[node])
                        {
                            var scc = new ProductScc<TNbwState>();
                            ProductNode<TNbwState> w;
                            do
                            {
                                w = stack.Pop();
                                onStack.Remove(w);
                                scc.Nodes.Add(w);
                            } while (!ReferenceEquals(w, node));

                            scc.HasCycle = scc.Nodes.Count > 1 ||
                                scc.Nodes[0].Successors.Any(
                                    s => ReferenceEquals(s.Target, scc.Nodes[0]));
                            result.Add(scc);
                        }
                    }
                }
            }

            return result;
        }

        #endregion

        #region Counterexample reconstruction

        private static List<TraceItem> BuildCounterexample<TNbwState>(
            ProductScc<TNbwState> scc,
            List<ProductNode<TNbwState>> allNodes,
            SymbolicNBW<IStatePredicate, State, TNbwState> nbw)
        {
            var trace = new List<TraceItem>();
            var cycleEntry = scc.Nodes.FirstOrDefault(n => nbw.IsAccepting(n.NbwState))
                ?? scc.Nodes[0];

            // BFS from initial product nodes to the cycle entry.
            var initial = allNodes.Where(n => n.Depth == 0).ToList();
            var parentOf = new Dictionary<ProductNode<TNbwState>, ProductNode<TNbwState>>();
            var seen = new HashSet<ProductNode<TNbwState>>(initial);
            var q = new Queue<ProductNode<TNbwState>>(initial);
            foreach (var n in initial) parentOf[n] = null;

            ProductNode<TNbwState> hit = null;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (ReferenceEquals(cur, cycleEntry)) { hit = cur; break; }
                foreach (var pe in cur.Successors)
                {
                    if (seen.Add(pe.Target))
                    {
                        parentOf[pe.Target] = cur;
                        q.Enqueue(pe.Target);
                    }
                }
            }
            hit ??= cycleEntry;

            var prefix = new List<ProductNode<TNbwState>>();
            for (var n = hit; n != null; n = parentOf.TryGetValue(n, out var p) ? p : null)
                prefix.Add(n);
            prefix.Reverse();

            foreach (var n in prefix)
                trace.Add(new TraceItem(n.IncomingStepFunction, n.SystemNode, isInCycle: false));

            // Walk one lap around the SCC. Prefer unvisited successors; close with cycleEntry.
            var sccSet = new HashSet<ProductNode<TNbwState>>(scc.Nodes);
            var visitedInCycle = new HashSet<ProductNode<TNbwState>> { cycleEntry };
            var node2 = cycleEntry;
            for (int i = 0; i < scc.Nodes.Count; i++)
            {
                var fresh = node2.Successors.FirstOrDefault(
                    e => sccSet.Contains(e.Target) && visitedInCycle.Add(e.Target));
                if (fresh != null)
                {
                    trace.Add(new TraceItem(fresh.StepFunction, fresh.Target.SystemNode, isInCycle: true));
                    node2 = fresh.Target;
                    continue;
                }
                // Try to close the cycle.
                var closer = node2.Successors.FirstOrDefault(
                    e => ReferenceEquals(e.Target, cycleEntry));
                if (closer != null)
                {
                    trace.Add(new TraceItem(closer.StepFunction, closer.Target.SystemNode, isInCycle: true));
                }
                break;
            }

            return trace;
        }

        #endregion

        #region Internal types

        private sealed class ProductNode<TNbwState>
        {
            public StateGraphNode SystemNode { get; }
            public TNbwState NbwState { get; }
            public string SysFingerprint { get; }
            public int Depth { get; }
            public IStepFunction IncomingStepFunction { get; }
            public ProductNode<TNbwState> Parent { get; }
            public List<ProductEdge<TNbwState>> Successors { get; } =
                new List<ProductEdge<TNbwState>>();

            public ProductNode(
                StateGraphNode systemNode,
                TNbwState nbwState,
                string sysFingerprint,
                int depth,
                IStepFunction incomingStepFunction,
                ProductNode<TNbwState> parent)
            {
                SystemNode = systemNode;
                NbwState = nbwState;
                SysFingerprint = sysFingerprint;
                Depth = depth;
                IncomingStepFunction = incomingStepFunction;
                Parent = parent;
            }
        }

        private sealed class ProductEdge<TNbwState>
        {
            public IStepFunction StepFunction { get; }
            public ProductNode<TNbwState> Target { get; }

            public ProductEdge(IStepFunction stepFunction, ProductNode<TNbwState> target)
            {
                StepFunction = stepFunction;
                Target = target;
            }
        }

        private sealed class ProductScc<TNbwState>
        {
            public List<ProductNode<TNbwState>> Nodes { get; } =
                new List<ProductNode<TNbwState>>();
            public bool HasCycle { get; set; }
        }

        #endregion
    }
}
