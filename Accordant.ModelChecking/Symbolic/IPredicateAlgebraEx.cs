namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// Optional extension of <see cref="IPredicateAlgebra{TPredicate}"/>
    /// that an algebra implements when it can decide equivalence and
    /// implication of predicates <em>precisely</em> (typically by
    /// delegating to an SMT solver such as Z3).
    ///
    /// <para>Callers should not depend on this interface directly; the
    /// <see cref="EbaExtensions.AreEquivalent{T}"/> and
    /// <see cref="EbaExtensions.Implies{T}"/> extension methods probe
    /// for it and otherwise fall back to a conservative implementation
    /// expressed in terms of <see cref="IPredicateAlgebra{T}.IsSatisfiable"/>:
    /// <list type="bullet">
    ///   <item><c>AreEquivalent(a, b) ≡ ¬IsSatisfiable(a ⊕ b)</c></item>
    ///   <item><c>Implies(a, b) ≡ ¬IsSatisfiable(a ∧ ¬b)</c></item>
    /// </list>
    /// The fallback is sound under the standard EBA invariant that
    /// <c>IsSatisfiable</c> may be conservative-true (i.e. it never reports
    /// false for a satisfiable predicate); it may be conservative
    /// (return <c>false</c> when the algebra cannot prove
    /// equivalence/implication).</para>
    /// </summary>
    public interface IPredicateAlgebraEx<TPredicate> : IPredicateAlgebra<TPredicate>
    {
        /// <summary>
        /// Decide whether <paramref name="a"/> and <paramref name="b"/>
        /// denote the same set of elements. Implementations are required
        /// to be precise when they return <c>true</c>; returning
        /// <c>false</c> on equivalent inputs is permitted but discouraged.
        /// </summary>
        bool AreEquivalent(TPredicate a, TPredicate b);

        /// <summary>
        /// Decide whether every element satisfying <paramref name="a"/>
        /// also satisfies <paramref name="b"/>. Same precision contract
        /// as <see cref="AreEquivalent"/>.
        /// </summary>
        bool Implies(TPredicate a, TPredicate b);
    }
}
