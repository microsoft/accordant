namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// Optional extension of
    /// <see cref="IEffectiveBooleanAlgebra{TPredicate,TElement}"/> that an
    /// algebra implements when it can produce a concrete element
    /// witnessing satisfiability of a predicate. Typical SMT-backed
    /// implementations satisfy this trivially via a model-extraction
    /// call.
    ///
    /// <para>Callers should not depend on this interface directly; the
    /// <see cref="EbaExtensions.TryGetModel{TP,TE}"/> extension method
    /// probes for it and otherwise returns <c>false</c>, allowing the
    /// caller to fall back to a domain-specific element chooser.</para>
    /// </summary>
    public interface IEffectiveBooleanAlgebraEx<TPredicate, TElement>
        : IEffectiveBooleanAlgebra<TPredicate, TElement>,
          IPredicateAlgebraEx<TPredicate>
    {
        /// <summary>
        /// Try to produce a concrete element of the universe that
        /// satisfies <paramref name="predicate"/>. Returns <c>true</c> and
        /// sets <paramref name="element"/> when one is found;
        /// <c>false</c> otherwise (including for unsatisfiable predicates
        /// and for predicates the algebra cannot decide). The reported
        /// element must satisfy
        /// <see cref="IEffectiveBooleanAlgebra{T,E}.Models"/>.
        /// </summary>
        bool TryGetModel(TPredicate predicate, out TElement element);
    }
}
