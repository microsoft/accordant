namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Symbolic RLTL model checking. Mirrors <see cref="SymbolicLtlCheck"/>
    /// but accepts an RLTL property (Section 7 of the POPL'25 paper: LTL
    /// extended with extended-regular-expression prefix operators
    /// <c>R;φ</c>, <c>R:φ</c>, <c>R⊳φ</c>, <c>R⊳⊳φ</c>).
    ///
    /// Algorithm:
    /// <list type="number">
    ///   <item>Negate the property: ¬φ (RLTL <see cref="Rltl{TPred}.Negate"/>
    ///   keeps NNF using the regex-operator duals).</item>
    ///   <item>Build a symbolic ABW for ¬φ via
    ///   <see cref="RltlDerivative{TPred,TElem}.ToABW"/>.</item>
    ///   <item>Eliminate alternation with the incremental Miyano-Hayashi
    ///   construction to obtain a lazy NBW whose states are
    ///   <see cref="BreakpointState{TState}"/> over <see cref="Rltl{TPred}"/>.
    ///   </item>
    ///   <item>Check emptiness of the product (System × NBW). Two strategies
    ///   are available: <see cref="NestedDfsCheck"/> (linear-space nested
    ///   DFS, used when no fairness is requested) and
    ///   <see cref="SccProductCheck"/> (Tarjan SCCs, used whenever a
    ///   <see cref="Fairness"/> constraint is supplied — fairness is
    ///   inherently an SCC-level property).</item>
    /// </list>
    /// </summary>
    public static class SymbolicRltlCheck
    {
        /// <summary>
        /// Check an RLTL property against a model program.
        /// </summary>
        /// <param name="root">Root of the state graph (model program).</param>
        /// <param name="property">The RLTL property φ to check (should hold).</param>
        /// <param name="maxDepth">Maximum exploration depth (0 = unlimited).</param>
        /// <param name="fairness">Optional fairness constraint. When
        ///   non-<c>null</c> and not <see cref="Fairness.None"/>, emptiness
        ///   is checked with <see cref="SccProductCheck"/> so that only
        ///   <i>fair</i> accepting cycles count as counterexamples.</param>
        /// <param name="dedupTableau">When <c>true</c>, enables runtime
        ///   ERE-equivalence-based deduplication of RLTL closure / tableau
        ///   nodes. Residual regexes appearing in derivative leaves (e.g.
        ///   the <c>R'</c> in <c>R';φ</c>) are canonicalised by language
        ///   equivalence; combined with RLTL hash-consing, two RLTL atoms
        ///   that differ only by an equivalent embedded regex collapse to
        ///   the same ABW state. Off by default so existing baselines are
        ///   unchanged.</param>
        /// <param name="minimizeNbw">When <c>true</c>, additionally enables
        ///   RLTL-language-equivalence-based canonicalisation of every RLTL
        ///   atom emitted by the symbolic derivative. Two ABW / NBW states
        ///   whose underlying RLTL formula has the same ω-language collapse
        ///   to a single canonical representative (state minimisation by
        ///   precise equivalence via <see cref="RltlLanguageEquivalence"/>).
        ///   This is strictly more powerful — and considerably more expensive
        ///   — than <paramref name="dedupTableau"/>; off by default. When
        ///   set, <paramref name="dedupTableau"/> is implicitly enabled
        ///   alongside.</param>
        /// <param name="mergeWeakEquivalent">When <c>true</c>, every freshly
        ///   produced breakpoint state <c>(S,O)</c> in the alternation
        ///   elimination is canonicalised by <i>weak language equivalence</i>
        ///   against the breakpoint states already discovered: two breakpoints
        ///   whose macrostate conjunctions are language-equivalent and whose
        ///   obligation conjunctions are language-equivalent are merged. This
        ///   is the JACM Example 5.1 state-reduction step (e.g. it collapses
        ///   <c>G(Fa∧F¬a)</c>'s NBW from 8 to 3 reachable states). When set,
        ///   both <paramref name="dedupTableau"/> and
        ///   <paramref name="minimizeNbw"/> are implicitly enabled alongside —
        ///   without the per-atom canonicaliser, macrostate elements would
        ///   themselves differ syntactically and defeat the merge. Off by
        ///   default; very expensive (one NBW emptiness check per existing
        ///   representative, per new breakpoint, for both S and O).</param>
        /// <param name="mergeWeakEquivalentBp">When <c>true</c>, applies a
        ///   bisimulation-style minimisation to the fully-constructed
        ///   breakpoint NBW (JACM Lemma 5.x, state-reduction): two NBW
        ///   states are merged iff they share acceptance status and, after
        ///   recursively rewriting successor leaves to class
        ///   representatives, have structurally equal transition lists.
        ///   This is the structural counterpart of
        ///   <paramref name="mergeWeakEquivalent"/> — it works on the
        ///   constructed graph rather than per-pair language-equivalence
        ///   queries, and stays tractable on inputs (e.g.
        ///   <c>GFa ∧ GFb ∧ GFc</c>) where the language-level merge
        ///   times out. Off by default.</param>
        /// <param name="subsumeMacrostate">When <c>true</c>, applies the
        ///   structural macrostate-merge rule
        ///   <see cref="RltlMacrostateTransitionMerge{TPred,TElem}"/>
        ///   (JACM Example 5.1 state-reduction lemma): in every
        ///   freshly-produced macrostate <c>S</c>, universal copies
        ///   <c>q₁, q₂</c> with identical transition terms
        ///   <c>δ(q₁) = δ(q₂)</c> and matching colour
        ///   (<see cref="RltlColour"/>) are collapsed to a single
        ///   representative; obligation membership is forwarded onto the
        ///   representative. Per-macrostate cost is O(|S|) with ADD-node
        ///   reference equality. Off by default.</param>
        /// <returns>Result with counterexample if the property is violated.</returns>
        public static PropertyCheckingResult Check(
            StateGraphNode root,
            Rltl<IStatePredicate> property,
            int maxDepth = 0,
            Fairness fairness = null,
            bool dedupTableau = false,
            bool minimizeNbw = false,
            bool mergeWeakEquivalent = false,
            bool subsumeMacrostate = false,
            bool mergeWeakEquivalentBp = false)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (property == null) throw new ArgumentNullException(nameof(property));

            // Weak-equivalence merging implies the cheaper canonicalisations.
            if (mergeWeakEquivalent)
            {
                minimizeNbw = true;
                dedupTableau = true;
            }

            var eba = StatePropEbaProvider.Default;
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);

            IEreCanonicalizer<IStatePredicate> ereCanon = null;
            IRltlCanonicalizer<IStatePredicate> rltlCanon = null;
            RltlAlgebra<IStatePredicate> algebra = RltlAlgebra.Default;
            if (dedupTableau || minimizeNbw)
            {
                var ereDeriv = new EreDerivative<IStatePredicate, State>(eba, registry);
                var checker = new EreEquivalenceChecker<IStatePredicate, State>(ereDeriv);
                ereCanon = new EreCanonicalizer<IStatePredicate, State>(checker);
                algebra = new RltlAlgebra<IStatePredicate>(eba, ereCanon);
            }
            if (minimizeNbw)
            {
                rltlCanon = new RltlCanonicalizer<IStatePredicate, State>(eba, algebra);
            }

            // 1. Negate (keeps NNF via De Morgan + regex-operator duals).
            var negPhi = algebra.Not(property);

            // 2. ABW for ¬φ via the RLTL symbolic derivative.
            var derivative = new RltlDerivative<IStatePredicate, State>(
                eba, registry, ereCanon, rltlCanon);
            var abw = derivative.ToABW(negPhi);

            // 3. Incremental Æ → lazy NBW (IncrementalAE is generic in TState).
            Func<BreakpointState<Rltl<IStatePredicate>>, BreakpointState<Rltl<IStatePredicate>>> bpCanon = null;
            if (mergeWeakEquivalent)
            {
                var merger = new RltlBreakpointCanonicalizer<IStatePredicate, State>(eba, algebra);
                bpCanon = merger.Canonicalize;
            }
            Func<StateSet<Rltl<IStatePredicate>>, MacroReduction<Rltl<IStatePredicate>>> macroReducer = null;
            if (subsumeMacrostate)
            {
                var merger = new RltlMacrostateTransitionMerge<IStatePredicate, State>(
                    abw.GetTransition);
                macroReducer = merger.Reduce;
            }
            var incAE = new IncrementalAE<IStatePredicate, State, Rltl<IStatePredicate>>(
                abw, bpCanon, macroReducer);
            var nbw = incAE.ToNBW();

            // Lightweight on-the-fly leaf dedup (JACM Lemma 5.x-flavoured but
            // shallow — not a partition fixpoint). Replaces a freshly-
            // discovered breakpoint with an existing one whenever their
            // transition terms are structurally identical and they share the
            // same accepting colour. Lazy: drives the underlying
            // IncrementalAE on demand, never expanding aliased BPs further.
            if (mergeWeakEquivalentBp)
            {
                var bpEq = BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer();
                var bpOrd = BreakpointState<Rltl<IStatePredicate>>.GetComparer(
                    Comparer<Rltl<IStatePredicate>>.Default);
                nbw = BpWeakEquivalenceMinimizer.DedupOnTheFly(nbw, bpEq, bpOrd);
            }

            // 4. Pick the emptiness check based on fairness.
            var bpComparer = BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer();
            bool useSCC = fairness != null && !ReferenceEquals(fairness, Fairness.None);
            return useSCC
                ? SccProductCheck.Check(root, nbw, maxDepth, bpComparer, fairness)
                : NestedDfsCheck.Check(root, nbw, maxDepth, bpComparer);
        }
    }
}
