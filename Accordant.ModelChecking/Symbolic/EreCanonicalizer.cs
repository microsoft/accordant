namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Canonicalises an <see cref="Ere{TPred}"/> by language-equivalence:
    /// two regexes whose recognised languages are equal map to a single
    /// reference-equal representative.
    ///
    /// <para>
    /// Used by <see cref="RltlAlgebra{TPred}"/> at construction time to
    /// canonicalise the regex sub-formula of <c>R;φ</c>, <c>R:φ</c>,
    /// <c>R⊳φ</c>, <c>R⊳⊳φ</c>, <c>(R)^F</c>, <c>(R)^¬F</c>, <c>(R)^ω</c>.
    /// Combined with RLTL hash-consing this gives RLTL structural equality
    /// modulo ERE equivalence: two RLTL formulas that differ only by
    /// language-equivalent embedded regexes become reference-equal, which
    /// is the cheap and effective form of equivalence-as-state-reduction
    /// for tableau / closure dedup (G8-c).
    /// </para>
    ///
    /// <para>
    /// The element type <typeparamref name="TPred"/>-only surface lets
    /// <see cref="RltlAlgebra{TPred}"/> consume a canonicaliser without
    /// having to also be parametric in the EBA's element universe.
    /// </para>
    /// </summary>
    public interface IEreCanonicalizer<TPred>
    {
        /// <summary>
        /// Returns a canonical representative of <paramref name="r"/>'s
        /// equivalence class. The first regex submitted from a class wins
        /// and is returned for all subsequent equivalent inputs.
        /// </summary>
        Ere<TPred> Canonicalize(Ere<TPred> r);
    }

    /// <summary>
    /// Equivalence-class-based canonicaliser for <see cref="Ere{TPred}"/>
    /// backed by <see cref="EreEquivalenceChecker{TPred,TElem}"/>.
    ///
    /// <para>
    /// Maintains an input→representative cache so already-seen regexes are
    /// returned in O(1). For unseen regexes a linear scan of existing
    /// representatives is performed; representatives are kept compact by
    /// hash-consing on Ere references and by short-circuiting on
    /// <c>Ere.Equals</c> before invoking the (more expensive) bisim check.
    /// </para>
    /// </summary>
    public sealed class EreCanonicalizer<TPred, TElem> : IEreCanonicalizer<TPred>
    {
        private readonly EreEquivalenceChecker<TPred, TElem> _checker;
        private readonly List<Ere<TPred>> _representatives;
        private readonly Dictionary<Ere<TPred>, Ere<TPred>> _cache;

        public EreCanonicalizer(EreEquivalenceChecker<TPred, TElem> checker)
        {
            _checker = checker ?? throw new ArgumentNullException(nameof(checker));
            _representatives = new List<Ere<TPred>>();
            _cache = new Dictionary<Ere<TPred>, Ere<TPred>>();
        }

        /// <summary>The representatives discovered so far (one per equivalence class).</summary>
        public IReadOnlyList<Ere<TPred>> Representatives => _representatives;

        /// <summary>Number of distinct equivalence classes discovered.</summary>
        public int ClassCount => _representatives.Count;

        public Ere<TPred> Canonicalize(Ere<TPred> r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (_cache.TryGetValue(r, out var cached)) return cached;

            foreach (var rep in _representatives)
            {
                if (ReferenceEquals(r, rep) || r.Equals(rep) || _checker.AreEquivalent(r, rep))
                {
                    _cache[r] = rep;
                    return rep;
                }
            }

            _representatives.Add(r);
            _cache[r] = r;
            return r;
        }
    }
}
