namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System.Collections.Generic;

    /// <summary>
    /// Hash-cons builder for <see cref="Ere{TPred}"/> terms.
    ///
    /// <para>Every constructed ERE is interned: structurally equal terms
    /// become the same object (reference equality) and share a unique
    /// non-negative integer <see cref="Ere{TPred}.Id"/>. <c>Id 0</c> is
    /// reserved for <see cref="EreEmpty{TPred}"/> ("Bottom"), <c>Id 1</c>
    /// for <see cref="EreEpsilon{TPred}"/>.</para>
    ///
    /// <para>Storage layout:</para>
    /// <list type="bullet">
    ///   <item><c>_byId</c>: dense array (<see cref="List{T}"/>) of canonical
    ///   terms indexed directly by <see cref="Ere{TPred}.Id"/>. Holders of
    ///   an Id resolve to the term in O(1) without hashing.</item>
    ///   <item><c>_intern</c>: <see cref="HashSet{T}"/> used only during
    ///   <see cref="Intern"/> to dedup-on-construction via the existing
    ///   structural Equals/GetHashCode on <see cref="Ere{TPred}"/>.</item>
    /// </list>
    ///
    /// <para>One builder is shared per closed <typeparamref name="TPred"/>
    /// type via <see cref="Ere{TPred}.DefaultBuilder"/>; all static
    /// factories on <see cref="Ere{TPred}"/> route through it. Direct use
    /// of the builder API is optional; the static factories give the same
    /// canonicalisation guarantees.</para>
    /// </summary>
    public sealed class EreBuilder<TPred>
    {
        private readonly List<Ere<TPred>> _byId = new List<Ere<TPred>>();
        private readonly Dictionary<Ere<TPred>, Ere<TPred>> _intern =
            new Dictionary<Ere<TPred>, Ere<TPred>>();
        private IPredicateAlgebra<TPred> _algebra;

        public EreBuilder()
        {
            // Reserve canonical slots for the two structural singletons so
            // their Ids are stable across builder instances (Bottom = 0).
            Assign(EreEmpty<TPred>.Instance);
            Assign(EreEpsilon<TPred>.Instance);
        }

        /// <summary>
        /// The optional predicate algebra for this builder. When set, smart
        /// constructors may use it to combine predicates (e.g. <c>[p]|[q] →
        /// [p∨q]</c>, <c>~([p]·Σ*) → [¬p]·Σ* | ε</c>). When <c>null</c>, all
        /// algebra-dependent rewrites are skipped and the term is preserved
        /// as-is — soundness is unaffected, only the canonicalisation degree
        /// changes. Register via <see cref="RegisterAlgebra"/>.
        /// </summary>
        public IPredicateAlgebra<TPred> Algebra => _algebra;

        /// <summary>
        /// Bind an <see cref="IPredicateAlgebra{TPred}"/> instance to this
        /// builder. Idempotent for the same instance; throws if a different
        /// algebra is already bound. Typically called once per
        /// <typeparamref name="TPred"/> at startup.
        /// </summary>
        public void RegisterAlgebra(IPredicateAlgebra<TPred> algebra)
        {
            if (algebra == null) throw new System.ArgumentNullException(nameof(algebra));
            if (_algebra != null && !object.ReferenceEquals(_algebra, algebra))
                throw new System.InvalidOperationException(
                    "An IPredicateAlgebra<TPred> is already registered on this builder.");
            _algebra = algebra;
        }

        /// <summary>The canonical Bottom term (<c>∅</c>), always at Id 0.</summary>
        public Ere<TPred> Bottom => _byId[0];

        /// <summary>The canonical Epsilon term (<c>ε</c>), always at Id 1.</summary>
        public Ere<TPred> Epsilon => _byId[1];

        /// <summary>Number of distinct canonical terms currently stored.</summary>
        public int Count => _byId.Count;

        /// <summary>
        /// Resolve a term by its Id. O(1) array indexing.
        /// </summary>
        public Ere<TPred> Get(int id) => _byId[id];

        /// <summary>
        /// Return the canonical instance equal to <paramref name="candidate"/>,
        /// allocating a fresh Id if this shape has not been seen yet.
        /// </summary>
        public Ere<TPred> Intern(Ere<TPred> candidate)
        {
            if (candidate == null) return null;
            if (candidate.HasId) return _byId[candidate.Id];
            if (_intern.TryGetValue(candidate, out var existing)) return existing;
            Assign(candidate);
            return candidate;
        }

        private void Assign(Ere<TPred> term)
        {
            term.AssignId(_byId.Count);
            _byId.Add(term);
            _intern[term] = term;
        }
    }
}
