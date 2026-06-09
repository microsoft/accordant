namespace TerminationDetection
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    /// <summary>
    /// Additional EWD998 properties: stability (absorbing) of termination
    /// and of termination-detection, structural well-formedness of the
    /// token, and a counter-non-negativity invariant.
    /// </summary>
    public class EWD998AdditionalLtlTests
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
            var initial = new SystemState { Nodes = nodes, Token = token };

            _root = StateGraph.ExploreStateGraph(steps, initial,
                stateConstraint: s =>
                {
                    var ss = (SystemState)s;
                    return ss.Nodes.All(n => n.Counter < 3 && n.Pending < 3) && ss.Token.Q < 3;
                });
        }

        // --- Atoms --------------------------------------------------------

        private static LtlFormula Detected =>
            LtlFormula.Prop(EWD998.TerminationDetected, "Detected");
        private static LtlFormula Terminated =>
            LtlFormula.Prop(EWD998.HasSystemTerminated, "Terminated");

        // --- Stability (absorbing-state) properties ----------------------

        /// <summary>
        /// Termination is absorbing: once every node is inactive and no
        /// messages are pending, no step can re-activate the system,
        /// so <c>HasSystemTerminated</c> stays true forever.
        /// <c>□(Terminated → □Terminated)</c>.
        /// </summary>
        [Test]
        public void Safety_TerminationIsAbsorbing()
        {
            var phi = LtlFormula.Always(LtlFormula.Implies(Terminated, LtlFormula.Always(Terminated)));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        /// <summary>
        /// Detection is absorbing: once the leader has detected
        /// termination, the system stays in that detected state.
        /// <c>□(Detected → □Detected)</c>. Stronger than the existing
        /// detection-implies-termination check — pins that no step can
        /// "un-detect" termination.
        /// </summary>
        [Test]
        public void Safety_DetectionIsAbsorbing()
        {
            var phi = LtlFormula.Always(LtlFormula.Implies(Detected, LtlFormula.Always(Detected)));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        // --- Structural invariants ---------------------------------------

        /// <summary>
        /// The token always lives at a valid node index.
        /// <c>□(0 ≤ token.NodeIndex &lt; N)</c>. Trivially true today;
        /// surfaces immediately if a future refactor of
        /// <see cref="EWD998.PassTokenStep"/> mis-handles the modular
        /// arithmetic at the leader boundary.
        /// </summary>
        [Test]
        public void Safety_TokenIndexInBounds()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(s =>
            {
                var ss = (SystemState)s;
                return ss.Token.NodeIndex >= 0 && ss.Token.NodeIndex < EWD998.N;
            }, "TokenIndexInBounds"));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        /// <summary>
        /// Per-node pending-message count is non-negative.
        /// <c>□(∀i. node[i].Pending ≥ 0)</c>. The Q counter on the token
        /// is allowed to go negative by design (it accumulates negative
        /// contributions from receives that happen *before* the
        /// corresponding send is observed by the token), so we only
        /// pin the per-node Pending field here.
        /// </summary>
        [Test]
        public void Safety_PendingNonNegative()
        {
            var phi = LtlFormula.Always(LtlFormula.Prop(s =>
            {
                var ss = (SystemState)s;
                return ss.Nodes.All(n => n.Pending >= 0);
            }, "PendingNonNegative"));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }

        // --- Conservation between detection and structural termination ---

        /// <summary>
        /// Stronger than the existing detection→termination check:
        /// detection requires not just "some terminated state has been
        /// reached" but specifically the strict structural conjunction
        /// (token at leader, token white, leader white &amp; inactive,
        /// Q+counter sum 0). This redundantly pins each conjunct.
        /// </summary>
        [Test]
        public void Safety_DetectionImpliesStructuralFingerprint()
        {
            var phi = LtlFormula.Always(LtlFormula.Implies(Detected,
                LtlFormula.Prop(s =>
                {
                    var ss = (SystemState)s;
                    return ss.Token.NodeIndex == 0
                        && ss.Token.Color == Color.White
                        && ss.Nodes[0].Color == Color.White
                        && !ss.Nodes[0].Active
                        && ss.Token.Q + ss.Nodes[0].Counter == 0;
                }, "DetectionFingerprint")));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsTrue(r.Valid, r.GetTraceString());
        }
    }
}
