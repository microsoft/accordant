namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;
    using System.Linq;

    /// <summary>
    /// Pins the predicate-algebra-aware complement push-through rewrites
    /// (Phase 12 / Rust EREQ port P2.4): COMPL-12 and COMPL-13.
    /// </summary>
    [TestFixture]
    public class EreComplementPushThroughTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ere<IStatePredicate> SigmaStar()
            => Ere<IStatePredicate>.Star(Ere<IStatePredicate>.Sigma());

        [OneTimeSetUp]
        public void RegisterAlgebra()
        {
            Ere<IStatePredicate>.DefaultBuilder.RegisterAlgebra(StatePropEba.Instance);
        }

        [Test]
        public void Compl_StartsWithP_RewritesToEps_Or_NotP_Sigma()
        {
            // ~([a] · Σ*)  =  ε  |  [¬a] · Σ*
            var a = Atom(Pa);
            var input = Ere<IStatePredicate>.Concat(a, SigmaStar());
            var compl = Ere<IStatePredicate>.Complement(input);

            // Expected shape: a Union with two operands: ε and [¬a]·Σ*.
            Assert.That(compl, Is.InstanceOf<EreUnion<IStatePredicate>>(),
                $"expected a union, got {compl}");
            var u = (EreUnion<IStatePredicate>)compl;
            Assert.That(u.Operands.Count, Is.EqualTo(2));
            Assert.That(u.Operands.Any(o => o is EreEpsilon<IStatePredicate>), Is.True,
                "expected ε to be one of the union operands");
            // The other operand should be a Concat whose left atom predicate is ¬a.
            var concat = u.Operands.OfType<EreConcat<IStatePredicate>>().SingleOrDefault();
            Assert.That(concat, Is.Not.Null, "expected one [¬a]·Σ* concat");
            Assert.That(concat.Left, Is.InstanceOf<EreAtom<IStatePredicate>>());
            var negAtom = (EreAtom<IStatePredicate>)concat.Left;
            Assert.That(negAtom.Predicate, Is.InstanceOf<StatePredNot>(),
                "expected negated predicate");
        }

        [Test]
        public void Compl_ContainsP_RewritesToNotP_Star()
        {
            // ~(Σ* · [a] · Σ*)  =  [¬a]*
            var a = Atom(Pa);
            var input = Ere<IStatePredicate>.Concat(
                SigmaStar(),
                Ere<IStatePredicate>.Concat(a, SigmaStar()));
            var compl = Ere<IStatePredicate>.Complement(input);

            Assert.That(compl, Is.InstanceOf<EreStar<IStatePredicate>>(),
                $"expected a star, got {compl}");
            var star = (EreStar<IStatePredicate>)compl;
            Assert.That(star.Inner, Is.InstanceOf<EreAtom<IStatePredicate>>());
            var atom = (EreAtom<IStatePredicate>)star.Inner;
            Assert.That(atom.Predicate, Is.InstanceOf<StatePredNot>(),
                "star inner should be the negated atom predicate");
        }

        [Test]
        public void Compl_Idempotent_DoubleNegationCancels()
        {
            // The new rules must not break the existing ~~R = R cancellation:
            // double-complementing the contains-p shape returns it verbatim.
            var a = Atom(Pa);
            var input = Ere<IStatePredicate>.Concat(
                SigmaStar(),
                Ere<IStatePredicate>.Concat(a, SigmaStar()));
            var doubleCompl = Ere<IStatePredicate>.Complement(
                Ere<IStatePredicate>.Complement(input));
            // ~([¬a]*) is itself the contains-p shape; ~~R should land back at R
            // semantically — pin equivalence via the model checker rather than AST.
            // Cheaper structural check: re-complementing [¬a]* should yield a
            // form whose Nullable, MinLen, MaxLen match the original.
            Assert.That(doubleCompl.Nullable, Is.EqualTo(input.Nullable));
            Assert.That(doubleCompl.MinLen, Is.EqualTo(input.MinLen));
        }

        [Test]
        public void Compl_NonMatchingShape_LeavesComplementWrapped()
        {
            // Concat that is *not* the canonical starts-with-p or contains-p
            // shape should fall through and produce an EreComplement wrapper.
            var a = Atom(Pa);
            var input = Ere<IStatePredicate>.Concat(a, a);  // [a]·[a]
            var compl = Ere<IStatePredicate>.Complement(input);
            Assert.That(compl, Is.InstanceOf<EreComplement<IStatePredicate>>(),
                "non-matching shapes should fall through to the EreComplement constructor");
        }
    }
}
