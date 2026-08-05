namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// On-the-fly canonicaliser for <see cref="BreakpointState{TState}"/>
    /// whose <c>TState</c> is an <see cref="Rltl{TPred}"/> formula, used to
    /// implement the "weak equivalence merging" step of the alternation
    /// elimination (Æ) construction — i.e. the state-reduction lemma of
    /// JACM Example 5.1.
    ///
    /// <para>
    /// Two breakpoint states <c>(S,O)</c> and <c>(S',O')</c> are considered
    /// <i>weakly equivalent</i> when their conjunctive RLTL "meanings"
    /// coincide as ω-languages:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>⋀S ≡ ⋀S'</c> (language equivalence of macrostate conjunction)</description></item>
    ///   <item><description><c>⋀O ≡ ⋀O'</c> (language equivalence of obligation conjunction)</description></item>
    /// </list>
    ///
    /// <para>
    /// Both equivalences are decided by
    /// <see cref="RltlLanguageEquivalence.AreEquivalent{TPred,TElem}"/>
    /// (sound + complete modulo the EBA's <c>IsSatisfiable</c> precision).
    /// The canonicaliser keeps a list of discovered representatives and a
    /// per-input cache; a fresh <c>(S,O)</c> matched against an existing
    /// representative collapses to it, otherwise it becomes the representative
    /// of a new class.
    /// </para>
    ///
    /// <para>
    /// This is the macrostate-level companion of the per-atom
    /// <see cref="RltlCanonicalizer{TPred,TElem}"/>: the per-atom canonicaliser
    /// can never see, for instance, that <c>{Fa, G(Fa∧F¬a)}</c> is language-
    /// equivalent to <c>{G(Fa∧F¬a)}</c> because the conjunction subsumes the
    /// loose <c>Fa</c>. The breakpoint canonicaliser does see it by
    /// conjoining the set and comparing as ω-languages.
    /// </para>
    ///
    /// <para>
    /// Cost: each unique new breakpoint triggers up to <c>K</c> NBW emptiness
    /// checks (where <c>K</c> is the current number of representative classes,
    /// times two — one for S, one for O). Strictly opt-in. See
    /// <see cref="SymbolicRltlCheck"/>'s <c>mergeWeakEquivalent</c> flag.
    /// </para>
    /// </summary>
    public sealed class RltlBreakpointCanonicalizer<TPred, TElem>
    {
        private readonly IEffectiveBooleanAlgebra<TPred, TElem> _eba;
        private readonly RltlAlgebra<TPred> _algebra;
        private readonly List<BreakpointState<Rltl<TPred>>> _representatives;
        private readonly Dictionary<BreakpointState<Rltl<TPred>>, BreakpointState<Rltl<TPred>>> _cache;

        public RltlBreakpointCanonicalizer(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            RltlAlgebra<TPred> algebra)
        {
            _eba = eba ?? throw new ArgumentNullException(nameof(eba));
            _algebra = algebra ?? throw new ArgumentNullException(nameof(algebra));
            _representatives = new List<BreakpointState<Rltl<TPred>>>();
            _cache = new Dictionary<BreakpointState<Rltl<TPred>>, BreakpointState<Rltl<TPred>>>(
                BreakpointState<Rltl<TPred>>.GetEqualityComparer());
        }

        /// <summary>The representatives discovered so far (one per equivalence class).</summary>
        public IReadOnlyList<BreakpointState<Rltl<TPred>>> Representatives => _representatives;

        /// <summary>Number of distinct weak-equivalence classes discovered.</summary>
        public int ClassCount => _representatives.Count;

        /// <summary>
        /// Returns the canonical representative of <paramref name="bp"/>'s
        /// weak-equivalence class. The first breakpoint submitted from a class
        /// wins and is returned for all subsequent equivalent inputs.
        /// </summary>
        public BreakpointState<Rltl<TPred>> Canonicalize(BreakpointState<Rltl<TPred>> bp)
        {
            if (bp == null) throw new ArgumentNullException(nameof(bp));
            if (_cache.TryGetValue(bp, out var cached)) return cached;

            var sConj = ConjoinSet(bp.Macrostate);
            var oConj = ConjoinSet(bp.Obligation);

            foreach (var rep in _representatives)
            {
                var sRepConj = ConjoinSet(rep.Macrostate);
                if (!RltlLanguageEquivalence.AreEquivalent(_eba, _algebra, sConj, sRepConj))
                    continue;
                var oRepConj = ConjoinSet(rep.Obligation);
                if (!RltlLanguageEquivalence.AreEquivalent(_eba, _algebra, oConj, oRepConj))
                    continue;
                _cache[bp] = rep;
                return rep;
            }

            _representatives.Add(bp);
            _cache[bp] = bp;
            return bp;
        }

        // Conjunction of an RLTL state set; empty set ↦ True (vacuous).
        private Rltl<TPred> ConjoinSet(StateSet<Rltl<TPred>> set)
        {
            Rltl<TPred> acc = null;
            foreach (var x in set)
                acc = acc == null ? x : _algebra.And(acc, x);
            return acc ?? _algebra.True;
        }
    }
}
