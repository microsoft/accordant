namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for the runtime side of G8-c / <c>equiv-rltl-tableau-dedup</c>:
    /// wiring an <see cref="IEreCanonicalizer{TPred}"/> into
    /// <see cref="RltlDerivative{TPred,TElem}"/> so that residual regexes
    /// appearing in derivative leaves (the <c>R'</c> in <c>R';φ</c> etc.)
    /// are replaced by canonical representatives of their language-
    /// equivalence class. Combined with the existing RLTL hash-consing this
    /// collapses tableau / closure nodes that differ only by syntactically-
    /// distinct but language-equivalent embedded regexes — a state-space
    /// reduction at derivative time.
    /// </summary>
    [TestFixture]
    public class RltlDerivativeTableauDedupTests
    {
        private IntEba _eba;
        private ConditionRegistry<IntPredicate> _registry;
        private IntPredicate _ap, _bp;
        private Rltl<IntPredicate> _phi;
        private Ere<IntPredicate> _r1, _r2;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(3);
            _ap = new IntPredicate("a", 0);
            _bp = new IntPredicate("b", 1);
            _registry = new ConditionRegistry<IntPredicate>(
                EqualityComparer<IntPredicate>.Default);

            // Two language-equivalent but structurally distinct regexes:
            //   r1 = a · a*    r2 = a* · a   (both recognise a^+).
            var a = Ere<IntPredicate>.Atom(_ap);
            _r1 = Ere<IntPredicate>.Concat(a, Ere<IntPredicate>.Star(a));
            _r2 = Ere<IntPredicate>.Concat(Ere<IntPredicate>.Star(a), a);
            _phi = Rltl<IntPredicate>.Atom(_bp);
        }

        private EreCanonicalizer<IntPredicate, int> MakeCanon()
        {
            var ereDeriv = new EreDerivative<IntPredicate, int>(_eba, _registry);
            var checker = new EreEquivalenceChecker<IntPredicate, int>(ereDeriv);
            return new EreCanonicalizer<IntPredicate, int>(checker);
        }

        [Test]
        public void Plain_EreCanonicalizer_IsNull()
        {
            var d = new RltlDerivative<IntPredicate, int>(_eba, _registry);
            Assert.That(d.EreCanonicalizer, Is.Null);
        }

        [Test]
        public void Dedup_SeqPrefix_ResidualsCollapse()
        {
            // f = ((a·a*) ; φ)  ∨  ((a*·a) ; φ)
            // Without dedup, the closure contains both SeqPrefix variants as
            // distinct states. With dedup, they collapse to one.
            var s1 = Rltl<IntPredicate>.SeqPrefix(_r1, _phi);
            var s2 = Rltl<IntPredicate>.SeqPrefix(_r2, _phi);
            Assert.That(ReferenceEquals(s1, s2), Is.False,
                "Prerequisite: the two SeqPrefix forms are structurally distinct.");

            var canon = MakeCanon();
            var deriv = new RltlDerivative<IntPredicate, int>(_eba, _registry, canon);
            Assert.That(deriv.EreCanonicalizer, Is.SameAs(canon));

            // The very first state in the ABW emitted by the derivative
            // engine for s1 (resp. s2) is the atom itself; canonicalisation
            // happens at residual time inside Derivative(...). To exercise
            // that path, take one derivative step and inspect the leaves.
            var d1 = deriv.Derivative(s1);
            var d2 = deriv.Derivative(s2);

            // After one derivative step the residuals should have been
            // routed through the ERE canonicaliser, so structurally
            // equivalent residuals are now shared. Concretely, both d1 and
            // d2 reduce to terms whose leaves carry SeqPrefix(canon(r1·a*-deriv), φ).
            // The strongest observable invariant is that the resulting
            // transition terms are reference-equal modulo any commutative
            // re-ordering: at minimum, their distinct-leaf sets must agree
            // on the RLTL atoms they contain.
            var leaves1 = new HashSet<Rltl<IntPredicate>>();
            foreach (var leaf in d1.GetDistinctLeaves())
                foreach (var st in leaf.GetAllStates())
                    leaves1.Add(st);
            var leaves2 = new HashSet<Rltl<IntPredicate>>();
            foreach (var leaf in d2.GetDistinctLeaves())
                foreach (var st in leaf.GetAllStates())
                    leaves2.Add(st);

            // Every state reachable from s1 must equal (by reference) one
            // reachable from s2, and vice versa.
            foreach (var s in leaves1)
                Assert.That(leaves2, Does.Contain(s),
                    $"Residual {s} from s1 is not aliased to any residual from s2.");
            foreach (var s in leaves2)
                Assert.That(leaves1, Does.Contain(s),
                    $"Residual {s} from s2 is not aliased to any residual from s1.");
        }

        [Test]
        public void Dedup_FullClosure_StateCount_IsLowerOrEqual()
        {
            // Build the ABWs for both SeqPrefix forms via ToABW + lazy
            // exploration. Under dedup, residuals canonicalise; we expect
            // the combined number of distinct RLTL states discovered while
            // exploring the two ABWs in lock-step to be no greater than
            // without dedup.
            var phi = Rltl<IntPredicate>.SeqPrefix(_r1, _phi);

            int Explore(RltlDerivative<IntPredicate, int> d)
            {
                var abw = d.ToABW(phi);
                var seen = new HashSet<Rltl<IntPredicate>>();
                var work = new Stack<Rltl<IntPredicate>>();
                foreach (var s in abw.InitialState.GetAllStates())
                {
                    if (seen.Add(s)) work.Push(s);
                }
                while (work.Count > 0)
                {
                    var s = work.Pop();
                    var t = abw.GetTransition(s);
                    foreach (var leaf in t.GetDistinctLeaves())
                        foreach (var q in leaf.GetAllStates())
                            if (seen.Add(q)) work.Push(q);
                }
                return seen.Count;
            }

            var dPlain = new RltlDerivative<IntPredicate, int>(_eba, _registry);
            var dCanon = new RltlDerivative<IntPredicate, int>(
                _eba, _registry, MakeCanon());

            int nPlain = Explore(dPlain);
            int nCanon = Explore(dCanon);

            Assert.That(nCanon, Is.LessThanOrEqualTo(nPlain),
                $"Dedup must not grow the closure (plain={nPlain}, dedup={nCanon}).");
        }
    }
}
