namespace DiningPhilosophers
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    /// <summary>
    /// Additional DP properties beyond mutex + per-philosopher liveness:
    /// fork conservation, deadlock-as-LTL (only on the naive variant),
    /// hunger-progress, and a no-self-deadlock check on the asymmetric
    /// variant under weak fairness.
    /// </summary>
    public class DiningAdditionalLtlTests
    {
        private StateGraphNode _naiveRoot;
        private StateGraphNode _asymRoot;

        private static readonly Fairness PhilFairness =
            Fairness.StrongFair(sf => sf is Dining.PhilStep);

        [SetUp]
        public void Setup()
        {
            _naiveRoot = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: false), Dining.InitialState());
            _asymRoot = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: true), Dining.InitialState());
        }

        // --- Atoms --------------------------------------------------------

        private static LtlFormula SomeEating => LtlFormula.Prop(Dining.SomeEating, "SomeEating");
        private static LtlFormula Hungry0 => LtlFormula.Prop(Dining.Hungry0, "Hungry0");
        private static LtlFormula Hungry1 => LtlFormula.Prop(Dining.Hungry1, "Hungry1");
        private static LtlFormula Hungry2 => LtlFormula.Prop(Dining.Hungry2, "Hungry2");
        private static LtlFormula Eating0 => LtlFormula.Prop(Dining.Eating0, "Eating0");
        private static LtlFormula Eating1 => LtlFormula.Prop(Dining.Eating1, "Eating1");
        private static LtlFormula Eating2 => LtlFormula.Prop(Dining.Eating2, "Eating2");

        // --- Fork conservation -------------------------------------------

        /// <summary>
        /// Every fork is owned by at most one philosopher, and the
        /// holder's PC must indicate it is actually holding a fork
        /// (HoldOne or Eating). Encodes the implicit data-structure
        /// invariant that the model is supposed to preserve.
        /// </summary>
        [Test]
        public void Safety_ForkConservation_Asymmetric()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(s =>
            {
                var d = (DiningState)s;
                return ForkValid(d.F0) && ForkValid(d.F1) && ForkValid(d.F2)
                    && ForkConsistent(d, d.F0) && ForkConsistent(d, d.F1) && ForkConsistent(d, d.F2);
            }, "ForkConservation"));

            var result = LtlCheck.Check(_asymRoot, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>Same invariant on the naive variant.</summary>
        [Test]
        public void Safety_ForkConservation_Naive()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(s =>
            {
                var d = (DiningState)s;
                return ForkValid(d.F0) && ForkValid(d.F1) && ForkValid(d.F2)
                    && ForkConsistent(d, d.F0) && ForkConsistent(d, d.F1) && ForkConsistent(d, d.F2);
            }, "ForkConservation"));

            var result = LtlCheck.Check(_naiveRoot, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        private static bool ForkValid(int owner) => owner >= -1 && owner <= 2;

        private static bool ForkConsistent(DiningState d, int owner)
        {
            if (owner == -1) return true;
            var pc = owner == 0 ? d.PC0 : owner == 1 ? d.PC1 : d.PC2;
            return pc == PhilPC.HoldOne || pc == PhilPC.Eating;
        }

        // --- Deadlock-as-LTL ---------------------------------------------

        /// <summary>
        /// On the asymmetric variant, somebody eats infinitely often.
        /// Pure LTL formulation of the no-deadlock property (which has
        /// previously only been observable via SCC probing).
        /// </summary>
        [Test]
        public void Liveness_SomebodyAlwaysEats_Asymmetric()
        {
            var phi = LtlFormula.InfinitelyOften(SomeEating);
            var result = LtlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>
        /// The naive variant deadlocks (all three philosophers stuck holding
        /// one fork each). Under strong fairness only on real philosopher
        /// steps, the deadlock cycle (stutter-step self-loop) is unfair
        /// to every philosopher's continuously-enabled second-fork
        /// pickup attempts... but the second-fork step is NOT continuously
        /// enabled in the deadlock state because the required fork is held
        /// by another philosopher. So <c>□◇SomeEating</c> must FAIL —
        /// the deadlock cycle satisfies vacuous strong fairness.
        /// </summary>
        [Test]
        public void Liveness_SomebodyAlwaysEats_Naive_FAILS_WithDeadlock()
        {
            var phi = LtlFormula.InfinitelyOften(SomeEating);
            var result = LtlCheck.Check(_naiveRoot, phi, fairness: PhilFairness);
            Assert.IsFalse(result.Valid,
                "The naive variant reaches a deadlock state with a stutter " +
                "self-loop; no philosopher eats from that point onward.");
        }

        // --- Hunger progress (one-shot leads-to) -------------------------

        /// <summary>
        /// On the asymmetric variant under strong fairness, every
        /// hungry philosopher eventually eats. Stronger than mere
        /// "somebody eats" — addresses individual progress.
        /// </summary>
        [Test]
        public void Liveness_EveryHungryPhilEats_Asymmetric()
        {
            var phi = LtlFormula.And(
                LtlFormula.LeadsTo(Hungry0, Eating0),
                LtlFormula.LeadsTo(Hungry1, Eating1),
                LtlFormula.LeadsTo(Hungry2, Eating2));
            var result = LtlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }
    }
}
