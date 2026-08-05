namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A transition term (TTerm⟨A, B⟩) represented as an Algebraic Decision Diagram (ADD).
    /// 
    /// Conditions from the EBA A are referenced by integer indices from a
    /// <see cref="ConditionRegistry{TPredicate}"/>. The ordering invariant ensures that
    /// in any ITE node (α ? f : g), inner ITEs have strictly larger condition indices
    /// than outer ITEs, producing a canonical representation.
    /// 
    /// <para>
    /// This is a pure data structure. All operations that require algebra knowledge
    /// (cleaning, leaf simplification, lifting with ACI normalization) are provided
    /// by <see cref="TransitionTermAlgebra{TPredicate, TElement, TLeaf}"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="TLeaf">The leaf type B of the transition term.</typeparam>
    public abstract class TransitionTerm<TLeaf> : IEquatable<TransitionTerm<TLeaf>>
    {
        /// <summary>Condition index for leaf nodes (no condition).</summary>
        internal const int LeafLevel = int.MaxValue;

        private int _id = -1;

        /// <summary>
        /// Unique non-negative identifier within an interning scope (see
        /// <see cref="TransitionTermAlgebra{TPredicate, TElement, TLeaf}.Intern"/>).
        /// <c>-1</c> means the term is un-interned (built via the bare
        /// <see cref="Leaf"/>/<see cref="Ite"/> statics rather than through the
        /// algebra). Within an algebra, structurally equal terms share an Id.
        /// </summary>
        public int Id => _id;

        internal bool HasId => _id >= 0;
        internal void AssignId(int id) { _id = id; }

        /// <summary>
        /// The condition index of this node. For leaves, this is <see cref="LeafLevel"/>.
        /// For ITE nodes, this is the index from the condition registry.
        /// </summary>
        public abstract int Level { get; }

        /// <summary>True if this is a leaf node.</summary>
        public bool IsLeaf => Level == LeafLevel;

        #region Factory Methods

        /// <summary>Creates a leaf transition term.</summary>
        public static TransitionTerm<TLeaf> Leaf(TLeaf value)
        {
            return new TransitionTermLeaf<TLeaf>(value);
        }

        /// <summary>
        /// Creates an ITE transition term (conditionIndex ? hi : lo),
        /// enforcing the ordering invariant and applying trivial condition elimination.
        /// </summary>
        /// <param name="conditionIndex">The condition index from the registry.</param>
        /// <param name="hi">The then-case (condition is true).</param>
        /// <param name="lo">The else-case (condition is false).</param>
        /// <returns>A canonical transition term.</returns>
        public static TransitionTerm<TLeaf> Ite(
            int conditionIndex,
            TransitionTerm<TLeaf> hi,
            TransitionTerm<TLeaf> lo)
        {
            if (hi == null) throw new ArgumentNullException(nameof(hi));
            if (lo == null) throw new ArgumentNullException(nameof(lo));

            // Trivial condition elimination: (_ ? f : f) ≈ f
            if (hi.Equals(lo))
                return hi;

            return new TransitionTermIte<TLeaf>(conditionIndex, hi, lo);
        }

        #endregion

        #region Evaluation

        /// <summary>
        /// Evaluates the transition term for a concrete element a ∈ Σ.
        /// Returns the leaf f[a] by following the ITE branches according to
        /// which predicates the element satisfies.
        /// </summary>
        public TLeaf Evaluate<TPredicate, TElement>(
            TElement element,
            ConditionRegistry<TPredicate> registry,
            IEffectiveBooleanAlgebra<TPredicate, TElement> algebra)
        {
            var current = this;
            while (current is TransitionTermIte<TLeaf> ite)
            {
                var predicate = registry.GetPredicate(ite.ConditionIndex);
                current = algebra.Models(element, predicate) ? ite.Hi : ite.Lo;
            }
            return ((TransitionTermLeaf<TLeaf>)current).Value;
        }

        #endregion

        #region Traversal

        /// <summary>Returns all leaves of this transition term (with duplicates).</summary>
        public IEnumerable<TLeaf> GetLeaves()
        {
            if (this is TransitionTermLeaf<TLeaf> leaf)
            {
                yield return leaf.Value;
            }
            else
            {
                var ite = (TransitionTermIte<TLeaf>)this;
                foreach (var l in ite.Hi.GetLeaves())
                    yield return l;
                foreach (var l in ite.Lo.GetLeaves())
                    yield return l;
            }
        }

        /// <summary>Returns all distinct leaves of this transition term.</summary>
        public IEnumerable<TLeaf> GetDistinctLeaves()
        {
            return GetLeaves().Distinct();
        }

        /// <summary>Returns all condition indices used in this transition term (with duplicates).</summary>
        public IEnumerable<int> GetConditionIndices()
        {
            if (this is TransitionTermIte<TLeaf> ite)
            {
                yield return ite.ConditionIndex;
                foreach (var c in ite.Hi.GetConditionIndices())
                    yield return c;
                foreach (var c in ite.Lo.GetConditionIndices())
                    yield return c;
            }
        }

        /// <summary>Returns all distinct condition indices used in this transition term.</summary>
        public IEnumerable<int> GetDistinctConditionIndices()
        {
            return GetConditionIndices().Distinct();
        }

        #endregion

        #region Equality

        /// <summary>
        /// Structural equality. Due to the ordered canonical form, structural
        /// equality implies semantic equivalence (modulo leaf equality).
        /// </summary>
        public abstract bool Equals(TransitionTerm<TLeaf> other);

        public override bool Equals(object obj) => Equals(obj as TransitionTerm<TLeaf>);

        public abstract override int GetHashCode();

        public abstract override string ToString();

        public static bool operator ==(TransitionTerm<TLeaf> left, TransitionTerm<TLeaf> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(TransitionTerm<TLeaf> left, TransitionTerm<TLeaf> right)
            => !(left == right);

        #endregion
    }

    /// <summary>
    /// A leaf node ℓ ∈ B of a transition term.
    /// </summary>
    public sealed class TransitionTermLeaf<TLeaf> : TransitionTerm<TLeaf>
    {
        public TLeaf Value { get; }

        public override int Level => LeafLevel;

        public TransitionTermLeaf(TLeaf value)
        {
            Value = value;
        }

        public override bool Equals(TransitionTerm<TLeaf> other)
        {
            return other is TransitionTermLeaf<TLeaf> leaf
                && EqualityComparer<TLeaf>.Default.Equals(Value, leaf.Value);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : Value.GetHashCode();
        }

        public override string ToString() => Value?.ToString() ?? "null";
    }

    /// <summary>
    /// An ITE node (α ? hi : lo) of a transition term.
    /// The ordering invariant guarantees that Hi and Lo nodes (if ITEs)
    /// have strictly larger condition indices than this node.
    /// </summary>
    public sealed class TransitionTermIte<TLeaf> : TransitionTerm<TLeaf>
    {
        /// <summary>Index into the condition registry.</summary>
        public int ConditionIndex { get; }

        /// <summary>The then-case: taken when the condition is satisfied.</summary>
        public TransitionTerm<TLeaf> Hi { get; }

        /// <summary>The else-case: taken when the condition is not satisfied.</summary>
        public TransitionTerm<TLeaf> Lo { get; }

        public override int Level => ConditionIndex;

        private int? _cachedHash;

        public TransitionTermIte(
            int conditionIndex,
            TransitionTerm<TLeaf> hi,
            TransitionTerm<TLeaf> lo)
        {
            // Negative indices are permitted: they denote propositions
            // (EREQ Phase-1 D1) and sort outermost under our
            // inner-larger ordering. int.MaxValue is the leaf marker
            // and may not appear as a real condition.
            if (conditionIndex == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(conditionIndex));

            // Enforce ordering invariant: children must have strictly larger levels
            if (!hi.IsLeaf && hi.Level <= conditionIndex)
                throw new ArgumentException(
                    $"Ordering violation: hi child level {hi.Level} must be > {conditionIndex}",
                    nameof(hi));
            if (!lo.IsLeaf && lo.Level <= conditionIndex)
                throw new ArgumentException(
                    $"Ordering violation: lo child level {lo.Level} must be > {conditionIndex}",
                    nameof(lo));

            ConditionIndex = conditionIndex;
            Hi = hi;
            Lo = lo;
        }

        public override bool Equals(TransitionTerm<TLeaf> other)
        {
            return other is TransitionTermIte<TLeaf> ite
                && ConditionIndex == ite.ConditionIndex
                && Hi.Equals(ite.Hi)
                && Lo.Equals(ite.Lo);
        }

        public override int GetHashCode()
        {
            if (_cachedHash == null)
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + ConditionIndex;
                    hash = hash * 31 + Hi.GetHashCode();
                    hash = hash * 31 + Lo.GetHashCode();
                    _cachedHash = hash;
                }
            }
            return _cachedHash.Value;
        }

        public override string ToString()
            => $"({ConditionIndex} ? {Hi} : {Lo})";
    }
}
