namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System.Collections.Generic;

    /// <summary>
    /// Algorithmic emptiness ("dead") and non-emptiness ("alive") check for
    /// <see cref="Ere{TPred}"/>.
    ///
    /// <para>
    /// A regex <c>R</c> is <em>alive</em> iff <c>FLang(R) ≠ ∅</c>, i.e. some
    /// finite word is accepted; <c>R</c> is <em>dead</em> iff <c>FLang(R) = ∅</c>.
    /// Aliveness is not a static structural property of the AST (e.g.,
    /// <c>α &amp; ¬α</c> is dead but not syntactically <see cref="EreEmpty{TPred}"/>),
    /// so we decide it by exploring the Brzozowski-derivative state space:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>R</c> is alive iff some reachable derivative state
    ///   <c>∂_u(R)</c> is <c>Nullable</c> along a path whose accumulated
    ///   guard predicate is satisfiable in the underlying EBA.</item>
    ///   <item>The derivative state space is finite (Antimirov / Brzozowski
    ///   give bounded number of distinct residuals modulo similarity laws,
    ///   which the existing ERE factories enforce).</item>
    /// </list>
    /// Results are memoised per regex on this checker instance.
    ///
    /// <para>
    /// <b>Limitation.</b> The check is only as precise as the EBA's
    /// <see cref="IPredicateAlgebra{TPred}.IsSatisfiable"/>. With a conservative
    /// EBA (e.g. <c>StatePropEba</c>), some semantically-dead regexes will be
    /// reported alive because their guarding predicate <c>α</c> is reported
    /// satisfiable even though <c>L(α) = ∅</c>. Used in
    /// <see cref="RltlDerivative{TPred,TElem}"/> this remains sound for
    /// closure semantics because the derivative loop will eventually
    /// structurally collapse such states to <see cref="EreEmpty{TPred}"/> on
    /// further unfolding.
    /// </para>
    /// </summary>
    public sealed class EreEmptinessChecker<TPred, TElem>
    {
        // Aliveness state, indexed by Ere<TPred>.Id. Grown by doubling.
        //   0 = unknown, 1 = alive, 2 = dead.
        private byte[] _aliveState = new byte[64];
        private readonly IPredicateAlgebra<TPred> _predAlg;
        private readonly EreDerivative<TPred, TElem> _ereDeriv;
        private readonly EreEmptinessCheckerOptions _options;

        /// <summary>The options the checker was constructed with.</summary>
        public EreEmptinessCheckerOptions Options => _options;

        /// <summary>Cumulative count of times the DNF-leaf-splitting rule
        /// has emitted more than one successor for a single transition-term
        /// leaf since this checker was constructed (= number of leaves split,
        /// not number of disjuncts produced). Reset on demand via
        /// <see cref="ResetCounters"/>. Always 0 when
        /// <see cref="EreEmptinessCheckerOptions.SplitDnfLeaves"/> is false.
        /// </summary>
        public long DnfLeafSplits { get; private set; }

        /// <summary>Cumulative count of distinct residual states enqueued
        /// for exploration across all <see cref="Decide"/> / <see cref="NonEmpty(Ere{TPred},ConsList{TPred},out ConsList{TPred})"/>
        /// calls since this checker was constructed (= sum of |seen| / |parent|
        /// across calls). Useful as a state-space-size proxy when measuring
        /// the impact of options like
        /// <see cref="EreEmptinessCheckerOptions.SplitDnfLeaves"/>. Reset via
        /// <see cref="ResetCounters"/>.</summary>
        public long StatesEnqueued { get; private set; }

        /// <summary>Reset performance / instrumentation counters to zero.</summary>
        public void ResetCounters() { DnfLeafSplits = 0; StatesEnqueued = 0; }

        public EreEmptinessChecker(EreDerivative<TPred, TElem> ereDeriv)
            : this(ereDeriv, EreEmptinessCheckerOptions.Default) { }

        public EreEmptinessChecker(
            EreDerivative<TPred, TElem> ereDeriv,
            EreEmptinessCheckerOptions options)
        {
            _ereDeriv = ereDeriv ?? throw new System.ArgumentNullException(nameof(ereDeriv));
            _predAlg = ereDeriv.TermAlgebra.Eba;
            _options = options ?? EreEmptinessCheckerOptions.Default;
        }

        /// <summary>True iff <c>FLang(R) ≠ ∅</c>.</summary>
        public bool IsAlive(Ere<TPred> r) => Decide(r);

        /// <summary>True iff <c>FLang(R) = ∅</c>.</summary>
        public bool IsDead(Ere<TPred> r) => !Decide(r);

        /// <summary>
        /// Witness-producing emptiness check. Returns <c>true</c> if
        /// <c>FLang(r) ≠ ∅</c>; in that case <paramref name="witnessReverse"/>
        /// is the path-condition list (backwards: head = most-recent /
        /// last-emitted predicate) of a satisfiable accepting run, with
        /// <paramref name="prefixReverse"/> already at the tail. The caller
        /// reverses the result to obtain the witness in forward order.
        ///
        /// <para>
        /// Per design: path-condition predicates are <em>threaded as a
        /// parameter</em>, never attached to the regex AST — the t-term DAG
        /// is preserved. Used by <see cref="EreEquivalenceChecker{TPred,TElem}"/>
        /// when a non-EreXor leaf is encountered and a distinguishing word
        /// is needed.
        /// </para>
        /// </summary>
        public bool NonEmpty(Ere<TPred> r, ConsList<TPred> prefixReverse,
            out ConsList<TPred> witnessReverse)
        {
            if (prefixReverse == null) prefixReverse = ConsList<TPred>.Empty;

            // r itself accepts the empty word — witness is just the prefix.
            if (r.Nullable) { witnessReverse = prefixReverse; return true; }
            if (r is EreEmpty<TPred>) { witnessReverse = null; return false; }

            // BFS with parent tracking so we can reconstruct a guarded path
            // from r to the first nullable residual reached.
            var parent = new Dictionary<Ere<TPred>, (Ere<TPred> p, TPred g)>();
            parent[r] = (null, default);
            var queue = new Queue<Ere<TPred>>();
            queue.Enqueue(r);
            Ere<TPred> found = null;

            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                var dR = _ereDeriv.Derivative(s);
                foreach (var (residual, guard) in EnumerateLeavesSplit(dR, _predAlg.Top))
                {
                    if (residual is EreEmpty<TPred>) continue;
                    if (!_predAlg.IsSatisfiable(guard)) continue;
                    if (parent.ContainsKey(residual)) continue;
                    parent[residual] = (s, guard);
                    StatesEnqueued++;
                    if (residual.Nullable) { found = residual; goto Done; }
                    queue.Enqueue(residual);
                }
            }
        Done:
            if (found == null) { witnessReverse = null; return false; }

            // Walk parents nullable → r, collecting guards in reverse order.
            var guards = new List<TPred>();
            var cur = found;
            while (true)
            {
                var (p, g) = parent[cur];
                if (p == null) break;
                guards.Add(g);
                cur = p;
            }
            // guards = [g_k, g_{k-1}, ..., g_1] (last-to-first symbol).
            // Push onto prefix in REVERSE so head ends up as g_k (most recent).
            var w = prefixReverse;
            for (int i = guards.Count - 1; i >= 0; i--) w = w.Push(guards[i]);
            witnessReverse = w;
            return true;
        }

        /// <summary>
        /// EREQ Phase-4 quantified witness variant of
        /// <see cref="NonEmpty(Ere{TPred}, ConsList{TPred}, out ConsList{TPred})"/>:
        /// each step carries both the per-letter predicate guard and the
        /// proposition valuations chosen by the search (per Phase-2 D1,
        /// negative-indexed propositions). Use this whenever the regex
        /// may contain <see cref="EreExists{TPred}"/> / <see cref="EreProposition{TPred}"/>
        /// and the caller needs the propositional assignment along the
        /// accepted word.
        /// </summary>
        public bool NonEmpty(
            Ere<TPred> r,
            ConsList<EreWitnessStep<TPred>> prefixReverse,
            out ConsList<EreWitnessStep<TPred>> witnessReverse)
        {
            if (prefixReverse == null) prefixReverse = ConsList<EreWitnessStep<TPred>>.Empty;

            if (r.Nullable) { witnessReverse = prefixReverse; return true; }
            if (r is EreEmpty<TPred>) { witnessReverse = null; return false; }

            // Same BFS topology as the predicate-only variant but the
            // parent map records the full step (predicate + propositions).
            var parent = new Dictionary<Ere<TPred>, (Ere<TPred> p, EreWitnessStep<TPred> s)>();
            parent[r] = (null, null);
            var queue = new Queue<Ere<TPred>>();
            queue.Enqueue(r);
            Ere<TPred> found = null;

            var emptyProps = new Dictionary<int, bool>(0);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var dR = _ereDeriv.Derivative(cur);
                foreach (var (residual, guard, propVals) in
                         EnumerateLeavesWithPropsSplit(dR, _predAlg.Top, emptyProps))
                {
                    if (residual is EreEmpty<TPred>) continue;
                    if (!_predAlg.IsSatisfiable(guard)) continue;
                    if (parent.ContainsKey(residual)) continue;
                    parent[residual] = (cur, new EreWitnessStep<TPred>(guard, propVals));
                    StatesEnqueued++;
                    if (residual.Nullable) { found = residual; goto Done; }
                    queue.Enqueue(residual);
                }
            }
        Done:
            if (found == null) { witnessReverse = null; return false; }

            var steps = new List<EreWitnessStep<TPred>>();
            var c = found;
            while (true)
            {
                var (p, s) = parent[c];
                if (p == null) break;
                steps.Add(s);
                c = p;
            }
            var w = prefixReverse;
            for (int i = steps.Count - 1; i >= 0; i--) w = w.Push(steps[i]);
            witnessReverse = w;
            return true;
        }

        private bool Decide(Ere<TPred> root)
        {
            var rs = GetState(root);
            if (rs == 1) return true;
            if (rs == 2) return false;

            // BFS over residuals, but each transition is taken only if the
            // accumulated path predicate is satisfiable. A residual state is
            // associated with the disjunction of all path predicates by which
            // it is reachable (we only need one satisfiable witness).
            var seen = new HashSet<Ere<TPred>> { root };
            var queue = new Queue<Ere<TPred>>();
            queue.Enqueue(root);

            bool alive = false;
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                if (s.Nullable) { alive = true; break; }
                if (s is EreEmpty<TPred>) continue;
                // Honour previously decided states: a known-dead residual
                // contributes no new alive witnesses and need not be expanded;
                // a known-alive residual immediately resolves the root as alive.
                var sState = GetState(s);
                if (sState == 2) continue;
                if (sState == 1) { alive = true; break; }

                var dR = _ereDeriv.Derivative(s);
                foreach (var (residual, guard) in EnumerateLeavesSplit(dR, _predAlg.Top))
                {
                    if (residual is EreEmpty<TPred>) continue;
                    if (!_predAlg.IsSatisfiable(guard)) continue;
                    if (seen.Add(residual)) { StatesEnqueued++; queue.Enqueue(residual); }
                }
            }

            if (!alive)
            {
                foreach (var s in seen) SetState(s, 2);
            }
            else
            {
                SetState(root, 1);
            }
            return alive;
        }

        private byte GetState(Ere<TPred> r)
            => r.Id >= 0 && r.Id < _aliveState.Length ? _aliveState[r.Id] : (byte)0;

        private void SetState(Ere<TPred> r, byte state)
        {
            if (r.Id < 0) return; // un-interned (shouldn't happen via the factories)
            if (r.Id >= _aliveState.Length)
            {
                int newLen = _aliveState.Length;
                while (r.Id >= newLen) newLen *= 2;
                System.Array.Resize(ref _aliveState, newLen);
            }
            _aliveState[r.Id] = state;
        }

        /// <summary>
        /// Enumerates leaves of a <see cref="TransitionTerm{TLeaf}"/> together
        /// with the conjunction of guard predicates along the path leading to
        /// each leaf. Walks the BDD-like ITE structure, tracking the positive
        /// (hi) and negative (lo) branches via the underlying
        /// <see cref="ConditionRegistry{TPred}"/>.
        ///
        /// <para>EREQ Phase-2 D5: proposition splits (negative condition
        /// indices) do not tighten the path predicate — both branches are
        /// always reachable and the predicate guard flows through unchanged.
        /// This keeps the predicate-only API sound on EREQ regexes without
        /// extending its return type.</para>
        /// </summary>
        private IEnumerable<(Ere<TPred> leaf, TPred guard)> EnumerateLeaves(
            TransitionTerm<Ere<TPred>> term, TPred pathGuard)
        {
            if (term is TransitionTermLeaf<Ere<TPred>> leaf)
            {
                yield return (leaf.Value, pathGuard);
                yield break;
            }
            var ite = (TransitionTermIte<Ere<TPred>>)term;
            if (ConditionRegistry<TPred>.IsProposition(ite.ConditionIndex))
            {
                foreach (var t in EnumerateLeaves(ite.Hi, pathGuard)) yield return t;
                foreach (var t in EnumerateLeaves(ite.Lo, pathGuard)) yield return t;
                yield break;
            }
            var pred = _ereDeriv.TermAlgebra.Registry.GetPredicate(ite.ConditionIndex);
            var hiGuard = _predAlg.And(pathGuard, pred);
            if (_predAlg.IsSatisfiable(hiGuard))
                foreach (var t in EnumerateLeaves(ite.Hi, hiGuard))
                    yield return t;
            var loGuard = _predAlg.And(pathGuard, _predAlg.Not(pred));
            if (_predAlg.IsSatisfiable(loGuard))
                foreach (var t in EnumerateLeaves(ite.Lo, loGuard))
                    yield return t;
        }

        /// <summary>
        /// EREQ Phase-4 variant of <see cref="EnumerateLeaves"/>: in addition
        /// to the per-leaf path-condition guard, records the proposition
        /// valuations along the branch (per <see cref="ConditionRegistry{TPredicate}"/>
        /// negative-index propositions). Each branch into the Hi/Lo of a
        /// proposition split copies the running valuation and writes the
        /// chosen Boolean.
        /// </summary>
        private IEnumerable<(Ere<TPred> leaf, TPred guard, IReadOnlyDictionary<int, bool> propVals)>
            EnumerateLeavesWithProps(
                TransitionTerm<Ere<TPred>> term,
                TPred pathGuard,
                IReadOnlyDictionary<int, bool> propVals)
        {
            if (term is TransitionTermLeaf<Ere<TPred>> leaf)
            {
                yield return (leaf.Value, pathGuard, propVals);
                yield break;
            }
            var ite = (TransitionTermIte<Ere<TPred>>)term;
            if (ConditionRegistry<TPred>.IsProposition(ite.ConditionIndex))
            {
                var hiVals = new Dictionary<int, bool>(propVals.Count + 1);
                foreach (var kv in propVals) hiVals[kv.Key] = kv.Value;
                hiVals[ite.ConditionIndex] = true;
                foreach (var t in EnumerateLeavesWithProps(ite.Hi, pathGuard, hiVals))
                    yield return t;

                var loVals = new Dictionary<int, bool>(propVals.Count + 1);
                foreach (var kv in propVals) loVals[kv.Key] = kv.Value;
                loVals[ite.ConditionIndex] = false;
                foreach (var t in EnumerateLeavesWithProps(ite.Lo, pathGuard, loVals))
                    yield return t;
                yield break;
            }
            var pred = _ereDeriv.TermAlgebra.Registry.GetPredicate(ite.ConditionIndex);
            var hiGuard = _predAlg.And(pathGuard, pred);
            if (_predAlg.IsSatisfiable(hiGuard))
                foreach (var t in EnumerateLeavesWithProps(ite.Hi, hiGuard, propVals))
                    yield return t;
            var loGuard = _predAlg.And(pathGuard, _predAlg.Not(pred));
            if (_predAlg.IsSatisfiable(loGuard))
                foreach (var t in EnumerateLeavesWithProps(ite.Lo, loGuard, propVals))
                    yield return t;
        }

        /// <summary>
        /// Phase 7: wrap <see cref="EnumerateLeaves"/> and, if
        /// <see cref="EreEmptinessCheckerOptions.SplitDnfLeaves"/> is on,
        /// expand any top-level <see cref="EreUnion{TPred}"/> leaf into
        /// one (operand, guard) pair per disjunct. Increments
        /// <see cref="DnfLeafSplits"/> once per split leaf (not per
        /// operand). Hash-cons identity ensures that equivalent operands
        /// produced across different leaves dedup naturally through the
        /// BFS <c>parent</c> map.
        /// </summary>
        private IEnumerable<(Ere<TPred> leaf, TPred guard)> EnumerateLeavesSplit(
            TransitionTerm<Ere<TPred>> term, TPred pathGuard)
        {
            foreach (var pair in EnumerateLeaves(term, pathGuard))
            {
                if (_options.SplitDnfLeaves && pair.leaf is EreUnion<TPred> u)
                {
                    DnfLeafSplits++;
                    foreach (var op in u.Operands)
                        yield return (op, pair.guard);
                }
                else
                {
                    yield return pair;
                }
            }
        }

        /// <summary>Phase 7 EREQ-aware variant of
        /// <see cref="EnumerateLeavesSplit"/>; see that method's remarks.</summary>
        private IEnumerable<(Ere<TPred> leaf, TPred guard, IReadOnlyDictionary<int, bool> propVals)>
            EnumerateLeavesWithPropsSplit(
                TransitionTerm<Ere<TPred>> term,
                TPred pathGuard,
                IReadOnlyDictionary<int, bool> propVals)
        {
            foreach (var triple in EnumerateLeavesWithProps(term, pathGuard, propVals))
            {
                if (_options.SplitDnfLeaves && triple.leaf is EreUnion<TPred> u)
                {
                    DnfLeafSplits++;
                    foreach (var op in u.Operands)
                        yield return (op, triple.guard, triple.propVals);
                }
                else
                {
                    yield return triple;
                }
            }
        }
    }
}
