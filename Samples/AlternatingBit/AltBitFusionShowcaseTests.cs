namespace AlternatingBit
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// Fusion showcase for the Alternating-Bit Protocol — exercises
    /// <see cref="Regex.Fusion"/> on real model states, both as a
    /// generic Example-7.1 sanity check and as a handshake-specific
    /// property where fusion's "shared boundary letter" semantics
    /// yields a strictly different language from naive concatenation.
    ///
    /// <para>
    /// History: this scenario was originally deferred as
    /// <c>alt-bit-fusion-handshake</c> because fusion's natural
    /// reading talks about a shared <em>transition</em>, and our regex
    /// alphabet is over <em>states</em>. The way through is to find a
    /// state predicate that fingerprints the moment immediately before
    /// the transition of interest (here: the "matching ack pending"
    /// state, which is the unique state that must hold one step before
    /// a successful <see cref="AltBit.ReceiveAckStep"/> flips the
    /// sender bit). With that predicate in hand, fusion glues the
    /// pre-transition prefix and the post-transition suffix at the
    /// shared handshake state.
    /// </para>
    /// </summary>
    public class AltBitFusionShowcaseTests
    {
        private StateGraphNode _root;

        private static readonly Fairness ChannelFairness = Fairness.StrongFair(sf =>
            sf is AltBit.SendStep || sf is AltBit.ReceiveStep || sf is AltBit.ReceiveAckStep);

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(AltBit.AllSteps(), AltBit.InitialState());

        // --- Predicates ---------------------------------------------------

        /// <summary>
        /// The "handshake-pending" state for sender bit 0: the sender
        /// currently holds bit 0 and a matching ack-0 is sitting in the
        /// R→S channel. Firing <see cref="AltBit.ReceiveAckStep"/> from
        /// here transitions to sender bit 1.
        /// </summary>
        private static bool HandshakePending0(IState s)
        {
            var st = (AltBitState)s;
            return st.SenderBit == 0 && st.AckChanHas && st.AckChanBit == 0;
        }

        private static bool SenderBit1Pred(IState s) => AltBit.SenderBit1(s);

        private static Regex HSPending0 => Regex.Prop(HandshakePending0, "HandshakePending0");
        private static Regex RSenderBit1 => Regex.Prop(SenderBit1Pred, "SenderBit1");
        private static Regex SigmaStar => Regex.Star(Regex.Sigma);

        // --- Sanity check: Example 7.1 on AltBit predicates --------------

        /// <summary>
        /// Verifies the JACM Example 7.1 identity
        /// <c>α* : β* ≡ α* · (α∧β) · β*</c> using AltBit-specific atoms:
        /// α = <see cref="AltBit.SenderBit0"/>, β =
        /// <see cref="AltBit.SenderBit1"/>. The intersection
        /// <c>α∧β</c> is unsatisfiable on AltBit states (the sender bit
        /// is exclusively 0 or 1), so both sides should accept the
        /// <em>empty</em> language on this model — proven by
        /// double-emptiness via RltlCheck's emptiness test.
        /// </summary>
        [Test]
        public void Fusion_Example7_1_Identity_Holds_For_AltBit_SenderBit_Atoms()
        {
            var alpha = Regex.Prop(AltBit.SenderBit0, "SenderBit0");
            var beta = Regex.Prop(AltBit.SenderBit1, "SenderBit1");

            var R = Regex.Fusion(Regex.Star(alpha), Regex.Star(beta));
            var S = Regex.Concat(Regex.Star(alpha),
                    Regex.Concat(Regex.Intersect(alpha, beta), Regex.Star(beta)));

            // R \ S empty (i.e., R ∩ ¬S empty) and S \ R empty.
            AssertRegexLanguageEmpty(Regex.Intersect(R, Regex.Complement(S)), "R \\ S");
            AssertRegexLanguageEmpty(Regex.Intersect(S, Regex.Complement(R)), "S \\ R");
        }

        // --- Handshake fusion: the originally-deferred scenario ----------

        /// <summary>
        /// Handshake property phrased with fusion:
        /// <c>(Σ* · HandshakePending0) : (HandshakePending0 · SenderBit1)</c>
        /// — a run prefix ending at a handshake-pending-0 state fused
        /// with a two-letter continuation that starts at the same
        /// handshake-pending-0 state and immediately transitions to a
        /// SenderBit=1 state. The boundary letter is shared (it must
        /// satisfy <see cref="HandshakePending0"/>).
        ///
        /// Under <see cref="ChannelFairness"/> some run must complete
        /// the handshake (sender bit must flip to 1), so the
        /// emptiness check on this fusion regex must fail — i.e., the
        /// language is <em>non-empty</em> on the reachable AltBit state
        /// graph.
        /// </summary>
        [Test]
        public void Fusion_Handshake_PrecedesSenderBitFlip_IsReachable()
        {
            var R = Regex.Concat(SigmaStar, HSPending0);
            var S = Regex.Concat(HSPending0, RSenderBit1);
            var fused = Regex.Fusion(R, S);

            // "There is a run with no prefix in L(fused)" — this should be
            // FALSE under channel fairness (handshake must complete).
            var noPrefix = !RltlFormula.SeqPrefix(fused, RltlFormula.True);

            var result = RltlCheck.Check(_root, noPrefix, fairness: ChannelFairness);
            Assert.That(result.Valid, Is.False,
                "Under channel fairness, every run must exhibit a handshake-pending-0 state " +
                "immediately followed by a SenderBit=1 state.");
        }

        /// <summary>
        /// Companion check: the naive concatenation
        /// <c>(Σ* · HandshakePending0) · (HandshakePending0 · SenderBit1)</c>
        /// — same shape but <em>without</em> fusing the boundary —
        /// requires <em>two consecutive</em> handshake-pending-0 states
        /// followed by SenderBit=1. In AltBit this can happen
        /// (a <see cref="AltBit.SendStep"/> firing while the ack is in
        /// flight leaves the ack pending and the sender bit unchanged,
        /// so HandshakePending0 persists), but it is strictly more
        /// restrictive than the fused version: the test merely confirms
        /// the two regexes are <em>not</em> language-equivalent on this
        /// model by exhibiting a run prefix that matches the fused form
        /// but not the concat form.
        /// </summary>
        [Test]
        public void Fusion_Differs_From_Concat_On_Single_Letter_Handshake()
        {
            var R = Regex.Concat(SigmaStar, HSPending0);
            var S = Regex.Concat(HSPending0, RSenderBit1);

            var fused = Regex.Fusion(R, S);
            var concat = Regex.Concat(R, S);

            // The fused language minus the concat language should be
            // non-empty: any prefix where HandshakePending0 is immediately
            // followed by SenderBit1 (with no second HandshakePending0 in
            // between) matches the fusion but not the concat.
            AssertRegexLanguageNonEmpty(
                Regex.Intersect(fused, Regex.Complement(concat)),
                "fused \\ concat");
        }

        // --- Helpers ------------------------------------------------------

        /// <summary>
        /// Asserts that <paramref name="r"/> accepts no prefix of any
        /// reachable AltBit run, via the RLTL emptiness encoding:
        /// L(r) ∩ reachable-prefixes = ∅ iff <c>¬(r ; True)</c> holds.
        /// </summary>
        private void AssertRegexLanguageEmpty(Regex r, string label)
        {
            var phi = !RltlFormula.SeqPrefix(r, RltlFormula.True);
            var result = RltlCheck.Check(_root, phi);
            Assert.That(result.Valid, Is.True,
                $"Expected {label} to be empty on the AltBit state graph. {result.GetTraceString()}");
        }

        private void AssertRegexLanguageNonEmpty(Regex r, string label)
        {
            var phi = !RltlFormula.SeqPrefix(r, RltlFormula.True);
            var result = RltlCheck.Check(_root, phi);
            Assert.That(result.Valid, Is.False,
                $"Expected {label} to be non-empty on the AltBit state graph.");
        }
    }
}
