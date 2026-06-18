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
    /// Cross-check harness: every LTL-expressible property tested in
    /// <see cref="EWD998LtlTests"/> is lifted to RLTL via
    /// <see cref="LtlToRltl.Lift"/> and re-checked.
    /// </summary>
    public class EWD998CrossCheckTests
    {
        private StateGraphNode _rootNode;

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
            var initial = new SystemState { Nodes = nodes, Token = token };

            _rootNode = StateGraph.ExploreStateGraph(
                steps,
                initial,
                stateConstraint: s =>
                {
                    var ss = (SystemState)s;
                    return ss.Nodes.All(n => n.Counter < 3 && n.Pending < 3) && ss.Token.Q < 3;
                });
        }

        private static LtlFormula Detected => LtlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected");
        private static LtlFormula Terminated => LtlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");

        [Test]
        public void Safety_DetectedImpliesTerminated_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _rootNode,
                LtlFormula.Always(LtlFormula.Implies(Detected, Terminated)),
                fairness: Fairness.None,
                label: nameof(Safety_DetectedImpliesTerminated_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Liveness_TerminatedLeadsToDetected_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _rootNode,
                LtlFormula.LeadsTo(Terminated, Detected),
                fairness: Fairness.WeakFairAll,
                label: nameof(Liveness_TerminatedLeadsToDetected_CrossCheck)
            ).ThrowIfDisagree();

        [Test]
        public void Combined_SafetyAndLiveness_CrossCheck() =>
            LtlRltlCrossCheck.Run(
                _rootNode,
                LtlFormula.Always(LtlFormula.Implies(Detected, Terminated))
                    & LtlFormula.LeadsTo(Terminated, Detected),
                fairness: Fairness.WeakFairAll,
                label: nameof(Combined_SafetyAndLiveness_CrossCheck)
            ).ThrowIfDisagree();

        /// <summary>
        /// Token-traversal under <see cref="Fairness.None"/>: should fail in
        /// both checkers because no per-node deactivation can be forced
        /// (see <see cref="EWD998LtlTests.InfinitelyOften_TokenReturnsToLeader_FailsWithoutPerNodeDeactivationFairness"/>).
        /// </summary>
        [Test]
        public void InfinitelyOften_TokenReturnsToLeader_FailsWithoutFairness_CrossCheck()
        {
            var tokenAtLeader = LtlFormula.Prop(
                s => ((SystemState)s).Token.NodeIndex == 0, "TokenAtLeader");
            LtlRltlCrossCheck.Run(
                _rootNode,
                LtlFormula.InfinitelyOften(tokenAtLeader),
                fairness: Fairness.None,
                label: nameof(InfinitelyOften_TokenReturnsToLeader_FailsWithoutFairness_CrossCheck)
            ).ThrowIfDisagree();
        }
    }
}
