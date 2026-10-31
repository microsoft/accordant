namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Pins the contains-pattern merge under Union (Phase 12 P2.3;
    /// Rust EREQ SU-5 at lib.rs:2081–2086):
    /// <c>Σ*·R·Σ* + Σ*·S·Σ* ≡ Σ*·(R+S)·Σ*</c>.
    /// Particularly relevant for "contains-body" style regexes from
    /// MSO/RLTL encodings (mk_contains in Rust EREQ).
    /// </summary>
    [TestFixture]
    public class EreUnionContainsMergeTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);
        private static readonly StateProp Pc = new StateProp("c", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ere<IStatePredicate> SigmaStar()
            => Ere<IStatePredicate>.Star(Ere<IStatePredicate>.Sigma());

        private static Ere<IStatePredicate> Contains(Ere<IStatePredicate> body)
        {
            var s = SigmaStar();
            return Ere<IStatePredicate>.Concat(s, Ere<IStatePredicate>.Concat(body, s));
        }

        [Test]
        public void TwoContainsPatterns_MergeBodies()
        {
            var a = Atom(Pa);
            var b = Atom(Pb);

            var ca = Contains(a);
            var cb = Contains(b);

            var union = Ere<IStatePredicate>.Union(ca, cb);
            var expected = Contains(Ere<IStatePredicate>.Union(a, b));

            Assert.That(union, Is.SameAs(expected),
                "Σ*·a·Σ* + Σ*·b·Σ* should merge to Σ*·(a+b)·Σ*.");
        }

        [Test]
        public void ThreeContainsPatterns_MergeAll()
        {
            var a = Atom(Pa);
            var b = Atom(Pb);
            var c = Atom(Pc);

            var union = Ere<IStatePredicate>.Union(
                Contains(a),
                Ere<IStatePredicate>.Union(Contains(b), Contains(c)));

            var expectedBody = Ere<IStatePredicate>.Union(a,
                Ere<IStatePredicate>.Union(b, c));
            var expected = Contains(expectedBody);

            Assert.That(union, Is.SameAs(expected),
                "three contains-patterns should merge into one Σ*·(a+b+c)·Σ*.");
        }

        [Test]
        public void IdenticalContainsPatterns_DedupViaUnion()
        {
            var a = Atom(Pa);
            var ca = Contains(a);

            var union = Ere<IStatePredicate>.Union(ca, ca);
            Assert.That(union, Is.SameAs(ca),
                "Σ*·a·Σ* + Σ*·a·Σ* should collapse to a single Σ*·a·Σ* "
                + "(union ACI dedup runs before contains-merge).");
        }
    }
}
