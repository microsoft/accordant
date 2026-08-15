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
        ///
        /// <para>The result agrees with the pointwise operation
        /// <c>a ↦ ⟦left⟧(a) ⋄ ⟦right⟧(a)</c> on every element satisfying
        /// <paramref name="pathCondition"/> — so on all of Σ for the usual
        /// <c>⊤</c> argument. Branches unreachable under the path condition are
        /// cleaned away where the algebra can prove them so; see
        /// <see cref="ApplyMemo{TValue}"/> for how that interacts with
        /// memoisation and <see cref="PruningEventBudget"/> for the resulting
        /// complexity.</para>
        /// </summary>
        public TransitionTerm<TLeaf> ApplyBinary(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right,
            Func<TLeaf, TLeaf, TLeaf> operation,
            TPredicate pathCondition)
        {
            var memo = new ApplyMemo<TransitionTerm<TLeaf>>();
            return ApplyCore(left, right, operation, pathCondition, memo);
        }

        private TransitionTerm<TLeaf> ApplyCore(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf> right,
            Func<TLeaf, TLeaf, TLeaf> operation,
            TPredicate pathCondition,
            ApplyMemo<TransitionTerm<TLeaf>> memo)
        {
            var nodes = new NodePair(left, right);

            // The memo holds *only* results whose entire subcomputation was
            // pruning-free. Those are the exact structural apply of the two
            // operands, hence correct on all of Σ and reusable under any path
            // condition. See ApplyMemo for the argument. Results obtained with
            // pruning are never stored, so they can never be reused — on this
            // or on any other path.
            if (memo.Exact.TryGetValue(nodes, out var shared))
                return shared;

            long epochOnEntry = memo.PruneEpoch;
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

                // Two cases take the exact, path-condition-free split:
                //   * proposition splits (EREQ Phase-0 D5) are free Booleans —
                //     they never tighten the path and both branches are always
                //     reachable, so there is nothing to prune;
                //   * the pruning budget is spent, in which case pruning is
                //     permanently off for the rest of this Apply call.
                // Both build the exact apply of the two children, which is
                // always sound: pruning is a cleaning optimisation, never a
                // correctness requirement.
                if (ConditionRegistry<TPredicate>.IsProposition(splitLevel)
                    || !memo.PruningEnabled)
                {
                    var hi0 = ApplyCore(leftHi, rightHi, operation, pathCondition, memo);
                    var lo0 = ApplyCore(leftLo, rightLo, operation, pathCondition, memo);
                    result = MkIte(splitLevel, hi0, lo0);
                }
                else
                {
                    var condition = _registry.GetPredicate(splitLevel);

                    // Clean: check branch reachability under the current path.
                    var thenPath = _eba.And(pathCondition, condition);
                    var elsePath = _eba.And(pathCondition, _eba.Not(condition));

                    bool thenReachable = _eba.IsSatisfiable(thenPath);
                    bool elseReachable = _eba.IsSatisfiable(elsePath);

                    if (!thenReachable && !elseReachable)
                    {
                        // The caller's own path is unsatisfiable, so every value
                        // is vacuously correct on it. Still a pruning event: the
                        // answer holds only on this (empty) path.
                        memo.NotePrune();
                        result = ApplyCore(leftLo, rightLo, operation, pathCondition, memo);
                    }
                    else if (!thenReachable)
                    {
                        memo.NotePrune();
                        result = ApplyCore(leftLo, rightLo, operation, elsePath, memo);
                    }
                    else if (!elseReachable)
                    {
                        memo.NotePrune();
                        result = ApplyCore(leftHi, rightHi, operation, thenPath, memo);
                    }
                    else
                    {
                        var hi = ApplyCore(leftHi, rightHi, operation, thenPath, memo);
                        var lo = ApplyCore(leftLo, rightLo, operation, elsePath, memo);
                        result = MkIte(splitLevel, hi, lo);
                    }
                }
            }

            // Cacheable iff no pruning happened anywhere below — including in
            // this frame. The epoch is monotone, so the comparison also rules
            // out ancestors of an earlier prune: every frame on the stack above
            // a pruning frame sees a moved epoch and declines to cache.
            if (memo.PruneEpoch == epochOnEntry)
                memo.Exact[nodes] = result;

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
        /// Same contract as <see cref="ApplyBinary"/>: exact on every element
        /// satisfying <paramref name="pathCondition"/>.
        /// </summary>
        public TransitionTerm<TResult> ApplyCross<TLeaf2, TResult>(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf2> right,
            Func<TLeaf, TLeaf2, TResult> operation,
            TPredicate pathCondition)
        {
            var memo = new ApplyMemo<TransitionTerm<TResult>>();
            return ApplyCrossCore(left, right, operation, pathCondition, memo);
        }

        private TransitionTerm<TResult> ApplyCrossCore<TLeaf2, TResult>(
            TransitionTerm<TLeaf> left,
            TransitionTerm<TLeaf2> right,
            Func<TLeaf, TLeaf2, TResult> operation,
            TPredicate pathCondition,
            ApplyMemo<TransitionTerm<TResult>> memo)
        {
            // Same memoisation scheme as ApplyCore: only pruning-free results
            // are cached (they are exact on all of Σ); pruned results are never
            // stored and therefore never reused on another path.
            var nodes = new NodePair(left, right);

            if (memo.Exact.TryGetValue(nodes, out var shared))
                return shared;

            long epochOnEntry = memo.PruneEpoch;
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

                // Proposition split, or budget spent: exact structural apply.
                // See ApplyCore.
                if (ConditionRegistry<TPredicate>.IsProposition(splitLevel)
                    || !memo.PruningEnabled)
                {
                    var hi0 = ApplyCrossCore(leftHi, rightHi, operation, pathCondition, memo);
                    var lo0 = ApplyCrossCore(leftLo, rightLo, operation, pathCondition, memo);
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
                        memo.NotePrune();
                        result = ApplyCrossCore(leftLo, rightLo, operation, pathCondition, memo);
                    }
                    else if (!thenReachable)
                    {
                        memo.NotePrune();
                        result = ApplyCrossCore(leftLo, rightLo, operation, elsePath, memo);
                    }
                    else if (!elseReachable)
                    {
                        memo.NotePrune();
                        result = ApplyCrossCore(leftHi, rightHi, operation, thenPath, memo);
                    }
                    else
                    {
                        var hi = ApplyCrossCore(leftHi, rightHi, operation, thenPath, memo);
                        var lo = ApplyCrossCore(leftLo, rightLo, operation, elsePath, memo);
                        result = TransitionTerm<TResult>.Ite(splitLevel, hi, lo);
                    }
                }
            }

            if (memo.PruneEpoch == epochOnEntry)
                memo.Exact[nodes] = result;

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

        /// <summary>
        /// Memo table for one <see cref="ApplyBinary"/> /
        /// <see cref="ApplyCross{TLeaf2,TResult}"/> call.
        ///
        /// <para><b>What is cached.</b> Exactly one thing: the results of node
        /// pairs whose <em>entire</em> subcomputation performed no
        /// satisfiability pruning. Nothing else is ever stored, so a pruned
        /// result is used once, on the single path that produced it, and can
        /// never leak onto another path.</para>
        ///
        /// <para><b>Why pruning-free results are safe to share.</b> If no
        /// pruning happened anywhere inside the subcomputation for a node pair
        /// <c>(ℓ, r)</c>, the returned term is the exact structural apply of
        /// the two operands. By induction over the recursion: leaves return
        /// <c>op(a, b)</c>; every split reassembles its two children — each
        /// itself pruning-free, hence exact by the induction hypothesis, or a
        /// cache hit, hence exact by the same invariant — with
        /// <c>MkIte</c>/<c>Ite</c>, whose only reduction is the
        /// semantics-preserving <c>(α ? f : f) → f</c> (Apply calls them
        /// without a path condition, so their own cleaning never engages).
        /// Shannon expansion at the split level then gives
        /// <c>⟦result⟧(σ) = op(⟦ℓ⟧(σ), ⟦r⟧(σ))</c> for <em>every</em> σ ∈ Σ,
        /// not just for the σ on the current path. Such a term is valid under
        /// every path condition, which is what preserves DAG memoisation: a
        /// pruning-free Apply visits each node pair at most once.</para>
        ///
        /// <para><b>Why the epoch is needed.</b> <see cref="PruneEpoch"/> is a
        /// monotone counter of pruning events. A frame records the epoch on
        /// entry and caches its result only if the epoch has not moved by the
        /// time it returns. Because the counter is monotone and global to the
        /// call, this also disqualifies every ancestor of a pruning frame:
        /// each of them sees a moved epoch and declines to cache. Cache hits
        /// do not touch the counter — they are exact results — so reuse never
        /// taints a caller.</para>
        ///
        /// <para><b>Bounded work.</b> Not caching pruned results means a
        /// pruning-tainted node pair may be recomputed on each path that
        /// reaches it, which on adversarial inputs is exponential. That is
        /// capped by a fixed budget: <see cref="PruningEnabled"/> goes false
        /// once <see cref="PruneEpoch"/> reaches
        /// <see cref="PruningEventBudget"/>, and since the counter only ever
        /// increases, pruning is then off for the remainder of the call. From
        /// that point every result is pruning-free, hence cacheable, so the
        /// recursion degenerates into the classical DAG apply. See the
        /// complexity note on <see cref="PruningEventBudget"/>.</para>
        /// </summary>
        private sealed class ApplyMemo<TValue>
        {
            /// <summary>
            /// Results proven exact on all of Σ — i.e. produced without any
            /// pruning — keyed by operand node pair only. There is deliberately
            /// no path-keyed table, so no notion of predicate equality (and in
            /// particular no <see cref="ConditionRegistry{TPredicate}"/>
            /// comparer) can influence memoisation.
            /// </summary>
            internal readonly Dictionary<NodePair, TValue> Exact =
                new Dictionary<NodePair, TValue>();

            /// <summary>
            /// Monotone counter of pruning events performed by this call. It
            /// serves two purposes at once: a frame is cacheable iff the
            /// counter did not move while it ran, and the counter is the meter
            /// for <see cref="PruningEventBudget"/>.
            /// </summary>
            internal long PruneEpoch;

            /// <summary>
            /// False once the fixed budget is spent; monotone, so pruning stays
            /// off for the rest of the call.
            /// </summary>
            internal bool PruningEnabled => PruneEpoch < PruningEventBudget;

            internal void NotePrune() => PruneEpoch++;
        }

        /// <summary>
        /// Number of satisfiability-pruning events a single
        /// <see cref="ApplyBinary"/> / <see cref="ApplyCross{TLeaf2,TResult}"/>
        /// call may perform before pruning is switched off for the remainder of
        /// that call. Fixed and internal on purpose: it never affects the
        /// semantics of a result, only how aggressively intermediate terms are
        /// cleaned, so there is nothing for a caller to tune.
        ///
        /// <para><b>Complexity.</b> Let <c>N</c> be the number of reachable
        /// operand node pairs (<c>N ≤ |left| · |right|</c>), <c>D</c> the
        /// recursion depth — bounded by the number of distinct condition
        /// levels in the operands, since every step strictly increases the
        /// split level — and <c>K</c> this budget. Count the frames that miss
        /// the memo, since a hit is O(1) and each miss does O(1) work plus at
        /// most two satisfiability queries and two child calls:</para>
        /// <list type="bullet">
        ///   <item>A miss that ends up cached stores an entry that was not
        ///   there before, and after it no frame for that node pair can miss
        ///   again. So there are at most <c>N</c> of them.</item>
        ///   <item>A miss that ends up uncached must, by the epoch rule, have a
        ///   pruning event in its own subtree — that is, it lies on the
        ///   recursion path from the root to some pruning event. Each pruning
        ///   event puts at most <c>D + 1</c> frames on that path, and the
        ///   budget caps the number of pruning events at <c>K</c>, so the
        ///   uncached misses number at most <c>K · (D + 1)</c>.</item>
        /// </list>
        /// <para>Hence at most <c>N + K · (D + 1)</c> misses, at most
        /// <c>2 · (N + K · (D + 1))</c> satisfiability queries and at most that
        /// many result nodes. With <c>K</c> a constant, all three are
        /// polynomial — in fact linear in the operand node-pair count plus a
        /// fixed additive term — no matter how the predicates interact. The
        /// exact phase that follows budget exhaustion is by itself the
        /// classical <c>O(N)</c> DAG apply, since all of its results are
        /// cacheable.</para>
        /// </summary>
        private const int PruningEventBudget = 256;

        /// <summary>
        /// The identity pair of the two operand nodes. Uses reference
        /// equality: operand nodes are hash-consed, so reference identity is
        /// the right notion, and unlike a packed pair of identity hash codes
        /// it cannot produce a false cache hit through a hash collision.
        /// </summary>
        private readonly struct NodePair : IEquatable<NodePair>
        {
            private readonly object _left;
            private readonly object _right;

            internal NodePair(object left, object right)
            {
                _left = left;
                _right = right;
            }

            public bool Equals(NodePair other)
                => ReferenceEquals(_left, other._left)
                   && ReferenceEquals(_right, other._right);

            public override bool Equals(object obj) => obj is NodePair p && Equals(p);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = System.Runtime.CompilerServices.RuntimeHelpers
                        .GetHashCode(_left) * 397;
                    return hash ^ System.Runtime.CompilerServices.RuntimeHelpers
                        .GetHashCode(_right);
                }
            }
        }

        #endregion
    }
}
