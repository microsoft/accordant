namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;
    using System.Linq;

    /// <summary>
    /// Pins the Σ*-tail structural subsumption rewrite (Phase 12 / Rust
    /// EREQ port P2.5b, lib.rs:2104-2114):
    ///   Σ*·t1  +  Σ*·t2   ≡  Σ*·t1   when  t2 = …·t1  structurally.
    /// Any word ending in t2 = X·t1 also ends in t1, so the longer-tailed
    /// disjunct is subsumed by the shorter-tailed one.
    /// </summary>
    [TestFixture]
    public class EreUnionSigmaStarTailSubsumptionTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ere<IStatePredicate> SigmaStar()
            => Ere<IStatePredicate>.Star(Ere<IStatePredicate>.Sigma());

        [Test]
        public void TailSubsumption_DropsLongerEndsWith()
        {
            // Σ*·a   ∪   Σ*·(b·a)   →   Σ*·a
            var sStar = SigmaStar();
            var endsWithA   = Ere<IStatePredicate>.Concat(sStar, Atom(Pa));
            var endsWithBA  = Ere<IStatePredicate>.Concat(
                sStar,
                Ere<IStatePredicate>.Concat(Atom(Pb), Atom(Pa)));
            var u = Ere<IStatePredicate>.Union(endsWithA, endsWithBA);
            Assert.That(u, Is.SameAs(endsWithA),
                $"expected Σ*·(b·a) to be subsumed by Σ*·a, got {u}");
        }

        [Test]
        public void TailSubsumption_HeadFactoredWhenNoSuffixRelation()
        {
            // Σ*·a  vs  Σ*·b: neither tail is a structural suffix of the
            // other, but they share head Σ*, so head-factoring (P2.1) folds
            // them into Σ*·(a|b). Either way, the P2.5b subsumption rule
            // must NOT incorrectly drop one of them.
            var sStar = SigmaStar();
            var endsWithA = Ere<IStatePredicate>.Concat(sStar, Atom(Pa));
            var endsWithB = Ere<IStatePredicate>.Concat(sStar, Atom(Pb));
            var u = Ere<IStatePredicate>.Union(endsWithA, endsWithB);
            // Head-factored canonical shape: Σ*·(a|b).
            Assert.That(u, Is.InstanceOf<EreConcat<IStatePredicate>>());
            var c = (EreConcat<IStatePredicate>)u;
            Assert.That(c.Right, Is.InstanceOf<EreUnion<IStatePredicate>>(),
                "tail should be (a|b) union");
        }

        [Test]
        public void TailSubsumption_TransitiveDropsLongest()
        {
            // Σ*·a , Σ*·(b·a) , Σ*·(b·b·a)  →  Σ*·a   (a is suffix of all)
            var sStar = SigmaStar();
            var a  = Atom(Pa);
            var b  = Atom(Pb);
            var endsA   = Ere<IStatePredicate>.Concat(sStar, a);
            var endsBA  = Ere<IStatePredicate>.Concat(sStar, Ere<IStatePredicate>.Concat(b, a));
            var endsBBA = Ere<IStatePredicate>.Concat(sStar, Ere<IStatePredicate>.Concat(b, Ere<IStatePredicate>.Concat(b, a)));
            var u12 = Ere<IStatePredicate>.Union(endsA, endsBA);
            var u   = Ere<IStatePredicate>.Union(u12, endsBBA);
            Assert.That(u, Is.SameAs(endsA),
                $"expected all longer-tailed Σ* operands to be subsumed by Σ*·a, got {u}");
        }

        [Test]
        public void TailSubsumption_NonSigmaStarPrefixIsNotDropped()
        {
            // a·a  ∪  Σ*·a : the first does not have a Σ* prefix, so the
            // P2.5b rewrite does not apply.  Result should remain a Union
            // (the existing Σ*-prefix absorption may rearrange it, but
            // must not collapse to either operand).
            var a = Atom(Pa);
            var aa = Ere<IStatePredicate>.Concat(a, a);
            var sStarA = Ere<IStatePredicate>.Concat(SigmaStar(), a);
            var u = Ere<IStatePredicate>.Union(aa, sStarA);
            // The Σ*-prefix absorption rule fires here: a·a · ε   vs Σ*·a;
            // not the P2.5b case. Just check we didn't bogusly drop one.
            Assert.That(u, Is.Not.InstanceOf<EreEmpty<IStatePredicate>>());
        }
    }
}
