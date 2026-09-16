namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for the RLTL-level canonicaliser
    /// (<see cref="RltlCanonicalizer{TPred,TElem}"/>) and its wiring into
    /// <see cref="RltlDerivative{TPred,TElem}"/> for "symbolic NBW state
    /// minimisation by precise equivalence" (todo
    /// <c>equiv-nbw-state-min</c>).
    /// </summary>
    [TestFixture]
    public class RltlCanonicalizerTests
    {
        private IntEba _eba;
        private RltlAlgebra<IntPredicate> _algebra;
        private IntPredicate _p, _q;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(3);
            _algebra = new RltlAlgebra<IntPredicate>(_eba);
            _p = new IntPredicate("p", 0);
            _q = new IntPredicate("q", 1);
        }

        [Test]
        public void Canon_TwoFormulas_StructurallyEqual_AliasReflexively()
        {
            var canon = new RltlCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var f = Rltl<IntPredicate>.Eventually(Rltl<IntPredicate>.Atom(_p));
            Assert.That(canon.Canonicalize(f), Is.SameAs(f));
            Assert.That(canon.Canonicalize(f), Is.SameAs(f));
            Assert.That(canon.ClassCount, Is.EqualTo(1));
        }

        [Test]
        public void Canon_GG_Equivalent_To_G()
        {
            // G G p ≡ G p but structurally distinct (RltlAlgebra does not
            // simplify nested Globally).
            var p = Rltl<IntPredicate>.Atom(_p);
            var Gp = Rltl<IntPredicate>.Globally(p);
            var GGp = Rltl<IntPredicate>.Globally(Gp);

            Assert.That(ReferenceEquals(Gp, GGp), Is.False,
                "Prerequisite: G p and G G p must be structurally distinct.");
            Assert.That(RltlLanguageEquivalence.AreEquivalent(_eba, _algebra, Gp, GGp),
                Is.True, "Prerequisite: G p and G G p must be language-equivalent.");

            var canon = new RltlCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var r1 = canon.Canonicalize(Gp);
            var r2 = canon.Canonicalize(GGp);
            Assert.That(ReferenceEquals(r1, r2), Is.True);
            Assert.That(canon.ClassCount, Is.EqualTo(1));
        }

        [Test]
        public void Canon_DistinctClasses_StayDistinct()
        {
            // F p and F q are language-inequivalent.
            var Fp = Rltl<IntPredicate>.Eventually(Rltl<IntPredicate>.Atom(_p));
            var Fq = Rltl<IntPredicate>.Eventually(Rltl<IntPredicate>.Atom(_q));

            var canon = new RltlCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var r1 = canon.Canonicalize(Fp);
            var r2 = canon.Canonicalize(Fq);
            Assert.That(ReferenceEquals(r1, r2), Is.False);
            Assert.That(canon.ClassCount, Is.EqualTo(2));
        }

        [Test]
        public void Derivative_WithRltlCanon_CollapsesEquivalentAtoms()
        {
            // Build two top-level RLTL formulas that are language-equivalent
            // but use distinct AST shapes for some subformula. After one
            // derivative step, the residual atoms must coincide.
            var p = Rltl<IntPredicate>.Atom(_p);
            var Gp = Rltl<IntPredicate>.Globally(p);
            var GGp = Rltl<IntPredicate>.Globally(Gp);

            var registry = new ConditionRegistry<IntPredicate>(
                EqualityComparer<IntPredicate>.Default);
            var canon = new RltlCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var d = new RltlDerivative<IntPredicate, int>(_eba, registry, null, canon);

            // Touch both forms via the derivative engine. After exploring
            // both, their canonical residual sets coincide.
            var d1 = d.Derivative(Gp);
            var d2 = d.Derivative(GGp);

            var atoms1 = new HashSet<Rltl<IntPredicate>>();
            foreach (var leaf in d1.GetDistinctLeaves())
                foreach (var st in leaf.GetAllStates())
                    atoms1.Add(canon.Canonicalize(st));
            var atoms2 = new HashSet<Rltl<IntPredicate>>();
            foreach (var leaf in d2.GetDistinctLeaves())
                foreach (var st in leaf.GetAllStates())
                    atoms2.Add(canon.Canonicalize(st));

            foreach (var s in atoms1)
                Assert.That(atoms2, Does.Contain(s));
            foreach (var s in atoms2)
                Assert.That(atoms1, Does.Contain(s));
        }
    }
}
