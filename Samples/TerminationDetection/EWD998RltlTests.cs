namespace TerminationDetection
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end RLTL model-checking of the EWD998 termination-detection
    /// protocol. Parallels <see cref="EWD998LtlTests"/> but uses the
    /// <see cref="RltlFormula"/> DSL — which is a strict superset of
    /// <c>LtlFormula</c> because it also exposes regex-prefix operators
    /// (<c>; : ⊳ ⊳⊳</c>).
    ///
    /// <para>
    /// Liveness properties (leads-to, infinitely-often) only hold under
    /// fairness: without it, the protocol admits unfair infinite runs in
    /// which (for example) one node sends messages forever and the token
    /// never moves. Each test therefore passes the relevant
    /// <see cref="Fairness"/> constraint to
    /// <see cref="RltlCheck.Check(StateGraphNode, RltlFormula, int, Fairness)"/>.
    /// When fairness is supplied, the checker switches from the
    /// linear-space nested-DFS path to the SCC-based product checker
    /// (<c>SccProductCheck</c>), which can evaluate fairness on whole
    /// cycles.
    /// </para>
    /// </summary>
    public class EWD998RltlTests
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
            {
                nodes.Add(new NodeState
                {
                    Active = true, Pending = 0, Color = Color.White, Counter = 0
                });
            }
            var initialState = new SystemState { Nodes = nodes, Token = token };

            _rootNode = StateGraph.ExploreStateGraph(
                steps,
                initialState,
                stateConstraint: s =>
                {
                    var sys = (SystemState)s;
                    return sys.Nodes.All(n => n.Counter < 3 && n.Pending < 3) &&
                           sys.Token.Q < 3;
                });
        }

        #region Pure-LTL smoke tests (parity with EWD998LtlTests)

        /// <summary>
        /// Safety: □(TerminationDetected → HasSystemTerminated).
        /// "No false positives in termination detection."
        /// </summary>
        [Test]
        public void Safety_DetectionImpliesTermination()
        {
            var detected = RltlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected");
            var terminated = RltlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");

            var safety = RltlFormula.Always(RltlFormula.Implies(detected, terminated));

            var result = RltlCheck.Check(_rootNode, safety);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Liveness via leads-to: <c>HasSystemTerminated ~&gt; TerminationDetected</c>
        /// under weak fairness on all step functions (parity with
        /// <see cref="EWD998LtlTests"/>).
        /// </summary>
        [Test]
        public void Liveness_TerminationLeadsToDetection()
        {
            var detected = RltlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected");
            var terminated = RltlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");

            var liveness = RltlFormula.LeadsTo(terminated, detected);

            var result = RltlCheck.Check(_rootNode, liveness, maxDepth: 0,
                fairness: Fairness.WeakFairAll);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Combined safety ∧ liveness under weak fairness.
        /// </summary>
        [Test]
        public void Combined_SafetyAndLiveness_Via_AndOperator()
        {
            var detected = RltlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected");
            var terminated = RltlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");

            var safety = RltlFormula.Always(RltlFormula.Implies(detected, terminated));
            var liveness = RltlFormula.LeadsTo(terminated, detected);

            var combined = safety & liveness;
            var result = RltlCheck.Check(_rootNode, combined, maxDepth: 0,
                fairness: Fairness.WeakFairAll);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// After splitting <see cref="EWD998.DeactivateStep"/> into one
        /// instance per node, the spurious no-op self-loop is gone: at
        /// any terminated state the only enabled transitions force the
        /// token back to the leader and an <see cref="EWD998.InitiateProbeStep"/>
        /// eventually fires. As a result the leads-to property holds for
        /// this encoding <em>even without fairness</em> — documented
        /// here as the dual of the genuine liveness check above.
        /// </summary>
        [Test]
        public void Liveness_TerminationLeadsToDetection_HoldsEvenWithoutFairness()
        {
            var detected = RltlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected");
            var terminated = RltlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");

            var liveness = RltlFormula.LeadsTo(terminated, detected);

            var result = RltlCheck.Check(_rootNode, liveness, fairness: Fairness.None);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        #endregion

        #region Regex-shaped property (genuinely RLTL)

        /// <summary>
        /// A genuinely regex-shaped obligation:
        ///   <c>(Σ* · TerminationDetected) ⊳ HasSystemTerminated</c>
        ///
        /// "At every position k that is reached after some prefix ending in
        /// <c>TerminationDetected</c>, the system must actually have
        /// terminated (i.e. <c>HasSystemTerminated</c> holds at k)."
        ///
        /// This is equivalent to the safety property
        /// <c>□(TerminationDetected → HasSystemTerminated)</c>, but expressed
        /// in regex-prefix form. It exercises the full pipeline (ERE
        /// derivative, NBW construction over <c>Rltl</c>, nested DFS) on a
        /// real model program.
        /// </summary>
        [Test]
        public void Regex_PrefixEndingInDetection_ImpliesTermination()
        {
            var detectedRgx = Regex.Prop(EWD998.TerminationDetected, "TerminationDetected");
            var prefix = Regex.Sigma.Then(detectedRgx);

            var terminated = RltlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");

            // For every prefix matching Σ*·detected, the suffix must satisfy
            // (¬detected ∨ terminated) — equivalently, terminated holds at
            // the position immediately after a detected-letter.
            //
            // Σ* requires every letter to satisfy ⊤ — always trivially true.
            // The concatenation forces the LAST letter of the match to be
            // one where TerminationDetected holds. Trigger then requires
            // that at the suffix immediately starting at that position, the
            // remainder satisfies HasSystemTerminated.
            //
            // Note: in (R ; / ⊳ φ), the match consumes letters [0..k) and
            // the suffix starts at k. So we require terminated to hold at
            // position k — i.e., the state immediately following a detected
            // state. To express "detected → terminated AT the same state"
            // we use the OVERLAPPING variant ⊳⊳ instead, where the match
            // consumes [0..k+1) and the suffix starts at k.
            var rgxOverlap = Regex.Sigma.Then(detectedRgx);
            var phi = RltlFormula.Match(rgxOverlap, terminated);

            var result = RltlCheck.Check(_rootNode, phi);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Token-traversal property: <c>□◇ tokenAtLeader</c> — passing
        /// counterpart of
        /// <see cref="InfinitelyOften_TokenReturnsToLeader_FailsWithoutPerNodeDeactivationFairness"/>.
        /// Strong fairness on each per-node
        /// <see cref="EWD998.DeactivateStep"/>, <see cref="EWD998.PassTokenStep"/>
        /// and <see cref="EWD998.InitiateProbeStep"/> forces token migration
        /// back to the leader.
        /// </summary>
        [Test]
        public void InfinitelyOften_TokenReturnsToLeader_UnderPerNodeFairness()
        {
            var tokenAtLeader = RltlFormula.Prop(
                s => ((SystemState)s).Token.NodeIndex == 0,
                "TokenAtLeader");

            var formula = RltlFormula.InfinitelyOften(tokenAtLeader);

            var fairness = Fairness.StrongFair(sf =>
                sf is EWD998.DeactivateStep ||
                sf is EWD998.PassTokenStep ||
                sf is EWD998.InitiateProbeStep);

            var result = RltlCheck.Check(_rootNode, formula, maxDepth: 0, fairness: fairness);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Token-traversal property: <c>□◇ tokenAtLeader</c>. RLTL
        /// counterpart of
        /// <see cref="EWD998LtlTests.InfinitelyOften_TokenReturnsToLeader_FailsWithoutPerNodeDeactivationFairness"/>.
        /// Asserted as a counterexample under
        /// <see cref="Fairness.None"/> to exercise the RLTL <c>InfinitelyOften</c>
        /// combinator on a real <c>□◇</c> obligation.
        /// </summary>
        [Test]
        public void InfinitelyOften_TokenReturnsToLeader_FailsWithoutPerNodeDeactivationFairness()
        {
            var tokenAtLeader = RltlFormula.Prop(
                s => ((SystemState)s).Token.NodeIndex == 0,
                "TokenAtLeader");

            var formula = RltlFormula.InfinitelyOften(tokenAtLeader);

            var result = RltlCheck.Check(_rootNode, formula, maxDepth: 0,
                fairness: Fairness.None);
            Assert.That(result.Valid, Is.False,
                "Without per-node deactivation fairness an active node can park the token away from the leader forever.");
        }

        #endregion
    }
}
