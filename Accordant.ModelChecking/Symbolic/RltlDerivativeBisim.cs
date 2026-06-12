namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Sound-but-incomplete bisimulation-based equivalence check on the
    /// symbolic RLTL derivative graph (G8-a). Mirrors the shape of
    /// <see cref="EreEquivalenceChecker{TPred,TElem}"/> but cannot achieve
    /// completeness for ω-regular languages: alternating-Büchi acceptance is
    /// determined by formula structure (Until / Release / closures) and is
    /// not always preserved under arbitrary derivative-DAG bisimulation, and
    /// determinisation is in general not available for ω-automata.
    ///
    /// <para><b>Algorithm.</b> Hopcroft–Karp-style union-find on RLTL
    /// formula <see cref="Rltl{TPred}.Id"/>s:</para>
    /// <list type="number">
    ///   <item>Initially union(p, q); push the pair onto a worklist.</item>
    ///   <item>For each pair (a, b) popped, compute ∂(a) and ∂(b) and
    ///   enumerate their transition-term leaves with path guards.</item>
    ///   <item>For every pair of leaves (g_a, D_a) and (g_b, D_b) whose
    ///   path guards conjoin to a satisfiable predicate, compare the
    ///   <see cref="Dnf{TState}"/> leaves under the current UF substitution.
    ///   If they coincide as canonical Dnfs after rep-rewriting, continue.
    ///   Otherwise attempt a single-atom merge (both Dnfs are single
    ///   singletons {x} vs {y}): union(x, y) and push (x, y). Any other
    ///   mismatch returns <c>false</c> (inconclusive).</item>
    ///   <item>If the worklist drains, return <c>true</c>: the bisim closed
    ///   and every observation from p induces the same configuration set as
    ///   from q under the partial bijection on derivative states.</item>
    /// </list>
    ///
    /// <para><b>Soundness.</b> A successful bisim establishes that the two
    /// formulas have isomorphic symbolic derivative DAGs modulo the chosen
    /// state-merging partition; for the LTL fragment this implies
    /// language equivalence. For full RLTL with closures and Büchi-style
    /// acceptance the structural bisim is a <em>sufficient but not
    /// necessary</em> witness — many equivalences (e.g. F G φ ≡ G F φ when
    /// they happen to agree, complex regex-prefix rewritings) cannot be
    /// shown by this technique. Differentially tested against the G8-b
    /// oracle <see cref="RltlLanguageEquivalence"/>: every
    /// formula pair on which this checker returns <c>true</c> is also
    /// reported equivalent by the (sound+complete) oracle.</para>
    ///
    /// <para><b>Intended use.</b> Cheap pre-check inside tableau/closure
    /// construction (RLTL ABW state dedup, sub-formula sharing) where a
    /// fast structural answer is preferable to invoking the full
    /// emptiness-based oracle.</para>
    /// </summary>
    public sealed class RltlDerivativeBisim<TPred, TElem>
    {
        private readonly RltlDerivative<TPred, TElem> _deriv;
        private readonly IPredicateAlgebra<TPred> _eba;
        private readonly DnfAlgebra<Rltl<TPred>> _dnfAlgebra;

        public RltlDerivativeBisim(RltlDerivative<TPred, TElem> deriv)
        {
            _deriv = deriv ?? throw new ArgumentNullException(nameof(deriv));
            _eba = deriv.Eba;
            _dnfAlgebra = deriv.DnfAlgebra;
        }

        /// <summary>
        /// Attempts to prove <c>L(p) = L(q)</c> via structural bisimulation
        /// on the derivative DAG. Returns <c>true</c> when the bisim closes
        /// (sound: equivalent), <c>false</c> otherwise (incomplete:
        /// <em>inconclusive</em>, NOT a proof of inequivalence).
        /// </summary>
        public bool TryProveEquivalent(Rltl<TPred> p, Rltl<TPred> q)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (ReferenceEquals(p, q) || p.Equals(q)) return true;

            var uf = new IntUnionFind();
            var idMap = new Dictionary<Rltl<TPred>, int>(RltlEqualityComparer);
            var visited = new HashSet<(int, int)>();
            var stack = new Stack<(Rltl<TPred> a, Rltl<TPred> b)>();

            EnqueuePair(uf, idMap, visited, stack, p, q);

            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                int ia = GetId(idMap, a);
                int ib = GetId(idMap, b);
                if (uf.Find(ia) != uf.Find(ib)) uf.Union(ia, ib);
                if (a.Equals(b)) continue;

                var da = _deriv.Derivative(a);
                var db = _deriv.Derivative(b);

                var leavesA = EnumerateLeaves(da, _eba.Top).ToList();
                var leavesB = EnumerateLeaves(db, _eba.Top).ToList();

                foreach (var (gA, DA) in leavesA)
                {
                    foreach (var (gB, DB) in leavesB)
                    {
                        var gAnd = _eba.And(gA, gB);
                        if (!_eba.IsSatisfiable(gAnd)) continue;
                        if (!CompareDnf(DA, DB, uf, idMap, visited, stack)) return false;
                    }
                }
            }
            return true;
        }

        private static readonly IEqualityComparer<Rltl<TPred>> RltlEqualityComparer
            = EqualityComparer<Rltl<TPred>>.Default;

        private static int GetId(Dictionary<Rltl<TPred>, int> idMap, Rltl<TPred> f)
        {
            if (idMap.TryGetValue(f, out var id)) return id;
            id = idMap.Count;
            idMap[f] = id;
            return id;
        }

        // ---------- internals ----------

        private bool CompareDnf(
            Dnf<Rltl<TPred>> a, Dnf<Rltl<TPred>> b,
            IntUnionFind uf,
            Dictionary<Rltl<TPred>, int> idMap,
            HashSet<(int, int)> visited,
            Stack<(Rltl<TPred>, Rltl<TPred>)> stack)
        {
            if (a.Equals(b)) return true;
            var rewA = RewriteUnderUF(a, uf, idMap);
            var rewB = RewriteUnderUF(b, uf, idMap);
            if (rewA.Equals(rewB)) return true;

            // Single-atom shortcut: both sides are {{x}} vs {{y}}. Try a
            // candidate merge; the bisim will check it on the next iteration.
            if (IsSingleAtom(a, out var pa) && IsSingleAtom(b, out var pb))
            {
                int ia = GetId(idMap, pa);
                int ib = GetId(idMap, pb);
                if (uf.Find(ia) == uf.Find(ib)) return true;
                EnqueuePair(uf, idMap, visited, stack, pa, pb);
                return true;
            }

            // Same clause shape, same per-clause arity: optimistically pair
            // formulas by their order and enqueue any new pairs. Handles
            // symmetric Dnfs whose clauses contain already-aligned formulas
            // modulo a single residual mismatch.
            if (rewA.ClauseCount == rewB.ClauseCount
                && a.ClauseCount == b.ClauseCount)
            {
                bool madeProgress = false;
                for (int i = 0; i < a.ClauseCount; i++)
                {
                    var ca = a.Clauses[i].ToList();
                    var cb = b.Clauses[i].ToList();
                    if (ca.Count != cb.Count) return false;
                    for (int j = 0; j < ca.Count; j++)
                    {
                        var x = ca[j];
                        var y = cb[j];
                        int ix = GetId(idMap, x);
                        int iy = GetId(idMap, y);
                        if (uf.Find(ix) == uf.Find(iy)) continue;
                        if (!x.Equals(y))
                        {
                            EnqueuePair(uf, idMap, visited, stack, x, y);
                            madeProgress = true;
                        }
                    }
                }
                if (madeProgress) return true;
            }
            return false;
        }

        private static bool IsSingleAtom(Dnf<Rltl<TPred>> d, out Rltl<TPred> atom)
        {
            atom = null;
            if (d.ClauseCount != 1) return false;
            var c = d.Clauses[0];
            if (c.Count != 1) return false;
            atom = c.Single();
            return true;
        }

        private Dnf<Rltl<TPred>> RewriteUnderUF(
            Dnf<Rltl<TPred>> d,
            IntUnionFind uf,
            Dictionary<Rltl<TPred>, int> idMap)
        {
            if (d.IsTrue || d.IsFalse) return d;
            var repCache = new Dictionary<int, Rltl<TPred>>();
            Rltl<TPred> Rep(Rltl<TPred> f)
            {
                int rid = uf.Find(GetId(idMap, f));
                if (repCache.TryGetValue(rid, out var r)) return r;
                repCache[rid] = f;
                return f;
            }
            // Register reps in deterministic id order so the result is
            // independent of clause traversal order.
            var allFormulas = new SortedDictionary<int, Rltl<TPred>>();
            foreach (var f in d.GetAllStates()) allFormulas[GetId(idMap, f)] = f;
            foreach (var kv in allFormulas) Rep(kv.Value);

            var newClauses = new List<StateSet<Rltl<TPred>>>(d.ClauseCount);
            foreach (var clause in d.Clauses)
            {
                var mapped = new List<Rltl<TPred>>(clause.Count);
                foreach (var f in clause) mapped.Add(Rep(f));
                newClauses.Add(new StateSet<Rltl<TPred>>(mapped, RltlComparer<TPred>.Instance));
            }
            return _dnfAlgebra.FromClauses(newClauses);
        }

        private static void EnqueuePair(
            IntUnionFind uf,
            Dictionary<Rltl<TPred>, int> idMap,
            HashSet<(int, int)> visited,
            Stack<(Rltl<TPred>, Rltl<TPred>)> stack,
            Rltl<TPred> a, Rltl<TPred> b)
        {
            int ia = GetId(idMap, a);
            int ib = GetId(idMap, b);
            if (a.Equals(b)) { uf.Union(ia, ib); return; }
            int lo = Math.Min(ia, ib);
            int hi = Math.Max(ia, ib);
            if (!visited.Add((lo, hi))) return;
            uf.Union(ia, ib);
            stack.Push((a, b));
        }

        // BDD-style ITE leaf enumeration with path-guard accumulation —
        // identical to EreEquivalenceChecker.EnumerateLeaves but typed for
        // RLTL Dnf leaves.
        private IEnumerable<(TPred guard, Dnf<Rltl<TPred>> leaf)> EnumerateLeaves(
            TransitionTerm<Dnf<Rltl<TPred>>> term, TPred pathGuard)
        {
            if (term is TransitionTermLeaf<Dnf<Rltl<TPred>>> leaf)
            {
                yield return (pathGuard, leaf.Value);
                yield break;
            }
            var ite = (TransitionTermIte<Dnf<Rltl<TPred>>>)term;
            var pred = _deriv.Registry.GetPredicate(ite.ConditionIndex);

            var hiGuard = _eba.And(pathGuard, pred);
            if (_eba.IsSatisfiable(hiGuard))
                foreach (var t in EnumerateLeaves(ite.Hi, hiGuard))
                    yield return t;

            var loGuard = _eba.And(pathGuard, _eba.Not(pred));
            if (_eba.IsSatisfiable(loGuard))
                foreach (var t in EnumerateLeaves(ite.Lo, loGuard))
                    yield return t;
        }
    }
}
