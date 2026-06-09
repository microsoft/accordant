namespace DiningPhilosophers
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    /// <summary>
    /// LTL-only verification of the three-philosopher dining table for
    /// both the naive pickup order (deadlocks) and the asymmetric order
    /// (deadlock-free). Companion RLTL versions live in
    /// <see cref="DiningRltlTests"/>.
    /// </summary>
    public class DiningLtlTests
    {
        private StateGraphNode _naiveRoot;
        private StateGraphNode _asymRoot;

        /// <summary>
        /// Strong fairness on every per-philosopher step. The
        /// <see cref="Dining.DeadlockStutterStep"/> is intentionally
        /// excluded: in the naive variant's deadlock state it is the only
        /// enabled step, and being "fair" to it would defeat the point of
        /// detecting starvation there.
        /// </summary>
        private static readonly Fairness PhilFairness = Fairness.StrongFair(sf => sf is Dining.PhilStep);

        [SetUp]
        public void Setup()
        {
            _naiveRoot = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: false), Dining.InitialState());
            _asymRoot = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: true), Dining.InitialState());
        }

        // --- LTL atoms ----------------------------------------------------

        private static LtlFormula Eating0 => LtlFormula.Prop(Dining.Eating0, "Eating0");
        private static LtlFormula Eating1 => LtlFormula.Prop(Dining.Eating1, "Eating1");
        private static LtlFormula Eating2 => LtlFormula.Prop(Dining.Eating2, "Eating2");
        private static LtlFormula Hungry0 => LtlFormula.Prop(Dining.Hungry0, "Hungry0");
        private static LtlFormula Hungry1 => LtlFormula.Prop(Dining.Hungry1, "Hungry1");
        private static LtlFormula Hungry2 => LtlFormula.Prop(Dining.Hungry2, "Hungry2");
        private static LtlFormula TwoEating => LtlFormula.Prop(Dining.TwoEating, "TwoEating");
        private static LtlFormula SomeEating => LtlFormula.Prop(Dining.SomeEating, "SomeEating");

        // --- Safety (holds in both variants) -----------------------------

        /// <summary>
        /// Mutual exclusion of adjacent philosophers: at most one
        /// philosopher is in <c>Eating</c> at any time. In a 3-ring every
        /// pair of philosophers shares a fork, so this strengthens to
        /// "no two are eating simultaneously".
        /// </summary>
        [Test]
        public void Safety_NoTwoEating_Naive()
        {
            var phi = LtlFormula.Always(LtlFormula.Not(TwoEating));
            var result = LtlCheck.Check(_naiveRoot, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        [Test]
        public void Safety_NoTwoEating_Asymmetric()
        {
            var phi = LtlFormula.Always(LtlFormula.Not(TwoEating));
            var result = LtlCheck.Check(_asymRoot, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- Liveness — asymmetric variant succeeds ----------------------

        /// <summary>
        /// In the asymmetric (Chandy/Misra-style) ordering every
        /// philosopher eats infinitely often, under strong fairness on
        /// the per-philosopher steps. Strong fairness is required: each
        /// pickup step is repeatedly disabled by competitors holding the
        /// relevant fork, so weak fairness ("eventually always enabled")
        /// would not force progress.
        /// </summary>
        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil0()
        {
            var phi = LtlFormula.InfinitelyOften(Eating0);
            var result = LtlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil1()
        {
            var phi = LtlFormula.InfinitelyOften(Eating1);
            var result = LtlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil2()
        {
            var phi = LtlFormula.InfinitelyOften(Eating2);
            var result = LtlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- Liveness — naive variant fails ------------------------------

        // --- Liveness — naive variant fails even under strong fairness ---

        /// <summary>
        /// In the naive ordering all three philosophers can simultaneously
        /// pick up their left fork and then wait forever for their right
        /// fork. The resulting deadlock state has only the
        /// <see cref="Dining.DeadlockStutterStep"/> self-loop, so
        /// <c>◇ SomeEating</c> fails. The counterexample survives <em>even
        /// under strong fairness</em>: in the deadlock SCC no
        /// <see cref="Dining.PhilStep"/> has an outgoing edge, so the
        /// strong-fair condition for <c>PhilStep</c>s is vacuously satisfied.
        /// </summary>
        [Test]
        public void Liveness_Naive_EventualEating_FailsUnderStrongFairness()
        {
            var phi = LtlFormula.Eventually(SomeEating);
            var result = LtlCheck.Check(_naiveRoot, phi, fairness: PhilFairness);
            Assert.IsFalse(result.Valid,
                "Naive pickup order admits the classic three-fork deadlock; " +
                "no philosopher ever reaches the Eating state on that path " +
                "and the deadlock cycle is vacuously fair for PhilStep.");
        }
    }
}
