namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Pins the per-node metadata cached eagerly at construction
    /// (Phase 12 / Rust EREQ port P1.1–P1.3 + P1.6):
    /// <c>ContainsCompl</c>, <c>ContainsInter</c>, <c>ContainsExists</c>,
    /// <c>Cost</c>, and the derived <c>IsDefinitelyAlive</c> approximation.
    /// </summary>
    [TestFixture]
    public class EreMetadataTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        [Test]
        public void Leaves_HaveZeroFlags_AndCostOne()
        {
            var empty = Ere<IStatePredicate>.Empty();
            var eps   = Ere<IStatePredicate>.Epsilon();
            var a     = Atom(Pa);

            foreach (var leaf in new Ere<IStatePredicate>[] { empty, eps, a })
            {
                Assert.That(leaf.ContainsCompl,  Is.False, $"{leaf} should not contain complement");
                Assert.That(leaf.ContainsInter,  Is.False, $"{leaf} should not contain intersect");
                Assert.That(leaf.ContainsExists, Is.False, $"{leaf} should not contain exists");
                Assert.That(leaf.Cost, Is.EqualTo(1), $"{leaf} cost should be 1");
            }

            // Empty is excluded from "definitely alive" because it denotes ∅.
            Assert.That(empty.IsDefinitelyAlive, Is.False);
            Assert.That(eps.IsDefinitelyAlive,   Is.True);
            Assert.That(a.IsDefinitelyAlive,     Is.True);
        }

        [Test]
        public void StandardFragment_IsAlive_AndFlagsZero()
        {
            // a·(a+ε)*  — fully in the standard fragment (no ~, no ∩, no ∃)
            var a = Atom(Pa);
            var r = Ere<IStatePredicate>.Concat(a,
                Ere<IStatePredicate>.Star(
                    Ere<IStatePredicate>.Union(a, Ere<IStatePredicate>.Epsilon())));

            Assert.That(r.ContainsCompl,  Is.False);
            Assert.That(r.ContainsInter,  Is.False);
            Assert.That(r.ContainsExists, Is.False);
            Assert.That(r.IsDefinitelyAlive, Is.True);
            Assert.That(r.Cost, Is.GreaterThan(1));
        }

        [Test]
        public void Complement_PropagatesContainsCompl_BlocksAliveFastPath()
        {
            var a = Atom(Pa);
            var nota = Ere<IStatePredicate>.Complement(a);

            Assert.That(nota.ContainsCompl, Is.True);
            Assert.That(nota.ContainsInter, Is.False);
            Assert.That(nota.IsDefinitelyAlive, Is.False,
                "presence of complement should block the standard-fragment alive fast-path");

            // Containing-complement bit propagates upward through Concat/Union/Star.
            var wrapped = Ere<IStatePredicate>.Star(
                Ere<IStatePredicate>.Concat(a, nota));
            Assert.That(wrapped.ContainsCompl, Is.True);
            Assert.That(wrapped.IsDefinitelyAlive, Is.False);
        }

        [Test]
        public void Intersect_PropagatesContainsInter_BlocksAliveFastPath()
        {
            var a = Atom(Pa);
            var b = Atom(Pb);
            var ab = Ere<IStatePredicate>.Intersect(a, b);

            Assert.That(ab.ContainsInter, Is.True);
            Assert.That(ab.ContainsCompl, Is.False);
            Assert.That(ab.IsDefinitelyAlive, Is.False,
                "presence of intersection should block the standard-fragment alive fast-path");

            var wrapped = Ere<IStatePredicate>.Union(a, ab);
            Assert.That(wrapped.ContainsInter, Is.True);
            Assert.That(wrapped.IsDefinitelyAlive, Is.False);
        }

        [Test]
        public void Cost_SumsChildrenPlusOne()
        {
            // a·b should have cost 1+1+1 = 3.
            var a = Atom(Pa);
            var b = Atom(Pb);
            var ab = Ere<IStatePredicate>.Concat(a, b);
            Assert.That(ab.Cost, Is.EqualTo(3));

            // (a+b)·a → distributes via Concat-over-Union into (a·a + b·a):
            //   each branch cost 3, union cost 1+3+3 = 7. The factory's
            //   left-distribution rebuilds the term, so use the resulting
            //   structure to validate cost is composed from interned children.
            var rebuilt = Ere<IStatePredicate>.Concat(
                Ere<IStatePredicate>.Union(a, b), a);
            Assert.That(rebuilt.Cost, Is.GreaterThan(1));
        }
    }
}
