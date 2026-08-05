namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System.Collections.Generic;

    /// <summary>
    /// Hash-cons builder for <see cref="Rltl{TPred}"/> terms. Mirrors
    /// <see cref="EreBuilder{TPred}"/>: structurally equal RLTL formulas
    /// become the same object (reference equality) and share a unique
    /// non-negative integer <see cref="Rltl{TPred}.Id"/>.
    ///
    /// <para>Reserved ids: <c>0 = ⊥ (RltlFalse)</c>, <c>1 = ⊤ (RltlTrue)</c>.</para>
    ///
    /// <para>Storage layout matches the ERE builder: a dense <c>_byId</c>
    /// list (direct array indexing once an Id is known) plus a
    /// <see cref="Dictionary{TKey, TValue}"/> consulted only during
    /// <see cref="Intern"/> to dedup-on-construction via the existing
    /// structural Equals/GetHashCode on <see cref="Rltl{TPred}"/>.</para>
    /// </summary>
    public sealed class RltlBuilder<TPred>
    {
        private readonly List<Rltl<TPred>> _byId = new List<Rltl<TPred>>();
        private readonly Dictionary<Rltl<TPred>, Rltl<TPred>> _intern =
            new Dictionary<Rltl<TPred>, Rltl<TPred>>();

        public RltlBuilder()
        {
            Assign(RltlFalse<TPred>.Instance);
            Assign(RltlTrue<TPred>.Instance);
        }

        /// <summary>The canonical False (⊥) formula, always at Id 0.</summary>
        public Rltl<TPred> False => _byId[0];

        /// <summary>The canonical True (⊤) formula, always at Id 1.</summary>
        public Rltl<TPred> True => _byId[1];

        /// <summary>Number of distinct canonical formulas currently stored.</summary>
        public int Count => _byId.Count;

        public Rltl<TPred> Get(int id) => _byId[id];

        public Rltl<TPred> Intern(Rltl<TPred> candidate)
        {
            if (candidate == null) return null;
            if (candidate.HasId) return _byId[candidate.Id];
            if (_intern.TryGetValue(candidate, out var existing)) return existing;
            Assign(candidate);
            return candidate;
        }

        private void Assign(Rltl<TPred> term)
        {
            term.AssignId(_byId.Count);
            _byId.Add(term);
            _intern[term] = term;
        }
    }
}
