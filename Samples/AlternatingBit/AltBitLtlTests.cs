namespace AlternatingBit
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    /// <summary>
    /// LTL-only verification of the Alternating-Bit Protocol. The
    /// safety and liveness checks here are mirrored in
    /// <see cref="AltBitRltlTests"/> via the RLTL DSL — these tests
    /// establish the baseline using <c>LtlCheck</c>.
    /// </summary>
    public class AltBitLtlTests
    {
        private StateGraphNode _root;

        /// <summary>
        /// Strong fairness on the three "useful" steps —
        /// <see cref="AltBit.SendStep"/>, <see cref="AltBit.ReceiveStep"/>,
        /// <see cref="AltBit.ReceiveAckStep"/>. No fairness on the
        /// <c>Lose*</c> steps (an infinitely-lossy channel is permitted)
        /// and none on the <see cref="AltBit.StutterStep"/> absorbing
        /// self-loop.
        /// </summary>
        private static readonly Fairness ChannelFairness = Fairness.StrongFair(sf =>
            sf is AltBit.SendStep || sf is AltBit.ReceiveStep || sf is AltBit.ReceiveAckStep);

        [SetUp]
        public void Setup()
        {
            _root = StateGraph.ExploreStateGraph(AltBit.AllSteps(), AltBit.InitialState());
        }

        // --- LTL atoms ----------------------------------------------------

        private static LtlFormula InOrder => LtlFormula.Prop(AltBit.InOrder, "InOrder");
        private static LtlFormula AllDelivered => LtlFormula.Prop(AltBit.AllDelivered, "AllDelivered");

        // --- Safety -------------------------------------------------------

        /// <summary>
        /// In-order, no-duplicates delivery: <c>□ InOrder</c>. The receiver
        /// only ever exposes a prefix of the canonical payload sequence
        /// <c>[0, 1, …, MaxMessages-1]</c>. Holds regardless of channel
        /// behaviour — it is the core safety guarantee of ABP.
        /// </summary>
        [Test]
        public void Safety_InOrderDelivery()
        {
            var phi = LtlFormula.Always(InOrder);
            var result = LtlCheck.Check(_root, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- Liveness under strong fairness on the useful steps ----------

        /// <summary>
        /// Progress under strong fairness on send/receive/receive-ack:
        /// <c>◇ AllDelivered</c>. Weak fairness is <em>not</em> enough
        /// here — the useful steps are repeatedly disabled (Send is
        /// disabled while a message sits in the data channel, etc.) so
        /// they would not be forced. Strong fairness keys on
        /// "enabled infinitely often", which is the right condition.
        /// </summary>
        [Test]
        public void Liveness_EventualDelivery_UnderStrongFairness()
        {
            var phi = LtlFormula.Eventually(AllDelivered);
            var result = LtlCheck.Check(_root, phi, fairness: ChannelFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>
        /// Without fairness the same property fails: the channel may lose
        /// every message forever, so <c>AllDelivered</c> need never hold.
        /// This is also a regression test for the
        /// <c>LtlCheck.FindBadFairSubCycle</c> fix — under
        /// <see cref="Fairness.None"/> any cycle that never reaches
        /// <c>AllDelivered</c> is a valid counterexample.
        /// </summary>
        [Test]
        public void Liveness_EventualDelivery_FailsWithoutFairness()
        {
            var phi = LtlFormula.Eventually(AllDelivered);
            var result = LtlCheck.Check(_root, phi, fairness: Fairness.None);
            Assert.IsFalse(result.Valid,
                "Without fairness an infinite-loss cycle exists where the data " +
                "channel is repeatedly filled and emptied without a Receive — " +
                "AllDelivered is never reached.");
        }
    }
}
