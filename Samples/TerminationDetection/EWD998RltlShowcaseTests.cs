namespace TerminationDetection
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// RLTL regex-flavoured showcase for EWD998: complements
    /// <see cref="EWD998RltlTests"/> with properties that lean on the
    /// regex DSL (intersection, complement, fusion, bounded counts).
    /// </summary>
    public class EWD998RltlShowcaseTests
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

        private static Regex SigmaStar => Regex.Star(Regex.Sigma);
        private static Regex RDetected => Regex.Prop(EWD998.TerminationDetected, "TerminationDetected");
        private static Regex RTerminated => Regex.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");

        /// <summary>
        /// Safety via forbidden-prefix: a run prefix that ends in a state
        /// where termination is <em>detected</em> without the system being
        /// terminated must never match. Phrased as
        /// <c>R = Σ* · (TerminationDetected ∧ ¬HasSystemTerminated)</c> and
        /// asserted via <c>Trigger(R, False)</c>. Equivalent in extension
        /// to the LTL safety <c>□(detected → terminated)</c>, but the
        /// regex form makes the bad witness explicit as a shape.
        /// </summary>
        [Test]
        public void Safety_NoFalsePositiveDetection_AsForbiddenPrefix()
        {
            var detectedButNotTerminated = Regex.Prop(
                s => EWD998.TerminationDetected(s) && !EWD998.HasSystemTerminated(s),
                "TerminationDetected ∧ ¬HasSystemTerminated");
            var bad = Regex.Concat(SigmaStar, detectedButNotTerminated);
            var phi = RltlFormula.Trigger(bad, RltlFormula.False);

            var result = RltlCheck.Check(_rootNode, phi);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Regex intersection: the prefix
        /// <c>(Σ* · TerminationDetected) ∩ (Σ* · HasSystemTerminated)</c>
        /// matches exactly the prefixes ending in a state where both
        /// atoms hold simultaneously. Asserting <c>Match(R, True)</c>
        /// just exercises the intersection machinery; the real content
        /// is the safety property <c>R ⊳⊳ HasSystemTerminated</c> — at
        /// every such position, the system is in fact terminated
        /// (trivially true on the intersection by construction).
        /// </summary>
        [Test]
        public void Intersection_DetectedAndTerminatedPrefix_SuffixIsTerminated()
        {
            var detectedPrefix = Regex.Concat(SigmaStar, RDetected);
            var terminatedPrefix = Regex.Concat(SigmaStar, RTerminated);
            var both = Regex.Intersect(detectedPrefix, terminatedPrefix);

            var terminated = RltlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");
            var phi = RltlFormula.Match(both, terminated);

            var result = RltlCheck.Check(_rootNode, phi);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }
    }
}
