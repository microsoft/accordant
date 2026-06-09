namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Canonicalises an <see cref="Rltl{TPred}"/> formula by language
    /// equivalence: two formulas whose recognised ω-languages are equal map
    /// to a single reference-equal representative.
    ///
    /// <para>
    /// Used by <see cref="RltlDerivative{TPred,TElem}"/> at derivative time
    /// to canonicalise newly created RLTL atoms appearing as
    /// <see cref="Dnf{TLeaf}"/> leaves of the transition term. Combined
    /// with the breakpoint NBW construction in
    /// <see cref="IncrementalAE{TPredicate,TElement,TState}"/>, this
    /// collapses NBW states whose acceptance-language residuals are
    /// equivalent — the "state minimisation via precise equivalence" pass.
    /// </para>
    /// </summary>
    public interface IRltlCanonicalizer<TPred>
    {
        /// <summary>
        /// Returns a canonical representative of <paramref name="f"/>'s
        /// language-equivalence class. The first formula submitted from a
        /// class wins and is returned for all subsequent equivalent inputs.
        /// </summary>
        Rltl<TPred> Canonicalize(Rltl<TPred> f);
    }

    /// <summary>
    /// Equivalence-class-based canonicaliser for <see cref="Rltl{TPred}"/>
    /// backed by <see cref="RltlLanguageEquivalence"/> (the sound+complete
    /// oracle, modulo the underlying EBA's <c>IsSatisfiable</c> precision).
    ///
    /// <para>
    /// Maintains an input→representative cache so already-seen formulas are
    /// returned in O(1). For unseen formulas a linear scan of existing
    /// representatives is performed. <see cref="RltlLanguageEquivalence"/>
    /// internally constructs vanilla (non-canonicalising) RLTL derivative
    /// engines, so the canonicaliser is non-recursive.
    /// </para>
    ///
    /// <para>
    /// The lookup cost per unique formula is one NBW emptiness check per
    /// existing representative; this is expensive, so canonicalisation is
    /// strictly opt-in. Combined with the cheaper ERE-level canonicaliser
    /// (<see cref="EreCanonicalizer{TPred,TElem}"/>) it provides
    /// equivalence-as-state-reduction beyond what hash-consing + ERE
    /// canonicalisation alone can achieve (e.g. LTL-level identities not
    /// captured by the <see cref="RltlAlgebra{TPred}"/> smart constructors).
    /// </para>
    /// </summary>
    public sealed class RltlCanonicalizer<TPred, TElem> : IRltlCanonicalizer<TPred>
    {
        private readonly IEffectiveBooleanAlgebra<TPred, TElem> _eba;
        private readonly RltlAlgebra<TPred> _algebra;
        private readonly List<Rltl<TPred>> _representatives;
        private readonly Dictionary<Rltl<TPred>, Rltl<TPred>> _cache;

        public RltlCanonicalizer(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            RltlAlgebra<TPred> algebra)
        {
            _eba = eba ?? throw new ArgumentNullException(nameof(eba));
            _algebra = algebra ?? throw new ArgumentNullException(nameof(algebra));
            _representatives = new List<Rltl<TPred>>();
            _cache = new Dictionary<Rltl<TPred>, Rltl<TPred>>();
        }

        /// <summary>The representatives discovered so far (one per equivalence class).</summary>
        public IReadOnlyList<Rltl<TPred>> Representatives => _representatives;

        /// <summary>Number of distinct equivalence classes discovered.</summary>
        public int ClassCount => _representatives.Count;

        public Rltl<TPred> Canonicalize(Rltl<TPred> f)
        {
            if (f == null) throw new ArgumentNullException(nameof(f));
            if (_cache.TryGetValue(f, out var cached)) return cached;

            foreach (var rep in _representatives)
            {
                if (ReferenceEquals(f, rep)
                    || RltlLanguageEquivalence.AreEquivalent(_eba, _algebra, f, rep))
                {
                    _cache[f] = rep;
                    return rep;
                }
            }

            _representatives.Add(f);
            _cache[f] = f;
            return f;
        }
    }
}
