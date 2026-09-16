namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for the weak-equivalent breakpoint canonicaliser
    /// (<see cref="RltlBreakpointCanonicalizer{TPred,TElem}"/>) and its wiring
    /// into <see cref="IncrementalAE{TPredicate,TElement,TState}"/>: on-the-
    /// fly merging of language-equivalent breakpoint states <c>(S,O)</c>
    /// during the alternation-elimination construction, implementing the JACM
    /// Example 5.1 state-reduction step.
    /// </summary>
    [TestFixture]
    public class RltlBreakpointCanonicalizerTests
    {
        private IntEba _eba;
        private RltlAlgebra<IntPredicate> _algebra;
        private IntPredicate _p;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(2);
            _algebra = new RltlAlgebra<IntPredicate>(_eba);
            _p = new IntPredicate("a", 0);
        }

        [Test]
        public void Canon_StructurallyEqualBPs_AliasReflexively()
        {
            var merger = new RltlBreakpointCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var Fp = _algebra.Eventually(_algebra.Atom(_p));
            var cmp = Comparer<Rltl<IntPredicate>>.Create((x, y) => x.GetHashCode().CompareTo(y.GetHashCode()));
            var s = new StateSet<Rltl<IntPredicate>>(new[] { Fp }, cmp);
            var o = StateSet<Rltl<IntPredicate>>.Empty(cmp);
            var bp1 = new BreakpointState<Rltl<IntPredicate>>(s, o);
            var bp2 = new BreakpointState<Rltl<IntPredicate>>(s, o);

            var r1 = merger.Canonicalize(bp1);
            var r2 = merger.Canonicalize(bp2);
            Assert.That(ReferenceEquals(r1, r2), Is.True);
            Assert.That(merger.ClassCount, Is.EqualTo(1));
        }

        [Test]
        public void Canon_LanguageEquivalentBPs_AreMerged()
        {
            // Two BPs whose macrostate conjunctions are language-equivalent
            // (G p and G G p) and obligations are both empty.
            var merger = new RltlBreakpointCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var p = _algebra.Atom(_p);
            var Gp = _algebra.Globally(p);
            var GGp = _algebra.Globally(Gp);

            Assert.That(ReferenceEquals(Gp, GGp), Is.False);
            Assert.That(RltlLanguageEquivalence.AreEquivalent(_eba, _algebra, Gp, GGp), Is.True,
                "Prerequisite: G p ≡ G G p.");

            var cmp = Comparer<Rltl<IntPredicate>>.Create((x, y) => x.GetHashCode().CompareTo(y.GetHashCode()));
            var emptyO = StateSet<Rltl<IntPredicate>>.Empty(cmp);
            var bpGp  = new BreakpointState<Rltl<IntPredicate>>(
                new StateSet<Rltl<IntPredicate>>(new[] { Gp }, cmp), emptyO);
            var bpGGp = new BreakpointState<Rltl<IntPredicate>>(
                new StateSet<Rltl<IntPredicate>>(new[] { GGp }, cmp), emptyO);

            var r1 = merger.Canonicalize(bpGp);
            var r2 = merger.Canonicalize(bpGGp);
            Assert.That(ReferenceEquals(r1, r2), Is.True);
            Assert.That(merger.ClassCount, Is.EqualTo(1));
        }

        [Test]
        public void Canon_LanguageInequivalentBPs_StayDistinct()
        {
            // F p vs G p are language-inequivalent.
            var merger = new RltlBreakpointCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var p = _algebra.Atom(_p);
            var Fp = _algebra.Eventually(p);
            var Gp = _algebra.Globally(p);
            var cmp = Comparer<Rltl<IntPredicate>>.Create((x, y) => x.GetHashCode().CompareTo(y.GetHashCode()));
            var emptyO = StateSet<Rltl<IntPredicate>>.Empty(cmp);
            var bpF = new BreakpointState<Rltl<IntPredicate>>(
                new StateSet<Rltl<IntPredicate>>(new[] { Fp }, cmp), emptyO);
            var bpG = new BreakpointState<Rltl<IntPredicate>>(
                new StateSet<Rltl<IntPredicate>>(new[] { Gp }, cmp), emptyO);

            var r1 = merger.Canonicalize(bpF);
            var r2 = merger.Canonicalize(bpG);
            Assert.That(ReferenceEquals(r1, r2), Is.False);
            Assert.That(merger.ClassCount, Is.EqualTo(2));
        }

        [Test]
        public void Canon_DifferentObligations_StayDistinct()
        {
            // Same S, different O — must remain distinct because the
            // obligation tracks Büchi acceptance.
            var merger = new RltlBreakpointCanonicalizer<IntPredicate, int>(_eba, _algebra);
            var p = _algebra.Atom(_p);
            var Gp = _algebra.Globally(p);
            var cmp = Comparer<Rltl<IntPredicate>>.Create((x, y) => x.GetHashCode().CompareTo(y.GetHashCode()));
            var S = new StateSet<Rltl<IntPredicate>>(new[] { Gp }, cmp);
            var emptyO = StateSet<Rltl<IntPredicate>>.Empty(cmp);
            var nonEmptyO = new StateSet<Rltl<IntPredicate>>(new[] { Gp }, cmp);
            var bp1 = new BreakpointState<Rltl<IntPredicate>>(S, emptyO);
            var bp2 = new BreakpointState<Rltl<IntPredicate>>(S, nonEmptyO);

            var r1 = merger.Canonicalize(bp1);
            var r2 = merger.Canonicalize(bp2);
            Assert.That(ReferenceEquals(r1, r2), Is.False);
            Assert.That(merger.ClassCount, Is.EqualTo(2));
        }

        [Test]
        public void IncrementalAE_WithMerger_GFaFNa_Collapses_8_To_3()
        {
            // Full integration: G(Fa ∧ F¬a) under the IntEba precise oracle.
            // Without the merger the RLTL pipeline produces 5 reachable BP
            // states; with the merger it reaches the JACM Example 5.1 minimum
            // of 3.
            var ralg = new RltlAlgebra<IntPredicate>(_eba);
            var rltl = ralg.Globally(
                ralg.And(
                    ralg.Eventually(ralg.Atom(_p)),
                    ralg.Eventually(ralg.NegAtom(_p))));

            int Run(bool merge)
            {
                var registry = new ConditionRegistry<IntPredicate>();
                var ed = new EreDerivative<IntPredicate, int>(_eba, registry);
                var ereCanon = new EreCanonicalizer<IntPredicate, int>(
                    new EreEquivalenceChecker<IntPredicate, int>(ed));
                var ra = new RltlAlgebra<IntPredicate>(_eba, ereCanon);
                var rltlCanon = new RltlCanonicalizer<IntPredicate, int>(_eba, ra);
                var deriv = new RltlDerivative<IntPredicate, int>(_eba, registry, ereCanon, rltlCanon);
                var abw = deriv.ToABW(rltl);
                Func<BreakpointState<Rltl<IntPredicate>>, BreakpointState<Rltl<IntPredicate>>> bpCanon = null;
                if (merge)
                {
                    var merger = new RltlBreakpointCanonicalizer<IntPredicate, int>(_eba, ra);
                    bpCanon = merger.Canonicalize;
                }
                var ae = new IncrementalAE<IntPredicate, int, Rltl<IntPredicate>>(abw, bpCanon);
                var nbw = ae.ToNBW();

                var seen = new HashSet<BreakpointState<Rltl<IntPredicate>>>(
                    BreakpointState<Rltl<IntPredicate>>.GetEqualityComparer());
                var queue = new Queue<BreakpointState<Rltl<IntPredicate>>>(nbw.InitialStates);
                foreach (var s in nbw.InitialStates) seen.Add(s);
                while (queue.Count > 0)
                {
                    var s = queue.Dequeue();
                    foreach (var term in nbw.GetTransition(s))
                        foreach (var leaf in term.GetDistinctLeaves())
                            foreach (var succ in leaf)
                                if (seen.Add(succ)) queue.Enqueue(succ);
                }
                return seen.Count;
            }

            int noMerge = Run(merge: false);
            int merged  = Run(merge: true);
            Assert.That(merged, Is.LessThanOrEqualTo(noMerge));
            Assert.That(merged, Is.EqualTo(3),
                "JACM Example 5.1: the weak-equivalent merge collapses to 3 reachable NBW states.");
        }
    }
}
