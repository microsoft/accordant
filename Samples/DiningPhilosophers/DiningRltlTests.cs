namespace DiningPhilosophers
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// RLTL counterparts of <see cref="DiningLtlTests"/>. The safety and
    /// liveness properties translate directly; both checkers should agree
    /// for these LTL-expressible formulas.
    /// </summary>
    public class DiningRltlTests
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

        // --- RLTL atoms ---------------------------------------------------

        private static RltlFormula Eating0 => RltlFormula.Prop(Dining.Eating0, "Eating0");
        private static RltlFormula Eating1 => RltlFormula.Prop(Dining.Eating1, "Eating1");
        private static RltlFormula Eating2 => RltlFormula.Prop(Dining.Eating2, "Eating2");
        private static RltlFormula Hungry0 => RltlFormula.Prop(Dining.Hungry0, "Hungry0");
        private static RltlFormula Hungry1 => RltlFormula.Prop(Dining.Hungry1, "Hungry1");
        private static RltlFormula Hungry2 => RltlFormula.Prop(Dining.Hungry2, "Hungry2");
        private static RltlFormula TwoEating => RltlFormula.Prop(Dining.TwoEating, "TwoEating");
        private static RltlFormula SomeEating => RltlFormula.Prop(Dining.SomeEating, "SomeEating");

        // --- Safety ------------------------------------------------------

        [Test]
        public void Safety_NoTwoEating_Naive()
        {
            var phi = RltlFormula.Always(RltlFormula.Not(TwoEating));
            var result = RltlCheck.Check(_naiveRoot, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        [Test]
        public void Safety_NoTwoEating_Asymmetric()
        {
            var phi = RltlFormula.Always(RltlFormula.Not(TwoEating));
            var result = RltlCheck.Check(_asymRoot, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- Liveness — asymmetric variant succeeds ----------------------

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil0()
        {
            var phi = RltlFormula.InfinitelyOften(Eating0);
            var result = RltlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil1()
        {
            var phi = RltlFormula.InfinitelyOften(Eating1);
            var result = RltlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil2()
        {
            var phi = RltlFormula.InfinitelyOften(Eating2);
            var result = RltlCheck.Check(_asymRoot, phi, fairness: PhilFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- Liveness — naive variant fails ------------------------------

        [Test]
        public void Liveness_Naive_EventualEating_FailsUnderStrongFairness()
        {
            var phi = RltlFormula.Eventually(SomeEating);
            var result = RltlCheck.Check(_naiveRoot, phi, fairness: PhilFairness);
            Assert.IsFalse(result.Valid,
                "Naive pickup order deadlocks before anyone reaches the Eating state; " +
                "the deadlock cycle is vacuously fair for PhilStep.");
        }
    }
}
