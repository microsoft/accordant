namespace TerminationDetection
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Testing;
    using NUnit.Framework;

    /// <summary>
    /// Drives every LTL-expressible EWD998 property through the
    /// four-backend differential oracle under several fairness
    /// configurations. Any disagreement is a bug.
    /// </summary>
    public class EWD998OracleSweepTests
    {
        private StateGraphNode _root;

        [SetUp]
        public void Setup()
        {
            var steps = new List<IStepFunction>();
            steps.Add(new EWD998.InitiateProbeStep());
            for (int i = 0; i < EWD998.N; i++)
            {
                if (i != 0) steps.Add(new EWD998.PassTokenStep(i));
                steps.Add(new EWD998.SendMessageStep(i));
                steps.Add(new EWD998.ReceiveMessageStep(i));
                steps.Add(new EWD998.DeactivateStep(i));
            }
            var token = new TokenState { NodeIndex = 0, Q = 0, Color = Color.Black };
            var nodes = new List<NodeState>();
            for (int i = 0; i < EWD998.N; i++)
                nodes.Add(new NodeState { Active = true, Pending = 0, Color = Color.White, Counter = 0 });

            _root = StateGraph.ExploreStateGraph(
                steps,
                new SystemState { Nodes = nodes, Token = token },
                stateConstraint: (s) =>
                {
                    var st = (SystemState)s;
                    return st.Nodes.All(n => n.Counter < 3 && n.Pending < 3) && st.Token.Q < 3;
                });
        }

        private static LtlFormula Detected => LtlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected");
        private static LtlFormula Terminated => LtlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");
        private static LtlFormula TokenAtLeader =>
            LtlFormula.Prop(state => ((SystemState)state).Token.NodeIndex == 0, "TokenAtLeader");

        private static readonly Fairness PerNodeFairness = Fairness.StrongFair(sf =>
            sf is EWD998.DeactivateStep || sf is EWD998.PassTokenStep || sf is EWD998.InitiateProbeStep);

        // --- Safety ----------------------------------------------------

        [Test]
        public void Oracle_Safety_DetectedImpliesTerminated_NoFairness()
        {
            var phi = LtlFormula.Always(LtlFormula.Implies(Detected, Terminated));
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.None, nameof(Oracle_Safety_DetectedImpliesTerminated_NoFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_Safety_DetectedImpliesTerminated_WeakFairAll()
        {
            var phi = LtlFormula.Always(LtlFormula.Implies(Detected, Terminated));
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.WeakFairAll, nameof(Oracle_Safety_DetectedImpliesTerminated_WeakFairAll));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        // --- Liveness: terminated → detected --------------------------

        [Test]
        public void Oracle_Liveness_TerminatedLeadsToDetected_NoFairness()
        {
            var phi = LtlFormula.LeadsTo(Terminated, Detected);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.None, nameof(Oracle_Liveness_TerminatedLeadsToDetected_NoFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        // --- Eventually-terminated fails ------------------------------

        [Test]
        public void Oracle_Eventually_TerminatedOrDetected_FailsNoFairness()
        {
            var phi = LtlFormula.Eventually(Terminated | Detected);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.None, nameof(Oracle_Eventually_TerminatedOrDetected_FailsNoFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.False);
        }

        // --- Infinitely often token at leader (per-node fairness) ----

        [Test]
        public void Oracle_InfinitelyOften_TokenAtLeader_PerNodeFairness()
        {
            var phi = LtlFormula.InfinitelyOften(TokenAtLeader);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, PerNodeFairness, nameof(Oracle_InfinitelyOften_TokenAtLeader_PerNodeFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        [Test]
        public void Oracle_InfinitelyOften_TokenAtLeader_NoFairness_Fails()
        {
            var phi = LtlFormula.InfinitelyOften(TokenAtLeader);
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.None, nameof(Oracle_InfinitelyOften_TokenAtLeader_NoFairness_Fails));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.False);
        }

        // --- Until smoke test ----------------------------------------

        [Test]
        public void Oracle_Until_NotDetectedUntilTerminated_NoFairness()
        {
            var phi = LtlFormula.Always(LtlFormula.Implies(Detected, Terminated));
            var r = LtlMultiBackendCrossCheck.Run(_root, phi, Fairness.None, nameof(Oracle_Until_NotDetectedUntilTerminated_NoFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }

        // --- Trivially true smoke ------------------------------------

        [Test]
        public void Oracle_True_NoFairness()
        {
            var r = LtlMultiBackendCrossCheck.Run(_root, LtlFormula.True, Fairness.None, nameof(Oracle_True_NoFairness));
            r.ThrowIfDisagree();
            Assert.That(r.Verdicts[0].Result.Valid, Is.True);
        }
    }
}
