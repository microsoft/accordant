namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;


    /// <summary>
    /// End-to-end model-program × RLTL benchmark driver. Composes a model
    /// program's state graph (<see cref="StateGraphNode"/>) with the
    /// alternation-eliminated NBW for the negation of an RLTL property and
    /// runs nested DFS / SCC emptiness, returning a verdict together with
    /// instrumentation suitable for comparing
    /// <see cref="Mode.Lazy"/> (the default <see cref="SymbolicRltlCheck"/>
    /// path, where NBW states are only materialised as the product
    /// emptiness check requests them) against
    /// <see cref="Mode.Eager"/> (force the full reachable NBW first,
    /// then run the same emptiness check).
    ///
    /// <para>This is the driver that backs paper §6.2 end-to-end
    /// measurements — the missing complement to the standalone NBW
    /// microbenchmarks in <c>NbwProductBenchmarkTests</c>.</para>
    /// </summary>
    public static class EndToEndCheck
    {
        /// <summary>NBW construction strategy.</summary>
        public enum Mode
        {
            /// <summary>Default path: lazy NBW expansion driven by product
            /// emptiness exploration (matches <see cref="SymbolicRltlCheck"/>).
            /// The NBW footprint after the check reflects exactly the
            /// fragment touched by the product search.</summary>
            Lazy,

            /// <summary>Force the complete reachable NBW to be materialised
            /// before the product search. Measures the cost of building the
            /// property automaton in isolation, regardless of whether the
            /// model program ever reaches a state where it matters.</summary>
            Eager,
        }

        /// <summary>Per-run report.</summary>
        public sealed class Report
        {
            /// <summary>Mode that produced this report.</summary>
            public Mode Mode { get; internal set; }
            /// <summary>True iff the property holds (no counterexample).</summary>
            public bool Valid { get; internal set; }
            /// <summary>Length of the counterexample lasso (prefix+cycle), or 0.</summary>
            public int CounterexampleLength { get; internal set; }

            /// <summary>Total reachable states in the model program's state
            /// graph (BFS from <see cref="StateGraphNode"/> root over
            /// <c>Edges</c>).</summary>
            public int ModelStates { get; internal set; }
            /// <summary>Initial NBW states.</summary>
            public int NbwInitialStates { get; internal set; }
            /// <summary>NBW states discovered by end of the run. In
            /// <see cref="Mode.Lazy"/>, this is the product-exploration
            /// footprint; in <see cref="Mode.Eager"/>, this is the full
            /// reachable NBW.</summary>
            public int NbwStatesDiscovered { get; internal set; }
            /// <summary>NBW transitions cached by end of the run.</summary>
            public int NbwTransitionsCached { get; internal set; }
            /// <summary>NBW states reachable in total (only set for
            /// <see cref="Mode.Eager"/>, otherwise null).</summary>
            public int? NbwStatesReachableTotal { get; internal set; }

            /// <summary>Wall-clock duration of the construction + check.</summary>
            public TimeSpan Elapsed { get; internal set; }
            /// <summary>Time spent in the eager NBW closure phase (Eager only).</summary>
            public TimeSpan EagerClosureElapsed { get; internal set; }

            /// <summary>Human-readable single-line summary.</summary>
            public string OneLine()
            {
                var verdict = Valid ? "OK    " : "VIOL  ";
                var lazyTag = NbwStatesReachableTotal.HasValue
                    ? string.Format(
                        "{0,5}/{1,-5}",
                        NbwStatesDiscovered,
                        NbwStatesReachableTotal.Value)
                    : string.Format("{0,5}      ", NbwStatesDiscovered);
                return string.Format(
                    "{0,-6} {1,-5}  M={2,5}  NBW(disc/tot)={3}  trans={4,5}  t={5,7:0.0}ms",
                    Mode, verdict, ModelStates,
                    lazyTag, NbwTransitionsCached, Elapsed.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Run the end-to-end check.
        /// </summary>
        /// <param name="root">Root of the (already explored) model state graph.</param>
        /// <param name="property">RLTL property φ that should hold (the
        ///   driver checks emptiness of model × NBW(¬φ)).</param>
        /// <param name="mode">Lazy or Eager NBW expansion strategy.</param>
        /// <param name="maxDepth">Optional bound on product exploration
        ///   depth (0 = unlimited).</param>
        /// <param name="fairness">Optional fairness; when non-null, SCC
        ///   emptiness is used instead of nested DFS.</param>
        /// <param name="eagerAntimirov">Forwarded to
        ///   <see cref="IncrementalAE{TPredicate,TElement,TState}"/>.
        ///   Default <c>false</c> uses the DnfLeaves form (union of clauses
        ///   at each BDD leaf), which is the cheaper general-purpose
        ///   normalisation and is dramatically faster on conjunctive
        ///   recurrence properties (paper §6.2). Pass <c>true</c> for the
        ///   legacy eager Antimirov form (ablation studies).</param>
        public static Report Run(
            StateGraphNode root,
            Rltl<IStatePredicate> property,
            Mode mode = Mode.Lazy,
            int maxDepth = 0,
            Fairness fairness = null,
            bool eagerAntimirov = false)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (property == null) throw new ArgumentNullException(nameof(property));

            var report = new Report { Mode = mode };
            report.ModelStates = CountReachableModelStates(root);

            var eba = StatePropEbaProvider.Default;
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);

            var algebra = RltlAlgebra.Default;

            // Build NBW(¬φ) — same construction as SymbolicRltlCheck with
            // default knobs (no dedup / minimisation), so reported numbers
            // are baseline. Knob-on variants are a follow-up benchmark.
            var sw = Stopwatch.StartNew();
            var negPhi = algebra.Not(property);
            var derivative = new RltlDerivative<IStatePredicate, State>(
                eba, registry, null, null);
            var abw = derivative.ToABW(negPhi);
            var incAE = new IncrementalAE<IStatePredicate, State, Rltl<IStatePredicate>>(
                abw, null, null, eagerAntimirov);
            var nbw = incAE.ToNBW();

            report.NbwInitialStates = nbw.InitialStates.Count;

            if (mode == Mode.Eager)
            {
                var swEager = Stopwatch.StartNew();
                ForceMaterialise(nbw);
                swEager.Stop();
                report.EagerClosureElapsed = swEager.Elapsed;
                report.NbwStatesReachableTotal = nbw.States.Count;
            }

            var bpComparer = BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer();
            bool useSCC = fairness != null && !ReferenceEquals(fairness, Fairness.None);
            var result = useSCC
                ? SccProductCheck.Check(root, nbw, maxDepth, bpComparer, fairness)
                : NestedDfsCheck.Check(root, nbw, maxDepth, bpComparer);
            sw.Stop();

            report.Elapsed = sw.Elapsed;
            report.NbwStatesDiscovered = nbw.States.Count;
            report.NbwTransitionsCached = nbw.CachedTransitions.Count;
            report.Valid = result.Valid;
            report.CounterexampleLength = result.Trace == null
                ? 0
                : result.Trace.Count;
            return report;
        }

        /// <summary>
        /// BFS over <see cref="StateGraphNode.Edges"/> from a root, counting
        /// distinct reachable nodes by reference.
        /// </summary>
        private static int CountReachableModelStates(StateGraphNode root)
        {
            // StateGraphNode does not override Equals, so the default
            // comparer is reference equality — which is what we want.
            var seen = new HashSet<StateGraphNode>();
            var stack = new Stack<StateGraphNode>();
            stack.Push(root); seen.Add(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n.Edges == null) continue;
                foreach (var e in n.Edges)
                {
                    if (e.Target != null && seen.Add(e.Target))
                        stack.Push(e.Target);
                }
            }
            return seen.Count;
        }

        /// <summary>
        /// Force the lazy NBW to materialise every reachable state by BFS
        /// over <see cref="SymbolicNBW{P,E,S}.GetTransition"/>. After this
        /// call, <see cref="SymbolicNBW{P,E,S}.States"/> contains the full
        /// reachable set.
        /// </summary>
        private static void ForceMaterialise(
            SymbolicNBW<IStatePredicate, State, BreakpointState<Rltl<IStatePredicate>>> nbw)
        {
            var stack = new Stack<BreakpointState<Rltl<IStatePredicate>>>();
            var seen = new HashSet<BreakpointState<Rltl<IStatePredicate>>>(
                BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer());
            foreach (var s in nbw.InitialStates)
            {
                if (seen.Add(s)) stack.Push(s);
            }
            while (stack.Count > 0)
            {
                var s = stack.Pop();
                var trans = nbw.GetTransition(s);
                foreach (var term in trans)
                {
                    foreach (var leaf in term.GetDistinctLeaves())
                    {
                        foreach (var succ in leaf)
                        {
                            if (seen.Add(succ)) stack.Push(succ);
                        }
                    }
                }
            }
        }

        /// <summary>Format a table of reports with a header row.</summary>
        public static string Tabulate(IEnumerable<(string label, Report report)> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("scenario                              mode   verdict  model    NBW(disc/tot)   trans     time(ms)");
            sb.AppendLine("------------------------------------- ------ -------- -------- --------------- --------- ---------");
            foreach (var (label, r) in rows)
            {
                var nbwCol = r.NbwStatesReachableTotal.HasValue
                    ? string.Format("{0,5}/{1,-5}", r.NbwStatesDiscovered, r.NbwStatesReachableTotal.Value)
                    : string.Format("{0,5}      ", r.NbwStatesDiscovered);
                sb.AppendFormat(
                    "{0,-37} {1,-6} {2,-8} {3,8} {4,15} {5,9} {6,9:0.0}\n",
                    Truncate(label, 37),
                    r.Mode,
                    r.Valid ? "VALID" : "VIOL",
                    r.ModelStates,
                    nbwCol,
                    r.NbwTransitionsCached,
                    r.Elapsed.TotalMilliseconds);
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int n)
            => s == null ? string.Empty : (s.Length <= n ? s : s.Substring(0, n));
    }
}
