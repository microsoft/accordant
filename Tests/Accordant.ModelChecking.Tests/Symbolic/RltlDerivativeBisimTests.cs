namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for G8-a: <see cref="RltlDerivativeBisim{TPred,TElem}"/>.
    /// Two roles:
    /// <list type="bullet">
    ///   <item><b>Positive cases</b> — bisim succeeds on syntactic / shallow
    ///   structural equivalences. These are also confirmed by the G8-b
    ///   oracle so the answers agree.</item>
    ///   <item><b>Differential soundness</b> — for every formula pair we
    ///   exercise, if bisim says <c>true</c> the oracle must also say
    ///   <c>true</c>. Bisim is allowed to say <c>false</c> when the oracle
    ///   says <c>true</c> (incompleteness).</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class RltlDerivativeBisimTests
    {
        private IntEba _eba;
        private RltlAlgebra<IntPredicate> _alg;
        private RltlDerivative<IntPredicate, int> _deriv;
        private RltlDerivativeBisim<IntPredicate, int> _bisim;
        private IntPredicate _ap, _bp;
        private Rltl<IntPredicate> _a, _b;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(3);
            _alg = new RltlAlgebra<IntPredicate>(_eba);
            var registry = new ConditionRegistry<IntPredicate>(
                EqualityComparer<IntPredicate>.Default);
            _deriv = new RltlDerivative<IntPredicate, int>(_eba, registry);
            _bisim = new RltlDerivativeBisim<IntPredicate, int>(_deriv);

            _ap = new IntPredicate("a", 0);
            _bp = new IntPredicate("b", 1);
            _a = _alg.Atom(_ap);
            _b = _alg.Atom(_bp);
        }

        private bool OracleSays(Rltl<IntPredicate> p, Rltl<IntPredicate> q)
            => RltlLanguageEquivalence.AreEquivalent(_eba, _alg, p, q);

        // ---------- Trivial / reflexive cases ----------

        [Test]
        public void Reflexive_True()
            => Assert.That(_bisim.TryProveEquivalent(_alg.True, _alg.True), Is.True);

        [Test]
        public void Reflexive_Atom()
            => Assert.That(_bisim.TryProveEquivalent(_a, _a), Is.True);

        [Test]
        public void Reflexive_Until()
        {
            var phi = _alg.Until(_a, _b);
            Assert.That(_bisim.TryProveEquivalent(phi, phi), Is.True);
        }

        // ---------- Shallow structural equivalences ----------

        [Test]
        public void DoubleNegation_Atom()
        {
            var phi = _a;
            var doubleNeg = _alg.Not(_alg.Not(phi));
            Assert.That(_bisim.TryProveEquivalent(phi, doubleNeg), Is.True);
            Assert.That(OracleSays(phi, doubleNeg), Is.True);
        }

        [Test]
        public void DoubleNegation_Until()
        {
            var phi = _alg.Until(_a, _b);
            var doubleNeg = _alg.Not(_alg.Not(phi));
            Assert.That(_bisim.TryProveEquivalent(phi, doubleNeg), Is.True);
            Assert.That(OracleSays(phi, doubleNeg), Is.True);
        }

        [Test]
        public void And_Commutativity()
        {
            var ab = _alg.And(_a, _b);
            var ba = _alg.And(_b, _a);
            Assert.That(_bisim.TryProveEquivalent(ab, ba), Is.True);
        }

        // ---------- Clearly inequivalent ----------

        [Test]
        public void Distinct_Atoms_Inconclusive_Or_False()
        {
            // Oracle will say inequivalent; bisim must NOT say true (sound).
            Assert.That(OracleSays(_a, _b), Is.False);
            Assert.That(_bisim.TryProveEquivalent(_a, _b), Is.False);
        }

        [Test]
        public void True_Vs_False_Soundness()
        {
            Assert.That(OracleSays(_alg.True, _alg.False), Is.False);
            Assert.That(_bisim.TryProveEquivalent(_alg.True, _alg.False), Is.False);
        }

        [Test]
        public void Until_Vs_Atom_Soundness()
        {
            var u = _alg.Until(_a, _b);
            Assert.That(OracleSays(u, _a), Is.False);
            Assert.That(_bisim.TryProveEquivalent(u, _a), Is.False);
        }

        // ---------- Differential soundness sweep ----------

        // For each pair in this catalogue, run BOTH the bisim and the
        // oracle. Invariant: bisim(p,q) => oracle(p,q). Bisim may be
        // inconclusive (false) when oracle is true.
        [Test]
        public void DifferentialSoundness_Catalogue()
        {
            var phi = _alg.Eventually(_a);
            var psi = _alg.Globally(_b);
            var notA = _alg.Not(_a);
            var pairs = new (Rltl<IntPredicate> p, Rltl<IntPredicate> q)[]
            {
                (_a, _a),
                (_a, _b),
                (_alg.True, _alg.True),
                (_alg.True, _alg.False),
                (phi, phi),
                (phi, psi),
                (phi, _alg.Not(_alg.Not(phi))),
                (psi, _alg.Not(_alg.Eventually(notA))),  // G b ≡ ¬F¬b
                (_alg.And(_a, _b), _alg.And(_b, _a)),
                (_alg.Or(_a, _b), _alg.Or(_b, _a)),
                (_alg.Until(_a, _b), _alg.Until(_a, _b)),
                (_alg.Until(_a, _b), _alg.Release(_b, _a)),
                (_alg.Eventually(_alg.Eventually(_a)), _alg.Eventually(_a)),
                (_alg.Globally(_alg.Globally(_a)), _alg.Globally(_a)),
            };

            foreach (var (p, q) in pairs)
            {
                bool bisimSays = _bisim.TryProveEquivalent(p, q);
                if (bisimSays)
                {
                    bool oracleSays = OracleSays(p, q);
                    Assert.That(oracleSays, Is.True,
                        $"Soundness violation: bisim claimed {p} ≡ {q} but oracle disagrees.");
                }
            }
        }

        [Test]
        public void DifferentialSoundness_IdempotenceFamilies()
        {
            // F F a ≡ F a, G G a ≡ G a — well-known. Bisim may or may not
            // catch them; if it does, oracle must agree.
            var ffa = _alg.Eventually(_alg.Eventually(_a));
            var fa = _alg.Eventually(_a);
            if (_bisim.TryProveEquivalent(ffa, fa))
                Assert.That(OracleSays(ffa, fa), Is.True);

            var gga = _alg.Globally(_alg.Globally(_a));
            var ga = _alg.Globally(_a);
            if (_bisim.TryProveEquivalent(gga, ga))
                Assert.That(OracleSays(gga, ga), Is.True);
        }
    }
}
