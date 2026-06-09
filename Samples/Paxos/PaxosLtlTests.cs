namespace Accordant.Samples.Paxos
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    /// <summary>
    /// LTL properties for the bounded single-decree Paxos model.
    /// </summary>
    public class PaxosLtlTests
    {
        private StateGraphNode _root;

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(Paxos.AllSteps(), Paxos.InitialState());

        /// <summary>
        /// Agreement: at every reachable state, all decided proposers
        /// have agreed on the same value.
        /// </summary>
        [Test]
        public void Safety_Agreement()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(Paxos.Agreement, "Agreement"));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        /// <summary>Validity: any decided value was proposed by someone.</summary>
        [Test]
        public void Safety_Validity()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(Paxos.Validity, "Validity"));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        /// <summary>
        /// Decided is stable: once a proposer decides a value, it
        /// remains decided with the same value forever.
        /// </summary>
        [Test]
        public void Safety_DecidedStable()
        {
            for (int p = 0; p < Paxos.P; p++)
            {
                var v = Paxos.Preferred(p);
                // Note: a proposer can only ever decide its own preferred
                // value (it overwrites only when it adopts a higher-ballot
                // accepted value — but with 2 proposers and ballots 1<2,
                // proposer 1 may adopt proposer 0's value, hence we check
                // for each *possible* decided value separately.
                for (int candV = 1; candV <= Paxos.P; candV++)
                {
                    var dv = LtlFormula.Prop(Paxos.DecidedValue(p, candV),
                        $"Decided_{p}={candV}");
                    var phi = LtlFormula.Always(LtlFormula.Implies(
                        dv, LtlFormula.Always(dv)));
                    var r = LtlCheck.Check(_root, phi);
                    Assert.IsTrue(r.Valid, $"p={p}, v={candV}: {r.GetTraceString()}");
                }
            }
        }

        /// <summary>
        /// Liveness: under strong fairness on every protocol step, some
        /// proposer eventually decides. The schedule includes
        /// PrepareDeliver/AcceptDeliver/Phase1Done/Phase2Done for both
        /// proposers, so progress is guaranteed.
        /// </summary>
        [Test]
        public void Liveness_SomeoneDecides_UnderFullFairness()
        {
            var fair = Fairness.StrongFair(sf =>
                sf is Paxos.PrepareDeliverStep
                || sf is Paxos.AcceptDeliverStep
                || sf is Paxos.Phase1DoneStep
                || sf is Paxos.Phase2DoneStep);
            var phi = LtlFormula.Eventually(LtlFormula.Prop(Paxos.AnyDecided, "AnyDecided"));
            var r = LtlCheck.Check(_root, phi, fairness: fair);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        /// <summary>
        /// Without fairness, the Idle self-loop avoids any decision —
        /// liveness fails.
        /// </summary>
        [Test]
        public void Liveness_SomeoneDecides_NoFairness_Fails()
        {
            var phi = LtlFormula.Eventually(LtlFormula.Prop(Paxos.AnyDecided, "AnyDecided"));
            var r = LtlCheck.Check(_root, phi, fairness: Fairness.None);
            Assert.IsFalse(r.Valid, "Idle stutter at the initial state is a non-deciding cycle.");
        }
    }
}

