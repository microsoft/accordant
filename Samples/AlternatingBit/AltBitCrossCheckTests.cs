namespace AlternatingBit
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Testing;
    using NUnit.Framework;

    /// <summary>
    /// Cross-check harness for the Alternating-Bit Protocol — every
    /// LTL-expressible property from <see cref="AltBitLtlTests"/> is
    /// lifted to RLTL via <see cref="LtlToRltl.Lift"/> and re-checked.
    /// </summary>
    public class AltBitCrossCheckTests
    {
        private StateGraphNode _root;

        private static readonly Fairness ChannelFairness = Fairness.StrongFair(sf =>
            sf is AltBit.SendStep || sf is AltBit.ReceiveStep || sf is AltBit.ReceiveAckStep);

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(AltBit.AllSteps(), AltBit.InitialState());

        private static LtlFormula InOrder => LtlFormula.Prop(AltBit.InOrder, "InOrder");
        private static LtlFormula AllDelivered => LtlFormula.Prop(AltBit.AllDelivered, "AllDelivered");

        [Test]
        public void Safety_InOrderDelivery_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _root,
                LtlFormula.Always(InOrder),
                fairness: Fairness.None,
                label: nameof(Safety_InOrderDelivery_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Liveness_EventualDelivery_UnderStrongFairness_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _root,
                LtlFormula.Eventually(AllDelivered),
                fairness: ChannelFairness,
                label: nameof(Liveness_EventualDelivery_UnderStrongFairness_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Liveness_EventualDelivery_FailsWithoutFairness_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _root,
                LtlFormula.Eventually(AllDelivered),
                fairness: Fairness.None,
                label: nameof(Liveness_EventualDelivery_FailsWithoutFairness_CrossCheck)
            ).ThrowIfDisagree();
    }
}
