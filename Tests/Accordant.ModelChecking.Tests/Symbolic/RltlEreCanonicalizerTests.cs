namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for G8-c: canonicalisation of embedded ERE sub-formulas in
    /// <see cref="RltlAlgebra{TPred}"/>. With an
    /// <see cref="IEreCanonicalizer{TPred}"/> attached, two RLTL formulas
    /// that differ only by language-equivalent embedded regexes become
    /// reference-equal — RLTL structural equality modulo ERE equivalence.
    /// </summary>
    [TestFixture]
    public class RltlEreCanonicalizerTests
    {
        private IntEba _eba;
        private RltlAlgebra<IntPredicate> _plain;
        private RltlAlgebra<IntPredicate> _canon;
        private EreCanonicalizer<IntPredicate, int> _canonImpl;
        private IntPredicate _ap, _bp;
        private Rltl<IntPredicate> _phi;

        // Two language-equivalent regexes that are syntactically distinct:
        //   r1 = a · a*    (a-then-zero-or-more-a)
        //   r2 = a* · a    (zero-or-more-a-then-a)
        // L(r1) = L(r2) = a^+ but Concat is non-commutative, so r1 ≢ r2.
        private Ere<IntPredicate> _r1, _r2;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(3);
            _ap = new IntPredicate("a", 0);
            _bp = new IntPredicate("b", 1);

            _plain = new RltlAlgebra<IntPredicate>(_eba);

            var registry = new ConditionRegistry<IntPredicate>(
                EqualityComparer<IntPredicate>.Default);
            var deriv = new EreDerivative<IntPredicate, int>(_eba, registry);
            var checker = new EreEquivalenceChecker<IntPredicate, int>(deriv);
            _canonImpl = new EreCanonicalizer<IntPredicate, int>(checker);
            _canon = new RltlAlgebra<IntPredicate>(_eba, _canonImpl);

            var a = Ere<IntPredicate>.Atom(_ap);
            _r1 = Ere<IntPredicate>.Concat(a, Ere<IntPredicate>.Star(a));
            _r2 = Ere<IntPredicate>.Concat(Ere<IntPredicate>.Star(a), a);

            // Sanity: the two regexes are NOT structurally equal but ARE
            // language-equivalent. Both invariants are prerequisites for
            // these tests to actually test what they claim.
            Assert.That(ReferenceEquals(_r1, _r2), Is.False,
                "Test prerequisite: r1 and r2 must be distinct ERE references.");
            Assert.That(checker.AreEquivalent(_r1, _r2), Is.True,
                "Test prerequisite: r1 and r2 must be language-equivalent.");

            _phi = _canon.Atom(_bp);
        }

        // ---------- Back-compat: no canonicaliser ----------

        [Test]
        public void PlainAlgebra_DoesNotMergeEquivalentRegexes()
        {
            var s1 = _plain.SeqPrefix(_r1, _phi);
            var s2 = _plain.SeqPrefix(_r2, _phi);
            Assert.That(ReferenceEquals(s1, s2), Is.False);
        }

        [Test]
        public void PlainAlgebra_EreCanonicalizerIsNull()
            => Assert.That(_plain.EreCanonicalizer, Is.Null);

        // ---------- Canonicaliser wired in: all embedded-ERE constructors ----------

        [Test]
        public void Canon_SeqPrefix_EquivalentRegexes_AreReferenceEqual()
        {
            var s1 = _canon.SeqPrefix(_r1, _phi);
            var s2 = _canon.SeqPrefix(_r2, _phi);
            Assert.That(ReferenceEquals(s1, s2), Is.True);
        }

        [Test]
        public void Canon_OvlPrefix_EquivalentRegexes_AreReferenceEqual()
        {
            var s1 = _canon.OvlPrefix(_r1, _phi);
            var s2 = _canon.OvlPrefix(_r2, _phi);
            Assert.That(ReferenceEquals(s1, s2), Is.True);
        }

        [Test]
        public void Canon_Trigger_EquivalentRegexes_AreReferenceEqual()
        {
            var s1 = _canon.Trigger(_r1, _phi);
            var s2 = _canon.Trigger(_r2, _phi);
            Assert.That(ReferenceEquals(s1, s2), Is.True);
        }

        [Test]
        public void Canon_Match_EquivalentRegexes_AreReferenceEqual()
        {
            var s1 = _canon.Match(_r1, _phi);
            var s2 = _canon.Match(_r2, _phi);
            Assert.That(ReferenceEquals(s1, s2), Is.True);
        }

        [Test]
        public void Canon_WeakClosure_EquivalentRegexes_AreReferenceEqual()
        {
            var s1 = _canon.WeakClosure(_r1);
            var s2 = _canon.WeakClosure(_r2);
            Assert.That(ReferenceEquals(s1, s2), Is.True);
        }

        [Test]
        public void Canon_NegWeakClosure_EquivalentRegexes_AreReferenceEqual()
        {
            var s1 = _canon.NegWeakClosure(_r1);
            var s2 = _canon.NegWeakClosure(_r2);
            Assert.That(ReferenceEquals(s1, s2), Is.True);
        }

        [Test]
        public void Canon_OmegaClosure_EquivalentRegexes_AreReferenceEqual()
        {
            var s1 = _canon.OmegaClosure(_r1);
            var s2 = _canon.OmegaClosure(_r2);
            Assert.That(ReferenceEquals(s1, s2), Is.True);
        }

        // ---------- Negation propagates canonicalisation ----------

        [Test]
        public void Canon_NotSeqPrefix_PreservesCanonicalRegex()
        {
            // ¬(r1 ; φ) = r1 ⊳ ¬φ, and ¬(r2 ; φ) = r2 ⊳ ¬φ. Under
            // canonicalisation both r1 and r2 map to the same rep, so the
            // two Triggers are reference-equal.
            var n1 = _canon.Not(_canon.SeqPrefix(_r1, _phi));
            var n2 = _canon.Not(_canon.SeqPrefix(_r2, _phi));
            Assert.That(ReferenceEquals(n1, n2), Is.True);
            Assert.That(n1, Is.InstanceOf<RltlTrigger<IntPredicate>>());
        }

        // ---------- Distinct (non-equivalent) regexes stay distinct ----------

        [Test]
        public void Canon_DistinctRegexes_StayDistinct()
        {
            var a = Ere<IntPredicate>.Atom(_ap);
            var b = Ere<IntPredicate>.Atom(_bp);
            var s1 = _canon.SeqPrefix(a, _phi);
            var s2 = _canon.SeqPrefix(b, _phi);
            Assert.That(ReferenceEquals(s1, s2), Is.False);
        }

        // ---------- Canonicaliser bookkeeping ----------

        [Test]
        public void Canon_ClassCount_TracksDistinctClasses()
        {
            var a = Ere<IntPredicate>.Atom(_ap);
            var b = Ere<IntPredicate>.Atom(_bp);

            _ = _canon.SeqPrefix(_r1, _phi);   // class 1
            _ = _canon.SeqPrefix(_r2, _phi);   // joins class 1
            _ = _canon.SeqPrefix(a,   _phi);   // class 2
            _ = _canon.SeqPrefix(b,   _phi);   // class 3
            Assert.That(_canonImpl.ClassCount, Is.EqualTo(3));
        }

        [Test]
        public void Canon_RepeatedCanonicalize_IsIdempotent()
        {
            var c1 = _canonImpl.Canonicalize(_r1);
            var c2 = _canonImpl.Canonicalize(_r2);
            var c1again = _canonImpl.Canonicalize(_r1);
            Assert.That(ReferenceEquals(c1, c2), Is.True);
            Assert.That(ReferenceEquals(c1, c1again), Is.True);
        }
    }
}
