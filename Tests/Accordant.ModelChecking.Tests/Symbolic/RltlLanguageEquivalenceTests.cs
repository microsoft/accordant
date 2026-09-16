namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for <see cref="RltlLanguageEquivalence"/> (G8-b): sound and
    /// complete language equivalence for RLTL formulas via two-way emptiness.
    /// Uses <see cref="IntEba"/> for precise <c>IsSatisfiable</c> so that
    /// equivalences relying on predicate-level contradictions can be decided.
    /// </summary>
    [TestFixture]
    public class RltlLanguageEquivalenceTests
    {
        private IntEba _eba;
        private RltlAlgebra<IntPredicate> _alg;
        private IntPredicate _a, _b;
        private Rltl<IntPredicate> _A, _B, _TT, _FF;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(3);
            _alg = new RltlAlgebra<IntPredicate>(_eba);
            _a = new IntPredicate("a", 0, 1);
            _b = new IntPredicate("b", 1, 2);
            _A = _alg.Atom(_a);
            _B = _alg.Atom(_b);
            _TT = _alg.True;
            _FF = _alg.False;
        }

        private bool Eq(Rltl<IntPredicate> p, Rltl<IntPredicate> q)
            => RltlLanguageEquivalence.AreEquivalent(_eba, _alg, p, q);
        private bool Sub(Rltl<IntPredicate> p, Rltl<IntPredicate> q)
            => RltlLanguageEquivalence.Includes(_eba, _alg, p, q);
        private bool Empty(Rltl<IntPredicate> p)
            => RltlLanguageEquivalence.IsLanguageEmpty<IntPredicate, int>(_eba, p);

        // ---------- IsLanguageEmpty ----------

        [Test]
        public void IsLanguageEmpty_False_IsEmpty()
            => Assert.That(Empty(_FF), Is.True);

        [Test]
        public void IsLanguageEmpty_True_IsNotEmpty()
            => Assert.That(Empty(_TT), Is.False);

        [Test]
        public void IsLanguageEmpty_GloballyFalse_IsEmpty()
            => Assert.That(Empty(_alg.Globally(_FF)), Is.True);

        [Test]
        public void IsLanguageEmpty_EventuallyTrue_IsNotEmpty()
            => Assert.That(Empty(_alg.Eventually(_TT)), Is.False);

        [Test]
        public void IsLanguageEmpty_AAndNotA_IsEmpty()
        {
            // a ∧ ¬a is unsatisfiable as a predicate; the formula has no models
            // even at position 0.
            var notA = _alg.Atom(_eba.Not(_a));
            Assert.That(Empty(_alg.And(_A, notA)), Is.True);
        }

        [Test]
        public void IsLanguageEmpty_FaAndGNotA_IsEmpty()
        {
            // F a ∧ G ¬a — a must occur somewhere AND a must never occur.
            // Detected as empty via precise IsSatisfiable on the derivative
            // path conditions.
            var notA = _alg.Atom(_eba.Not(_a));
            var phi = _alg.And(_alg.Eventually(_A), _alg.Globally(notA));
            Assert.That(Empty(phi), Is.True);
        }

        // ---------- AreEquivalent — trivial reflexivity ----------

        [Test]
        public void AreEquivalent_Reflexive()
        {
            Assert.That(Eq(_TT, _TT), Is.True);
            Assert.That(Eq(_FF, _FF), Is.True);
            Assert.That(Eq(_A, _A), Is.True);
            Assert.That(Eq(_alg.Eventually(_A), _alg.Eventually(_A)), Is.True);
        }

        [Test]
        public void AreEquivalent_True_NotEquivTo_False()
            => Assert.That(Eq(_TT, _FF), Is.False);

        [Test]
        public void AreEquivalent_DistinctAtoms_NotEquiv()
            => Assert.That(Eq(_A, _B), Is.False);

        // ---------- AreEquivalent — semantic LTL equivalences ----------

        [Test]
        public void AreEquivalent_DoubleNegation()
            => Assert.That(Eq(_A, _alg.Not(_alg.Not(_A))), Is.True);

        [Test]
        public void AreEquivalent_F_Idempotent()
        {
            // F a ≡ F F a
            var fa = _alg.Eventually(_A);
            var ffa = _alg.Eventually(fa);
            Assert.That(Eq(fa, ffa), Is.True);
        }

        [Test]
        public void AreEquivalent_G_Idempotent()
        {
            // G a ≡ G G a
            var ga = _alg.Globally(_A);
            var gga = _alg.Globally(ga);
            Assert.That(Eq(ga, gga), Is.True);
        }

        [Test]
        public void AreEquivalent_FDistributesOverOr()
        {
            // F(a ∨ b) ≡ F a ∨ F b
            var lhs = _alg.Eventually(_alg.Or(_A, _B));
            var rhs = _alg.Or(_alg.Eventually(_A), _alg.Eventually(_B));
            Assert.That(Eq(lhs, rhs), Is.True);
        }

        [Test]
        public void AreEquivalent_GDistributesOverAnd()
        {
            // G(a ∧ b) ≡ G a ∧ G b
            var lhs = _alg.Globally(_alg.And(_A, _B));
            var rhs = _alg.And(_alg.Globally(_A), _alg.Globally(_B));
            Assert.That(Eq(lhs, rhs), Is.True);
        }

        [Test]
        public void AreEquivalent_DualityFG()
        {
            // ¬F a ≡ G ¬a
            var notFa = _alg.Not(_alg.Eventually(_A));
            var gNotA = _alg.Globally(_alg.Atom(_eba.Not(_a)));
            Assert.That(Eq(notFa, gNotA), Is.True);
        }

        [Test]
        public void AreEquivalent_DualityUR()
        {
            // ¬(a U b) ≡ (¬a) R (¬b)
            var notU = _alg.Not(_alg.Until(_A, _B));
            var rDual = _alg.Release(
                _alg.Atom(_eba.Not(_a)),
                _alg.Atom(_eba.Not(_b)));
            Assert.That(Eq(notU, rDual), Is.True);
        }

        // ---------- AreEquivalent — known inequivalences ----------

        [Test]
        public void AreEquivalent_F_NotEquivTo_G()
            => Assert.That(Eq(_alg.Eventually(_A), _alg.Globally(_A)), Is.False);

        [Test]
        public void AreEquivalent_GF_NotEquivTo_FG()
        {
            // G F a (infinitely often) ≢ F G a (eventually always).
            var gfa = _alg.Globally(_alg.Eventually(_A));
            var fga = _alg.Eventually(_alg.Globally(_A));
            Assert.That(Eq(gfa, fga), Is.False);
        }

        // ---------- Inclusion ----------

        [Test]
        public void Includes_AtomImpliesEventually()
            => Assert.That(Sub(_A, _alg.Eventually(_A)), Is.True);

        [Test]
        public void Includes_GloballyImpliesAtom()
            => Assert.That(Sub(_alg.Globally(_A), _A), Is.True);

        [Test]
        public void Includes_FaImpliesFaOrb()
            => Assert.That(Sub(_alg.Eventually(_A),
                               _alg.Eventually(_alg.Or(_A, _B))), Is.True);

        [Test]
        public void Includes_EventuallyDoesNotImplyGlobally()
            => Assert.That(Sub(_alg.Eventually(_A), _alg.Globally(_A)), Is.False);

        // ---------- RLTL-specific (regex-prefix operators) ----------

        [Test]
        public void AreEquivalent_NegateSeqPrefix_YieldsTrigger()
        {
            // R;φ and the corresponding Trigger formula via Negate must satisfy:
            // ¬¬(R;φ) ≡ R;φ.
            var r = Ere<IntPredicate>.Atom(_a);
            var seq = _alg.SeqPrefix(r, _B);
            Assert.That(Eq(_alg.Not(_alg.Not(seq)), seq), Is.True);
        }

        [Test]
        public void AreEquivalent_OmegaClosureFalse_IsEmpty()
        {
            // (⊥)^ω has empty ω-language.
            var phi = _alg.OmegaClosure(Ere<IntPredicate>.Empty());
            Assert.That(Empty(phi), Is.True);
        }
    }
}
