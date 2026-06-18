namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// The predicate-only fragment of an Effective Boolean Algebra: the
    /// boolean lattice operations and conservative satisfiability test,
    /// without the <c>Models</c> relation that ties predicates to a
    /// concrete element universe Σ.
    ///
    /// Useful for components — like <see cref="LtlAlgebra{TPred}"/> and
    /// <see cref="RltlAlgebra{TPred}"/> — that need to push boolean
    /// combinations of predicates back into the EBA but never need to
    /// evaluate them against concrete elements.
    /// </summary>
    public interface IPredicateAlgebra<TPredicate>
    {
        /// <summary>Top element ⊤ — satisfied by all elements.</summary>
        TPredicate Top { get; }

        /// <summary>Bottom element ⊥ — satisfied by no elements.</summary>
        TPredicate Bottom { get; }

        /// <summary>Conjunction: α ⊓ β.</summary>
        TPredicate And(TPredicate a, TPredicate b);

        /// <summary>Disjunction: α ⊔ β.</summary>
        TPredicate Or(TPredicate a, TPredicate b);

        /// <summary>Complement: αᶜ.</summary>
        TPredicate Not(TPredicate a);

        /// <summary>
        /// Conservative satisfiability test. Returning <c>false</c>
        /// guarantees unsatisfiability; returning <c>true</c> means
        /// satisfiable or unknown.
        /// </summary>
        bool IsSatisfiable(TPredicate predicate);
    }
}
