namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// An Effective Boolean Algebra (EBA) over a universe of elements.
    /// Provides the predicate operations needed for transition terms:
    /// conjunction, disjunction, complement, and satisfiability checking.
    /// </summary>
    /// <typeparam name="TPredicate">The type of predicates in the algebra.</typeparam>
    /// <typeparam name="TElement">The type of elements in the universe Σ.</typeparam>
    public interface IEffectiveBooleanAlgebra<TPredicate, TElement>
        : IPredicateAlgebra<TPredicate>
    {
        /// <summary>
        /// The models relation: a ⊨ α. Returns true if element satisfies the predicate.
        /// </summary>
        bool Models(TElement element, TPredicate predicate);
    }
}
