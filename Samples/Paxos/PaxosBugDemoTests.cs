namespace Accordant.Samples.Paxos
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// Bug-injection demo for the Paxos model: replaces the
    /// <see cref="Paxos.Quorum"/>=2 threshold inside
    /// <see cref="Paxos.Phase1DoneStep"/> and
    /// <see cref="Paxos.Phase2DoneStep"/> with quorum=1, breaking the
    /// classical Paxos majority requirement. Each test asserts that the
    /// corresponding LTL/RLTL safety property reports a counterexample.
    /// </summary>
    public class PaxosBugDemoTests
    {
        private StateGraphNode _root;

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(
                Paxos.AllStepsBuggyQuorum(), Paxos.InitialState());

        /// <summary>
        /// LTL Agreement fails: with quorum=1, two proposers can
        /// independently get a single promise/accept and decide
        /// different values.
        /// </summary>
        [Test]
        public void Bug_Agreement_Fails()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(Paxos.Agreement, "Agreement"));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsFalse(r.Valid, "Buggy quorum should break Agreement.");
            Assert.That(r.GetTraceString(), Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// RLTL forbidden-prefix Agreement also fails — there is a
        /// reachable run with <c>Decided_0=1 · ... · Decided_1=2</c>.
        /// </summary>
        [Test]
        public void Bug_RltlForbiddenAgreement_Matches()
        {
            var sigmaStar = Regex.Star(Regex.Sigma);
            var bad = Regex.Concat(sigmaStar,
                        Regex.Concat(
                            Regex.Prop(Paxos.DecidedValue(0, 1), "Dec_0=1"),
                              Regex.Concat(sigmaStar,
                                Regex.Prop(Paxos.DecidedValue(1, 2), "Dec_1=2"))));
            var phi = RltlFormula.Trigger(bad, RltlFormula.False);
            var r = RltlCheck.Check(_root, phi);
            Assert.IsFalse(r.Valid, "Disagreement prefix should match in the buggy model.");
            Assert.That(r.GetTraceString(), Is.Not.Null.And.Not.Empty);
        }
    }
}

