using Microsoft.Accordant;

namespace Microsoft.Accordant.ModelChecking.Ltl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Sentinel step function representing implicit stutter at a deadlocked
    /// (no-outgoing-edge) system node. The explicit-LTL backend injects this
    /// in <see cref="LtlCheck.BuildProductGraph"/> so that terminal nodes are
    /// treated as self-looping under the standard LTL convention that
    /// paths are infinite. This brings the explicit backend into semantic
    /// agreement with the symbolic backends (which already stutter at
    /// terminals — see <see cref="Symbolic.SymbolicLtlCheck"/> line ~153 and
    /// <see cref="Symbolic.NestedDfsCheck"/> line ~95).
    /// </summary>
    public sealed class StutterStep : IStepFunction
    {
        public static readonly StutterStep Instance = new StutterStep();
        private StutterStep() { }
        public string StepFunctionId => "<stutter>";
        public IList<StepResult> Apply(IState state, IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            => null;
    }

    /// <summary>
    /// A node in the product graph: (SystemState, LtlFormula).
    /// Used for on-the-fly LTL model checking with derivatives.
    /// </summary>
    public class ProductNode
    {
        public StateGraphNode SystemNode { get; }
        public LtlFormula Formula { get; }
        public List<ProductEdge> Edges { get; } = new List<ProductEdge>();

        private readonly string _fingerprint;

        public ProductNode(StateGraphNode systemNode, LtlFormula formula)
        {
            SystemNode = systemNode;
            Formula = formula;
            // Use canonical ToString() for structural fingerprint (not just hash code).
            // NOTE: ToString-based fingerprinting means syntactically distinct but
            // semantically equivalent derivatives are NOT deduplicated. This is the
            // dominant scalability limit of the explicit checker; see
            // Microsoft.Accordant.ModelChecking/Symbolic/SCALABILITY.md for details.
            _fingerprint = $"{systemNode.GetNodeFingerprint()}|{formula}";
        }

        public string GetFingerprint() => _fingerprint;

        public override string ToString() => $"({SystemNode.State}, {Formula})";
    }

    /// <summary>
    /// An edge in the product graph.
    /// </summary>
    public class ProductEdge
    {
        public ProductNode Target { get; }
        public IStepFunction StepFunction { get; }

        public ProductEdge(ProductNode target, IStepFunction stepFunction)
        {
            Target = target;
            StepFunction = stepFunction;
        }
    }

    /// <summary>
    /// SCC in the product graph, wrapping the formula information.
    /// </summary>
    public class ProductSCC
    {
        public List<ProductNode> Nodes { get; } = new List<ProductNode>();
        public bool HasCycle { get; internal set; }

        /// <summary>
        /// Returns all distinct Until formulas that are "active" (still waiting) in any node of this SCC.
        /// </summary>
        public IEnumerable<LtlUntil> GetActiveUntilFormulas()
        {
            return Nodes
                .SelectMany(n => GetActiveUntils(n.Formula))
                .Distinct();
        }

        private static IEnumerable<LtlUntil> GetActiveUntils(LtlFormula formula)
        {
            // An Until is "active" if it appears in the formula (not yet discharged)
            return formula.GetUntilSubformulas();
        }
    }

    /// <summary>
    /// LTL model checker using derivative-based on-the-fly construction.
    /// Integrates with the existing SCC-based checking infrastructure.
    /// </summary>
    public static class LtlCheck
    {
        /// <summary>
        /// Check an arbitrary LTL formula over a state graph.
        /// 
        /// This is the main entry point for full LTL model checking.
        /// Uses derivative-based on-the-fly product construction with SCC analysis.
        /// </summary>
        /// <param name="root">The root of the system state graph.</param>
        /// <param name="formula">The LTL formula to check.</param>
        /// <param name="fairness">Fairness constraints (default: weak fairness on all).</param>
        /// <returns>Result indicating success or failure with counterexample.</returns>
        public static PropertyCheckingResult Check(
            StateGraphNode root,
            LtlFormula formula,
            Fairness fairness = null)
        {
            fairness ??= Fairness.WeakFairAll;

            // Build the product graph on-the-fly
            var (productRoot, allNodes) = BuildProductGraph(root, formula);

            // If the initial formula is already false, fail immediately
            if (formula.IsFalse)
            {
                return PropertyCheckingResult.Failure(new List<TraceItem>
                {
                    new TraceItem(null, root, isInCycle: false)
                });
            }

            // Safety violation detection: any reachable (sys, False) sink node
            // indicates a finite-trace counterexample. BuildProductGraph routes
            // edges whose derivative becomes False to such sinks (rather than
            // silently dropping them) so this BFS will find them.
            var safetyTrace = FindSafetyCounterexample(productRoot);
            if (safetyTrace != null)
            {
                return PropertyCheckingResult.Failure(safetyTrace);
            }

            // Find SCCs in the product graph
            var productSCCs = FindProductSCCs(productRoot, allNodes);

            // Check each SCC for "bad" cycles
            foreach (var scc in productSCCs)
            {
                if (!scc.HasCycle) continue;

                var badSub = FindBadFairSubCycle(scc, fairness);
                if (badSub != null)
                {
                    // Found a counterexample
                    var trace = BuildCounterexampleTrace(productRoot, badSub, allNodes);
                    var systemSCC = ExtractSystemSCC(badSub);
                    return PropertyCheckingResult.Failure(trace, systemSCC);
                }
            }

            return PropertyCheckingResult.Success();
        }

        /// <summary>
        /// Builds the product graph (System × Formula) on-the-fly using derivatives.
        /// </summary>
        private static (ProductNode root, Dictionary<string, ProductNode> allNodes) BuildProductGraph(
            StateGraphNode systemRoot,
            LtlFormula initialFormula)
        {
            var allNodes = new Dictionary<string, ProductNode>();
            var queue = new Queue<ProductNode>();

            var root = new ProductNode(systemRoot, initialFormula);
            allNodes[root.GetFingerprint()] = root;
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                // Standard LTL convention: paths are infinite. If the system
                // node has no outgoing edges, inject an implicit stutter
                // self-loop so the property is evaluated against an infinite
                // run rather than dropped silently. This matches the symbolic
                // backends' terminal-stutter handling.
                if (current.SystemNode.Edges == null || current.SystemNode.Edges.Count == 0)
                {
                    var derivedFormula = current.Formula.Derivative(current.SystemNode.State);

                    if (derivedFormula.IsFalse)
                    {
                        var rejectSink = new ProductNode(current.SystemNode, derivedFormula);
                        var rejectFp = rejectSink.GetFingerprint();
                        if (!allNodes.TryGetValue(rejectFp, out var existingReject))
                        {
                            allNodes[rejectFp] = rejectSink;
                            existingReject = rejectSink;
                        }
                        current.Edges.Add(new ProductEdge(existingReject, StutterStep.Instance));
                        continue;
                    }

                    var stutterTarget = new ProductNode(current.SystemNode, derivedFormula);
                    var stutterFp = stutterTarget.GetFingerprint();
                    if (!allNodes.TryGetValue(stutterFp, out var existingStutter))
                    {
                        allNodes[stutterFp] = stutterTarget;
                        queue.Enqueue(stutterTarget);
                        existingStutter = stutterTarget;
                    }
                    current.Edges.Add(new ProductEdge(existingStutter, StutterStep.Instance));
                    continue;
                }

                foreach (var edge in current.SystemNode.Edges)
                {
                    // Compute derivative: what formula remains after seeing this state?
                    var derivedFormula = current.Formula.Derivative(current.SystemNode.State);

                    if (derivedFormula.IsFalse)
                    {
                        // Safety violation: this transition would force a False
                        // continuation. Route to a reject sink (one per target
                        // system state) so the violation is preserved for the
                        // safety counterexample search instead of being dropped.
                        var rejectSink = new ProductNode(edge.Target, derivedFormula);
                        var rejectFp = rejectSink.GetFingerprint();
                        if (!allNodes.TryGetValue(rejectFp, out var existingReject))
                        {
                            allNodes[rejectFp] = rejectSink;
                            existingReject = rejectSink;
                            // Do NOT enqueue: a (sys, False) node is a terminal sink.
                        }
                        current.Edges.Add(new ProductEdge(existingReject, edge.StepFunction));
                        continue;
                    }

                    var successor = new ProductNode(edge.Target, derivedFormula);
                    var successorFp = successor.GetFingerprint();

                    if (!allNodes.TryGetValue(successorFp, out var existingNode))
                    {
                        allNodes[successorFp] = successor;
                        queue.Enqueue(successor);
                        existingNode = successor;
                    }

                    current.Edges.Add(new ProductEdge(existingNode, edge.StepFunction));
                }
            }

            return (root, allNodes);
        }

        /// <summary>
        /// BFS from the product root looking for any reachable (sys, False)
        /// sink node, which encodes a safety violation: no infinite extension
        /// can satisfy the formula past that transition. Returns the trace from
        /// root to the sink (inclusive), or null if no safety violation is
        /// reachable.
        /// </summary>
        private static List<TraceItem> FindSafetyCounterexample(ProductNode productRoot)
        {
            var visited = new Dictionary<string, (ProductNode parent, IStepFunction step)>();
            visited[productRoot.GetFingerprint()] = (null, null);
            var queue = new Queue<ProductNode>();
            queue.Enqueue(productRoot);

            ProductNode rejectNode = null;
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (n.Formula.IsFalse && !ReferenceEquals(n, productRoot))
                {
                    rejectNode = n;
                    break;
                }
                foreach (var e in n.Edges)
                {
                    var tid = e.Target.GetFingerprint();
                    if (!visited.ContainsKey(tid))
                    {
                        visited[tid] = (n, e.StepFunction);
                        queue.Enqueue(e.Target);
                    }
                }
            }

            if (rejectNode == null) return null;

            // Reconstruct path root → ... → rejectNode
            var path = new List<(ProductNode node, IStepFunction step)>();
            var cur = rejectNode;
            while (cur != null)
            {
                var (parent, step) = visited[cur.GetFingerprint()];
                path.Add((cur, step));
                cur = parent;
            }
            path.Reverse();

            var trace = new List<TraceItem>();
            foreach (var (node, step) in path)
            {
                trace.Add(new TraceItem(step, node.SystemNode, isInCycle: false));
            }
            return trace;
        }

        /// <summary>
        /// Find SCCs in the product graph using Tarjan's algorithm.
        /// </summary>
        private static List<ProductSCC> FindProductSCCs(
            ProductNode root,
            Dictionary<string, ProductNode> allNodes)
        {
            var result = new List<ProductSCC>();
            var indexMap = new Dictionary<string, int>();
            var lowLinkMap = new Dictionary<string, int>();
            var onStack = new HashSet<string>();
            var stack = new Stack<ProductNode>();
            int index = 0;

            void StrongConnect(ProductNode node)
            {
                var nodeId = node.GetFingerprint();
                indexMap[nodeId] = index;
                lowLinkMap[nodeId] = index;
                index++;
                stack.Push(node);
                onStack.Add(nodeId);

                foreach (var edge in node.Edges)
                {
                    var successor = edge.Target;
                    var successorId = successor.GetFingerprint();

                    if (!indexMap.ContainsKey(successorId))
                    {
                        StrongConnect(successor);
                        lowLinkMap[nodeId] = Math.Min(lowLinkMap[nodeId], lowLinkMap[successorId]);
                    }
                    else if (onStack.Contains(successorId))
                    {
                        lowLinkMap[nodeId] = Math.Min(lowLinkMap[nodeId], indexMap[successorId]);
                    }
                }

                if (lowLinkMap[nodeId] == indexMap[nodeId])
                {
                    var scc = new ProductSCC();
                    ProductNode w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w.GetFingerprint());
                        scc.Nodes.Add(w);
                    } while (w.GetFingerprint() != nodeId);

                    scc.HasCycle = DetermineHasCycle(scc);
                    result.Add(scc);
                }
            }

            StrongConnect(root);
            return result;
        }

        private static bool DetermineHasCycle(ProductSCC scc)
        {
            if (scc.Nodes.Count > 1)
                return true;

            if (scc.Nodes.Count == 1)
            {
                var node = scc.Nodes[0];
                var nodeId = node.GetFingerprint();
                return node.Edges.Any(e => e.Target.GetFingerprint() == nodeId);
            }

            return false;
        }

        /// <summary>
        /// Check if an SCC is a "bad" cycle (violates the LTL formula).
        /// A cycle is bad if some Until obligation is never discharged.
        /// 
        /// Key insight: An Until (φ U ψ) represents an obligation that ψ must eventually hold.
        /// But if the Until never becomes "active" (i.e., we're never in a state where we're
        /// waiting for ψ), then it's not a violation.
        /// </summary>
        /// <summary>
        /// Find a "bad" sub-cycle of <paramref name="scc"/> that simultaneously
        /// (a) witnesses an undischarged Until obligation and
        /// (b) is fair with respect to <paramref name="fairness"/>.
        /// Returns the offending sub-SCC, or <c>null</c> if no such cycle exists.
        ///
        /// Standard LTL acceptance check: for each Until (φ U ψ) in the SCC,
        /// project to the subset of nodes where ψ does NOT yet hold; if that
        /// subgraph contains a non-trivial SCC, the Until is never discharged
        /// along an infinite path through it. We additionally require the
        /// witnessing sub-cycle to satisfy the system-level fairness constraint.
        /// </summary>
        private static ProductSCC FindBadFairSubCycle(ProductSCC scc, Fairness fairness)
        {
            var allUntils = scc.GetActiveUntilFormulas().ToList();

            foreach (var until in allUntils)
            {
                var noGoal = scc.Nodes
                    .Where(n => !until.Goal.Derivative(n.SystemNode.State).IsTrue)
                    .ToList();

                if (noGoal.Count == 0)
                    continue; // ψ holds in every SCC state → Until always discharged

                bool untilActiveSomewhere = noGoal.Any(n =>
                    ContainsActiveUntil(n.Formula, until, n.SystemNode.State));

                if (!untilActiveSomewhere)
                    continue; // pending Until is dormant everywhere ψ fails — vacuous

                foreach (var subScc in FindSubSCCsWithCycle(noGoal))
                {
                    if (IsFairCycle(subScc, fairness))
                        return subScc;
                }
            }

            return null;
        }

        /// <summary>
        /// Enumerate all non-trivial SCCs (size &gt;1, or a single node with a
        /// self-loop) of the subgraph induced by <paramref name="subset"/>,
        /// considering only edges whose targets are also in the subset.
        /// Uses an iterative Tarjan to avoid deep-recursion stack overflows.
        /// </summary>
        private static IEnumerable<ProductSCC> FindSubSCCsWithCycle(IReadOnlyList<ProductNode> subset)
        {
            var results = new List<ProductSCC>();
            if (subset.Count == 0) return results;
            var ids = new HashSet<string>(subset.Select(n => n.GetFingerprint()));

            var indexMap = new Dictionary<string, int>();
            var lowLink = new Dictionary<string, int>();
            var onStack = new HashSet<string>();
            var sccStack = new Stack<ProductNode>();
            int index = 0;

            foreach (var start in subset)
            {
                if (indexMap.ContainsKey(start.GetFingerprint())) continue;

                var work = new Stack<(ProductNode node, IEnumerator<ProductEdge> it)>();

                void Push(ProductNode n)
                {
                    var nid = n.GetFingerprint();
                    indexMap[nid] = index;
                    lowLink[nid] = index;
                    index++;
                    sccStack.Push(n);
                    onStack.Add(nid);
                    var edges = n.Edges
                        .Where(e => ids.Contains(e.Target.GetFingerprint()))
                        .GetEnumerator();
                    work.Push((n, edges));
                }

                Push(start);

                while (work.Count > 0)
                {
                    var (node, it) = work.Peek();
                    var nid = node.GetFingerprint();

                    if (it.MoveNext())
                    {
                        var target = it.Current.Target;
                        var tid = target.GetFingerprint();
                        if (!indexMap.ContainsKey(tid))
                        {
                            Push(target);
                        }
                        else if (onStack.Contains(tid))
                        {
                            lowLink[nid] = Math.Min(lowLink[nid], indexMap[tid]);
                        }
                    }
                    else
                    {
                        work.Pop();
                        if (work.Count > 0)
                        {
                            var parentId = work.Peek().node.GetFingerprint();
                            lowLink[parentId] = Math.Min(lowLink[parentId], lowLink[nid]);
                        }

                        if (lowLink[nid] == indexMap[nid])
                        {
                            var members = new List<ProductNode>();
                            ProductNode w;
                            do
                            {
                                w = sccStack.Pop();
                                onStack.Remove(w.GetFingerprint());
                                members.Add(w);
                            } while (w.GetFingerprint() != nid);

                            bool hasCycle = members.Count > 1
                                || node.Edges.Any(e =>
                                    ids.Contains(e.Target.GetFingerprint()) &&
                                    e.Target.GetFingerprint() == nid);

                            if (hasCycle)
                            {
                                var sub = new ProductSCC();
                                foreach (var m in members) sub.Nodes.Add(m);
                                sub.HasCycle = true;
                                results.Add(sub);
                            }
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Check if a specific Until formula is "actively pending" in the given formula.
        /// An Until is pending if it appears at a position where it could actually fail
        /// (not inside an Or where another branch is already satisfied at this state).
        /// </summary>
        private static bool ContainsActiveUntil(LtlFormula formula, LtlUntil targetUntil, IState state)
        {
            // Base cases
            if (formula.IsTrue || formula.IsFalse)
                return false;

            if (formula.Equals(targetUntil))
                return true;

            // Recursive cases
            if (formula is LtlAnd and)
            {
                // Until is active in And if it's active in ANY child
                return and.Children.Any(c => ContainsActiveUntil(c, targetUntil, state));
            }

            if (formula is LtlOr or)
            {
                // Until is active in Or only if we can't satisfy the Or without the Until.
                // Check if any non-Until-containing branch evaluates to true.
                foreach (var child in or.Children)
                {
                    // Check if this child can satisfy the Or without needing the Until
                    if (!ContainsUntil(child, targetUntil))
                    {
                        // This child doesn't contain the Until - check if it evaluates to true
                        var childDeriv = child.Derivative(state);
                        if (childDeriv.IsTrue)
                        {
                            // This branch satisfies the Or, so Until is dormant
                            return false;
                        }
                    }
                }
                // All non-Until branches are false, so Until is active
                return or.Children.Any(c => ContainsActiveUntil(c, targetUntil, state));
            }

            if (formula is LtlNot not)
                return ContainsActiveUntil(not.Inner, targetUntil, state);

            if (formula is LtlNext next)
                return ContainsActiveUntil(next.Inner, targetUntil, state);

            if (formula is LtlUntil until)
            {
                if (formula.Equals(targetUntil))
                    return true;
                return ContainsActiveUntil(until.Hold, targetUntil, state) ||
                       ContainsActiveUntil(until.Goal, targetUntil, state);
            }

            if (formula is LtlRelease release)
            {
                return ContainsActiveUntil(release.Release_, targetUntil, state) ||
                       ContainsActiveUntil(release.Hold, targetUntil, state);
            }

            return false;
        }

        /// <summary>
        /// Check if a formula structurally contains a specific Until (without state evaluation).
        /// </summary>
        private static bool ContainsUntil(LtlFormula formula, LtlUntil targetUntil)
        {
            if (formula.IsTrue || formula.IsFalse)
                return false;

            if (formula.Equals(targetUntil))
                return true;

            if (formula is LtlAnd and)
                return and.Children.Any(c => ContainsUntil(c, targetUntil));

            if (formula is LtlOr or)
                return or.Children.Any(c => ContainsUntil(c, targetUntil));

            if (formula is LtlNot not)
                return ContainsUntil(not.Inner, targetUntil);

            if (formula is LtlNext next)
                return ContainsUntil(next.Inner, targetUntil);

            if (formula is LtlUntil until)
            {
                if (formula.Equals(targetUntil))
                    return true;
                return ContainsUntil(until.Hold, targetUntil) ||
                       ContainsUntil(until.Goal, targetUntil);
            }

            if (formula is LtlRelease release)
            {
                return ContainsUntil(release.Release_, targetUntil) ||
                       ContainsUntil(release.Hold, targetUntil);
            }

            return false;
        }

        /// <summary>
        /// Check whether a product SCC corresponds to a fair cycle under
        /// system-level fairness constraints.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The projected run through the product SCC induces a system
        /// trace that visits exactly the distinct system states underlying
        /// the SCC's product nodes. Fairness is a property of that
        /// projected system trace, so:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>
        ///     <b>enabled</b> at a visited system state s is the set of
        ///     step functions appearing on s's <em>system</em> outgoing
        ///     edges (system-level enablement; independent of the NBW
        ///     component).
        ///   </description></item>
        ///   <item><description>
        ///     <b>taken</b> in the cycle is the set of step functions on
        ///     <em>product</em> edges whose source and target both lie in
        ///     the product SCC. Using system edges here over-counts:
        ///     a system edge s→s' may exist but no product edge between
        ///     formula-variants of s and s' may stay within the SCC, so
        ///     that step is never actually taken by the projected run.
        ///     The previous implementation extracted a system-only SCC
        ///     and used <see cref="Fairness.IsFairCycle"/>, which made
        ///     exactly this over-counting error and could classify
        ///     genuinely unfair product cycles as fair, leading to
        ///     spurious counterexamples on LTL liveness properties.
        ///   </description></item>
        /// </list>
        /// </remarks>
        /// <summary>
        /// Public for testability. Internal callers (the SCC pass above)
        /// invoke this directly.
        /// </summary>
        public static bool IsFairCycle(ProductSCC productSCC, Fairness fairness)
        {
            // Unique system nodes visited by the product SCC.
            var systemNodes = new Dictionary<string, StateGraphNode>();
            foreach (var pn in productSCC.Nodes)
            {
                var fp = pn.SystemNode.GetNodeFingerprint();
                if (!systemNodes.ContainsKey(fp))
                    systemNodes[fp] = pn.SystemNode;
            }

            // "enabled at group" comes from system edges (system-level
            // enablement). "taken" comes from product edges whose source
            // AND target are both inside the product SCC.
            var productSccFps = new HashSet<string>(productSCC.Nodes.Select(n => n.GetFingerprint()));

            IEnumerable<IStepFunction> EnabledAt(StateGraphNode sys)
                => sys.Edges.Select(e => e.StepFunction);

            IEnumerable<IStepFunction> Taken()
            {
                foreach (var pn in productSCC.Nodes)
                    foreach (var edge in pn.Edges)
                        if (productSccFps.Contains(edge.Target.GetFingerprint()))
                            yield return edge.StepFunction;
            }

            var analysis = CycleFairness.Compute(systemNodes.Values, EnabledAt, Taken());
            return CycleFairness.IsFair(analysis, fairness);
        }

        /// <summary>
        /// Project a product SCC to its underlying system-level SCC.
        /// Used purely for reporting (attaching the SCC to a
        /// <see cref="PropertyCheckingResult"/>); fairness is checked
        /// against the product SCC directly to avoid the over-counting
        /// trap described on <see cref="IsFairCycle(ProductSCC, Fairness)"/>.
        /// </summary>
        private static StronglyConnectedComponent ExtractSystemSCC(ProductSCC productSCC)
        {
            var systemSCC = new StronglyConnectedComponent();

            var seenSystemNodes = new HashSet<string>();
            foreach (var productNode in productSCC.Nodes)
            {
                var systemFp = productNode.SystemNode.GetNodeFingerprint();
                if (seenSystemNodes.Add(systemFp))
                {
                    systemSCC.Nodes.Add(productNode.SystemNode);
                }
            }

            systemSCC.GetType()
                .GetProperty(nameof(StronglyConnectedComponent.HasCycle))
                ?.SetValue(systemSCC, productSCC.HasCycle);

            return systemSCC;
        }

        /// <summary>
        /// Build a counterexample trace from the root to the bad cycle.
        /// </summary>
        private static List<TraceItem> BuildCounterexampleTrace(
            ProductNode root,
            ProductSCC badSCC,
            Dictionary<string, ProductNode> allNodes)
        {
            var trace = new List<TraceItem>();
            var sccNodeIds = new HashSet<string>(badSCC.Nodes.Select(n => n.GetFingerprint()));

            // BFS to find path from root to SCC
            var visited = new Dictionary<string, (ProductNode parent, IStepFunction step)>();
            var queue = new Queue<ProductNode>();

            visited[root.GetFingerprint()] = (null, null);
            queue.Enqueue(root);

            ProductNode entryNode = null;

            while (queue.Count > 0 && entryNode == null)
            {
                var node = queue.Dequeue();
                var nodeId = node.GetFingerprint();

                if (sccNodeIds.Contains(nodeId))
                {
                    entryNode = node;
                    break;
                }

                foreach (var edge in node.Edges)
                {
                    var successorId = edge.Target.GetFingerprint();
                    if (!visited.ContainsKey(successorId))
                    {
                        visited[successorId] = (node, edge.StepFunction);
                        queue.Enqueue(edge.Target);
                    }
                }
            }

            if (entryNode == null)
                return trace;

            // Reconstruct path from root to entry node
            var pathNodes = new List<(ProductNode node, IStepFunction step)>();
            var current = entryNode;
            while (current != null)
            {
                var currentId = current.GetFingerprint();
                var (parent, step) = visited[currentId];
                pathNodes.Add((current, step));
                current = parent;
            }

            pathNodes.Reverse();

            // Add path to trace (not in cycle)
            foreach (var (node, step) in pathNodes)
            {
                trace.Add(new TraceItem(step, node.SystemNode, isInCycle: false));
            }

            // Add cycle portion
            var cycleNode = entryNode;
            var cycleVisited = new HashSet<string> { cycleNode.GetFingerprint() };

            for (int i = 0; i < Math.Min(badSCC.Nodes.Count, 5); i++)
            {
                foreach (var edge in cycleNode.Edges)
                {
                    var edgeTargetId = edge.Target.GetFingerprint();
                    if (sccNodeIds.Contains(edgeTargetId) && !cycleVisited.Contains(edgeTargetId))
                    {
                        trace.Add(new TraceItem(edge.StepFunction, edge.Target.SystemNode, isInCycle: true));
                        cycleVisited.Add(edgeTargetId);
                        cycleNode = edge.Target;
                        break;
                    }
                }
            }

            return trace;
        }

        #region Convenience Methods (Mapping to Existing Interface)

        /// <summary>
        /// Check that a property always holds in every reachable state.
        /// Equivalent to Check.Always() but using LTL infrastructure.
        /// 
        /// LTL formula: □P (G P)
        /// </summary>
        public static PropertyCheckingResult Always(
            StateGraphNode root,
            Func<IState, bool> predicate)
        {
            var p = LtlFormula.Prop(predicate, "P");
            return Check(root, LtlFormula.Always(p), Fairness.None);
        }

        /// <summary>
        /// Check that a property eventually becomes true on all paths.
        /// Equivalent to Check.Eventually() but using LTL infrastructure.
        /// 
        /// LTL formula: ◇P (F P)
        /// </summary>
        public static PropertyCheckingResult Eventually(
            StateGraphNode root,
            Func<IState, bool> predicate,
            Fairness fairness = null)
        {
            var p = LtlFormula.Prop(predicate, "P");
            return Check(root, LtlFormula.Eventually(p), fairness);
        }

        /// <summary>
        /// Check that a property holds infinitely often.
        /// Equivalent to Check.InfinitelyOften() but using LTL infrastructure.
        /// 
        /// LTL formula: □◇P (GF P)
        /// </summary>
        public static PropertyCheckingResult InfinitelyOften(
            StateGraphNode root,
            Func<IState, bool> predicate,
            Fairness fairness = null)
        {
            var p = LtlFormula.Prop(predicate, "P");
            return Check(root, LtlFormula.InfinitelyOften(p), fairness);
        }

        /// <summary>
        /// Leads-to (response) property: whenever P holds, Q eventually holds.
        /// Equivalent to Check.LeadsTo() but using LTL infrastructure.
        /// 
        /// LTL formula: □(P → ◇Q)
        /// </summary>
        public static PropertyCheckingResult LeadsTo(
            StateGraphNode root,
            Func<IState, bool> trigger,
            Func<IState, bool> response,
            Fairness fairness = null)
        {
            var p = LtlFormula.Prop(trigger, "P");
            var q = LtlFormula.Prop(response, "Q");
            return Check(root, LtlFormula.LeadsTo(p, q), fairness);
        }

        /// <summary>
        /// Check that a property eventually stabilizes (becomes true and stays true).
        /// Equivalent to Check.Stabilizes() but using LTL infrastructure.
        /// 
        /// LTL formula: ◇□P (FG P)
        /// </summary>
        public static PropertyCheckingResult Stabilizes(
            StateGraphNode root,
            Func<IState, bool> predicate,
            Fairness fairness = null)
        {
            var p = LtlFormula.Prop(predicate, "P");
            return Check(root, LtlFormula.Stabilizes(p), fairness);
        }

        #endregion
    }
}
