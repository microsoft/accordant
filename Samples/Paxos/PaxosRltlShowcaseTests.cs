namespace Accordant.Samples.Paxos
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// RLTL showcase for single-decree Paxos: regex-shaped expressions of
    /// the Paxos safety invariants (Agreement, monotonicity of Decided).
    /// </summary>
    public class PaxosRltlShowcaseTests
    {
        private StateGraphNode _root;

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(Paxos.AllSteps(), Paxos.InitialState());

        private static Regex SigmaStar => Regex.Star(Regex.Sigma);
        private static Regex Decided(int p, int v) =>
            Regex.Prop(Paxos.DecidedValue(p, v), $"Dec_{p}={v}");

        /// <summary>
        /// Agreement as a forbidden prefix family: for every pair of
        /// distinct values <c>(v1, v2)</c> and proposers <c>(p1, p2)</c>,
        /// the prefix <c>Σ* · DecidedValue(p1, v1) · Σ* · DecidedValue(p2, v2)</c>
        /// never matches. Direct regex encoding of the LTL Agreement
        /// invariant.
        /// </summary>
        [Test]
        public void Agreement_AsForbiddenPrefixFamily()
        {
            for (int p1 = 0; p1 < Paxos.P; p1++)
            for (int p2 = 0; p2 < Paxos.P; p2++)
            {
                if (p1 == p2) continue;
                for (int v1 = 1; v1 <= Paxos.P; v1++)
                for (int v2 = 1; v2 <= Paxos.P; v2++)
                {
                    if (v1 == v2) continue;
                    var bad = Regex.Concat(SigmaStar,
                                Regex.Concat(Decided(p1, v1),
                                  Regex.Concat(SigmaStar, Decided(p2, v2))));
                    var phi = RltlFormula.Trigger(bad, RltlFormula.False);
                    var r = RltlCheck.Check(_root, phi);
                    Assert.IsTrue(r.Valid,
                        $"p1={p1},v1={v1},p2={p2},v2={v2}: {r.GetTraceString()}");
                }
            }
        }

        /// <summary>
        /// Monotonicity of decision: once a proposer decides with value
        /// <c>v</c>, no prefix ending in "decided with a different value"
        /// is reachable. Captures the LTL "Decided stable" property
        /// purely as a forbidden regex.
        /// </summary>
        [Test]
        public void DecidedStable_AsForbiddenPrefix()
        {
            for (int p = 0; p < Paxos.P; p++)
            for (int v1 = 1; v1 <= Paxos.P; v1++)
            for (int v2 = 1; v2 <= Paxos.P; v2++)
            {
                if (v1 == v2) continue;
                var bad = Regex.Concat(SigmaStar,
                            Regex.Concat(Decided(p, v1),
                              Regex.Concat(SigmaStar, Decided(p, v2))));
                var phi = RltlFormula.Trigger(bad, RltlFormula.False);
                var r = RltlCheck.Check(_root, phi);
                Assert.IsTrue(r.Valid, $"p={p},v1={v1},v2={v2}: {r.GetTraceString()}");
            }
        }

        /// <summary>
        /// Match-shape Agreement: at every prefix ending in "proposer 0
        /// has decided with value v", every future state where proposer 1
        /// is also decided must agree. Phrased as
        /// <c>(Σ* · Decided_0=v) ⊳⊳ □(Decided_1=v ∨ ¬Decided_1)</c>.
        /// </summary>
        [Test]
        public void Match_AgreementAcrossProposers()
        {
            for (int v = 1; v <= Paxos.P; v++)
            {
                var prefix = Regex.Concat(SigmaStar, Decided(0, v));
                var dec1Agree = RltlFormula.Prop(Paxos.DecidedValue(1, v), $"Dec_1={v}");
                var notDec1 = RltlFormula.Not(
                    RltlFormula.Prop(Paxos.Decided(1), "Dec_1"));
                var body = RltlFormula.Always(RltlFormula.Or(dec1Agree, notDec1));
                var phi = RltlFormula.Match(prefix, body);
                var r = RltlCheck.Check(_root, phi);
                Assert.IsTrue(r.Valid, $"v={v}: {r.GetTraceString()}");
            }
        }

        /// <summary>
        /// Intersection demo: the prefix language
        /// <c>(Σ* · Decided_0=v) ∩ (Σ* · Decided_1=v)</c> with the same
        /// value <c>v</c> is reachable (both proposers can decide on the
        /// same value when proposer 1 adopts proposer 0's value via the
        /// "highest accepted" rule). Asserted by Trigger-to-False failing.
        /// </summary>
        [Test]
        public void Intersection_BothDecideSameValue_IsReachable()
        {
            // Pick the proposer-0-preferred value as the most likely to
            // appear in both proposers' Decided sets.
            int v = Paxos.Preferred(0);
            var both = Regex.Intersect(
                Regex.Concat(SigmaStar, Decided(0, v)),
                Regex.Concat(SigmaStar, Decided(1, v)));
            var phi = RltlFormula.Trigger(both, RltlFormula.False);
            var r = RltlCheck.Check(_root, phi);
            Assert.IsFalse(r.Valid,
                "Both proposers should be able to decide the same value (P0's preference).");
        }
    }
}

