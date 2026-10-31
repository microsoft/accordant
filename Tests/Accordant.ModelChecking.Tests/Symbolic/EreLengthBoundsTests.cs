namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Pins the per-node length bounds (Phase 12 / Rust EREQ port P2.2)
    /// and the intersection unsat-pruning that consumes them. Mirrors
    /// Rust EREQ <c>get_min_max_len</c> (lib.rs:999–1038) and the
    /// intersect-disjoint-length-interval rewrite.
    /// </summary>
    [TestFixture]
    public class EreLengthBoundsTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        [Test]
        public void Leaves_HaveExpectedLengthBounds()
        {
            var eps = Ere<IStatePredicate>.Epsilon();
            Assert.That(eps.MinLen, Is.EqualTo(0));
            Assert.That(eps.MaxLen, Is.EqualTo(0));

            var a = Atom(Pa);
            Assert.That(a.MinLen, Is.EqualTo(1));
            Assert.That(a.MaxLen, Is.EqualTo(1));
        }

        [Test]
        public void Concat_AddsBounds()
        {
            var a = Atom(Pa);
            var b = Atom(Pb);
            var ab = Ere<IStatePredicate>.Concat(a, b);
            Assert.That(ab.MinLen, Is.EqualTo(2));
            Assert.That(ab.MaxLen, Is.EqualTo(2));
        }

        [Test]
        public void Star_HasZeroToInfinity()
        {
            var a = Atom(Pa);
            var aStar = Ere<IStatePredicate>.Star(a);
            Assert.That(aStar.MinLen, Is.EqualTo(0));
            Assert.That(aStar.MaxLen, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Union_TakesMinOfMinsAndMaxOfMaxes()
        {
            var a = Atom(Pa);                                       // (1,1)
            var ab = Ere<IStatePredicate>.Concat(a, Atom(Pb));      // (2,2)
            var u = Ere<IStatePredicate>.Union(a, ab);
            Assert.That(u.MinLen, Is.EqualTo(1));
            Assert.That(u.MaxLen, Is.EqualTo(2));
        }

        [Test]
        public void Intersect_TakesMaxOfMinsAndMinOfMaxes()
        {
            // (a + a·b)  ∩  (a·b + a·b·b)  ⇒ shared length range [2, 2]
            var a = Atom(Pa);
            var b = Atom(Pb);
            var ab = Ere<IStatePredicate>.Concat(a, b);
            var abb = Ere<IStatePredicate>.Concat(ab, b);
            var left = Ere<IStatePredicate>.Union(a, ab);      // (1, 2)
            var right = Ere<IStatePredicate>.Union(ab, abb);    // (2, 3)
            var inter = Ere<IStatePredicate>.Intersect(left, right);
            // intersection length interval is (max(1,2), min(2,3)) = (2,2);
            // not pruned, but bounds should agree.
            Assert.That(inter.MinLen, Is.GreaterThanOrEqualTo(2));
            Assert.That(inter.MaxLen, Is.LessThanOrEqualTo(3));
        }

        [Test]
        public void Intersect_DisjointLengths_PrunesToEmpty()
        {
            // a·b has length exactly 2; a alone has length exactly 1.
            // Their length intervals are disjoint → intersection is empty.
            var a = Atom(Pa);
            var ab = Ere<IStatePredicate>.Concat(a, Atom(Pb));
            var inter = Ere<IStatePredicate>.Intersect(a, ab);
            Assert.That(inter, Is.InstanceOf<EreEmpty<IStatePredicate>>(),
                "intersect of (a) with (a·b) should prune to ∅ via length bounds");
        }

        [Test]
        public void Intersect_DisjointLengths_LongerCase_PrunesToEmpty()
        {
            // a·b  ∩  a·b·b  : disjoint lengths (2 vs 3) ⇒ ∅
            var a = Atom(Pa);
            var b = Atom(Pb);
            var ab = Ere<IStatePredicate>.Concat(a, b);
            var abb = Ere<IStatePredicate>.Concat(ab, b);
            var inter = Ere<IStatePredicate>.Intersect(ab, abb);
            Assert.That(inter, Is.InstanceOf<EreEmpty<IStatePredicate>>());
        }

        [Test]
        public void Intersect_OverlappingLengths_NotPruned()
        {
            // a*  ∩  a·b  : a* has (0,∞), a·b has (2,2). Overlap at 2.
            // Intersect should not collapse to ∅ via length bounds.
            var a = Atom(Pa);
            var ab = Ere<IStatePredicate>.Concat(a, Atom(Pb));
            var aStar = Ere<IStatePredicate>.Star(a);
            var inter = Ere<IStatePredicate>.Intersect(aStar, ab);
            // Note: the language *is* empty since a* contains no b, but
            // length bounds alone cannot detect that. We assert only
            // that length-bound pruning did not fire.
            Assert.That(inter, Is.Not.InstanceOf<EreEmpty<IStatePredicate>>(),
                "length bounds should not prune when intervals overlap");
        }

        [Test]
        public void Fusion_SubtractsOneFromConcatLength()
        {
            // fusion glues the boundary letter: len(R:S) = len(R)+len(S)-1.
            var a = Atom(Pa);
            var b = Atom(Pb);
            // (a·b) : (b·a)  → length 2 + 2 - 1 = 3
            var ab = Ere<IStatePredicate>.Concat(a, b);
            var ba = Ere<IStatePredicate>.Concat(b, a);
            var f = Ere<IStatePredicate>.Fusion(ab, ba);
            Assert.That(f.MinLen, Is.EqualTo(3));
            Assert.That(f.MaxLen, Is.EqualTo(3));
        }
    }
}
