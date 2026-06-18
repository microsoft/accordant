namespace TerminationDetection
{
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// Bug-injection demonstration for EWD998 termination detection:
    /// replaces every <see cref="EWD998.ReceiveMessageStep"/> with
    /// <see cref="EWD998.BuggyReceiveMessageStep"/> (forgets to set the
    /// receiver's colour black). A token round that traverses the node
    /// after the receive but before the next send picks up no taint,
    /// so the leader can declare termination prematurely. The
    /// classical safety property
    /// <c>TerminationDetected → HasSystemTerminated</c> then fails.
    /// </summary>
    public class EWD998BugDemoTests
    {
        private StateGraphNode _root;

        [SetUp]
        public void Setup() =>
            _root = StateGraph.ExploreStateGraph(
                EWD998.AllStepsBuggy(),
                EWD998.InitialState(),
                stateConstraint: s =>
                {
                    var ss = (SystemState)s;
                    return ss.Nodes.All(n => n.Counter < 3 && n.Pending < 3) && ss.Token.Q < 3;
                });

        /// <summary>
        /// LTL safety: the leader should only ever detect termination
        /// when the system has actually terminated. The buggy
        /// pass-token step admits a state where the leader believes
        /// termination while a passive node still has a pending
        /// message (i.e. an in-flight wakeup).
        /// </summary>
        [Test]
        public void Bug_LtlDetectImpliesTerminated_Fails()
        {
            var detected = LtlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected");
            var terminated = LtlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated");
            var phi = LtlFormula.Always(LtlFormula.Implies(detected, terminated));
            var r = LtlCheck.Check(_root, phi);
            Assert.IsFalse(r.Valid, "Buggy pass-token should let the leader detect termination prematurely.");
            Assert.That(r.GetTraceString(), Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// RLTL forbidden-prefix form:
        /// <c>Σ* · (TerminationDetected ∧ ¬HasSystemTerminated)</c> must
        /// match in the buggy state graph.
        /// </summary>
        [Test]
        public void Bug_RltlForbiddenPrematureDetection_Matches()
        {
            var sigmaStar = Regex.Star(Regex.Sigma);
            var bad = Regex.Concat(sigmaStar,
                Regex.Intersect(
                    Regex.Prop(EWD998.TerminationDetected, "TerminationDetected"),
                    Regex.Prop(s => !EWD998.HasSystemTerminated(s), "¬HasSystemTerminated")));
            var phi = RltlFormula.Trigger(bad, RltlFormula.False);
            var r = RltlCheck.Check(_root, phi);
            Assert.IsFalse(r.Valid, "Bad prefix is reachable in the buggy model.");
            Assert.That(r.GetTraceString(), Is.Not.Null.And.Not.Empty);
        }
    }
}
