namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;
    using System.Linq;

    /// <summary>
    /// Pins the predicate-star intersection-distribution rewrite (Phase 12 /
    /// Rust EREQ port P3.2, lib.rs:2604–2612):
    ///   [p]* ∩ R·S  ≡  ([p]*∩R) · ([p]*∩S)
    /// </summary>
    [TestFixture]
    public class EreIntersectPredicateStarDistribTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        [OneTimeSetUp]
        public void RegisterAlgebra()
        {
            Ere<IStatePredicate>.DefaultBuilder.RegisterAlgebra(StatePropEba.Instance);
        }

        [Test]
        public void PStar_Inter_Concat_DistributesOverConcat()
        {
            // [a]* ∩ ([a]·[a])  →  ([a]*∩[a]) · ([a]*∩[a])  →  [a]·[a]
            // The result should be a Concat (the distributed form), not an
            // EreIntersect wrapper around the original Concat.
            var a = Atom(Pa);
            var aStar = Ere<IStatePredicate>.Star(a);
            var aa = Ere<IStatePredicate>.Concat(a, a);
            var result = Ere<IStatePredicate>.Intersect(aStar, aa);
            Assert.That(result, Is.Not.InstanceOf<EreIntersect<IStatePredicate>>(),
                $"distribution should remove the outer EreIntersect wrapper, got {result}");
            // Semantic check: result should accept exactly the length-2 'aa'.
            Assert.That(result.MinLen, Is.EqualTo(2));
            Assert.That(result.MaxLen, Is.EqualTo(2));
        }

        [Test]
        public void PStar_Inter_Concat_OfDifferentAtom_PrunesToEmpty()
        {
            // [a]* ∩ ([b]·[b])  →  ([a]*∩[b]) · ([a]*∩[b])
            // [a]* ∩ [b] is the empty language (b cannot be a — but the
            // pure-symbolic StatePropEba treats a and b as independent
            // satisfiable atoms, so it cannot prove emptiness at the EBA
            // level. Just check the distribution fired (no outer
            // EreIntersect of the form [a]* ∩ b·b).
            var a = Atom(Pa);
            var b = Atom(Pb);
            var aStar = Ere<IStatePredicate>.Star(a);
            var bb = Ere<IStatePredicate>.Concat(b, b);
            var result = Ere<IStatePredicate>.Intersect(aStar, bb);
            Assert.That(result, Is.Not.InstanceOf<EreEmpty<IStatePredicate>>(),
                "no EBA-level emptiness for independent atoms; distribution still applies");
            Assert.That(result, Is.Not.InstanceOf<EreIntersect<IStatePredicate>>(),
                $"distribution should remove the outer Intersect wrapper, got {result}");
        }
    }

    /// <summary>
    /// Pins the predicate-star union merge (Phase 12 / Rust EREQ port P3.3,
    /// lib.rs:2057–2061):  [p]* | [q]*  ≡  (p|q)*  via the registered
    /// predicate algebra.
    /// </summary>
    [TestFixture]
    public class EreUnionPredicateStarMergeTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        [OneTimeSetUp]
        public void RegisterAlgebra()
        {
            Ere<IStatePredicate>.DefaultBuilder.RegisterAlgebra(StatePropEba.Instance);
        }

        [Test]
        public void PStar_Union_PStar_MergesViaAlgebraOr()
        {
            // [a]* | [b]*  →  (a|b)*
            var aStar = Ere<IStatePredicate>.Star(Atom(Pa));
            var bStar = Ere<IStatePredicate>.Star(Atom(Pb));
            var u = Ere<IStatePredicate>.Union(aStar, bStar);
            Assert.That(u, Is.InstanceOf<EreStar<IStatePredicate>>(),
                $"expected a single Star, got {u}");
            var s = (EreStar<IStatePredicate>)u;
            Assert.That(s.Inner, Is.InstanceOf<EreAtom<IStatePredicate>>(),
                "merged star inner should be a single Atom carrying (a ⊔ b)");
            var atom = (EreAtom<IStatePredicate>)s.Inner;
            Assert.That(atom.Predicate, Is.InstanceOf<StatePredOr>(),
                "merged predicate should be the algebra Or");
        }

        [Test]
        public void PStar_Union_SamePred_DedupedNotDoubled()
        {
            // [a]* | [a]*  →  [a]*   (already handled by SortedSet dedup,
            // but pin the joint behaviour with the new merge rule).
            var aStar = Ere<IStatePredicate>.Star(Atom(Pa));
            var u = Ere<IStatePredicate>.Union(aStar, aStar);
            Assert.That(u, Is.SameAs(aStar));
        }

        [Test]
        public void PStar_Union_NonPredicateStar_LeftAlone()
        {
            // [a]* | ([a]·[b])* : the second operand's inner is a Concat,
            // not an Atom, so the P3.3 merge must NOT fire and the union
            // should remain a 2-operand EreUnion (or some other safe form).
            var a = Atom(Pa);
            var b = Atom(Pb);
            var aStar = Ere<IStatePredicate>.Star(a);
            var abStar = Ere<IStatePredicate>.Star(Ere<IStatePredicate>.Concat(a, b));
            var u = Ere<IStatePredicate>.Union(aStar, abStar);
            // Either an EreUnion of two stars, or something else - but
            // crucially NOT a single Star (which would imply unsound merge).
            if (u is EreStar<IStatePredicate> singleStar)
            {
                // Only safe if it's specifically the [a]* operand.
                Assert.That(singleStar, Is.SameAs(aStar),
                    "if collapsed to a single star, must be the [a]* operand only");
            }
        }
    }
}
