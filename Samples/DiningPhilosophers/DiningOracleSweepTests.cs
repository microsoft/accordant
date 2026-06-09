namespace DiningPhilosophers
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Testing;
    using NUnit.Framework;

    /// <summary>
    /// Drives every LTL-expressible DiningPhilosophers property
    /// through the four-backend differential oracle under several
    /// fairness configurations. Any disagreement is a bug.
    /// </summary>
    public class DiningOracleSweepTests
    {
        private StateGraphNode _naiveRoot;
        private StateGraphNode _asymRoot;

        private static readonly Fairness PhilFairness = Fairness.StrongFair(sf => sf is Dining.PhilStep);

        [SetUp]
        public void Setup()
        {
            _naiveRoot = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: false), Dining.InitialState());
            _asymRoot = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: true), Dining.InitialState());
        }

        private static LtlFormula Eating0 => LtlFormula.Prop(Dining.Eating0, "Eating0");
        private static LtlFormula Eating1 => LtlFormula.Prop(Dining.Eating1, "Eating1");
        private static LtlFormula Eating2 => LtlFormula.Prop(Dining.Eating2, "Eating2");
        private static LtlFormula TwoEating => LtlFormula.Prop(Dining.TwoEating, "TwoEating");
        private static LtlFormula SomeEating => LtlFormula.Prop(Dining.SomeEating, "SomeEating");

        // --- Safety: no two adjacent philosophers eat ------------------

        [Test]
        public void Oracle_Safety_NoTwoEating_Naive()
        {
            var phi = LtlFormula.Always(LtlFormula.Not(TwoEating));
            var r = LtlMultiBackendCrossCheck.Run(_naiveRoot, phi, Fairness.None, nameof(Oracle_Safety_NoTwoEating_Naive));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_Safety_NoTwoEating_Asym()
        {
            var phi = LtlFormula.Always(LtlFormula.Not(TwoEating));
            var r = LtlMultiBackendCrossCheck.Run(_asymRoot, phi, Fairness.None, nameof(Oracle_Safety_NoTwoEating_Asym));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_Safety_NoTwoEating_Asym_WeakFair()
        {
            var phi = LtlFormula.Always(LtlFormula.Not(TwoEating));
            var r = LtlMultiBackendCrossCheck.Run(_asymRoot, phi, Fairness.WeakFairAll, nameof(Oracle_Safety_NoTwoEating_Asym_WeakFair));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        // --- Liveness: every philosopher eats infinitely often (asym) --

        [Test]
        public void Oracle_Liveness_EatsInfinitelyOften_Asym_Phil0()
        {
            var phi = LtlFormula.InfinitelyOften(Eating0);
            var r = LtlMultiBackendCrossCheck.Run(_asymRoot, phi, PhilFairness, nameof(Oracle_Liveness_EatsInfinitelyOften_Asym_Phil0));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_Liveness_EatsInfinitelyOften_Asym_Phil1()
        {
            var phi = LtlFormula.InfinitelyOften(Eating1);
            var r = LtlMultiBackendCrossCheck.Run(_asymRoot, phi, PhilFairness, nameof(Oracle_Liveness_EatsInfinitelyOften_Asym_Phil1));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_Liveness_EatsInfinitelyOften_Asym_Phil2()
        {
            var phi = LtlFormula.InfinitelyOften(Eating2);
            var r = LtlMultiBackendCrossCheck.Run(_asymRoot, phi, PhilFairness, nameof(Oracle_Liveness_EatsInfinitelyOften_Asym_Phil2));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        // --- Naive deadlock: eventual eating fails even under strong fairness

        [Test]
        public void Oracle_Liveness_Naive_EventualEating_FailsUnderStrongFairness()
        {
            var phi = LtlFormula.Eventually(SomeEating);
            var r = LtlMultiBackendCrossCheck.Run(_naiveRoot, phi, PhilFairness, nameof(Oracle_Liveness_Naive_EventualEating_FailsUnderStrongFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.False);
        }

        [Test]
        public void Oracle_Liveness_Naive_EventualEating_FailsWithoutFairness()
        {
            var phi = LtlFormula.Eventually(SomeEating);
            var r = LtlMultiBackendCrossCheck.Run(_naiveRoot, phi, Fairness.None, nameof(Oracle_Liveness_Naive_EventualEating_FailsWithoutFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.False);
        }
    }
}
