namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// EREQ Phase-1 (D5) tests: <see cref="TransitionTermAlgebra{TPred,TElem,TLeaf}"/>
    /// must treat proposition splits (negative indices) as free
    /// Booleans — no path-condition tightening, both branches always
    /// reachable.
    /// </summary>
    [TestFixture]
    public class TransitionTermAlgebraPropositionTests
    {
        private IntEba _eba;
        private ConditionRegistry<IntPredicate> _registry;
        private StringLeafAlgebra _leafAlgebra;
        private TransitionTermAlgebra<IntPredicate, int, string> _algebra;

        private int _alphaIdx;
        private int _pPropIdx;
        private int _qPropIdx;

        [SetUp]
        public void SetUp()
        {
            _eba = new IntEba(4);
            _registry = new ConditionRegistry<IntPredicate>();
            _leafAlgebra = new StringLeafAlgebra();
            _algebra = new TransitionTermAlgebra<IntPredicate, int, string>(_eba, _registry, _leafAlgebra);

            _alphaIdx = _registry.Register(new IntPredicate("α", 0, 1));
            _pPropIdx = _registry.RegisterProposition("p");
            _qPropIdx = _registry.RegisterProposition("q");
        }

        [Test]
        public void MkIte_PropositionSplit_DoesNotTouchEba()
        {
            // (p ? "hi" : "lo") - even with a non-default pathCondition,
            // a proposition split must skip GetPredicate / IsSatisfiable
            // entirely. We pass the universe predicate as pathCondition;
            // the test passes if MkIte returns an ITE node (rather than
            // collapsing to one branch via path cleaning).
            var hi = _algebra.Leaf("hi");
            var lo = _algebra.Leaf("lo");
            var path = new IntPredicate("⊤", 0, 1, 2, 3);

            var result = _algebra.MkIte(_pPropIdx, hi, lo, path);

            Assert.IsFalse(result.IsLeaf, "Proposition split must not collapse via path cleaning.");
            Assert.AreEqual(_pPropIdx, result.Level);
        }

        [Test]
        public void MkIte_PropositionSplit_StillCollapsesWhenBranchesEqual()
        {
            // Trivial condition elimination is independent of the path
            // cleaning branch and must still fire.
            var leaf = _algebra.Leaf("same");
            var result = _algebra.MkIte(_pPropIdx, leaf, leaf, default);
            Assert.AreSame(leaf, result);
        }

        [Test]
        public void Apply_MixedPropositionAndPredicate_BothBranchesExplored()
        {
            // Build T1 = (α ? "a" : "b") and T2 = (p ? "x" : "y").
            // Apply with concat - the proposition layer of T2 must
            // produce ITE nodes at level _pPropIdx with both branches
            // present, while the α layer below still benefits from
            // predicate path tracking.
            var a = _algebra.Leaf("a");
            var b = _algebra.Leaf("b");
            var x = _algebra.Leaf("x");
            var y = _algebra.Leaf("y");

            var t1 = _algebra.MkIte(_alphaIdx, a, b);
            var t2 = _algebra.MkIte(_pPropIdx, x, y);

            var combined = _algebra.ApplyBinary(t1, t2, (l, r) => l + r, _eba.Top);

            // The outer level must be the proposition (more outer due to
            // negative index < 0 == _alphaIdx).
            Assert.IsFalse(combined.IsLeaf);
            Assert.AreEqual(_pPropIdx, combined.Level);

            // Both branches present (no path collapse).
            var ite = (TransitionTermIte<string>)combined;
            Assert.IsFalse(ite.Hi.Equals(ite.Lo));
        }

        [Test]
        public void Apply_PropositionOnlySplit_BothBranchesReachable()
        {
            // T1 = (p ? "a" : "b"), T2 = (p ? "x" : "y").
            // Apply with concat: both branches of p must be explored,
            // yielding (p ? "ax" : "by").
            var t1 = _algebra.MkIte(_pPropIdx, _algebra.Leaf("a"), _algebra.Leaf("b"));
            var t2 = _algebra.MkIte(_pPropIdx, _algebra.Leaf("x"), _algebra.Leaf("y"));

            var combined = _algebra.ApplyBinary(t1, t2, (l, r) => l + r, _eba.Top);

            Assert.AreEqual(_pPropIdx, combined.Level);
            var ite = (TransitionTermIte<string>)combined;
            Assert.IsTrue(ite.Hi.IsLeaf);
            Assert.IsTrue(ite.Lo.IsLeaf);
            Assert.AreEqual("ax", ((TransitionTermLeaf<string>)ite.Hi).Value);
            Assert.AreEqual("by", ((TransitionTermLeaf<string>)ite.Lo).Value);
        }

        [Test]
        public void Apply_DistinctPropositions_OrderedByIndex()
        {
            // T1 = (p ? "a" : "b") with p = -1
            // T2 = (q ? "x" : "y") with q = -2
            // q is more negative, so should sort outermost (inner-larger
            // ordering with negative indices).
            var t1 = _algebra.MkIte(_pPropIdx, _algebra.Leaf("a"), _algebra.Leaf("b"));
            var t2 = _algebra.MkIte(_qPropIdx, _algebra.Leaf("x"), _algebra.Leaf("y"));

            var combined = _algebra.ApplyBinary(t1, t2, (l, r) => l + r, _eba.Top);

            Assert.AreEqual(_qPropIdx, combined.Level,
                "Outermost level should be the more-negative proposition index.");
        }
    }
}

