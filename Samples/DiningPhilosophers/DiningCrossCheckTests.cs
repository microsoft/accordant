namespace DiningPhilosophers
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Testing;
    using NUnit.Framework;

    /// <summary>
    /// Cross-check harness: every LTL-expressible property tested in
    /// <see cref="DiningLtlTests"/> is lifted to RLTL via
    /// <see cref="LtlToRltl.Lift"/> and re-checked.
    /// </summary>
    public class DiningCrossCheckTests
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

        private static LtlFormula Eating0 => LtlFormula.Prop(Dining.Eating0, "Eating0");
        private static LtlFormula Eating1 => LtlFormula.Prop(Dining.Eating1, "Eating1");
        private static LtlFormula Eating2 => LtlFormula.Prop(Dining.Eating2, "Eating2");
        private static LtlFormula TwoEating => LtlFormula.Prop(Dining.TwoEating, "TwoEating");
        private static LtlFormula SomeEating => LtlFormula.Prop(Dining.SomeEating, "SomeEating");

        [Test]
        public void Safety_NoTwoEating_Naive_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _naiveRoot,
                LtlFormula.Always(LtlFormula.Not(TwoEating)),
                fairness: Fairness.None,
                label: nameof(Safety_NoTwoEating_Naive_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Safety_NoTwoEating_Asymmetric_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _asymRoot,
                LtlFormula.Always(LtlFormula.Not(TwoEating)),
                fairness: Fairness.None,
                label: nameof(Safety_NoTwoEating_Asymmetric_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil0_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _asymRoot,
                LtlFormula.InfinitelyOften(Eating0),
                fairness: PhilFairness,
                label: nameof(Liveness_EatsInfinitelyOften_Asymmetric_Phil0_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil1_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _asymRoot,
                LtlFormula.InfinitelyOften(Eating1),
                fairness: PhilFairness,
                label: nameof(Liveness_EatsInfinitelyOften_Asymmetric_Phil1_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Liveness_EatsInfinitelyOften_Asymmetric_Phil2_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _asymRoot,
                LtlFormula.InfinitelyOften(Eating2),
                fairness: PhilFairness,
                label: nameof(Liveness_EatsInfinitelyOften_Asymmetric_Phil2_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Liveness_Naive_EventualEating_FailsUnderStrongFairness_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _naiveRoot,
                LtlFormula.Eventually(SomeEating),
                fairness: PhilFairness,
                label: nameof(Liveness_Naive_EventualEating_FailsUnderStrongFairness_CrossCheck)
            ).ThrowIfDisagree();
    }
}
