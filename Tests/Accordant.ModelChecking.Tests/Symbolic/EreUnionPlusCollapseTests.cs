namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Pins the Union rewrites for R⁺ collapses (Phase 12 P1.4 + P1.5,
    /// mirroring Rust EREQ lib.rs:3549–3559):
    /// <list type="bullet">
    ///   <item><c>ε  | R·R*  ≡  R*</c></item>
    ///   <item><c>R* | R·R*  ≡  R*</c></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class EreUnionPlusCollapseTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        [Test]
        public void Epsilon_Plus_RPlus_CollapsesToStar()
        {
            // R⁺ = R · R*
            var a = Atom(Pa);
            var aStar = Ere<IStatePredicate>.Star(a);
            var aPlus = Ere<IStatePredicate>.Concat(a, aStar);

            var union = Ere<IStatePredicate>.Union(Ere<IStatePredicate>.Epsilon(), aPlus);

            Assert.That(union, Is.SameAs(aStar),
                "ε + a·a* should collapse to a*.");
        }

        [Test]
        public void Star_Plus_RPlus_CollapsesToStar()
        {
            var a = Atom(Pa);
            var aStar = Ere<IStatePredicate>.Star(a);
            var aPlus = Ere<IStatePredicate>.Concat(a, aStar);

            var union = Ere<IStatePredicate>.Union(aStar, aPlus);

            Assert.That(union, Is.SameAs(aStar),
                "a* + a·a* should collapse to a*.");
        }

        [Test]
        public void DifferentBody_DoesNotCollapse()
        {
            // ε + b·b*  collapses to b* (same body) — sanity.
            // ε + a·b*  must NOT collapse to b* (a ≠ b).
            var a = Atom(Pa);
            var b = Atom(Pb);
            var bStar = Ere<IStatePredicate>.Star(b);

            var nonPlus = Ere<IStatePredicate>.Concat(a, bStar);
            var union = Ere<IStatePredicate>.Union(Ere<IStatePredicate>.Epsilon(), nonPlus);

            // The result must include both ε and a·b* somehow — not just bStar.
            Assert.That(union, Is.Not.SameAs(bStar),
                "Collapse must require Concat's left to equal the star's inner.");
        }

        [Test]
        public void TripleUnion_StarAndPlusAndOther()
        {
            // a* + a·a* + b  →  a* + b
            var a = Atom(Pa);
            var b = Atom(Pb);
            var aStar = Ere<IStatePredicate>.Star(a);
            var aPlus = Ere<IStatePredicate>.Concat(a, aStar);

            var union = Ere<IStatePredicate>.Union(
                Ere<IStatePredicate>.Union(aStar, aPlus), b);
            var expected = Ere<IStatePredicate>.Union(aStar, b);

            Assert.That(union, Is.SameAs(expected),
                "a* + a·a* + b should reduce to a* + b.");
        }
    }
}
