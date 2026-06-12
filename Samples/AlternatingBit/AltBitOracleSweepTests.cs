namespace AlternatingBit
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Testing;
    using NUnit.Framework;

    /// <summary>
    /// Drives every LTL-expressible AltBit property through the
    /// four-backend differential oracle
    /// (<see cref="LtlMultiBackendCrossCheck"/>) under multiple
    /// fairness configurations. Disagreement between backends is a
    /// bug in at least one of them.
    /// </summary>
    public class AltBitOracleSweepTests
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
        public void Oracle_Safety_InOrderDelivery_NoFairness()
        {
            var phi = LtlFormula.Always(InOrder);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.None, nameof(Oracle_Safety_InOrderDelivery_NoFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True, "InOrder safety should hold across all backends.");
        }

        [Test]
        public void Oracle_Safety_InOrderDelivery_WeakFairAll()
        {
            var phi = LtlFormula.Always(InOrder);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.WeakFairAll, nameof(Oracle_Safety_InOrderDelivery_WeakFairAll));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_Safety_InOrderDelivery_ChannelFairness()
        {
            var phi = LtlFormula.Always(InOrder);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, ChannelFairness, nameof(Oracle_Safety_InOrderDelivery_ChannelFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_Liveness_EventualDelivery_FailsWithoutFairness()
        {
            var phi = LtlFormula.Eventually(AllDelivered);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.None, nameof(Oracle_Liveness_EventualDelivery_FailsWithoutFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.False, "Should be falsified across all backends without fairness.");
        }

        [Test]
        public void Oracle_Liveness_EventualDelivery_HoldsUnderChannelFairness()
        {
            var phi = LtlFormula.Eventually(AllDelivered);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, ChannelFairness, nameof(Oracle_Liveness_EventualDelivery_HoldsUnderChannelFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }
    }
}
