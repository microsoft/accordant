namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Provides operations on transition terms with built-in cleaning and
    /// leaf simplification. Combines three algebras:
    /// <list type="bullet">
    ///   <item>The condition EBA A (predicates over the alphabet)</item>
    ///   <item>The condition registry (ordering of conditions)</item>
    ///   <item>The leaf algebra B (Boolean operations on leaves with ACI)</item>
    /// </list>
    /// 
    /// All operations aggressively clean transition terms by:
    /// <list type="number">
    ///   <item>Tracking path conditions and pruning unreachable branches via SAT(A)</item>
    ///   <item>Applying trivial condition elimination: (α ? f : f) → f</item>
    ///   <item>Simplifying leaves using the leaf algebra's Boolean laws</item>
    /// </list>
    /// </summary>
    /// <typeparam name="TPredicate">Predicate type in the condition EBA.</typeparam>
    /// <typeparam name="TElement">Element type in the alphabet universe Σ.</typeparam>
    /// <typeparam name="TLeaf">Leaf type B of transition terms.</typeparam>
    public class TransitionTermAlgebra<TPredicate, TElement, TLeaf>
    {
        private readonly IEffectiveBooleanAlgebra<TPredicate, TElement> _eba;
        private readonly ConditionRegistry<TPredicate> _registry;
        private readonly ILeafAlgebra<TLeaf> _leafAlgebra;

        // Hash-cons table for transition terms produced by this algebra.
        //   _byId: dense storage; Id i → canonical term at _byId[i].
        //   _intern: dedup table used only at construction; keyed on structural
        //   equality (the existing TransitionTerm.Equals/GetHashCode).
        // Bottom / Top are interned on first access; some leaf algebras (e.g.
        // StateSetLeafAlgebra in NBW context) don't define Top, so eager
        // construction would fail there.
        private readonly List<TransitionTerm<TLeaf>> _byId =
            new List<TransitionTerm<TLeaf>>();
        private readonly Dictionary<TransitionTerm<TLeaf>, TransitionTerm<TLeaf>> _intern =
            new Dictionary<TransitionTerm<TLeaf>, TransitionTerm<TLeaf>>();
        private TransitionTerm<TLeaf> _bottom;
        private TransitionTerm<TLeaf> _top;

        public TransitionTermAlgebra(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            ConditionRegistry<TPredicate> registry,
            ILeafAlgebra<TLeaf> leafAlgebra)
        {
            _eba = eba ?? throw new ArgumentNullException(nameof(eba));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _leafAlgebra = leafAlgebra ?? throw new ArgumentNullException(nameof(leafAlgebra));
        }

        /// <summary>The condition EBA.</summary>
        public IEffectiveBooleanAlgebra<TPredicate, TElement> Eba => _eba;

        /// <summary>The condition registry (ordering).</summary>
        public ConditionRegistry<TPredicate> Registry => _registry;

        /// <summary>The leaf algebra.</summary>
        public ILeafAlgebra<TLeaf> LeafAlgebra => _leafAlgebra;

        /// <summary>Number of distinct canonical transition terms interned so far.</summary>
        public int InternedCount => _byId.Count;

        /// <summary>
        /// Total order on condition predicates within this algebra. Two
        /// predicates compare equal iff they are registered at the same
        /// index; otherwise the order is given by registration order in
        /// <see cref="Registry"/>. Unregistered predicates are registered
        /// on the fly. This is the strict order that
        /// <see cref="TransitionTermIte{TLeaf}"/> enforces between an
        /// outer ITE's condition and the conditions of its child ITEs.
        /// </summary>
        public int Compare(TPredicate a, TPredicate b)
            => _registry.Register(a).CompareTo(_registry.Register(b));

        /// <summary>
        /// Return the canonical instance equal to <paramref name="candidate"/>,
        /// allocating a fresh <see cref="TransitionTerm{TLeaf}.Id"/> if this
        /// shape has not been seen yet.
        /// </summary>
        public TransitionTerm<TLeaf> Intern(TransitionTerm<TLeaf> candidate)
        {
            if (candidate == null) return null;
            if (candidate.HasId && candidate.Id < _byId.Count
                && ReferenceEquals(_byId[candidate.Id], candidate))
                return candidate;
            if (_intern.TryGetValue(candidate, out var existing)) return existing;
            candidate.AssignId(_byId.Count);
            _byId.Add(candidate);
            _intern[candidate] = candidate;
            return candidate;
        }

        #region Smart Constructors

        /// <summary>Creates a (canonical, interned) leaf transition term.</summary>
        public TransitionTerm<TLeaf> Leaf(TLeaf value)
            => Intern(TransitionTerm<TLeaf>.Leaf(value));

        /// <summary>The bottom leaf ⊥ (canonical, interned).</summary>
        public TransitionTerm<TLeaf> Bottom
            => _bottom ?? (_bottom = Intern(TransitionTerm<TLeaf>.Leaf(_leafAlgebra.Bottom)));

        /// <summary>The top leaf ⊤ (canonical, interned).</summary>
        public TransitionTerm<TLeaf> Top
            => _top ?? (_top = Intern(TransitionTerm<TLeaf>.Leaf(_leafAlgebra.Top)));

        /// <summary>
        /// Creates an ITE (α ? hi : lo) with built-in cleaning.
        /// Applies trivial condition elimination and checks feasibility.
        /// </summary>
        /// <param name="conditionIndex">Condition index from the registry.</param>
        /// <param name="hi">Then-case.</param>
        /// <param name="lo">Else-case.</param>
        /// <param name="pathCondition">
        /// The accumulated path condition for cleaning.
        /// Pass null to skip path-based cleaning.
        /// </param>
        public TransitionTerm<TLeaf> MkIte(
            int conditionIndex,
            TransitionTerm<TLeaf> hi,
            TransitionTerm<TLeaf> lo,
            TPredicate pathCondition = default)
        {
            // Trivial condition elimination: (α ? f : f) → f
            if (hi.Equals(lo))
                return hi;

            // Path-condition-based cleaning.
            //
            // Proposition splits (negative indices) are free Booleans:
            // both branches are always reachable, the path condition is
            // unchanged. Skip cleaning for them. See EREQ Phase-0 D5.
            if (!ConditionRegistry<TPredicate>.IsProposition(conditionIndex)
                && pathCondition != null
                && !EqualityComparer<TPredicate>.Default.Equals(pathCondition, default))
            {
                var condition = _registry.GetPredicate(conditionIndex);

                var thenPath = _eba.And(pathCondition, condition);
                if (!_eba.IsSatisfiable(thenPath))
                    return lo; // (⊥ ? _ : g) → g

                var elsePath = _eba.And(pathCondition, _eba.Not(condition));
                if (!_eba.IsSatisfiable(elsePath))
                    return hi; // (⊤ ? f : _) → f
            }

            return Intern(TransitionTerm<TLeaf>.Ite(conditionIndex, hi, lo));
        }

        /// <summary>
        /// Creates (α ? f) with implicit else-case ⊥.
        /// The paper's shorthand when ⊥ ∈ B.
        /// </summary>
        public TransitionTerm<TLeaf> MkGuard(int conditionIndex, TransitionTerm<TLeaf> hi)
            => MkIte(conditionIndex, hi, Bottom);

        #endregion

        #region Binary Operations (Apply with built-in cleaning + leaf simplification)

        /// <summary>
        /// Disjunction of transition terms: f ∨ g.
        /// Lifted via ITE propagation with ACI leaf normalization.
        /// ⊥ is eliminated (unit of ∨). ⊤ short-circuits.
        /// </summary>
        public TransitionTerm<TLeaf> Or(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right)
        {
            return ApplyBinary(left, right, _leafAlgebra.Or, _eba.Top);
        }

        /// <summary>
        /// Conjunction of transition terms: f ∧ g.
        /// Lifted via ITE propagation with ACI leaf normalization.
        /// ⊤ is eliminated (unit of ∧). ⊥ short-circuits.
        /// </summary>
        public TransitionTerm<TLeaf> And(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right)
        {
            return ApplyBinary(left, right, _leafAlgebra.And, _eba.Top);
        }

        /// <summary>
        /// Complement of a transition term: ¬f.
        /// Lifted via ITE propagation: ¬(α ? f : g) = (α ? ¬f : ¬g).
        /// </summary>
        public TransitionTerm<TLeaf> Not(TransitionTerm<TLeaf> term)
        {
            return MapUnary(term, _leafAlgebra.Not);
        }

        /// <summary>
        /// Symmetric difference of transition terms: f ⊕ g.
        /// Lifted via ITE propagation with leaf-level XOR. Used by the
        /// bisimulation-based equivalence algorithm (CAV'26 §6), where
        /// δ(p ⊕ q) = δp ⊕ δq.
        /// </summary>
        public TransitionTerm<TLeaf> Xor(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right)
        {
            return ApplyBinary(left, right, _leafAlgebra.Xor, _eba.Top);
        }

        /// <summary>
        /// Top-level disjunction (Antimirov normal form).
        /// Instead of propagating ∨ into ITE branches, maintains
        /// a list of disjuncts. Useful for nondeterministic representations
        /// where it is irrelevant how conditions in separate disjuncts
        /// relate to each other.
        /// </summary>
        /// <param name="disjuncts">The disjuncts to combine.</param>
        /// <returns>
        /// A list of transition terms representing the disjunction,
        /// with duplicates removed and ⊥-disjuncts eliminated.
        /// </returns>
        public IReadOnlyList<TransitionTerm<TLeaf>> DisjunctiveForm(
            IEnumerable<TransitionTerm<TLeaf>> disjuncts)
        {
            var result = new List<TransitionTerm<TLeaf>>();
            var seen = new HashSet<TransitionTerm<TLeaf>>();

            foreach (var d in disjuncts)
            {
                // Skip ⊥ disjuncts (unit of ∨)
                if (d is TransitionTermLeaf<TLeaf> leaf && _leafAlgebra.IsBottom(leaf.Value))
                    continue;

                // ⊤ short-circuit
                if (d is TransitionTermLeaf<TLeaf> topLeaf && _leafAlgebra.IsTop(topLeaf.Value))
                    return new List<TransitionTerm<TLeaf>> { d };

                // Idempotency: skip duplicates
                if (seen.Add(d))
                    result.Add(d);
            }

            if (result.Count == 0)
                result.Add(Bottom);

            return result;
        }

        /// <summary>
        /// General Apply: lifts a binary operation ⋄ : B × B → B to TTerm.
        /// Merges the ordered ITE structures with built-in cleaning.
        /// </summary>
        public TransitionTerm<TLeaf> ApplyBinary(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right,
            Func<TLeaf, TLeaf, TLeaf> operation,
            TPredicate pathCondition)
        {
            var cache = new Dictionary<long, TransitionTerm<TLeaf>>();
            return ApplyCore(left, right, operation, pathCondition, cache);
        }

        private TransitionTerm<TLeaf> ApplyCore(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right,
            Func<TLeaf, TLeaf, TLeaf> operation,
            TPredicate pathCondition,
            Dictionary<long, TransitionTerm<TLeaf>> cache)
        {
            // Memoization
            long key = CombineIds(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(left),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(right));
            if (cache.TryGetValue(key, out var cached))
                return cached;

            TransitionTerm<TLeaf> result;

            if (left.IsLeaf && right.IsLeaf)
            {
                var leftVal = ((TransitionTermLeaf<TLeaf>)left).Value;
                var rightVal = ((TransitionTermLeaf<TLeaf>)right).Value;
                result = Leaf(operation(leftVal, rightVal));
            }
            else
            {
                int splitLevel;
                TransitionTerm<TLeaf> leftHi, leftLo, rightHi, rightLo;

                DecomposePair(left, right, out splitLevel, out leftHi, out leftLo, out rightHi, out rightLo);

                // Proposition splits (EREQ Phase-0 D5): no path tightening,
                // both branches always reachable.
                if (ConditionRegistry<TPredicate>.IsProposition(splitLevel))
                {
                    var hi0 = ApplyCore(leftHi, rightHi, operation, pathCondition, cache);
                    var lo0 = ApplyCore(leftLo, rightLo, operation, pathCondition, cache);
                    result = MkIte(splitLevel, hi0, lo0);
                }
                else
                {
                var condition = _registry.GetPredicate(splitLevel);

                // Clean: check then-branch reachability
                var thenPath = _eba.And(pathCondition, condition);
                var elsePath = _eba.And(pathCondition, _eba.Not(condition));

                bool thenReachable = _eba.IsSatisfiable(thenPath);
                bool elseReachable = _eba.IsSatisfiable(elsePath);

                if (!thenReachable && !elseReachable)
                {
                    // Shouldn't normally happen; fallback to lo
                    result = ApplyCore(leftLo, rightLo, operation, pathCondition, cache);
                }
                else if (!thenReachable)
                {
                    result = ApplyCore(leftLo, rightLo, operation, elsePath, cache);
                }
                else if (!elseReachable)
                {
                    result = ApplyCore(leftHi, rightHi, operation, thenPath, cache);
                }
                else
                {
                    var hi = ApplyCore(leftHi, rightHi, operation, thenPath, cache);
                    var lo = ApplyCore(leftLo, rightLo, operation, elsePath, cache);
                    result = MkIte(splitLevel, hi, lo);
                }
                }
            }

            cache[key] = result;
            return result;
        }

        /// <summary>
        /// Decomposes a pair of terms at the top-most condition level.
        /// If one term has a lower level, the other is passed through unchanged.
        /// </summary>
        private static void DecomposePair(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right,
            out int splitLevel,
            out TransitionTerm<TLeaf> leftHi,
            out TransitionTerm<TLeaf> leftLo,
            out TransitionTerm<TLeaf> rightHi,
            out TransitionTerm<TLeaf> rightLo)
        {
            int leftLevel = left.Level;
            int rightLevel = right.Level;

            if (leftLevel == rightLevel)
            {
                // Same condition: decompose both
                splitLevel = leftLevel;
                var li = (TransitionTermIte<TLeaf>)left;
                var ri = (TransitionTermIte<TLeaf>)right;
                leftHi = li.Hi; leftLo = li.Lo;
                rightHi = ri.Hi; rightLo = ri.Lo;
            }
            else if (leftLevel < rightLevel)
            {
                // Left has smaller level: split on left, right passes through
                splitLevel = leftLevel;
                var li = (TransitionTermIte<TLeaf>)left;
                leftHi = li.Hi; leftLo = li.Lo;
                rightHi = right; rightLo = right;
            }
            else
            {
                // Right has smaller level: split on right, left passes through
                splitLevel = rightLevel;
                var ri = (TransitionTermIte<TLeaf>)right;
                leftHi = left; leftLo = left;
                rightHi = ri.Hi; rightLo = ri.Lo;
            }
        }

        #endregion

        #region Unary Operations (Map with leaf simplification)

        /// <summary>
        /// General Map: lifts a unary operation ♦ : B → B to TTerm.
        /// From the paper equation (2): ♦(α ? f : g) = (α ? ♦f : ♦g)
        /// </summary>
        public TransitionTerm<TLeaf> MapUnary(
            TransitionTerm<TLeaf> term,
            Func<TLeaf, TLeaf> operation)
        {
            if (term is TransitionTermLeaf<TLeaf> leaf)
                return Leaf(operation(leaf.Value));

            var ite = (TransitionTermIte<TLeaf>)term;
            var hi = MapUnary(ite.Hi, operation);
            var lo = MapUnary(ite.Lo, operation);
            return MkIte(ite.ConditionIndex, hi, lo);
        }

        /// <summary>
        /// Cross-type Map: lifts ♦ : B → B' to TTerm, producing a new leaf type.
        /// </summary>
        public TransitionTerm<TResult> MapUnary<TResult>(
            TransitionTerm<TLeaf> term,
            Func<TLeaf, TResult> operation)
        {
            if (term is TransitionTermLeaf<TLeaf> leaf)
                return TransitionTerm<TResult>.Leaf(operation(leaf.Value));

            var ite = (TransitionTermIte<TLeaf>)term;
            var hi = MapUnary(ite.Hi, operation);
            var lo = MapUnary(ite.Lo, operation);
            return TransitionTerm<TResult>.Ite(ite.ConditionIndex, hi, lo);
        }

        /// <summary>
        /// Cross-type Apply: lifts ⋄ : B₁ × B₂ → B' to TTerm.
        /// Used when the leaf types differ (e.g., in alternation elimination
        /// and RLTL derivative rules per Sections 5 and 7.3 of the paper).
        /// </summary>
        public TransitionTerm<TResult> ApplyCross<TLeaf2, TResult>(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf2> right,
            Func<TLeaf, TLeaf2, TResult> operation,
            TPredicate pathCondition)
        {
            var cache = new Dictionary<long, TransitionTerm<TResult>>();
            return ApplyCrossCore(left, right, operation, pathCondition, cache);
        }

        private TransitionTerm<TResult> ApplyCrossCore<TLeaf2, TResult>(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf2> right,
            Func<TLeaf, TLeaf2, TResult> operation,
            TPredicate pathCondition,
            Dictionary<long, TransitionTerm<TResult>> cache)
        {
            long key = CombineIds(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(left),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(right));
            if (cache.TryGetValue(key, out var cached))
                return cached;

            TransitionTerm<TResult> result;

            if (left.IsLeaf && right.IsLeaf)
            {
                var leftVal = ((TransitionTermLeaf<TLeaf>)left).Value;
                var rightVal = ((TransitionTermLeaf<TLeaf2>)right).Value;
                result = TransitionTerm<TResult>.Leaf(operation(leftVal, rightVal));
            }
            else
            {
                int splitLevel;
                TransitionTerm<TLeaf> leftHi, leftLo;
                TransitionTerm<TLeaf2> rightHi, rightLo;

                DecomposePairCross(left, right, out splitLevel, out leftHi, out leftLo, out rightHi, out rightLo);

                if (ConditionRegistry<TPredicate>.IsProposition(splitLevel))
                {
                    var hi0 = ApplyCrossCore(leftHi, rightHi, operation, pathCondition, cache);
                    var lo0 = ApplyCrossCore(leftLo, rightLo, operation, pathCondition, cache);
                    result = TransitionTerm<TResult>.Ite(splitLevel, hi0, lo0);
                }
                else
                {
                var condition = _registry.GetPredicate(splitLevel);
                var thenPath = _eba.And(pathCondition, condition);
                var elsePath = _eba.And(pathCondition, _eba.Not(condition));

                bool thenReachable = _eba.IsSatisfiable(thenPath);
                bool elseReachable = _eba.IsSatisfiable(elsePath);

                if (!thenReachable && !elseReachable)
                {
                    result = ApplyCrossCore(leftLo, rightLo, operation, pathCondition, cache);
                }
                else if (!thenReachable)
                {
                    result = ApplyCrossCore(leftLo, rightLo, operation, elsePath, cache);
                }
                else if (!elseReachable)
                {
                    result = ApplyCrossCore(leftHi, rightHi, operation, thenPath, cache);
                }
                else
                {
                    var hi = ApplyCrossCore(leftHi, rightHi, operation, thenPath, cache);
                    var lo = ApplyCrossCore(leftLo, rightLo, operation, elsePath, cache);
                    result = TransitionTerm<TResult>.Ite(splitLevel, hi, lo);
                }
                }
            }

            cache[key] = result;
            return result;
        }

        private static void DecomposePairCross<TLeaf2>(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf2> right,
            out int splitLevel,
            out TransitionTerm<TLeaf> leftHi,
            out TransitionTerm<TLeaf> leftLo,
            out TransitionTerm<TLeaf2> rightHi,
            out TransitionTerm<TLeaf2> rightLo)
        {
            int leftLevel = left.Level;
            int rightLevel = right.Level;

            if (leftLevel == rightLevel)
            {
                splitLevel = leftLevel;
                var li = (TransitionTermIte<TLeaf>)left;
                var ri = (TransitionTermIte<TLeaf2>)right;
                leftHi = li.Hi; leftLo = li.Lo;
                rightHi = ri.Hi; rightLo = ri.Lo;
            }
            else if (leftLevel < rightLevel)
            {
                splitLevel = leftLevel;
                var li = (TransitionTermIte<TLeaf>)left;
                leftHi = li.Hi; leftLo = li.Lo;
                rightHi = right; rightLo = right;
            }
            else
            {
                splitLevel = rightLevel;
                var ri = (TransitionTermIte<TLeaf2>)right;
                leftHi = left; leftLo = left;
                rightHi = ri.Hi; rightLo = ri.Lo;
            }
        }

        #endregion

        #region Alternation Product (@)

        /// <summary>
        /// The alternation product f @ g from the paper (Section 5.1, equation 6).
        /// Used in the Æ alternation elimination algorithm.
        /// Lifted to transition terms via ITE propagation with built-in cleaning.
        /// 
        /// The concrete @ operation on DNF leaves is defined by the caller.
        /// </summary>
        public TransitionTerm<TResult> AlternationProduct<TLeaf2, TResult>(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf2> right,
            Func<TLeaf, TLeaf2, TResult> atOperation)
        {
            return ApplyCross(left, right, atOperation, _eba.Top);
        }

        #endregion

        #region Utilities

        private static long CombineIds(int a, int b)
        {
            return ((long)a << 32) | (uint)b;
        }

        #endregion
    }
}
