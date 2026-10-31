namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Pins Union head-factoring (Phase 12 P2.1; Rust EREQ SU-7 at
    /// lib.rs:2118–2121): <c>H·T₁ + H·T₂ + … + H·Tₙ ≡ H·(T₁+T₂+…+Tₙ)</c>.
    /// Useful when derivative classes share the same head predicate.
    /// </summary>
    [TestFixture]
    public class EreUnionHeadFactoringTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);
        private static readonly StateProp Pc = new StateProp("c", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        [Test]
        public void TwoConcatsSharingHead_Factor()
        {
            // a·b + a·c  →  a·(b+c)
            var a = Atom(Pa);
            var b = Atom(Pb);
            var c = Atom(Pc);

            var lhs = Ere<IStatePredicate>.Concat(a, b);
            var rhs = Ere<IStatePredicate>.Concat(a, c);
            var union = Ere<IStatePredicate>.Union(lhs, rhs);

            var expected = Ere<IStatePredicate>.Concat(a,
                Ere<IStatePredicate>.Union(b, c));
            Assert.That(union, Is.SameAs(expected),
                "a·b + a·c should factor as a·(b+c).");
        }

        [Test]
        public void DistinctHeads_DoNotFactor()
        {
            // a·b + c·b: heads differ; should not factor (we don't do
            // common-tail factoring — that's a separate rule).
            var a = Atom(Pa);
            var b = Atom(Pb);
            var c = Atom(Pc);

            var lhs = Ere<IStatePredicate>.Concat(a, b);
            var rhs = Ere<IStatePredicate>.Concat(c, b);
            var union = Ere<IStatePredicate>.Union(lhs, rhs);

            Assert.That(union, Is.Not.SameAs(lhs));
            Assert.That(union, Is.Not.SameAs(rhs));
            Assert.That(union.ToString(), Does.Contain("+"),
                "distinct heads should leave a binary union shape");
        }

        [Test]
        public void ThreeWayFactoring()
        {
            // a·b + a·c + a·(b·c)  →  a·(b + c + b·c)
            var a = Atom(Pa);
            var b = Atom(Pb);
            var c = Atom(Pc);

            var u = Ere<IStatePredicate>.Union(
                Ere<IStatePredicate>.Concat(a, b),
                Ere<IStatePredicate>.Union(
                    Ere<IStatePredicate>.Concat(a, c),
                    Ere<IStatePredicate>.Concat(a,
                        Ere<IStatePredicate>.Concat(b, c))));

            var expectedTail = Ere<IStatePredicate>.Union(
                b, Ere<IStatePredicate>.Union(c,
                    Ere<IStatePredicate>.Concat(b, c)));
            var expected = Ere<IStatePredicate>.Concat(a, expectedTail);
            Assert.That(u, Is.SameAs(expected));
        }

        [Test]
        public void MixedConcatAndNonConcat_FactorsOnlyMatchingGroup()
        {
            // a·b + a·c + d  →  a·(b+c) + d
            var a = Atom(Pa);
            var b = Atom(Pb);
            var c = Atom(Pc);
            var d = Ere<IStatePredicate>.Atom(
                new StatePredAtom(new StateProp("d", _ => true)));

            var u = Ere<IStatePredicate>.Union(
                Ere<IStatePredicate>.Concat(a, b),
                Ere<IStatePredicate>.Union(
                    Ere<IStatePredicate>.Concat(a, c),
                    d));

            var expected = Ere<IStatePredicate>.Union(
                Ere<IStatePredicate>.Concat(a, Ere<IStatePredicate>.Union(b, c)),
                d);
            Assert.That(u, Is.SameAs(expected));
        }
    }
}
