namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Sound and complete <em>language equivalence</em> for <see cref="Rltl{TPred}"/>
    /// formulas — the trusted oracle. Implements equivalence as two-way
    /// inclusion via emptiness on the Boolean closure of the input formulas:
    ///
    /// <code>
    ///   Equiv(φ, ψ) ⟺ L(φ ∧ ¬ψ) = ∅ ∧ L(¬φ ∧ ψ) = ∅
    /// </code>
    ///
    /// <para>
    /// RLTL is closed under Boolean ops (see <see cref="RltlAlgebra{TPred}"/>),
    /// so the conjunctions <c>φ ∧ ¬ψ</c> and <c>¬φ ∧ ψ</c> are themselves
    /// RLTL formulas. For each one we build an alternating Büchi automaton
    /// via <see cref="RltlDerivative{TPred,TElem}.ToABW"/>, eliminate
    /// alternation incrementally (<see cref="IncrementalAE{TPredicate,TElement,TState}"/>),
    /// and decide language emptiness of the resulting NBW with
    /// <see cref="SymbolicNbwEmptiness"/>.
    /// </para>
    ///
    /// <para>
    /// Precision: results are sound and complete <em>modulo the precision
    /// of <see cref="IPredicateAlgebra{T}.IsSatisfiable"/></em>. With a
    /// conservative-true IsSatisfiable, the answer characterises symbolic
    /// equivalence over independent predicate symbols (every distinct
    /// predicate is treated as satisfiable); with a precise IsSatisfiable
    /// (e.g. a Z3-backed EBA), the answer is true semantic equivalence.
    /// </para>
    ///
    /// <para>
    /// This is the "trusted oracle" baseline for cheaper but incomplete
    /// equivalence procedures such as the planned RLTL derivative-bisimulation
    /// check (G8-a).
    /// </para>
    /// </summary>
    public static class RltlLanguageEquivalence
    {
        /// <summary>
        /// Returns <c>true</c> iff <c>L(φ) = L(ψ)</c> modulo
        /// <c>algebra.Eba.IsSatisfiable</c> precision.
        /// </summary>
        public static bool AreEquivalent<TPredicate, TElement>(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            RltlAlgebra<TPredicate> algebra,
            Rltl<TPredicate> phi,
            Rltl<TPredicate> psi)
        {
            if (eba == null) throw new ArgumentNullException(nameof(eba));
            if (algebra == null) throw new ArgumentNullException(nameof(algebra));
            if (phi == null) throw new ArgumentNullException(nameof(phi));
            if (psi == null) throw new ArgumentNullException(nameof(psi));

            if (ReferenceEquals(phi, psi)) return true;
            return Includes(eba, algebra, phi, psi)
                && Includes(eba, algebra, psi, phi);
        }

        /// <summary>
        /// Returns <c>true</c> iff <c>L(φ) ⊆ L(ψ)</c> modulo
        /// <c>algebra.Eba.IsSatisfiable</c> precision.
        /// Implemented as <c>IsLanguageEmpty(φ ∧ ¬ψ)</c>.
        /// </summary>
        public static bool Includes<TPredicate, TElement>(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            RltlAlgebra<TPredicate> algebra,
            Rltl<TPredicate> phi,
            Rltl<TPredicate> psi)
        {
            if (eba == null) throw new ArgumentNullException(nameof(eba));
            if (algebra == null) throw new ArgumentNullException(nameof(algebra));
            var diff = algebra.And(phi, algebra.Not(psi));
            return IsLanguageEmpty(eba, diff);
        }

        /// <summary>
        /// Returns <c>true</c> iff <c>L(φ) = ∅</c> modulo
        /// <c>eba.IsSatisfiable</c> precision. The pipeline is:
        /// RLTL φ → ABW → (incremental Æ) → lazy NBW → NDFS emptiness.
        /// </summary>
        public static bool IsLanguageEmpty<TPredicate, TElement>(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            Rltl<TPredicate> phi)
        {
            if (eba == null) throw new ArgumentNullException(nameof(eba));
            if (phi == null) throw new ArgumentNullException(nameof(phi));

            // Constant short-circuits avoid building an automaton for trivial cases.
            if (phi is RltlFalse<TPredicate>) return true;
            if (phi is RltlTrue<TPredicate>) return false;

            var registry = new ConditionRegistry<TPredicate>(
                EqualityComparer<TPredicate>.Default);
            var derivative = new RltlDerivative<TPredicate, TElement>(eba, registry);
            var abw = derivative.ToABW(phi);
            var incAE = new IncrementalAE<TPredicate, TElement, Rltl<TPredicate>>(abw);
            var nbw = incAE.ToNBW();

            return SymbolicNbwEmptiness.IsEmpty(
                nbw, eba, BreakpointState<Rltl<TPredicate>>.GetEqualityComparer());
        }
    }
}
