namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bisimulation-based language-equivalence checker for symbolic ERE
    /// (CAV'26: Veanes et al., "Symbolic Extended Regular Expression
    /// Equivalence", §4 algorithm + §6 implementation).
    ///
    /// <para>The decision procedure is</para>
    /// <code>
    ///   Eq(p, q)  ⇔  Empty(p ⊕ q)
    /// </code>
    /// <para>where <c>⊕</c> is the primitive symmetric-difference operator
    /// (see <see cref="EreXor{TPred}"/>) and <c>Empty(r)</c> is decided by a
    /// union-find–based bisimulation over the symbolic-derivative state
    /// space of <c>r</c>.</para>
    ///
    /// <para>Invariant (paper Inv(U,S)): regexes in the same
    /// <see cref="IntUnionFind"/> class are <em>candidate</em> bisimilar; if
    /// the loop completes without finding a nullable XOR leaf or a
    /// non-empty non-XOR leaf, the invariant is closed and
    /// <c>L(p) = L(q)</c>.</para>
    ///
    /// <para>Key §6 optimisations included:</para>
    /// <list type="bullet">
    ///   <item>Leaves that are not XOR-shaped fall through to plain ERE
    ///   emptiness checking via <see cref="EreEmptinessChecker{TPred,TElem}"/>
    ///   ("minutes vs. microseconds").</item>
    ///   <item>Path-guard satisfiability pruning during leaf enumeration
    ///   (skips unreachable transitions in the symbolic alphabet).</item>
    ///   <item>Derivative caching via the shared
    ///   <see cref="EreDerivative{TPred,TElem}"/> instance.</item>
    /// </list>
    ///
    /// <para><b>Soundness notes.</b></para>
    /// <list type="bullet">
    ///   <item>The constructor canonicalisation of <see cref="Ere{TPred}.Xor"/>
    ///   already handles many shortcuts at the AST level
    ///   (<c>r ⊕ r = ⊥</c>, <c>r ⊕ ~s = ~(r ⊕ s)</c>, etc.). After
    ///   canonicalisation, <c>p ⊕ q</c> is either a non-XOR ERE (which we
    ///   decide by direct emptiness) or an <see cref="EreXor{TPred}"/> with
    ///   2+ operands.</item>
    ///   <item>For an n-ary XOR leaf, the bisim invariant generalises
    ///   naturally: <c>p1 ~U … ~U pn</c> as a single class. Merge unions
    ///   them all; the "already merged" check asks whether all operands
    ///   share the same representative.</item>
    ///   <item>XNOR leaves (<see cref="EreXor{TPred}.Negated"/> = true) are
    ///   not bisim pairs in the same sense (they assert non-equivalence at
    ///   the surface). We treat them as non-XOR leaves and check their
    ///   emptiness directly — sound because <c>L(~X) = ∅ ⇔ L(X) = Σ*</c>.
    ///   </item>
    ///   <item>Like <see cref="EreEmptinessChecker{TPred,TElem}"/>, soundness
    ///   is modulo the underlying <see cref="IPredicateAlgebra{TPred}"/>'s
    ///   <c>IsSatisfiable</c> precision.</item>
    /// </list>
    ///
    /// <para><b>Not thread-safe.</b> One checker instance owns one
    /// union-find; instances are cheap, create one per top-level query
    /// when needed.</para>
    /// </summary>
    public sealed class EreEquivalenceChecker<TPred, TElem>
    {
        private readonly EreDerivative<TPred, TElem> _deriv;
        private readonly EreEmptinessChecker<TPred, TElem> _empt;
        private readonly IPredicateAlgebra<TPred> _predAlg;
        private readonly EreEquivalenceCheckerOptions _options;

        /// <summary>Cumulative count of times the E-frame UF merge rule has
        /// discharged a residual leaf since this checker was constructed.
        /// Reset on demand by the user via <see cref="ResetCounters"/>.
        /// Always 0 when <see cref="EreEquivalenceCheckerOptions.UseEFrameMerge"/>
        /// is false.
        /// <para>
        /// <b>Expected to be 0 on natural corpora.</b> See the dormancy
        /// note on
        /// <see cref="EreEquivalenceCheckerOptions.UseEFrameMerge"/>: the
        /// <see cref="Ere{TPred}.Exists(int, Ere{TPred})"/> factory
        /// pushes <c>∃p</c> inward across most ERE connectives, so the
        /// XOR-of-<c>∃p.body</c> trigger pattern rarely survives down to
        /// the bisim residual. A non-zero count is interesting and
        /// indicates either deliberate UF seeding via
        /// <see cref="TryEFrameDischarge"/> or an EREQ AST whose
        /// existential wrappers were constructed outside the standard
        /// smart-constructor path.
        /// </para>
        /// </summary>
        public long EFrameMergeFires { get; private set; }

        /// <summary>The options the checker was constructed with.</summary>
        public EreEquivalenceCheckerOptions Options => _options;

        /// <summary>Reset performance / instrumentation counters to zero.</summary>
        public void ResetCounters() { EFrameMergeFires = 0; }

        public EreEquivalenceChecker(
            EreDerivative<TPred, TElem> deriv,
            EreEmptinessChecker<TPred, TElem> emptinessChecker = null,
            EreEquivalenceCheckerOptions options = null)
        {
            _deriv = deriv ?? throw new ArgumentNullException(nameof(deriv));
            _empt = emptinessChecker ?? new EreEmptinessChecker<TPred, TElem>(deriv);
            _predAlg = deriv.TermAlgebra.Eba;
            _options = options ?? EreEquivalenceCheckerOptions.Default;
        }

        /// <summary>
        /// Returns true iff <c>L(p) = L(q)</c>. Equivalent to
        /// <c>!IsAlive(p ⊕ q)</c>.
        /// </summary>
        public bool AreEquivalent(Ere<TPred> p, Ere<TPred> q)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (ReferenceEquals(p, q) || p.Equals(q)) return true;
            // Single-letter fast path: defer to the EBA's precise
            // equivalence when both regexes are atoms. Saves an
            // Xor-Empty bisim for the common leaf-level case and lets
            // SMT-backed EBAs answer in one solver call.
            if (p is EreAtom<TPred> pa && q is EreAtom<TPred> qa
                && _predAlg.AreEquivalent(pa.Predicate, qa.Predicate))
                return true;
            var xor = Ere<TPred>.Xor(p, q);
            return IsLanguageEmpty(xor);
        }

        /// <summary>
        /// Witness-producing inequivalence check. Returns <c>true</c> when
        /// <c>L(p) ≠ L(q)</c>, with <paramref name="witnessReverse"/> a
        /// distinguishing word in <em>reversed</em> path-condition form
        /// (head = last symbol). The witness lies in
        /// <c>L(p) △ L(q)</c> — i.e. it is accepted by exactly one of
        /// <c>p</c> and <c>q</c>. Returns <c>false</c> when they are
        /// equivalent (<paramref name="witnessReverse"/> is <c>null</c>).
        /// </summary>
        public bool AreInequivalent(Ere<TPred> p, Ere<TPred> q,
            out ConsList<TPred> witnessReverse)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (ReferenceEquals(p, q) || p.Equals(q))
            {
                witnessReverse = null;
                return false;
            }
            var xor = Ere<TPred>.Xor(p, q);
            return NonEmpty(xor, out witnessReverse);
        }

        /// <summary>
        /// Decides <c>L(r) = ∅</c>. If <paramref name="r"/> is an
        /// (un-negated) <see cref="EreXor{TPred}"/>, runs the bisimulation
        /// algorithm; otherwise defers to <see cref="EreEmptinessChecker{TPred,TElem}"/>.
        /// </summary>
        public bool IsLanguageEmpty(Ere<TPred> r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            // Top-level differs on ε → trivially non-empty.
            if (r.Nullable) return false;

            // Non-XOR (or XNOR) — defer to plain emptiness.
            if (!(r is EreXor<TPred> x) || x.Negated)
                return _empt.IsDead(r);

            return Bisimulate(x);
        }

        /// <summary>
        /// Witness-producing non-emptiness. Returns <c>true</c> when
        /// <c>L(r) ≠ ∅</c>; <paramref name="witnessReverse"/> is then a
        /// satisfying word as a reversed path-condition list (head = last
        /// symbol). Returns <c>false</c> on emptiness with
        /// <paramref name="witnessReverse"/> = <c>null</c>.
        /// </summary>
        public bool NonEmpty(Ere<TPred> r, out ConsList<TPred> witnessReverse)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.Nullable) { witnessReverse = ConsList<TPred>.Empty; return true; }
            if (!(r is EreXor<TPred> x) || x.Negated)
                return _empt.NonEmpty(r, ConsList<TPred>.Empty, out witnessReverse);
            return BisimulateWitness(x, out witnessReverse);
        }

        // Paper Empty(r0), §4.
        private bool Bisimulate(EreXor<TPred> r0)
        {
            var U = new IntUnionFind();
            var S = new Stack<EreXor<TPred>>();

            // MergeLeaf(r0): union r0's operands into one class.
            // We've already ensured !r0.Nullable upstream.
            MergeLeaf(U, r0);
            S.Push(r0);

            while (S.Count > 0)
            {
                var r = S.Pop();
                var dr = _deriv.Derivative(r);

                foreach (var (leaf, guard) in EnumerateLeaves(dr, _predAlg.Top))
                {
                    if (!_predAlg.IsSatisfiable(guard)) continue;
                    if (leaf is EreEmpty<TPred>) continue;            // ⊥ leaf

                    if (leaf is EreXor<TPred> xleaf && !xleaf.Negated)
                    {
                        // XOR-shaped leaf: bisim step.
                        if (xleaf.Nullable) return false;
                        // EREQ E-frame merge rule (Phase 6): if every
                        // operand is ∃p.body_i with the same p, and all
                        // body_i are already in the same UF class, the
                        // pair is discharged by monotonicity of ∃p — no
                        // need to expand its derivative. The outer Ids
                        // are still merged to keep the partition
                        // consistent.
                        if (_options.UseEFrameMerge
                            && TryEFrameDischarge(U, xleaf))
                        {
                            EFrameMergeFires++;
                            MergeLeaf(U, xleaf);
                            continue;
                        }
                        if (MergeLeaf(U, xleaf)) S.Push(xleaf);
                    }
                    else
                    {
                        // Non-XOR (or XNOR) leaf: fall through to plain
                        // emptiness. The transition is reachable (guard
                        // satisfiable), so if L(leaf) ≠ ∅ we have a
                        // distinguishing word.
                        if (_empt.IsAlive(leaf)) return false;
                    }
                }
            }
            return true;
        }

        // Witness-tracking variant: stack entries carry the reversed
        // path-condition list W accumulated from r0 to the current node.
        // On nullable XOR leaf:    witness = W.Push(guard)
        // On live non-XOR leaf:    witness = NonEmpty(leaf, W.Push(guard))
        // Per design: W is threaded as a parameter — NEVER attached to the
        // regex or transition-term nodes (preserves DAG sharing).
        private bool BisimulateWitness(EreXor<TPred> r0,
            out ConsList<TPred> witness)
        {
            var U = new IntUnionFind();
            var S = new Stack<(ConsList<TPred> W, EreXor<TPred> r)>();

            MergeLeaf(U, r0);
            S.Push((ConsList<TPred>.Empty, r0));

            while (S.Count > 0)
            {
                var (W, r) = S.Pop();
                var dr = _deriv.Derivative(r);

                foreach (var (leaf, guard) in EnumerateLeaves(dr, _predAlg.Top))
                {
                    if (!_predAlg.IsSatisfiable(guard)) continue;
                    if (leaf is EreEmpty<TPred>) continue;

                    if (leaf is EreXor<TPred> xleaf && !xleaf.Negated)
                    {
                        if (xleaf.Nullable)
                        {
                            witness = W.Push(guard);
                            return true;
                        }
                        // EREQ E-frame merge (Phase 6): same rule as in
                        // Bisimulate. Safe in the witness loop: when the
                        // rule fires, the leaf's language equality is
                        // proven, so we will not be hiding a witness.
                        if (_options.UseEFrameMerge
                            && TryEFrameDischarge(U, xleaf))
                        {
                            EFrameMergeFires++;
                            MergeLeaf(U, xleaf);
                            continue;
                        }
                        if (MergeLeaf(U, xleaf))
                            S.Push((W.Push(guard), xleaf));
                    }
                    else
                    {
                        if (_empt.NonEmpty(leaf, W.Push(guard), out witness))
                            return true;
                    }
                }
            }
            witness = null;
            return false;
        }

        /// <summary>
        /// EREQ Phase 6: returns <c>true</c> iff every operand of
        /// <paramref name="x"/> is an <see cref="EreExists{TPred}"/> with the
        /// same projector and every body is already in the same union-find
        /// class. In that case the bisim invariant for this leaf is already
        /// discharged by monotonicity of <c>∃p</c>.
        /// <para>Exposed publicly so unit tests can verify the predicate
        /// directly with a hand-seeded <see cref="IntUnionFind"/>.</para>
        /// <para>
        /// <b>Practical use.</b> Inside the bisim driver the rule is
        /// almost always inert (see the dormancy note on
        /// <see cref="EFrameMergeFires"/>): the smart constructor for
        /// <c>∃p</c> has typically distributed past the leaf shape. The
        /// helper is therefore most useful in two scenarios:
        /// (1) deliberate test seeding — pre-merge bodies in the UF and
        /// invoke <c>TryEFrameDischarge</c> directly on a hand-built
        /// <see cref="EreXor{TPred}"/> to assert the rule fires; and
        /// (2) custom callers that build EREQ ASTs through a non-
        /// canonicalising path and want to apply the discharge before
        /// invoking the general bisim.
        /// </para>
        /// </summary>
        public static bool TryEFrameDischarge(IntUnionFind U, EreXor<TPred> x)
        {
            var ops = x.Operands;
            if (ops.Count < 2) return false;
            if (!(ops[0] is EreExists<TPred> e0)) return false;
            int proj = e0.PropositionIndex;
            int bodyRep = U.Find(e0.Body.Id);
            for (int i = 1; i < ops.Count; i++)
            {
                if (!(ops[i] is EreExists<TPred> ei)) return false;
                if (ei.PropositionIndex != proj) return false;
                if (U.Find(ei.Body.Id) != bodyRep) return false;
            }
            return true;
        }

        /// <summary>
        /// Unions all operand-Ids of <paramref name="x"/> into one class.
        /// Returns <c>true</c> iff this changed the partition (i.e. the
        /// operands were not already all in the same class).
        /// </summary>
        private static bool MergeLeaf(IntUnionFind U, EreXor<TPred> x)
        {
            var ops = x.Operands;
            int firstId = ops[0].Id;
            int rep = U.Find(firstId);
            bool changed = false;
            for (int i = 1; i < ops.Count; i++)
            {
                int id = ops[i].Id;
                if (U.Find(id) != rep)
                {
                    U.Union(firstId, id);
                    rep = U.Find(firstId);
                    changed = true;
                }
            }
            return changed;
        }

        // Same enumeration shape as EreEmptinessChecker: walk the BDD-style
        // ITE structure of the transition term, threading the conjunction of
        // guard predicates from root to leaf.
        private IEnumerable<(Ere<TPred> leaf, TPred guard)> EnumerateLeaves(
            TransitionTerm<Ere<TPred>> term, TPred pathGuard)
        {
            if (term is TransitionTermLeaf<Ere<TPred>> leaf)
            {
                yield return (leaf.Value, pathGuard);
                yield break;
            }
            var ite = (TransitionTermIte<Ere<TPred>>)term;
            // EREQ proposition splits (negative indices) carry no associated
            // predicate; both branches are always reachable, mirroring the
            // EreEmptinessChecker pattern.
            if (ConditionRegistry<TPred>.IsProposition(ite.ConditionIndex))
            {
                foreach (var t in EnumerateLeaves(ite.Hi, pathGuard)) yield return t;
                foreach (var t in EnumerateLeaves(ite.Lo, pathGuard)) yield return t;
                yield break;
            }
            var pred = _deriv.TermAlgebra.Registry.GetPredicate(ite.ConditionIndex);

            var hiGuard = _predAlg.And(pathGuard, pred);
            if (_predAlg.IsSatisfiable(hiGuard))
                foreach (var t in EnumerateLeaves(ite.Hi, hiGuard))
                    yield return t;

            var loGuard = _predAlg.And(pathGuard, _predAlg.Not(pred));
            if (_predAlg.IsSatisfiable(loGuard))
                foreach (var t in EnumerateLeaves(ite.Lo, loGuard))
                    yield return t;
        }
    }
}
