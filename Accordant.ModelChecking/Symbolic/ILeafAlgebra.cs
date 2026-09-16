namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System.Collections.Generic;

    /// <summary>
    /// A Boolean algebra over leaf elements of transition terms.
    /// 
    /// Defines the weak equivalence laws used to simplify leaves:
    /// <list type="bullet">
    ///   <item>⊥ is the unit (identity) of ∨ (disjunction) and zero of ∧</item>
    ///   <item>⊤ is the unit (identity) of ∧ (conjunction) and zero of ∨</item>
    ///   <item>∨ and ∧ are Associative, Commutative, and Idempotent (ACI)</item>
    ///   <item>Law of excluded middle: ¬φ ∨ φ ≈ ⊤</item>
    /// </list>
    /// 
    /// Implementors must ensure that Or and And return normalized results:
    /// internally represented as sorted sequences without duplicates.
    /// </summary>
    /// <typeparam name="TLeaf">The leaf type B.</typeparam>
    public interface ILeafAlgebra<TLeaf>
    {
        /// <summary>Top element ⊤ — the identity of ∧ and zero of ∨.</summary>
        TLeaf Top { get; }

        /// <summary>Bottom element ⊥ — the identity of ∨ and zero of ∧.</summary>
        TLeaf Bottom { get; }

        /// <summary>
        /// Disjunction: φ ∨ ψ.
        /// Must be ACI-normalized: associative, commutative, idempotent.
        /// Returns ⊤ if the result is equivalent to top.
        /// Eliminates ⊥ (unit of ∨).
        /// </summary>
        TLeaf Or(TLeaf a, TLeaf b);

        /// <summary>
        /// Conjunction: φ ∧ ψ.
        /// Must be ACI-normalized: associative, commutative, idempotent.
        /// Returns ⊥ if the result is equivalent to bottom.
        /// Eliminates ⊤ (unit of ∧).
        /// </summary>
        TLeaf And(TLeaf a, TLeaf b);

        /// <summary>
        /// Complement: ¬φ.
        /// May return null if complement is not supported for this leaf type.
        /// </summary>
        TLeaf Not(TLeaf a);

        /// <summary>
        /// Symmetric difference (exclusive or): φ ⊕ ψ.
        /// Implementations may use a primitive XOR form (e.g. <see cref="EreXor{TPred}"/>)
        /// or fall back to <c>(φ ∧ ¬ψ) ∨ (¬φ ∧ ψ)</c>.
        /// Must satisfy: <c>φ ⊕ ⊥ = φ</c>, <c>φ ⊕ φ = ⊥</c>,
        /// <c>φ ⊕ ⊤ = ¬φ</c>; ACI plus self-inverse.
        /// </summary>
        TLeaf Xor(TLeaf a, TLeaf b);

        /// <summary>Returns true if the leaf is equivalent to ⊤.</summary>
        bool IsTop(TLeaf a);

        /// <summary>Returns true if the leaf is equivalent to ⊥.</summary>
        bool IsBottom(TLeaf a);

        /// <summary>
        /// Equality comparer for leaves.
        /// Used for structural deduplication within ACI normalization.
        /// </summary>
        IEqualityComparer<TLeaf> Comparer { get; }
    }
}
