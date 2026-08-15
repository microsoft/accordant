namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class TransitionTermTests
    {
        private IntEba _eba;
        private ConditionRegistry<IntPredicate> _registry;
        private StringLeafAlgebra _leafAlgebra;
        private TransitionTermAlgebra<IntPredicate, int, string> _algebra;

        // Conditions: α = {0,1}, β = {2,3}, in a universe of {0,1,2,3}
        private int _alphaIdx;
        private int _betaIdx;

        [SetUp]
        public void SetUp()
        {
            _eba = new IntEba(4);
            _registry = new ConditionRegistry<IntPredicate>();
            _leafAlgebra = new StringLeafAlgebra();
            _algebra = new TransitionTermAlgebra<IntPredicate, int, string>(_eba, _registry, _leafAlgebra);

            _alphaIdx = _registry.Register(new IntPredicate("α", 0, 1));
            _betaIdx = _registry.Register(new IntPredicate("β", 2, 3));
        }

        #region Leaf Tests

        [Test]
        public void Leaf_CreatesLeafNode()
        {
            var leaf = TransitionTerm<string>.Leaf("q0");
            Assert.IsTrue(leaf.IsLeaf);
            Assert.AreEqual("q0", ((TransitionTermLeaf<string>)leaf).Value);
        }

        [Test]
        public void Leaf_StructuralEquality()
        {
            var a = TransitionTerm<string>.Leaf("q0");
            var b = TransitionTerm<string>.Leaf("q0");
            var c = TransitionTerm<string>.Leaf("q1");

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        #endregion

        #region ITE Construction and Ordering

        [Test]
        public void Ite_TrivialElimination_SameChildren()
        {
            // (α ? q0 : q0) → q0
            var leaf = TransitionTerm<string>.Leaf("q0");
            var result = TransitionTerm<string>.Ite(_alphaIdx, leaf, leaf);

            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual(leaf, result);
        }

        [Test]
        public void Ite_PreservesOrdering()
        {
            var hi = TransitionTerm<string>.Leaf("q1");
            var lo = TransitionTerm<string>.Leaf("q2");
            var ite = TransitionTerm<string>.Ite(_alphaIdx, hi, lo);

            Assert.IsFalse(ite.IsLeaf);
            Assert.AreEqual(_alphaIdx, ite.Level);
        }

        [Test]
        public void Ite_NestedOrdering_InnerMustHaveLargerIndex()
        {
            // Build (α ? (β ? q1 : q2) : q3) — valid because β > α
            var inner = TransitionTerm<string>.Ite(_betaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));
            var outer = TransitionTerm<string>.Ite(_alphaIdx,
                inner,
                TransitionTerm<string>.Leaf("q3"));

            Assert.AreEqual(_alphaIdx, outer.Level);
            var outerIte = (TransitionTermIte<string>)outer;
            Assert.AreEqual(_betaIdx, outerIte.Hi.Level);
        }

        [Test]
        public void Ite_OrderingViolation_Throws()
        {
            // Try to build (β ? (α ? q1 : q2) : q3) — invalid because α < β
            var inner = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));

            Assert.Throws<System.ArgumentException>(() =>
                TransitionTerm<string>.Ite(_betaIdx, inner, TransitionTerm<string>.Leaf("q3")));
        }

        [Test]
        public void Ite_StructuralEquality()
        {
            var a = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));
            var b = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));
            var c = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q3"));

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        #endregion

        #region Evaluation

        [Test]
        public void Evaluate_Leaf_ReturnsValue()
        {
            var leaf = TransitionTerm<string>.Leaf("q0");
            Assert.AreEqual("q0", leaf.Evaluate(0, _registry, _eba));
            Assert.AreEqual("q0", leaf.Evaluate(3, _registry, _eba));
        }

        [Test]
        public void Evaluate_Ite_FollowsThenBranch()
        {
            // (α ? q1 : q2) where α = {0,1}
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));

            // Element 0 satisfies α → q1
            Assert.AreEqual("q1", term.Evaluate(0, _registry, _eba));
            // Element 1 satisfies α → q1
            Assert.AreEqual("q1", term.Evaluate(1, _registry, _eba));
        }

        [Test]
        public void Evaluate_Ite_FollowsElseBranch()
        {
            // (α ? q1 : q2) where α = {0,1}
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));

            // Element 2 does not satisfy α → q2
            Assert.AreEqual("q2", term.Evaluate(2, _registry, _eba));
            // Element 3 does not satisfy α → q2
            Assert.AreEqual("q2", term.Evaluate(3, _registry, _eba));
        }

        [Test]
        public void Evaluate_NestedIte_AllPaths()
        {
            // (α ? (β ? q1 : q2) : q3) where α={0,1}, β={2,3}
            // Note: α∧β = ∅, so the inner β is only reachable when α is true
            // but since α={0,1} and β={2,3} are disjoint, β is never satisfied under α
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Ite(_betaIdx,
                    TransitionTerm<string>.Leaf("q1"),
                    TransitionTerm<string>.Leaf("q2")),
                TransitionTerm<string>.Leaf("q3"));

            // 0: α=true, β=false → q2
            Assert.AreEqual("q2", term.Evaluate(0, _registry, _eba));
            // 1: α=true, β=false → q2
            Assert.AreEqual("q2", term.Evaluate(1, _registry, _eba));
            // 2: α=false → q3
            Assert.AreEqual("q3", term.Evaluate(2, _registry, _eba));
            // 3: α=false → q3
            Assert.AreEqual("q3", term.Evaluate(3, _registry, _eba));
        }

        #endregion

        #region Traversal

        [Test]
        public void GetLeaves_CollectsAllLeaves()
        {
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));

            var leaves = term.GetLeaves().ToList();
            Assert.AreEqual(2, leaves.Count);
            Assert.Contains("q1", leaves);
            Assert.Contains("q2", leaves);
        }

        [Test]
        public void GetDistinctLeaves_DeduplicatesSharedLeaves()
        {
            // (α ? q1 : (β ? q1 : q2)) — q1 appears twice
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Ite(_betaIdx,
                    TransitionTerm<string>.Leaf("q1"),
                    TransitionTerm<string>.Leaf("q2")));

            var distinct = term.GetDistinctLeaves().ToList();
            Assert.AreEqual(2, distinct.Count);
            Assert.Contains("q1", distinct);
            Assert.Contains("q2", distinct);
        }

        [Test]
        public void GetDistinctConditionIndices()
        {
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Ite(_betaIdx,
                    TransitionTerm<string>.Leaf("q1"),
                    TransitionTerm<string>.Leaf("q2")),
                TransitionTerm<string>.Leaf("q3"));

            var indices = term.GetDistinctConditionIndices().ToList();
            Assert.AreEqual(2, indices.Count);
            Assert.Contains(_alphaIdx, indices);
            Assert.Contains(_betaIdx, indices);
        }

        #endregion
    }

    [TestFixture]
    public class TransitionTermAlgebraTests
    {
        private IntEba _eba;
        private ConditionRegistry<IntPredicate> _registry;
        private StringLeafAlgebra _leafAlgebra;
        private TransitionTermAlgebra<IntPredicate, int, string> _algebra;

        // Universe: {0,1,2,3}
        // α = {0,1} (even-ish), β = {2,3} (odd-ish)
        // α and β are complementary and disjoint
        private int _alphaIdx;
        private int _betaIdx;

        [SetUp]
        public void SetUp()
        {
            _eba = new IntEba(4);
            _registry = new ConditionRegistry<IntPredicate>();
            _leafAlgebra = new StringLeafAlgebra();
            _algebra = new TransitionTermAlgebra<IntPredicate, int, string>(_eba, _registry, _leafAlgebra);

            _alphaIdx = _registry.Register(new IntPredicate("α", 0, 1));
            _betaIdx = _registry.Register(new IntPredicate("β", 2, 3));
        }

        #region Smart Constructor (MkIte)

        [Test]
        public void MkIte_TrivialElimination()
        {
            var leaf = _algebra.Leaf("q0");
            var result = _algebra.MkIte(_alphaIdx, leaf, leaf);
            Assert.IsTrue(result.IsLeaf);
        }

        [Test]
        public void MkIte_PathConditionCleaning_ThenUnreachable()
        {
            // Path condition = ¬α (elements 2,3), condition = α (elements 0,1)
            // α ∧ ¬α = ∅ → then-branch unreachable → returns lo
            var notAlpha = _eba.Not(_registry.GetPredicate(_alphaIdx));
            var result = _algebra.MkIte(_alphaIdx,
                _algebra.Leaf("q1"),
                _algebra.Leaf("q2"),
                notAlpha);

            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual("q2", ((TransitionTermLeaf<string>)result).Value);
        }

        [Test]
        public void MkIte_PathConditionCleaning_ElseUnreachable()
        {
            // Path condition = α (elements 0,1), condition = α (elements 0,1)
            // ¬α ∧ α = ∅ → else-branch unreachable → returns hi
            var alpha = _registry.GetPredicate(_alphaIdx);
            var result = _algebra.MkIte(_alphaIdx,
                _algebra.Leaf("q1"),
                _algebra.Leaf("q2"),
                alpha);

            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual("q1", ((TransitionTermLeaf<string>)result).Value);
        }

        #endregion

        #region Or (Disjunction with ACI)

        [Test]
        public void Or_BottomIsUnit()
        {
            // ⊥ ∨ f = f
            var f = _algebra.Leaf("q0");
            var result = _algebra.Or(_algebra.Bottom, f);
            Assert.AreEqual(f, result);
        }

        [Test]
        public void Or_TopIsZero()
        {
            // ⊤ ∨ f = ⊤
            var f = _algebra.Leaf("q0");
            var result = _algebra.Or(_algebra.Top, f);
            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual("⊤", ((TransitionTermLeaf<string>)result).Value);
        }

        [Test]
        public void Or_Idempotent()
        {
            // f ∨ f = f
            var f = _algebra.Leaf("q0");
            var result = _algebra.Or(f, f);
            Assert.AreEqual(f, result);
        }

        [Test]
        public void Or_LiftedIntoIte()
        {
            // (α ? q1 : q2) ∨ leaf("q3")
            // = (α ? q1∨q3 : q2∨q3)
            var left = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));
            var right = _algebra.Leaf("q3");

            var result = _algebra.Or(left, right);

            // Evaluate at each element to verify semantics
            Assert.AreEqual("q1∨q3", result.Evaluate(0, _registry, _eba)); // α=true
            Assert.AreEqual("q2∨q3", result.Evaluate(2, _registry, _eba)); // α=false
        }

        [Test]
        public void Or_AciNormalization_Sorted()
        {
            // "b" ∨ "a" should produce "a∨b" (sorted)
            var a = _algebra.Leaf("a");
            var b = _algebra.Leaf("b");
            var result = _algebra.Or(a, b);

            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual("a∨b", ((TransitionTermLeaf<string>)result).Value);
        }

        #endregion

        #region And (Conjunction with ACI)

        [Test]
        public void And_TopIsUnit()
        {
            // ⊤ ∧ f = f
            var f = _algebra.Leaf("q0");
            var result = _algebra.And(_algebra.Top, f);
            Assert.AreEqual(f, result);
        }

        [Test]
        public void And_BottomIsZero()
        {
            // ⊥ ∧ f = ⊥
            var f = _algebra.Leaf("q0");
            var result = _algebra.And(_algebra.Bottom, f);
            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual("⊥", ((TransitionTermLeaf<string>)result).Value);
        }

        [Test]
        public void And_LiftedIntoIte()
        {
            // (α ? q1 : q2) ∧ leaf("q3")
            // = (α ? q1∧q3 : q2∧q3)
            var left = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));
            var right = _algebra.Leaf("q3");

            var result = _algebra.And(left, right);

            Assert.AreEqual("q1∧q3", result.Evaluate(0, _registry, _eba)); // α=true
            Assert.AreEqual("q2∧q3", result.Evaluate(2, _registry, _eba)); // α=false
        }

        #endregion

        #region Not (Complement)

        [Test]
        public void Not_TopBecomesBottom()
        {
            var result = _algebra.Not(_algebra.Top);
            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual("⊥", ((TransitionTermLeaf<string>)result).Value);
        }

        [Test]
        public void Not_BottomBecomesTop()
        {
            var result = _algebra.Not(_algebra.Bottom);
            Assert.IsTrue(result.IsLeaf);
            Assert.AreEqual("⊤", ((TransitionTermLeaf<string>)result).Value);
        }

        [Test]
        public void Not_LiftedIntoIte()
        {
            // ¬(α ? q1 : q2) = (α ? ¬q1 : ¬q2)
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));

            var result = _algebra.Not(term);

            Assert.AreEqual("¬q1", result.Evaluate(0, _registry, _eba));
            Assert.AreEqual("¬q2", result.Evaluate(2, _registry, _eba));
        }

        #endregion

        #region Apply with Cleaning (Example 3.1 from paper)

        [Test]
        public void Apply_CleaningRemovesUnreachableBranch()
        {
            // From Example 3.1: α implies β (α ⊂ β)
            // Setup: α={0}, β={0,1} in universe {0,1,2}
            var eba3 = new IntEba(3);
            var reg3 = new ConditionRegistry<IntPredicate>();
            var alg3 = new TransitionTermAlgebra<IntPredicate, int, string>(eba3, reg3, _leafAlgebra);

            var alphaSmall = reg3.Register(new IntPredicate("α", 0));
            var betaLarge = reg3.Register(new IntPredicate("β", 0, 1));

            // ¬(α ? φ : ⊥) ∨ (β ? φ : ⊥)
            // After cleaning: should simplify because when α is true, β is also true
            var guardedAlpha = TransitionTerm<string>.Ite(alphaSmall,
                TransitionTerm<string>.Leaf("φ"),
                TransitionTerm<string>.Leaf("⊥"));
            var negated = alg3.Not(guardedAlpha);
            var guardedBeta = TransitionTerm<string>.Ite(betaLarge,
                TransitionTerm<string>.Leaf("φ"),
                TransitionTerm<string>.Leaf("⊥"));

            var result = alg3.Or(negated, guardedBeta);

            // For element 0: α=true, β=true → ¬φ ∨ φ (StringLeafAlgebra doesn't simplify complementation)
            // For element 1: α=false, β=true → ⊤ ∨ φ = ⊤
            // For element 2: α=false, β=false → ⊤ ∨ ⊥ = ⊤
            Assert.AreEqual("¬φ∨φ", result.Evaluate(0, reg3, eba3));
            Assert.AreEqual("⊤", result.Evaluate(1, reg3, eba3));
            Assert.AreEqual("⊤", result.Evaluate(2, reg3, eba3));
        }

        #endregion

        #region Apply Merging Two ITEs

        [Test]
        public void Apply_SameCondition_MergesBranches()
        {
            // (α ? a : b) ∨ (α ? c : d) = (α ? a∨c : b∨d)
            var left = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("a"),
                TransitionTerm<string>.Leaf("b"));
            var right = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("c"),
                TransitionTerm<string>.Leaf("d"));

            var result = _algebra.Or(left, right);

            Assert.AreEqual("a∨c", result.Evaluate(0, _registry, _eba));
            Assert.AreEqual("b∨d", result.Evaluate(2, _registry, _eba));
        }

        [Test]
        public void Apply_DifferentConditions_SplitsOnSmaller()
        {
            // (α ? a : b) ∨ (β ? c : d) where α < β
            // = (α ? (β ? a∨c : a∨d) : (β ? b∨c : b∨d))
            var left = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("a"),
                TransitionTerm<string>.Leaf("b"));
            var right = TransitionTerm<string>.Ite(_betaIdx,
                TransitionTerm<string>.Leaf("c"),
                TransitionTerm<string>.Leaf("d"));

            var result = _algebra.Or(left, right);

            // Verify all 4 combinations:
            // 0: α=true, β=false → a∨d
            Assert.AreEqual("a∨d", result.Evaluate(0, _registry, _eba));
            // 1: α=true, β=false → a∨d
            Assert.AreEqual("a∨d", result.Evaluate(1, _registry, _eba));
            // 2: α=false, β=true → b∨c
            Assert.AreEqual("b∨c", result.Evaluate(2, _registry, _eba));
            // 3: α=false, β=true → b∨c
            Assert.AreEqual("b∨c", result.Evaluate(3, _registry, _eba));
        }

        #endregion

        #region Apply memoisation vs. path-condition pruning

        /// <summary>
        /// Regression: a sub-result that was obtained by <em>pruning</em> an
        /// unsatisfiable branch must never be reused on a different path.
        ///
        /// <para>The recursion prunes a branch whose path condition is
        /// unsatisfiable, so the value computed for a node pair that way is
        /// only valid on the path it was computed for. Here the pair
        /// <c>(leaf "x", (γ ? "u" : ⊥))</c> is reached twice: once under
        /// <c>α ∧ β</c>, where γ is unsatisfiable and the sub-result is
        /// correctly pruned to ⊥, and once under <c>¬α ∧ β</c>, where γ holds
        /// and the sub-result must be <c>x∧u</c>. Keyed on the node pair alone,
        /// the second lookup returned the pruned ⊥ and silently deleted the
        /// successor — which, in the RLTL/LTL pipeline, dropped whole
        /// transitions out of ABW macrostate conjunctions and turned real
        /// counterexamples into "property holds".</para>
        ///
        /// <para>The fix caches <em>only</em> results whose whole
        /// subcomputation was pruning-free — those are the exact structural
        /// apply and are valid on all of Σ (see
        /// <see cref="Apply_PruningFreeResults_AreSharedAcrossPaths"/>). A
        /// pruned result is not stored anywhere, so it is used exactly once, on
        /// the path that produced it.</para>
        /// </summary>
        [Test]
        public void Apply_PrunedSubresult_DoesNotLeakOntoAnotherPath()
        {
            // Universe {0,1,2,3}. a = {2,3}, b = {1,3}, g ("¬a") = {0,1}.
            var eba = new IntEba(4);
            var reg = new ConditionRegistry<IntPredicate>();
            var alg = new TransitionTermAlgebra<IntPredicate, int, string>(eba, reg, _leafAlgebra);

            int a = reg.Register(new IntPredicate("a", 2, 3));
            int b = reg.Register(new IntPredicate("b", 1, 3));
            int g = reg.Register(new IntPredicate("g", 0, 1));

            // The two "x" leaves are the same interned node, which is what
            // makes the memo table see one node pair on two different paths.
            var x = alg.Leaf("x");
            var left = alg.MkIte(a, alg.MkIte(b, x, alg.Leaf("y")), x);
            var right = alg.MkIte(
                b,
                alg.MkIte(g, alg.Leaf("u"), alg.Bottom),
                alg.MkIte(g, alg.Top, alg.Bottom));

            var result = alg.And(left, right);

            // element 1: a=false, b=true, g=true → x ∧ u
            Assert.AreEqual("u∧x", result.Evaluate(1, reg, eba));
            // element 3: a=true, b=true, g=false → x ∧ ⊥ = ⊥
            Assert.AreEqual("⊥", result.Evaluate(3, reg, eba));
            // element 0: a=false, b=false, g=true → x ∧ ⊤ = x
            Assert.AreEqual("x", result.Evaluate(0, reg, eba));
        }

        /// <summary>
        /// The same leak, through the cross-type Apply used by alternation
        /// elimination and the RLTL derivative.
        /// </summary>
        [Test]
        public void ApplyCross_PrunedSubresult_DoesNotLeakOntoAnotherPath()
        {
            var eba = new IntEba(4);
            var reg = new ConditionRegistry<IntPredicate>();
            var alg = new TransitionTermAlgebra<IntPredicate, int, string>(eba, reg, _leafAlgebra);

            int a = reg.Register(new IntPredicate("a", 2, 3));
            int b = reg.Register(new IntPredicate("b", 1, 3));
            int g = reg.Register(new IntPredicate("g", 0, 1));

            var x = alg.Leaf("x");
            var left = alg.MkIte(a, alg.MkIte(b, x, alg.Leaf("y")), x);

            // Right operand has a *different* leaf type, and its two γ-nodes
            // are separate objects so that only (x, ·) pairs are shared.
            var right = TransitionTerm<int>.Ite(
                b,
                TransitionTerm<int>.Ite(g, TransitionTerm<int>.Leaf(1), TransitionTerm<int>.Leaf(0)),
                TransitionTerm<int>.Ite(g, TransitionTerm<int>.Leaf(2), TransitionTerm<int>.Leaf(0)));

            var result = alg.ApplyCross<int, string>(left, right, (s, i) => s + i, eba.Top);

            // element 1: a=false, b=true, g=true → "x" + 1
            Assert.AreEqual("x1", result.Evaluate(1, reg, eba));
            // element 3: a=true, b=true, g=false → "x" + 0 (γ pruned here)
            Assert.AreEqual("x0", result.Evaluate(3, reg, eba));
            // element 0: a=false, b=false, g=true → "x" + 2
            Assert.AreEqual("x2", result.Evaluate(0, reg, eba));
        }

        /// <summary>
        /// Performance regression, expressed as work <em>counts</em> rather
        /// than wall-clock time: when no branch is ever pruned, every result is
        /// path-independent and Apply must therefore visit each operand node
        /// pair at most once (classical DAG memoisation).
        ///
        /// <para>The operands are two <c>n</c>-level DAGs with two nodes per
        /// level, i.e. <c>O(n)</c> nodes but <c>2ⁿ</c> distinct root-to-leaf
        /// paths — and every path yields a <em>distinct</em> path condition,
        /// because the conditions are independent bit tests. Keying the memo
        /// table on the path condition therefore degrades Apply from
        /// <c>O(|left|·|right|)</c> to <c>Θ(2ⁿ)</c>: the leaf operation would
        /// run <c>2ⁿ</c> = <see cref="ExponentialPathCount"/> times instead of
        /// once per distinct leaf pair.</para>
        /// </summary>
        [Test]
        public void Apply_PruningFreeResults_AreSharedAcrossPaths()
        {
            var eba = new CountingIntEba(1 << ExponentialLevels);
            var reg = new ConditionRegistry<IntPredicate>();
            var alg = new TransitionTermAlgebra<IntPredicate, int, string>(eba, reg, _leafAlgebra);

            RegisterIndependentBitConditions(reg, ExponentialLevels);

            var left = BuildAlternatingDag(alg, ExponentialLevels, "p", "q");
            var right = BuildAlternatingDag(alg, ExponentialLevels, "r", "s", stride: 2);

            int leafOps = 0;
            eba.SatisfiabilityChecks = 0;
            var result = alg.ApplyBinary(
                left, right,
                (l, r) => { leafOps++; return l + r; },
                eba.Top);

            // 4 distinct leaf pairs: (p,r) (p,s) (q,r) (q,s).
            Assert.AreEqual(4, leafOps,
                "Apply must evaluate each distinct leaf pair exactly once; "
                + $"{ExponentialPathCount} would mean the memo table lost DAG sharing.");

            // O(levels) node pairs, 2 satisfiability checks per pair.
            Assert.LessOrEqual(eba.SatisfiabilityChecks, 16 * ExponentialLevels + 16,
                "Satisfiability queries must stay linear in the DAG size.");

            // Structure: the result is still exactly the pointwise operation.
            AssertPointwise(result, left, right, (l, r) => l + r, reg, eba, 1 << ExponentialLevels);
        }

        /// <summary>
        /// The cross-type Apply must share pruning-free results too — it is the
        /// hot path of <c>AlternationElimination</c> / <c>IncrementalAE</c>.
        /// See <see cref="Apply_PruningFreeResults_AreSharedAcrossPaths"/>.
        /// </summary>
        [Test]
        public void ApplyCross_PruningFreeResults_AreSharedAcrossPaths()
        {
            var eba = new CountingIntEba(1 << ExponentialLevels);
            var reg = new ConditionRegistry<IntPredicate>();
            var alg = new TransitionTermAlgebra<IntPredicate, int, string>(eba, reg, _leafAlgebra);

            RegisterIndependentBitConditions(reg, ExponentialLevels);

            var left = BuildAlternatingDag(alg, ExponentialLevels, "p", "q");
            var right = BuildAlternatingDagOfInts(ExponentialLevels, 1, 2, stride: 2);

            int leafOps = 0;
            eba.SatisfiabilityChecks = 0;
            var result = alg.ApplyCross<int, string>(
                left, right,
                (l, r) => { leafOps++; return l + r; },
                eba.Top);

            Assert.AreEqual(4, leafOps,
                "ApplyCross must evaluate each distinct leaf pair exactly once; "
                + $"{ExponentialPathCount} would mean the memo table lost DAG sharing.");

            Assert.LessOrEqual(eba.SatisfiabilityChecks, 16 * ExponentialLevels + 16,
                "Satisfiability queries must stay linear in the DAG size.");

            for (int e = 0; e < (1 << ExponentialLevels); e++)
            {
                Assert.AreEqual(
                    left.Evaluate(e, reg, eba) + right.Evaluate(e, reg, eba),
                    result.Evaluate(e, reg, eba),
                    $"ApplyCross result disagrees with the pointwise operation at element {e}.");
            }
        }

        /// <summary>
        /// Differential soundness net for the pruning-aware memoisation: on
        /// deterministically generated DAGs over deliberately overlapping and
        /// conflicting predicates — where pruning fires constantly, so most
        /// sub-results are path-dependent and therefore uncacheable — the Apply
        /// result must equal the pointwise operation on every element of the
        /// universe. Any pruned result that leaked to a foreign path would show
        /// up here as a mismatch.
        /// </summary>
        [Test]
        public void Apply_MatchesPointwiseSemantics_UnderHeavyPruning()
        {
            const int universe = 8;
            var random = new System.Random(20260810);

            for (int trial = 0; trial < 200; trial++)
            {
                var eba = new IntEba(universe);
                var reg = new ConditionRegistry<IntPredicate>();
                var alg = new TransitionTermAlgebra<IntPredicate, int, string>(eba, reg, _leafAlgebra);

                // Overlapping / nested / disjoint predicates over {0..7}: many
                // conjunctions of their literals are unsatisfiable, so the
                // recursion prunes on most paths.
                int conditions = 3 + random.Next(3);
                for (int i = 0; i < conditions; i++)
                {
                    var elements = new List<int>();
                    for (int e = 0; e < universe; e++)
                        if (random.Next(3) != 0) elements.Add(e);
                    if (elements.Count == 0) elements.Add(random.Next(universe));
                    reg.Register(new IntPredicate("c" + i, elements));
                }

                var left = BuildRandomTerm(alg, random, 0, reg.Count);
                var right = BuildRandomTerm(alg, random, 0, reg.Count);

                // Pruning fires on most of these paths, so most sub-results are
                // never cached; the results must be exact all the same.
                AssertPointwise(alg.And(left, right), left, right, _leafAlgebra.And, reg, eba, universe);
                AssertPointwise(alg.Or(left, right), left, right, _leafAlgebra.Or, reg, eba, universe);
                AssertPointwise(
                    alg.ApplyCross<string, string>(left, right, (l, r) => l + "|" + r, eba.Top),
                    left, right, (l, r) => l + "|" + r, reg, eba, universe);
            }
        }

        /// <summary>
        /// The fixed pruning-event budget is what makes the worst case
        /// polynomial. Because a pruned result is never cached, a node pair
        /// whose subcomputation pruned is recomputed on every path that reaches
        /// it — on an adversarial input that is <c>Θ(2^levels)</c>. After the
        /// budget is spent, pruning is switched off for the rest of the call,
        /// every remaining result is exact and therefore cacheable, and the
        /// recursion degenerates into the classical DAG apply.
        ///
        /// <para>The adversarial input is an alternating DAG over
        /// <c>levels</c> mutually independent bit conditions — <c>O(levels)</c>
        /// nodes, <c>2^levels</c> root-to-leaf paths, all satisfiable — whose
        /// innermost condition ("bit 0 is clear") conflicts with the path on
        /// <em>every</em> one of them, so every path prunes and no ancestor is
        /// ever cacheable.</para>
        ///
        /// <para>Measured below the budget (7 levels, 128 paths, 128 pruning
        /// events) the cost is genuinely exponential: 510 satisfiability
        /// queries, a complete recursion tree — every single path is explored.
        /// Measured far above it (18 levels, 262144 paths) the cost stays at
        /// 1042, well inside the analytic bound
        /// <c>2·(nodePairs + budget·(depth+1))</c>; with the fallback removed
        /// the same input costs 1048574 queries, i.e. the full
        /// <c>2·(2^19 − 1)</c> recursion tree. Both results must be
        /// exact.</para>
        /// </summary>
        [Test]
        public void Apply_AdversarialPathDependence_IsBoundedByTheFixedFallback()
        {
            int exponential = MeasureAdversarialSatChecks(
                BelowBudgetLevels, cross: false);
            int bounded = MeasureAdversarialSatChecks(
                AboveBudgetLevels, cross: false);

            AssertFallbackBoundsWork(exponential, bounded);
        }

        /// <summary>
        /// The cross-type Apply carries the same fixed fallback — it is the hot
        /// path of <c>AlternationElimination</c> / <c>IncrementalAE</c> and the
        /// RLTL derivative. See
        /// <see cref="Apply_AdversarialPathDependence_IsBoundedByTheFixedFallback"/>.
        /// </summary>
        [Test]
        public void ApplyCross_AdversarialPathDependence_IsBoundedByTheFixedFallback()
        {
            int exponential = MeasureAdversarialSatChecks(
                BelowBudgetLevels, cross: true);
            int bounded = MeasureAdversarialSatChecks(
                AboveBudgetLevels, cross: true);

            AssertFallbackBoundsWork(exponential, bounded);
        }

        /// <summary>
        /// Switching pruning off mid-flight must not change what Apply
        /// computes. This walks the adversarial family across the point where
        /// the internal budget runs out — 6 levels prune 64 times (all
        /// pruning), 13 levels prune 8192 times (pruning stops after the first
        /// few hundred) — and checks the result against the pointwise operation
        /// on every element of the universe at each size, for both Apply and
        /// ApplyCross.
        /// </summary>
        [Test]
        public void Apply_IsExactAcrossThePruningBudgetBoundary()
        {
            for (int levels = 6; levels <= 13; levels++)
            {
                MeasureAdversarialSatChecks(levels, cross: false);
                MeasureAdversarialSatChecks(levels, cross: true);
            }
        }

        /// <summary>
        /// The memo is keyed on a pair of operand <em>nodes</em> and mentions
        /// no predicate, so no notion of predicate equality can influence it.
        /// In particular a <see cref="ConditionRegistry{TPredicate}"/> built
        /// with a custom — here deliberately lossy — equality comparer cannot
        /// change what Apply computes, nor how much work it does. (A memo keyed
        /// additionally on the accumulated path condition would have to answer
        /// which comparer decides that key, and a lossy one such as the one
        /// below would conflate genuinely different paths.)
        /// </summary>
        [Test]
        public void Apply_CustomRegistryPredicateComparer_CannotAffectMemoisation()
        {
            var lossy = new SameCardinalityComparer();

            // The comparer really is lossy: it identifies predicates denoting
            // different sets, so it would conflate distinct path conditions.
            var one = new IntPredicate("{0,1}", 0, 1);
            var other = new IntPredicate("{4,5}", 4, 5);
            Assert.IsFalse(one.Equals(other), "The two predicates differ.");
            Assert.IsTrue(lossy.Equals(one, other),
                "The comparer is supposed to conflate them.");

            var withDefault = RunComparerProbe(null);
            var withLossy = RunComparerProbe(lossy);

            Assert.AreEqual(withDefault.And, withLossy.And,
                "And must not depend on the registry's predicate comparer.");
            Assert.AreEqual(withDefault.Or, withLossy.Or,
                "Or must not depend on the registry's predicate comparer.");
            Assert.AreEqual(withDefault.Cross, withLossy.Cross,
                "ApplyCross must not depend on the registry's predicate comparer.");
            Assert.AreEqual(withDefault.SatChecks, withLossy.SatChecks,
                "Memoisation must not depend on the registry's predicate comparer.");
        }

        // ---- helpers ----------------------------------------------------

        /// <summary>
        /// Levels for the adversarial run that stays below the internal pruning
        /// budget, so it exhibits the unbounded, path-driven behaviour.
        /// </summary>
        private const int BelowBudgetLevels = 7;

        /// <summary>
        /// Levels for the adversarial run that blows straight through the
        /// internal budget and must fall back to the exact apply.
        /// </summary>
        private const int AboveBudgetLevels = 18;

        /// <summary>
        /// Asserts the two adversarial measurements: exponential below the
        /// budget, fixed above it, even though the path count grew by
        /// <c>2^(AboveBudgetLevels-BelowBudgetLevels)</c> = 2048×.
        /// Measured, for both Apply and ApplyCross: 510 satisfiability queries
        /// for 128 paths (a complete recursion tree, i.e. every path explored),
        /// 1042 for 262144 paths.
        /// </summary>
        private static void AssertFallbackBoundsWork(int exponential, int bounded)
        {
            Assert.GreaterOrEqual(exponential, 1 << BelowBudgetLevels,
                "Below the budget the adversarial input is supposed to cost at "
                + "least one satisfiability query per root-to-leaf path.");

            // Analytic bound: 2·(nodePairs + budget·(depth+1)) with a budget of
            // 256 pruning events, ~120 node pairs and depth 19 → ~10 000.
            Assert.LessOrEqual(bounded, 12_000,
                $"The fallback must cap the work at the fixed budget; measured "
                + $"{bounded} satisfiability queries for "
                + $"{1L << AboveBudgetLevels} paths.");

            Assert.LessOrEqual(bounded, 4 * exponential,
                $"The path count grew {1 << (AboveBudgetLevels - BelowBudgetLevels)}× "
                + $"but the work must not: {exponential} → {bounded} "
                + "satisfiability queries.");
        }

        /// <summary>
        /// Runs Apply (or ApplyCross) on an adversarial input with
        /// <c>2^levels</c> satisfiable paths whose innermost condition
        /// conflicts with the path on every one of them, and returns the number
        /// of satisfiability queries issued. Also asserts that the result is
        /// exact.
        /// </summary>
        private int MeasureAdversarialSatChecks(int levels, bool cross)
        {
            var eba = new CubeEba(levels + 1);
            var reg = new ConditionRegistry<Cubes>();
            var alg = new TransitionTermAlgebra<Cubes, int, string>(eba, reg, _leafAlgebra);

            for (int bit = 0; bit < levels; bit++)
                reg.Register(CubeEba.Literal(bit, true));

            // Innermost condition: "bit 0 is clear". On every path, exactly one
            // of it and its negation conflicts with the accumulated path
            // condition, so every path prunes.
            int conflict = reg.Register(CubeEba.Literal(0, false));

            var left = BuildAlternatingDag(
                alg, levels, "p", "q",
                bottomHi: alg.MkIte(conflict, alg.Leaf("p"), alg.Leaf("q")),
                bottomLo: alg.MkIte(conflict, alg.Leaf("q"), alg.Leaf("p")));

            eba.SatisfiabilityChecks = 0;

            if (cross)
            {
                var right = BuildAlternatingDagOfInts(
                    levels, 1, 2, stride: 2,
                    bottomHi: TransitionTerm<int>.Ite(
                        conflict, TransitionTerm<int>.Leaf(1), TransitionTerm<int>.Leaf(2)),
                    bottomLo: TransitionTerm<int>.Ite(
                        conflict, TransitionTerm<int>.Leaf(2), TransitionTerm<int>.Leaf(1)));

                var crossResult = alg.ApplyCross<int, string>(
                    left, right, (l, r) => l + r, eba.Top);
                int checks = eba.SatisfiabilityChecks;

                AssertPointwiseSampled(
                    e => crossResult.Evaluate(e, reg, eba),
                    e => left.Evaluate(e, reg, eba) + right.Evaluate(e, reg, eba),
                    levels);
                return checks;
            }
            else
            {
                var right = BuildAlternatingDag(
                    alg, levels, "r", "s", stride: 2,
                    bottomHi: alg.MkIte(conflict, alg.Leaf("r"), alg.Leaf("s")),
                    bottomLo: alg.MkIte(conflict, alg.Leaf("s"), alg.Leaf("r")));

                var result = alg.ApplyBinary(left, right, (l, r) => l + r, eba.Top);
                int checks = eba.SatisfiabilityChecks;

                AssertPointwiseSampled(
                    e => result.Evaluate(e, reg, eba),
                    e => left.Evaluate(e, reg, eba) + right.Evaluate(e, reg, eba),
                    levels);
                return checks;
            }
        }

        /// <summary>
        /// Checks that <paramref name="actual"/> agrees with
        /// <paramref name="expected"/> on every element of <c>{0 .. 2^bits-1}</c>
        /// when that is cheap, and on a deterministic pseudo-random sample of
        /// them otherwise.
        /// </summary>
        private static void AssertPointwiseSampled(
            System.Func<int, string> actual,
            System.Func<int, string> expected,
            int bits)
        {
            int universe = 1 << bits;
            if (universe <= 8192)
            {
                for (int e = 0; e < universe; e++)
                    Assert.AreEqual(expected(e), actual(e),
                        $"Apply result disagrees with the pointwise operation at element {e}.");
                return;
            }

            var random = new System.Random(20260810);
            for (int i = 0; i < 4096; i++)
            {
                int e = i < 2 ? i * (universe - 1) : random.Next(universe);
                Assert.AreEqual(expected(e), actual(e),
                    $"Apply result disagrees with the pointwise operation at element {e}.");
            }
        }

        /// <summary>Results of one <see cref="RunComparerProbe"/> run.</summary>
        private readonly struct ComparerProbe
        {
            internal ComparerProbe(
                TransitionTerm<string> and,
                TransitionTerm<string> or,
                TransitionTerm<string> cross,
                int satChecks)
            {
                And = and; Or = or; Cross = cross; SatChecks = satChecks;
            }

            internal TransitionTerm<string> And { get; }
            internal TransitionTerm<string> Or { get; }
            internal TransitionTerm<string> Cross { get; }
            internal int SatChecks { get; }
        }

        /// <summary>
        /// Runs a pruning-heavy Apply/ApplyCross over a registry built with the
        /// given predicate comparer, verifies the results pointwise, and
        /// reports them together with the number of satisfiability queries.
        /// The registered predicates have pairwise distinct cardinalities, so
        /// registration itself is unaffected by the comparer and the two runs
        /// are identical in everything but the comparer.
        /// </summary>
        private ComparerProbe RunComparerProbe(IEqualityComparer<IntPredicate> comparer)
        {
            const int universe = 8;
            var eba = new CountingIntEba(universe);
            var reg = new ConditionRegistry<IntPredicate>(comparer);
            var alg = new TransitionTermAlgebra<IntPredicate, int, string>(eba, reg, _leafAlgebra);

            int c0 = reg.Register(new IntPredicate("c0", 0, 1, 2, 3));
            int c1 = reg.Register(new IntPredicate("c1", 0, 1, 4));
            int c2 = reg.Register(new IntPredicate("c2", 0, 5));
            int c3 = reg.Register(new IntPredicate("c3", 6));
            Assert.AreEqual(4, reg.Count, "The comparer must not merge the conditions.");

            var tail = alg.MkIte(c3, alg.Leaf("w"), alg.Bottom);
            var shared = alg.MkIte(c2, alg.Leaf("u"), tail);
            var left = alg.MkIte(c0, alg.MkIte(c1, shared, alg.Leaf("y")), shared);
            var right = alg.MkIte(c1, shared, alg.MkIte(c2, alg.Leaf("z"), tail));

            eba.SatisfiabilityChecks = 0;
            var and = alg.And(left, right);
            var or = alg.Or(left, right);
            var cross = alg.ApplyCross<string, string>(
                left, right, (l, r) => l + "|" + r, eba.Top);
            int checks = eba.SatisfiabilityChecks;

            AssertPointwise(and, left, right, _leafAlgebra.And, reg, eba, universe);
            AssertPointwise(or, left, right, _leafAlgebra.Or, reg, eba, universe);
            AssertPointwise(cross, left, right, (l, r) => l + "|" + r, reg, eba, universe);

            return new ComparerProbe(and, or, cross, checks);
        }

        /// <summary>Levels in the exponential-path DAGs built below.</summary>
        private const int ExponentialLevels = 12;

        /// <summary>Root-to-leaf paths (and distinct path conditions) in them.</summary>
        private const int ExponentialPathCount = 1 << ExponentialLevels;

        /// <summary>
        /// Registers <paramref name="levels"/> mutually independent conditions
        /// "bit i is set" over the universe {0 .. 2^levels-1}, in index order,
        /// so that every conjunction of their literals is satisfiable and no
        /// pruning can occur.
        /// </summary>
        private static void RegisterIndependentBitConditions(
            ConditionRegistry<IntPredicate> registry, int levels)
        {
            int size = 1 << levels;
            for (int i = 0; i < levels; i++)
            {
                int bit = i;
                registry.Register(new IntPredicate(
                    "bit" + bit,
                    Enumerable.Range(0, size).Where(e => ((e >> bit) & 1) != 0)));
            }
        }

        /// <summary>
        /// Builds a DAG with two nodes per level — <c>Xᵢ = (i ? Xᵢ₊₁ : Yᵢ₊₁)</c>
        /// and <c>Yᵢ = (i ? Yᵢ₊₁ : Xᵢ₊₁)</c> — so it has <c>O(levels)</c>
        /// nodes but <c>2^levels</c> root-to-leaf paths. Returns X₀.
        /// <paramref name="stride"/> selects which condition indices the DAG
        /// branches on (0, stride, 2·stride, …); using different strides for
        /// the two operands de-synchronises them so that every combination of
        /// their nodes is actually reached. <paramref name="bottomHi"/> /
        /// <paramref name="bottomLo"/> optionally replace the two leaves with
        /// arbitrary inner-level subterms.
        /// </summary>
        private static TransitionTerm<string> BuildAlternatingDag<TPred>(
            TransitionTermAlgebra<TPred, int, string> algebra,
            int levels, string hiLeaf, string loLeaf, int stride = 1,
            TransitionTerm<string> bottomHi = null,
            TransitionTerm<string> bottomLo = null)
        {
            var x = bottomHi ?? algebra.Leaf(hiLeaf);
            var y = bottomLo ?? algebra.Leaf(loLeaf);
            for (int i = ((levels - 1) / stride) * stride; i >= 0; i -= stride)
            {
                var nx = algebra.MkIte(i, x, y);
                var ny = algebra.MkIte(i, y, x);
                x = nx;
                y = ny;
            }
            return x;
        }

        /// <summary>Cross-type twin of <see cref="BuildAlternatingDag"/>.</summary>
        private static TransitionTerm<int> BuildAlternatingDagOfInts(
            int levels, int hiLeaf, int loLeaf, int stride = 1,
            TransitionTerm<int> bottomHi = null,
            TransitionTerm<int> bottomLo = null)
        {
            var x = bottomHi ?? TransitionTerm<int>.Leaf(hiLeaf);
            var y = bottomLo ?? TransitionTerm<int>.Leaf(loLeaf);
            for (int i = ((levels - 1) / stride) * stride; i >= 0; i -= stride)
            {
                var nx = TransitionTerm<int>.Ite(i, x, y);
                var ny = TransitionTerm<int>.Ite(i, y, x);
                x = nx;
                y = ny;
            }
            return x;
        }

        private static readonly string[] RandomLeaves = { "a", "b", "c", "⊤", "⊥" };

        /// <summary>
        /// Builds a well-ordered random transition term over condition indices
        /// in <c>[level, count)</c>.
        /// </summary>
        private static TransitionTerm<string> BuildRandomTerm(
            TransitionTermAlgebra<IntPredicate, int, string> algebra,
            System.Random random, int level, int count)
        {
            if (level >= count || random.Next(4) == 0)
                return algebra.Leaf(RandomLeaves[random.Next(RandomLeaves.Length)]);

            return algebra.MkIte(
                level,
                BuildRandomTerm(algebra, random, level + 1, count),
                BuildRandomTerm(algebra, random, level + 1, count));
        }

        /// <summary>
        /// Asserts that <paramref name="result"/> agrees with the pointwise
        /// application of <paramref name="operation"/> on every element.
        /// </summary>
        private static void AssertPointwise(
            TransitionTerm<string> result,
            TransitionTerm<string> left,
            TransitionTerm<string> right,
            System.Func<string, string, string> operation,
            ConditionRegistry<IntPredicate> registry,
            IEffectiveBooleanAlgebra<IntPredicate, int> eba,
            int universe)
        {
            for (int e = 0; e < universe; e++)
            {
                var expected = operation(
                    left.Evaluate(e, registry, eba),
                    right.Evaluate(e, registry, eba));
                Assert.AreEqual(expected, result.Evaluate(e, registry, eba),
                    $"Apply result disagrees with the pointwise operation at element {e}.");
            }
        }

        /// <summary>
        /// <see cref="IntEba"/> that counts satisfiability queries, so tests
        /// can assert on the amount of work Apply performs.
        /// </summary>
        private sealed class CountingIntEba : IEffectiveBooleanAlgebraEx<IntPredicate, int>
        {
            private readonly IntEba _inner;

            public CountingIntEba(int size) => _inner = new IntEba(size);

            public int SatisfiabilityChecks { get; set; }

            public IntPredicate Top => _inner.Top;
            public IntPredicate Bottom => _inner.Bottom;
            public IntPredicate And(IntPredicate a, IntPredicate b) => _inner.And(a, b);
            public IntPredicate Or(IntPredicate a, IntPredicate b) => _inner.Or(a, b);
            public IntPredicate Not(IntPredicate a) => _inner.Not(a);
            public bool Models(int element, IntPredicate p) => _inner.Models(element, p);
            public bool AreEquivalent(IntPredicate a, IntPredicate b) => _inner.AreEquivalent(a, b);
            public bool Implies(IntPredicate a, IntPredicate b) => _inner.Implies(a, b);
            public bool TryGetModel(IntPredicate p, out int element) => _inner.TryGetModel(p, out element);

            public bool IsSatisfiable(IntPredicate predicate)
            {
                SatisfiabilityChecks++;
                return _inner.IsSatisfiable(predicate);
            }
        }

        /// <summary>
        /// Deliberately lossy predicate comparer: it identifies any two
        /// predicates denoting equally many elements. Legal as an equality
        /// comparer (it is an equivalence relation), and lossy enough to
        /// conflate distinct path conditions — which is exactly what a
        /// path-keyed memo table would have had to worry about.
        /// </summary>
        private sealed class SameCardinalityComparer : IEqualityComparer<IntPredicate>
        {
            public bool Equals(IntPredicate a, IntPredicate b)
                => ReferenceEquals(a, b)
                   || (a != null && b != null && a.Elements.Count == b.Elements.Count);

            public int GetHashCode(IntPredicate p) => p?.Elements.Count ?? 0;
        }

        /// <summary>
        /// A predicate over <c>bits</c> independent Boolean variables,
        /// represented as a DNF: a disjunction of cubes, each cube a
        /// conjunction of literals given by a "must be set" and a "must be
        /// clear" bit mask. An element of the universe <c>{0 .. 2^bits-1}</c>
        /// is read as an assignment to those variables.
        /// </summary>
        private sealed class Cubes : System.IEquatable<Cubes>
        {
            /// <summary>Cube <c>i</c> is <c>(True[i], False[i])</c>; all are consistent.</summary>
            internal readonly (int True, int False)[] Terms;

            internal Cubes(params (int True, int False)[] terms) => Terms = terms;

            public bool Equals(Cubes other)
            {
                if (other == null || Terms.Length != other.Terms.Length) return false;
                for (int i = 0; i < Terms.Length; i++)
                    if (Terms[i] != other.Terms[i]) return false;
                return true;
            }

            public override bool Equals(object obj) => Equals(obj as Cubes);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    foreach (var (t, f) in Terms)
                        hash = hash * 31 + (t * 397) ^ f;
                    return hash;
                }
            }

            public override string ToString()
                => Terms.Length == 0
                    ? "⊥"
                    : string.Join("∨", Terms.Select(t => $"[+{t.True:x},-{t.False:x}]"));
        }

        /// <summary>
        /// EBA over <see cref="Cubes"/>. Every operation runs in time
        /// independent of the universe size — satisfiability is a mask test —
        /// which is what makes the adversarial complexity measurements, whose
        /// inputs have 2^18 root-to-leaf paths, feasible as unit tests.
        /// Cube counts stay at one throughout those tests because every
        /// condition used there is a single literal.
        /// </summary>
        private sealed class CubeEba : IEffectiveBooleanAlgebraEx<Cubes, int>
        {
            private readonly int _bits;

            internal CubeEba(int bits)
            {
                if (bits > 30) throw new System.ArgumentOutOfRangeException(nameof(bits));
                _bits = bits;
                Top = new Cubes((0, 0));
                Bottom = new Cubes();
            }

            internal int SatisfiabilityChecks { get; set; }

            /// <summary>The predicate "variable <paramref name="bit"/> is <paramref name="set"/>".</summary>
            internal static Cubes Literal(int bit, bool set)
                => set ? new Cubes((1 << bit, 0)) : new Cubes((0, 1 << bit));

            public Cubes Top { get; }
            public Cubes Bottom { get; }

            public Cubes And(Cubes a, Cubes b)
            {
                var merged = new List<(int, int)>();
                foreach (var (at, af) in a.Terms)
                    foreach (var (bt, bf) in b.Terms)
                    {
                        int t = at | bt, f = af | bf;
                        if ((t & f) == 0 && !merged.Contains((t, f)))
                            merged.Add((t, f));
                    }
                return new Cubes(merged.ToArray());
            }

            public Cubes Or(Cubes a, Cubes b)
            {
                var merged = new List<(int, int)>(a.Terms);
                foreach (var term in b.Terms)
                    if (!merged.Contains(term)) merged.Add(term);
                return new Cubes(merged.ToArray());
            }

            public Cubes Not(Cubes a)
            {
                // ¬(c₁ ∨ … ∨ cₙ) = ¬c₁ ∧ … ∧ ¬cₙ, and ¬c is the disjunction of
                // the flipped literals of c.
                var result = Top;
                foreach (var (t, f) in a.Terms)
                {
                    var flipped = new List<(int, int)>();
                    for (int bit = 0; bit < _bits; bit++)
                    {
                        int mask = 1 << bit;
                        if ((t & mask) != 0) flipped.Add((0, mask));
                        if ((f & mask) != 0) flipped.Add((mask, 0));
                    }
                    result = And(result, new Cubes(flipped.ToArray()));
                }
                return result;
            }

            public bool IsSatisfiable(Cubes predicate)
            {
                SatisfiabilityChecks++;
                foreach (var (t, f) in predicate.Terms)
                    if ((t & f) == 0) return true;
                return false;
            }

            public bool Models(int element, Cubes predicate)
            {
                foreach (var (t, f) in predicate.Terms)
                    if ((element & t) == t && (element & f) == 0) return true;
                return false;
            }

            public bool AreEquivalent(Cubes a, Cubes b) => Implies(a, b) && Implies(b, a);

            public bool Implies(Cubes a, Cubes b)
            {
                foreach (var (t, f) in And(a, Not(b)).Terms)
                    if ((t & f) == 0) return false;
                return true;
            }

            public bool TryGetModel(Cubes predicate, out int element)
            {
                foreach (var (t, f) in predicate.Terms)
                    if ((t & f) == 0) { element = t; return true; }
                element = default;
                return false;
            }
        }

        #endregion

        #region DisjunctiveForm (Antimirov Normal Form)

        [Test]
        public void DisjunctiveForm_EliminatesBottom()
        {
            var disjuncts = new[]
            {
                _algebra.Bottom,
                _algebra.Leaf("q1"),
                _algebra.Bottom,
                _algebra.Leaf("q2")
            };

            var result = _algebra.DisjunctiveForm(disjuncts);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("q1", ((TransitionTermLeaf<string>)result[0]).Value);
            Assert.AreEqual("q2", ((TransitionTermLeaf<string>)result[1]).Value);
        }

        [Test]
        public void DisjunctiveForm_EliminatesDuplicates()
        {
            var q1 = _algebra.Leaf("q1");
            var disjuncts = new[] { q1, _algebra.Leaf("q2"), q1 };

            var result = _algebra.DisjunctiveForm(disjuncts);
            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void DisjunctiveForm_TopShortCircuits()
        {
            var disjuncts = new[]
            {
                _algebra.Leaf("q1"),
                _algebra.Top,
                _algebra.Leaf("q2")
            };

            var result = _algebra.DisjunctiveForm(disjuncts);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("⊤", ((TransitionTermLeaf<string>)result[0]).Value);
        }

        [Test]
        public void DisjunctiveForm_AllBottom_ReturnsBottom()
        {
            var disjuncts = new[] { _algebra.Bottom, _algebra.Bottom };
            var result = _algebra.DisjunctiveForm(disjuncts);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("⊥", ((TransitionTermLeaf<string>)result[0]).Value);
        }

        #endregion

        #region MapUnary

        [Test]
        public void MapUnary_TransformsLeaves()
        {
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("q1"),
                TransitionTerm<string>.Leaf("q2"));

            var result = _algebra.MapUnary(term, s => s.ToUpper());

            Assert.AreEqual("Q1", result.Evaluate(0, _registry, _eba));
            Assert.AreEqual("Q2", result.Evaluate(2, _registry, _eba));
        }

        [Test]
        public void MapUnary_CrossType_ChangesLeafType()
        {
            var term = TransitionTerm<string>.Ite(_alphaIdx,
                TransitionTerm<string>.Leaf("hello"),
                TransitionTerm<string>.Leaf("world"));

            TransitionTerm<int> result = _algebra.MapUnary(term, s => s.Length);

            Assert.AreEqual(5, result.Evaluate(0, _registry, _eba));
            Assert.AreEqual(5, result.Evaluate(2, _registry, _eba));
        }

        #endregion

        #region Condition Registry

        [Test]
        public void Registry_AssignsIncreasingIndices()
        {
            var reg = new ConditionRegistry<IntPredicate>();
            var i0 = reg.Register(new IntPredicate("a", 0));
            var i1 = reg.Register(new IntPredicate("b", 1));
            Assert.AreEqual(0, i0);
            Assert.AreEqual(1, i1);
        }

        [Test]
        public void Registry_DeduplicatesSamePredicate()
        {
            var reg = new ConditionRegistry<IntPredicate>();
            var i0 = reg.Register(new IntPredicate("a", 0, 1));
            var i1 = reg.Register(new IntPredicate("a", 0, 1));
            Assert.AreEqual(i0, i1);
            Assert.AreEqual(1, reg.Count);
        }

        #endregion

        #region Leaf Algebra (StringLeafAlgebra)

        [Test]
        public void LeafAlgebra_Or_ACI()
        {
            // Commutative: b∨a = a∨b
            Assert.AreEqual("a∨b", _leafAlgebra.Or("b", "a"));
            Assert.AreEqual("a∨b", _leafAlgebra.Or("a", "b"));

            // Idempotent: a∨a = a
            Assert.AreEqual("a", _leafAlgebra.Or("a", "a"));

            // Unit: ⊥∨a = a
            Assert.AreEqual("a", _leafAlgebra.Or("⊥", "a"));
            Assert.AreEqual("a", _leafAlgebra.Or("a", "⊥"));

            // Zero: ⊤∨a = ⊤
            Assert.AreEqual("⊤", _leafAlgebra.Or("⊤", "a"));
        }

        [Test]
        public void LeafAlgebra_And_ACI()
        {
            // Commutative
            Assert.AreEqual("a∧b", _leafAlgebra.And("b", "a"));

            // Idempotent
            Assert.AreEqual("a", _leafAlgebra.And("a", "a"));

            // Unit: ⊤∧a = a
            Assert.AreEqual("a", _leafAlgebra.And("⊤", "a"));

            // Zero: ⊥∧a = ⊥
            Assert.AreEqual("⊥", _leafAlgebra.And("⊥", "a"));
        }

        [Test]
        public void LeafAlgebra_Or_Associative()
        {
            // (a∨b)∨c = a∨b∨c
            var ab = _leafAlgebra.Or("a", "b"); // "a∨b"
            var abc = _leafAlgebra.Or(ab, "c");  // "a∨b∨c"
            Assert.AreEqual("a∨b∨c", abc);

            // a∨(b∨c) = a∨b∨c
            var bc = _leafAlgebra.Or("b", "c");
            var abc2 = _leafAlgebra.Or("a", bc);
            Assert.AreEqual("a∨b∨c", abc2);
        }

        #endregion
    }
}
