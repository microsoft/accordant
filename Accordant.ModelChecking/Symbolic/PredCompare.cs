namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Total order over <typeparamref name="TPred"/> that is consistent with
    /// <see cref="EqualityComparer{T}.Default"/>: <c>Compare(a,b) == 0</c> if
    /// and only if <c>Equals(a,b)</c>.
    /// <para>
    /// This is required for ACI normalization of formulas that use
    /// <see cref="System.Collections.Generic.SortedSet{T}"/> for deduplicating
    /// disjuncts/conjuncts. A comparer that ordered predicates by
    /// <c>GetHashCode</c> alone would (a) be inconsistent with <c>Equals</c>
    /// on hash collisions and (b) silently drop one of two genuinely different
    /// predicates whose hashes collide.
    /// </para>
    /// <para>
    /// Strategy:
    /// <list type="number">
    /// <item>If <typeparamref name="TPred"/> implements
    ///       <see cref="IComparable{T}"/>, use it.</item>
    /// <item>Otherwise, if <c>Equals(a,b)</c>, return 0.</item>
    /// <item>Otherwise, order by <c>GetHashCode</c>; on hash collision (which
    ///       implies the values are NOT equal here), tiebreak on
    ///       <see cref="object.ToString"/>.</item>
    /// </list>
    /// In every case the equivalence
    /// <c>Compare(a,b)==0 ⇔ EqualityComparer&lt;TPred&gt;.Default.Equals(a,b)</c>
    /// is preserved, which is what <c>SortedSet</c>-based dedup relies on.
    /// </para>
    /// </summary>
    internal static class PredCompare<TPred>
    {
        private static readonly bool _useDefault =
            typeof(IComparable<TPred>).IsAssignableFrom(typeof(TPred)) ||
            typeof(IComparable).IsAssignableFrom(typeof(TPred));

        public static int Compare(TPred a, TPred b)
        {
            // Fast / always-correct path for equal values.
            if (EqualityComparer<TPred>.Default.Equals(a, b)) return 0;

            // Use the natural ordering when available.
            if (_useDefault)
            {
                int c = Comparer<TPred>.Default.Compare(a, b);
                if (c != 0) return c;
                // Defensive: Compare returned 0 but Equals returned false
                // (unusual but not forbidden — fall through).
            }

            // Last-resort deterministic order: hash, then string repr.
            int hc = EqualityComparer<TPred>.Default.GetHashCode(a)
                     .CompareTo(EqualityComparer<TPred>.Default.GetHashCode(b));
            if (hc != 0) return hc;
            return string.Compare(
                a == null ? null : a.ToString(),
                b == null ? null : b.ToString(),
                StringComparison.Ordinal);
        }
    }
}
