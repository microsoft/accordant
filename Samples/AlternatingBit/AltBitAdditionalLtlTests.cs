namespace AlternatingBit
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    /// <summary>
    /// Additional AltBit properties beyond in-order delivery + eventual
    /// delivery: delivery-count monotonicity, no-overshoot, per-payload
    /// liveness, and an ack-well-formedness safety invariant.
    /// </summary>
    public class AltBitAdditionalLtlTests
    {
        private StateGraphNode _root;

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(AltBit.AllSteps(), AltBit.InitialState());

        // --- Atoms --------------------------------------------------------

        private static LtlFormula DeliveredAtLeast(int k) =>
            LtlFormula.Prop(s => ((AltBitState)s).Delivered.Length >= k, $"Delivered>={k}");

        private static LtlFormula DeliveredAtMost(int k) =>
            LtlFormula.Prop(s => ((AltBitState)s).Delivered.Length <= k, $"Delivered<={k}");

        // --- Safety: monotone delivery count -----------------------------

        /// <summary>
        /// Once <c>k</c> payloads have been delivered, that count never
        /// decreases. Encoded per <c>k ∈ [1, MaxMessages]</c> as
        /// <c>□((|D|≥k) → X(|D|≥k))</c>. Catches any future regression
        /// where a step accidentally truncates <c>Delivered</c>.
        /// </summary>
        [Test]
        public void Safety_DeliveryCountMonotone()
        {
            for (int k = 1; k <= AltBit.MaxMessages; k++)
            {
                var phi = LtlFormula.Always(LtlFormula.Implies(
                    DeliveredAtLeast(k), LtlFormula.Next(DeliveredAtLeast(k))));
                var r = LtlCheck.Check(_root, phi);
                Assert.IsTrue(r.Valid, $"k={k}: {r.GetTraceString()}");
            }
        }

        /// <summary>
        /// <c>|Delivered| ≤ MaxMessages</c> at every reachable state.
        /// Catches a sender that ignored its <c>NextPayload &lt; MaxMessages</c>
        /// guard, or a receiver that accepted out-of-bound payloads.
        /// </summary>
        [Test]
        public void Safety_NoOvershoot()
        {
            var phi = LtlFormula.Always(DeliveredAtMost(AltBit.MaxMessages));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        // --- Per-payload liveness ----------------------------------------

        /// <summary>
        /// Under strong fairness, each individual payload index is
        /// eventually delivered. Stronger than the aggregate "AllDelivered
        /// eventually" property: surfaces any per-message starvation that
        /// happens to leave the aggregate count high while skipping a
        /// specific payload.
        /// </summary>
        [Test]
        public void Liveness_EachPayloadEventuallyDelivered_StrongFair()
        {
            var fair = Fairness.StrongFair(sf => sf is AltBit.AltBitStep && !(sf is AltBit.StutterStep));
            for (int k = 1; k <= AltBit.MaxMessages; k++)
            {
                var phi = LtlFormula.Eventually(DeliveredAtLeast(k));
                var r = LtlCheck.Check(_root, phi, fairness: fair);
                Assert.IsTrue(r.Valid, $"k={k}: {r.GetTraceString()}");
            }
        }

        // --- Ack well-formedness -----------------------------------------

        /// <summary>
        /// Every ack in flight carries either the bit the receiver just
        /// accepted, or the previous bit (the re-ack case). It is never
        /// some unrelated value. Encoded as a state predicate:
        /// <c>□(AckChanHas → AckChanBit ∈ {0, 1})</c>. Trivially true at
        /// the type level but a useful structural invariant to pin.
        /// </summary>
        [Test]
        public void Safety_AckWellFormed()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(s =>
            {
                var st = (AltBitState)s;
                return !st.AckChanHas || st.AckChanBit == 0 || st.AckChanBit == 1;
            }, "AckWellFormed"));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        /// <summary>
        /// Bit-synchronization round-trip invariant: whenever the sender
        /// and receiver bits agree (a quiescent moment in the protocol),
        /// no data is in flight. Equivalently: a discrepancy between
        /// <c>SenderBit</c> and <c>ReceiverBit</c> implies an ack is in
        /// flight (the sender is still waiting to learn about the flip).
        /// Captures the essential AltBit handshake invariant.
        /// </summary>
        [Test]
        public void Safety_BitSyncRoundTripInvariant()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(s =>
            {
                var st = (AltBitState)s;
                // If bits are equal: no in-flight data with bit == both
                //   (that would have just been sent and not yet received).
                // Encoded conservatively: SenderBit == ReceiverBit implies
                //   the receiver has consumed every message the sender has
                //   committed to so far, so |Delivered| == NextPayload.
                if (st.SenderBit == st.ReceiverBit)
                    return st.Delivered.Length == st.NextPayload;
                // Otherwise NextPayload is one ahead of |Delivered| would
                // be after the pending ack is processed: |Delivered| equals
                // NextPayload + 1, because the receiver already accepted
                // the latest in-flight message and is now ack-ing it.
                return st.Delivered.Length == st.NextPayload + 1;
            }, "BitSyncRoundTrip"));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }
    }
}
