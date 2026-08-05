namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for the precise <see cref="StatePropEba.IsSatisfiable"/>
    /// decision procedure (todo <c>statepred-precise-sat</c>): each
    /// <see cref="StateProp"/> is treated as an independent Boolean variable
    /// and a brute-force truth-table decides the propositional fragment of
    /// the predicate algebra.
    /// </summary>
    [TestFixture]
    public class StatePropEbaSatTests
    {
        private StatePropEba _eba;
        private StateProp _p;
        private StateProp _q;
        private StateProp _r;

        [SetUp]
        public void Setup()
        {
            _eba = StatePropEba.Instance;
            _p = new StateProp("p", _ => true);
            _q = new StateProp("q", _ => true);
            _r = new StateProp("r", _ => true);
        }

        private IStatePredicate Atom(StateProp p) => new StatePredAtom(p);

        [Test]
        public void Constants() {
            Assert.That(_eba.IsSatisfiable(_eba.Top), Is.True);
            Assert.That(_eba.IsSatisfiable(_eba.Bottom), Is.False);
        }

        [Test]
        public void SingleAtom_Satisfiable() {
            Assert.That(_eba.IsSatisfiable(Atom(_p)), Is.True);
            Assert.That(_eba.IsSatisfiable(_eba.Not(Atom(_p))), Is.True);
        }

        [Test]
        public void Contradiction_PAndNotP_Unsatisfiable() {
            var phi = _eba.And(Atom(_p), _eba.Not(Atom(_p)));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }

        [Test]
        public void Tautology_PorNotP_Satisfiable() {
            var phi = _eba.Or(Atom(_p), _eba.Not(Atom(_p)));
            Assert.That(_eba.IsSatisfiable(phi), Is.True);
        }

        [Test]
        public void Conjunction_With_Hidden_Contradiction() {
            // (p ∧ q) ∧ ¬p — unsatisfiable; the conflict is below an inner conjunction.
            var phi = _eba.And(_eba.And(Atom(_p), Atom(_q)), _eba.Not(Atom(_p)));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }

        [Test]
        public void Disjunction_Of_Contradictions_Unsatisfiable() {
            // (p ∧ ¬p) ∨ (q ∧ ¬q) — both disjuncts unsat ⇒ whole formula unsat.
            var phi = _eba.Or(
                _eba.And(Atom(_p), _eba.Not(Atom(_p))),
                _eba.And(Atom(_q), _eba.Not(Atom(_q))));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }

        [Test]
        public void DistinctAtoms_Independent() {
            // p ∧ ¬q — satisfiable (p=true, q=false).
            var phi = _eba.And(Atom(_p), _eba.Not(Atom(_q)));
            Assert.That(_eba.IsSatisfiable(phi), Is.True);
        }

        [Test]
        public void ThreeAtoms_HiddenContradiction() {
            // (p ∨ q) ∧ ¬p ∧ ¬q ∧ r — unsatisfiable: ¬p ∧ ¬q forces p∨q false.
            var phi = _eba.And(
                _eba.And(
                    _eba.And(_eba.Or(Atom(_p), Atom(_q)), _eba.Not(Atom(_p))),
                    _eba.Not(Atom(_q))),
                Atom(_r));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }

        [Test]
        public void DoubleNegation_Preserved() {
            // ¬¬p ≡ p — built via the EBA, the constructor normalises double-not.
            var notNotP = _eba.Not(_eba.Not(Atom(_p)));
            Assert.That(_eba.IsSatisfiable(notNotP), Is.True);

            // ¬¬p ∧ ¬p — unsatisfiable.
            var phi = _eba.And(notNotP, _eba.Not(Atom(_p)));
            Assert.That(_eba.IsSatisfiable(phi), Is.False);
        }
    }
}
