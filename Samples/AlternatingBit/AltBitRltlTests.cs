namespace AlternatingBit
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end RLTL model-checking of the Alternating-Bit Protocol.
    /// Parallels <see cref="AltBitLtlTests"/> for the LTL-expressible
    /// properties and adds a genuinely regex-shaped property over the
    /// sender-bit phase sequence.
    /// </summary>
    public class AltBitRltlTests
    {
        private StateGraphNode _root;

        private static readonly Fairness ChannelFairness = Fairness.StrongFair(sf =>
            sf is AltBit.SendStep || sf is AltBit.ReceiveStep || sf is AltBit.ReceiveAckStep);

        [SetUp]
        public void Setup()
        {
            _root = StateGraph.ExploreStateGraph(AltBit.AllSteps(), AltBit.InitialState());
        }

        // --- RLTL atoms ---------------------------------------------------

        private static RltlFormula InOrder => RltlFormula.Prop(AltBit.InOrder, "InOrder");
        private static RltlFormula AllDelivered => RltlFormula.Prop(AltBit.AllDelivered, "AllDelivered");

        // --- Safety (parity with LTL) -------------------------------------

        /// <summary>In-order delivery: <c>□ InOrder</c>, expressed in RLTL.</summary>
        [Test]
        public void Safety_InOrderDelivery()
        {
            var phi = RltlFormula.Always(InOrder);
            var result = RltlCheck.Check(_root, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- Liveness (parity with LTL) -----------------------------------

        /// <summary>Eventual delivery under strong fairness on the useful steps.</summary>
        [Test]
        public void Liveness_EventualDelivery_UnderStrongFairness()
        {
            var phi = RltlFormula.Eventually(AllDelivered);
            var result = RltlCheck.Check(_root, phi, fairness: ChannelFairness);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>Same property fails under <see cref="Fairness.None"/>.</summary>
        [Test]
        public void Liveness_EventualDelivery_FailsWithoutFairness()
        {
            var phi = RltlFormula.Eventually(AllDelivered);
            var result = RltlCheck.Check(_root, phi, fairness: Fairness.None);
            Assert.IsFalse(result.Valid,
                "Without fairness an infinite-loss cycle keeps the protocol " +
                "from making any progress.");
        }

        // --- Genuinely regex-shaped property ------------------------------

        /// <summary>
        /// Sender-bit alternation. With <see cref="AltBit.MaxMessages"/>
        /// = 3 the sender goes through exactly four bit-phases —
        /// <c>0, 1, 0, 1</c> — and never returns to bit 0 a third time.
        /// The forbidden trace shape is therefore
        /// <code>
        ///   Σ* · SB=0 · Σ* · SB=1 · Σ* · SB=0 · Σ* · SB=1 · Σ* · SB=0
        /// </code>
        /// — five alternating sightings of the sender bit, which would
        /// require a third bit-0 phase. This property is expressible in
        /// LTL only by deeply nested Until/Next chains; in RLTL the ERE
        /// captures the phase pattern directly. Forbidden via the
        /// universal-regex prefix operator <c>R ⊳ False</c>.
        /// </summary>
        [Test]
        public void Regex_SenderBit_HasAtMostFourPhases()
        {
            var sigmaStar = Regex.Star(Regex.Sigma);
            var sb0 = Regex.Prop(AltBit.SenderBit0, "SB=0");
            var sb1 = Regex.Prop(AltBit.SenderBit1, "SB=1");

            // Σ* · SB=0 · Σ* · SB=1 · Σ* · SB=0 · Σ* · SB=1 · Σ* · SB=0
            var bad = Regex.Concat(sigmaStar,
                      Regex.Concat(sb0,
                      Regex.Concat(sigmaStar,
                      Regex.Concat(sb1,
                      Regex.Concat(sigmaStar,
                      Regex.Concat(sb0,
                      Regex.Concat(sigmaStar,
                      Regex.Concat(sb1,
                      Regex.Concat(sigmaStar, sb0))))))))); // 5th SB=0 sighting

            var phi = RltlFormula.Trigger(bad, RltlFormula.False);

            var result = RltlCheck.Check(_root, phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }
    }
}
