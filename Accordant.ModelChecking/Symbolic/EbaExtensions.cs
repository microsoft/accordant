namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// Capability-probing helpers for <see cref="IPredicateAlgebra{T}"/> /
    /// <see cref="IEffectiveBooleanAlgebra{TP,TE}"/>. Algorithms that wish
    /// to take advantage of a precise decision procedure when one is
    /// available — but stay correct when it is not — call into these
    /// extension methods rather than the interface members directly.
    ///
    /// <para>Each method first checks for the corresponding
    /// <c>…Ex</c> interface (<see cref="IPredicateAlgebraEx{T}"/> or
    /// <see cref="IEffectiveBooleanAlgebraEx{TP,TE}"/>); if found, it
    /// delegates. Otherwise it falls back to a sound default expressed
    /// in terms of <see cref="IPredicateAlgebra{T}.IsSatisfiable"/>.</para>
    ///
    /// <para><b>Defaults</b>:</para>
    /// <list type="bullet">
    ///   <item><c>AreEquivalent(a, b) := !IsSatisfiable((a ∧ ¬b) ∨ (¬a ∧ b))</c></item>
    ///   <item><c>Implies(a, b) := !IsSatisfiable(a ∧ ¬b)</c></item>
    ///   <item><c>TryGetModel(p) := (false, default)</c> — no model.</item>
    /// </list>
    ///
    /// <para>The fallback for <c>AreEquivalent</c> / <c>Implies</c> is
    /// conservative-correct under the standing EBA contract that
    /// <see cref="IPredicateAlgebra{T}.IsSatisfiable"/> may be a
    /// conservative-true approximation (it never reports false for a
    /// genuinely satisfiable predicate). When <c>IsSatisfiable</c> is
    /// only conservative-true the fallbacks may return <c>false</c> for
    /// equivalent inputs — i.e. they are sound but incomplete. SMT-backed
    /// EBAs override and get precise results in one solver call.</para>
    /// </summary>
    public static class EbaExtensions
    {
        /// <summary>
        /// Decide whether two predicates denote the same set of elements.
        /// Uses <see cref="IPredicateAlgebraEx{T}.AreEquivalent"/> when
        /// the algebra implements it; otherwise <c>!IsSatisfiable(a ⊕ b)</c>.
        /// </summary>
        public static bool AreEquivalent<T>(this IPredicateAlgebra<T> algebra, T a, T b)
        {
            if (algebra == null) throw new System.ArgumentNullException(nameof(algebra));
            if (algebra is IPredicateAlgebraEx<T> ex) return ex.AreEquivalent(a, b);
            // Fallback: a ⊕ b ≡ (a ∧ ¬b) ∨ (¬a ∧ b).
            var notA = algebra.Not(a);
            var notB = algebra.Not(b);
            var xor = algebra.Or(algebra.And(a, notB), algebra.And(notA, b));
            return !algebra.IsSatisfiable(xor);
        }

        /// <summary>
        /// Decide whether <paramref name="a"/> implies <paramref name="b"/>.
        /// Uses <see cref="IPredicateAlgebraEx{T}.Implies"/> when available;
        /// otherwise <c>!IsSatisfiable(a ∧ ¬b)</c>.
        /// </summary>
        public static bool Implies<T>(this IPredicateAlgebra<T> algebra, T a, T b)
        {
            if (algebra == null) throw new System.ArgumentNullException(nameof(algebra));
            if (algebra is IPredicateAlgebraEx<T> ex) return ex.Implies(a, b);
            return !algebra.IsSatisfiable(algebra.And(a, algebra.Not(b)));
        }

        /// <summary>
        /// Try to extract a concrete element satisfying
        /// <paramref name="predicate"/>. Uses
        /// <see cref="IEffectiveBooleanAlgebraEx{TP,TE}.TryGetModel"/> when
        /// available; otherwise returns <c>false</c>.
        /// </summary>
        public static bool TryGetModel<TPredicate, TElement>(
            this IEffectiveBooleanAlgebra<TPredicate, TElement> algebra,
            TPredicate predicate,
            out TElement element)
        {
            if (algebra == null) throw new System.ArgumentNullException(nameof(algebra));
            if (algebra is IEffectiveBooleanAlgebraEx<TPredicate, TElement> ex)
                return ex.TryGetModel(predicate, out element);
            element = default;
            return false;
        }
    }
}
