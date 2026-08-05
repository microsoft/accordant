namespace Accordant.ModelChecking.Tests
{
    using Microsoft.Accordant.ModelChecking.Bdd;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Unit tests for the BDD-backed EBA. Covers the propositional
    /// decision contract — including cases that exceed the toy
    /// <see cref="StatePropEba"/>'s 2²⁰ brute-force cutoff.
    /// </summary>
    [TestFixture]
    public class BddStatePropEbaTests
    {
        private BddStatePropEba _eba;
        private StateProp _p, _q, _r;
        private StatePredAtom _ap, _aq, _ar;

        [SetUp]
        public void Setup()
        {
            // The singleton's per-instance ordinal cache only affects
            // diagnostics, not decision correctness, so we share it.
            _eba = BddStatePropEba.Instance;
            _p = new StateProp("p", _ => true);
            _q = new StateProp("q", _ => true);
            _r = new StateProp("r", _ => true);
            _ap = new StatePredAtom(_p);
            _aq = new StatePredAtom(_q);
            _ar = new StatePredAtom(_r);
        }

        // ----------------- IsSatisfiable -----------------

        [Test]
        public void Top_Is_Sat() => Assert.That(_eba.IsSatisfiable(_eba.Top), Is.True);

        [Test]
        public void Bottom_Is_Unsat() => Assert.That(_eba.IsSatisfiable(_eba.Bottom), Is.False);

        [Test]
        public void Atom_Is_Sat() => Assert.That(_eba.IsSatisfiable(_ap), Is.True);

        [Test]
        public void PAndNotP_Is_Unsat()
        {
            var phi = _eba.And(_ap, _eba.Not(_ap));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }

        [Test]
        public void PqAndNotP_Is_Unsat()
        {
            var phi = _eba.And(_eba.And(_ap, _aq), _eba.Not(_ap));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }

        [Test]
        public void POrQ_AndNotP_AndNotQ_Is_Unsat()
        {
            var phi = _eba.And(
                _eba.And(_eba.Or(_ap, _aq), _eba.Not(_ap)),
                _eba.Not(_aq));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }

        // ----------------- AreEquivalent -----------------

        [Test]
        public void Idempotence_PAndP_Equiv_P()
            => Assert.That(_eba.AreEquivalent(_eba.And(_ap, _ap), _ap), Is.True);

        [Test]
        public void DeMorgan_Holds()
        {
            // ¬(p ∧ q) ≡ ¬p ∨ ¬q
            var lhs = _eba.Not(_eba.And(_ap, _aq));
            var rhs = _eba.Or(_eba.Not(_ap), _eba.Not(_aq));
            Assert.That(_eba.AreEquivalent(lhs, rhs), Is.True);
        }

        [Test]
        public void Distributivity_Holds()
        {
            // p ∧ (q ∨ r) ≡ (p ∧ q) ∨ (p ∧ r)
            var lhs = _eba.And(_ap, _eba.Or(_aq, _ar));
            var rhs = _eba.Or(_eba.And(_ap, _aq), _eba.And(_ap, _ar));
            Assert.That(_eba.AreEquivalent(lhs, rhs), Is.True);
        }

        [Test]
        public void NonEquivalent_Returns_False()
            => Assert.That(_eba.AreEquivalent(_ap, _aq), Is.False);

        // ----------------- Implies -----------------

        [Test]
        public void Atom_Implies_TopButNotBottom()
        {
            Assert.That(_eba.Implies(_ap, _eba.Top), Is.True);
            Assert.That(_eba.Implies(_ap, _eba.Bottom), Is.False);
        }

        [Test]
        public void PAndQ_Implies_P()
        {
            Assert.That(_eba.Implies(_eba.And(_ap, _aq), _ap), Is.True);
            Assert.That(_eba.Implies(_ap, _eba.And(_ap, _aq)), Is.False);
        }

        // ----------------- Beyond the toy cutoff -----------------

        [Test]
        public void Unsat_Detected_Beyond_BruteForce_Cutoff()
        {
            // The toy enumerator caps at 20 atoms and returns
            // conservative-true above that. The BDD backend has no cap:
            // build a 25-atom contradiction "p0 ∧ ¬p0" buried under
            // irrelevant fan-out and confirm we still say unsat.
            var atoms = new StatePredAtom[25];
            for (int i = 0; i < atoms.Length; i++)
                atoms[i] = new StatePredAtom(
                    new StateProp("p" + i, _ => true));

            IStatePredicate bigOr = atoms[1];
            for (int i = 2; i < atoms.Length; i++)
                bigOr = _eba.Or(bigOr, atoms[i]);

            // (atoms[0] ∧ big_or) ∧ ¬atoms[0]   — unsat regardless of big_or.
            var phi = _eba.And(_eba.And(atoms[0], bigOr), _eba.Not(atoms[0]));
            Assert.That(_eba.IsSatisfiable(phi), Is.False,
                "BDD backend must detect contradictions above the 20-atom toy cutoff.");
        }

        [Test]
        public void Equivalence_Detected_Beyond_BruteForce_Cutoff()
        {
            // 25-atom de-Morgan pair: ¬(∧ atoms_i) ≡ ∨ ¬atoms_i
            var atoms = new StatePredAtom[25];
            for (int i = 0; i < atoms.Length; i++)
                atoms[i] = new StatePredAtom(
                    new StateProp("q" + i, _ => true));

            IStatePredicate andAll = atoms[0];
            for (int i = 1; i < atoms.Length; i++)
                andAll = _eba.And(andAll, atoms[i]);

            IStatePredicate orNeg = _eba.Not(atoms[0]);
            for (int i = 1; i < atoms.Length; i++)
                orNeg = _eba.Or(orNeg, _eba.Not(atoms[i]));

            Assert.That(_eba.AreEquivalent(_eba.Not(andAll), orNeg), Is.True);
        }

        // ----------------- Models (delegates to structural) -----------------

        [Test]
        public void Models_Delegates_To_Atom_Evaluate()
        {
            var trueProp  = new StateProp("alwaysTrue",  _ => true);
            var falseProp = new StateProp("alwaysFalse", _ => false);
            var phi = _eba.And(new StatePredAtom(trueProp),
                               _eba.Not(new StatePredAtom(falseProp)));
            // State is the model-program state; we just need a stand-in.
            Assert.That(_eba.Models(element: null, predicate: phi), Is.True);
        }
    }

    /// <summary>
    /// Verifies that the <c>[ModuleInitializer]</c> in
    /// <see cref="BddBackend"/> fires automatically when this assembly
    /// is loaded — the visible effect is that
    /// <see cref="StatePropEbaProvider.Default"/> resolves to the BDD
    /// adapter instead of the toy.
    /// </summary>
    [TestFixture]
    public class BddBackendRegistrationTests
    {
        [Test]
        public void ModuleInitializer_Registers_Bdd_As_Default()
        {
            Assert.That(StatePropEbaProvider.Default,
                Is.SameAs(BddStatePropEba.Instance));
        }

        [Test]
        public void Reset_Then_Reregister_Roundtrips()
        {
            try
            {
                StatePropEbaProvider.ResetToFallback();
                Assert.That(StatePropEbaProvider.Default,
                    Is.SameAs(StatePropEba.Instance));

                BddBackend.RegisterAsDefault();
                Assert.That(StatePropEbaProvider.Default,
                    Is.SameAs(BddStatePropEba.Instance));
            }
            finally
            {
                BddBackend.RegisterAsDefault();
            }
        }
    }
}
