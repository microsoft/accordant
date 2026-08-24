namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Macrostate reducer based on transition-term equality (JACM
    /// Example 5.1 state-reduction lemma): two universal copies
    /// <c>q₁, q₂ ∈ S</c> are interchangeable in the breakpoint
    /// construction whenever
    /// <list type="bullet">
    ///   <item>they have <em>identical</em> transition terms
    ///         <c>δ(q₁) ≡ δ(q₂)</c> (weak equivalence collapses to
    ///         syntactic equality in the simple case, which is what we
    ///         check here — the ADD nodes returned by
    ///         <see cref="SymbolicABW{TPred,TElem,TState}.GetTransition"/>
    ///         are interned so equal terms are reference-equal
    ///         <see cref="TransitionTerm{TLeaf}"/> objects), and</item>
    ///   <item>they share the same colour per <see cref="RltlColour"/>
    ///         (same membership in the ABW's co-Büchi rejecting set
    ///         <c>F</c>), which preserves the breakpoint obligation
    ///         tracking.</item>
    /// </list>
    ///
    /// <para>
    /// One representative is kept per equivalence class, chosen as the
    /// element minimal under the macrostate's own
    /// <see cref="StateSet{TState}.Comparer"/> (deterministic,
    /// idempotent). The resulting <see cref="MacroReduction{TState}"/>
    /// carries a representative map so that
    /// <see cref="IncrementalAE{TPred,TElem,TState}.BuildCrossDnf"/> can
    /// forward obligations from dropped states onto their surviving
    /// representative — this is necessary for soundness because the
    /// dropped state might have been the only carrier of a particular
    /// breakpoint obligation.
    /// </para>
    ///
    /// <para>
    /// Unlike a language-subsumption rule, this check is purely
    /// structural: identical transition terms imply identical futures
    /// by construction, so the reduction is sound without any
    /// language-theoretic argument and triggers exactly when JACM
    /// Example 5.1 says it should — including on
    /// <c>GFa ∧ GF(¬a)</c>, where after a few steps the two universal
    /// copies of <c>F·</c> and <c>GF·</c> produce coinciding derivative
    /// terms.
    /// </para>
    /// </summary>
    public sealed class RltlMacrostateTransitionMerge<TPred, TElem>
    {
        private readonly Func<Rltl<TPred>, TransitionTerm<Dnf<Rltl<TPred>>>> _getDelta;

        /// <summary>
        /// Construct with a per-state transition lookup. Typical usage
        /// passes <c>abw.GetTransition</c> so the cached interned terms
        /// are reused.
        /// </summary>
        public RltlMacrostateTransitionMerge(
            Func<Rltl<TPred>, TransitionTerm<Dnf<Rltl<TPred>>>> getDelta)
        {
            _getDelta = getDelta ?? throw new ArgumentNullException(nameof(getDelta));
        }

        /// <summary>
        /// Bucket <paramref name="s"/> by <c>(δ(q), colour(q))</c> and
        /// keep the comparator-minimum element per bucket; map every
        /// dropped state to its bucket's representative.
        /// </summary>
        public MacroReduction<Rltl<TPred>> Reduce(StateSet<Rltl<TPred>> s)
        {
            if (s.Count <= 1) return MacroReduction<Rltl<TPred>>.Identity(s);
            var comparer = s.Comparer;

            // First pass: pick a representative per bucket.
            var reps = new Dictionary<(TransitionTerm<Dnf<Rltl<TPred>>>, bool), Rltl<TPred>>();
            foreach (var q in s)
            {
                var key = (_getDelta(q), RltlColour.IsRejecting(q));
                if (!reps.TryGetValue(key, out var current)
                    || comparer.Compare(q, current) < 0)
                {
                    reps[key] = q;
                }
            }
            if (reps.Count == s.Count) return MacroReduction<Rltl<TPred>>.Identity(s);

            // Second pass: build the rep map (only for non-survivors).
            var repMap = new Dictionary<Rltl<TPred>, Rltl<TPred>>();
            foreach (var q in s)
            {
                var rep = reps[(_getDelta(q), RltlColour.IsRejecting(q))];
                if (!ReferenceEquals(rep, q) && !rep.Equals(q))
                    repMap[q] = rep;
            }

            return new MacroReduction<Rltl<TPred>>(
                new StateSet<Rltl<TPred>>(reps.Values, comparer),
                repMap);
        }
    }
}
