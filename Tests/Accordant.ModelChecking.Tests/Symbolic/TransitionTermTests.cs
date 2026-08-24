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
