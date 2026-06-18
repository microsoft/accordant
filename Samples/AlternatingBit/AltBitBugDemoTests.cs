namespace AlternatingBit
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// Bug-injection demonstration for the Alternating-Bit protocol:
    /// replaces <see cref="AltBit.ReceiveStep"/> with
    /// <see cref="AltBit.BuggyReceiveStep"/> (drops the bit-match guard,
    /// so duplicates are re-delivered) and asserts that the LTL/RLTL
    /// in-order safety property reports a counterexample.
    /// </summary>
    public class AltBitBugDemoTests
    {
        private StateGraphNode _root;

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(
                AltBit.AllStepsBuggy(),
                AltBit.InitialState(),
                stateConstraint: s =>
                {
                    // The buggy receiver re-delivers indefinitely under
                    // ack loss, so bound the explored prefix.
                    var st = (AltBitState)s;
                    return st.Delivered.Length <= AltBit.MaxMessages + 1;
                });

        /// <summary>
        /// LTL <c>□ InOrder</c>: holds in the correct protocol; under
        /// the buggy receiver the first payload can be delivered twice,
        /// breaking the in-order prefix invariant.
        /// </summary>
        [Test]
        public void Bug_LtlInOrder_Fails_WithCounterexample()
        {
            var inOrder = LtlFormula.Prop(AltBit.InOrder, "InOrder");
            var phi = LtlFormula.Always(inOrder);
            var r = LtlCheck.Check(_root, phi);
            Assert.IsFalse(r.Valid, "Buggy receiver should violate in-order delivery.");
            Assert.That(r.GetTraceString(), Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// RLTL forbidden-prefix form: <c>Σ* · ¬InOrder</c> must be
        /// reachable in the buggy state graph.
        /// </summary>
        [Test]
        public void Bug_RltlForbiddenOutOfOrder_Matches()
        {
            var sigmaStar = Regex.Star(Regex.Sigma);
            var notInOrder = Regex.Prop(s => !AltBit.InOrder(s), "¬InOrder");
            var bad = Regex.Concat(sigmaStar, notInOrder);
            var phi = RltlFormula.Trigger(bad, RltlFormula.False);
            var r = RltlCheck.Check(_root, phi);
            Assert.IsFalse(r.Valid, "Bad prefix is reachable in the buggy model.");
            Assert.That(r.GetTraceString(), Is.Not.Null.And.Not.Empty);
        }
    }
}
