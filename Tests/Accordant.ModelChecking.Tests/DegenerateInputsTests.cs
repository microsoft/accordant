namespace Accordant.ModelChecking.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Testing;
    using NUnit.Framework;

    /// <summary>
    /// Cross-backend coverage on degenerate inputs.
    ///
    /// <para>
    /// Every backend (explicit <see cref="LtlCheck"/>, symbolic LTL via
    /// Tarjan SCC, symbolic LTL via nested DFS, and RLTL) must agree on
    /// the verdict for these scenarios. The four backends share almost
    /// no implementation so unanimous agreement here is strong evidence
    /// against regressions in any one path.
    /// </para>
    ///
    /// <para>
    /// Scenarios covered:
    /// <list type="bullet">
    ///   <item>Deadlocked (no outgoing edges) system node — exercises
    ///   the implicit stutter-at-terminal convention.</item>
    ///   <item>Single-node self-loop — degenerate but non-terminal.</item>
    ///   <item>Two-node chain ending in deadlock.</item>
    ///   <item>Tautology (<c>true</c>) and contradiction (<c>false</c>)
    ///   LTL constants.</item>
    /// </list>
    /// </para>
    /// </summary>
    [TestFixture]
    public class DegenerateInputsTests
    {
        #region Test infrastructure

        private sealed class TestState : State
        {
            public string Label { get; }
            public int Value { get; }

            public TestState(string label, int value)
            {
                Label = label;
                Value = value;
            }

            protected override void CloneInternal(Dictionary<object, object> map)
                => map[this] = new TestState(Label, Value);

            protected override void LockComponents(HashSet<object> visited) { }

            protected override string StringRepresentationInternal(Dictionary<object, string> paths, string path, bool forceRecompute)
                => $"{Label}({Value})";
            protected override void FreezeComponents(HashSet<object> visited) { }
        }

        private sealed class NoopStep : IStepFunction
        {
            private readonly string _id;
            public NoopStep(string id) { _id = id; }
            public string StepFunctionId => _id;
            public IList<StepResult> Apply(IState s, IReadOnlyList<(IStepFunction, StateGraphNode)> p)
                => null;
        }

        private static StateGraphNode MakeNode(string label, int value)
        {
            var st = new TestState(label, value);
            st.Freeze();
            return new StateGraphNode
            {
                State = st,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>()
            };
        }

        private static void AddEdge(StateGraphNode from, StateGraphNode to, string stepId = "step")
        {
            from.Edges.Add(new StateGraphEdge { Target = to, StepFunction = new NoopStep(stepId) });
        }

        private static void AssertAllAgree(MultiBackendCrossCheckResult r, bool expectedValid)
        {
            r.ThrowIfDisagree();
            foreach (var v in r.Verdicts.Where(x => !x.Skipped))
                Assert.That(v.Result.Valid, Is.EqualTo(expectedValid),
                    $"Backend {v.BackendName} disagrees on '{r.Label}'.");
        }

        #endregion

        #region Deadlock (no outgoing edges)

        /// <summary>
        /// Deadlock node + <c>G true</c>: trivially valid on every backend.
        /// Smoke test that no backend chokes on a terminal node.
        /// </summary>
        [Test]
        public void Deadlock_GTrue_ValidEverywhere()
        {
            var s0 = MakeNode("s0", 0);
            var phi = LtlFormula.Always(LtlFormula.True);
            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, phi, label: "deadlock G true"),
                expectedValid: true);
        }

        /// <summary>
        /// Deadlock node where <c>p</c> holds + <c>G p</c>: under the
        /// stutter convention, the implicit self-loop preserves <c>p</c>,
        /// so <c>G p</c> holds. Pins the explicit-LTL stutter fix.
        /// </summary>
        [Test]
        public void Deadlock_GP_PHolds_ValidEverywhere()
        {
            var s0 = MakeNode("s0", 1);
            var phi = LtlFormula.Always(LtlFormula.Prop(s => ((TestState)s).Value == 1, "p"));
            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, phi, label: "deadlock G p (p holds)"),
                expectedValid: true);
        }

        /// <summary>
        /// Deadlock node where <c>p</c> never holds + <c>F p</c>: under
        /// the stutter convention, the infinite stutter never reaches
        /// <c>p</c>, so <c>F p</c> fails everywhere. Without the
        /// explicit-backend stutter fix this would silently report Valid
        /// (no outgoing edges → no product transitions → no SCC), so
        /// this test pins the cross-backend agreement.
        /// </summary>
        [Test]
        public void Deadlock_FP_PNeverHolds_InvalidEverywhere()
        {
            var s0 = MakeNode("s0", 0);
            var phi = LtlFormula.Eventually(LtlFormula.Prop(s => ((TestState)s).Value == 99, "goal"));
            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, phi, fairness: Fairness.None,
                    label: "deadlock F goal (goal absent)"),
                expectedValid: false);
        }

        /// <summary>
        /// Two-node chain s0 → s1 where s1 is a deadlock with <c>p</c>:
        /// every run eventually reaches s1 and then stutters there;
        /// <c>F p</c> holds.
        /// </summary>
        [Test]
        public void TwoNodeChain_EndingInDeadlock_FP_ValidEverywhere()
        {
            var s0 = MakeNode("s0", 0);
            var s1 = MakeNode("s1", 1);
            AddEdge(s0, s1, "advance");

            var phi = LtlFormula.Eventually(LtlFormula.Prop(s => ((TestState)s).Value == 1, "p"));
            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, phi, fairness: Fairness.None,
                    label: "chain to deadlock, F p"),
                expectedValid: true);
        }

        #endregion

        #region Single-node self-loop

        /// <summary>
        /// Single-node self-loop where <c>p</c> never holds + <c>F p</c>:
        /// the loop is the obvious counterexample.
        /// </summary>
        [Test]
        public void SelfLoop_FP_PNeverHolds_InvalidEverywhere()
        {
            var s0 = MakeNode("s0", 0);
            AddEdge(s0, s0, "loop");

            var phi = LtlFormula.Eventually(LtlFormula.Prop(s => ((TestState)s).Value == 99, "p"));
            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, phi, fairness: Fairness.None,
                    label: "self-loop F p"),
                expectedValid: false);
        }

        /// <summary>
        /// Single-node self-loop where <c>p</c> holds + <c>G p</c>:
        /// trivially valid.
        /// </summary>
        [Test]
        public void SelfLoop_GP_PAlwaysHolds_ValidEverywhere()
        {
            var s0 = MakeNode("s0", 1);
            AddEdge(s0, s0, "loop");

            var phi = LtlFormula.Always(LtlFormula.Prop(s => ((TestState)s).Value == 1, "p"));
            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, phi, fairness: Fairness.None,
                    label: "self-loop G p"),
                expectedValid: true);
        }

        #endregion

        #region Constant LTL formulas

        /// <summary>
        /// Constant <c>true</c> against a non-trivial system: every backend
        /// returns Valid. Exercises the no-accepting-states NBW path (the
        /// NBW for <c>¬true = false</c> has empty language, so no NBW
        /// transition produces any counterexample).
        /// </summary>
        [Test]
        public void ConstantTrue_AnySystem_ValidEverywhere()
        {
            var s0 = MakeNode("s0", 0);
            var s1 = MakeNode("s1", 1);
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, LtlFormula.True, fairness: Fairness.None,
                    label: "constant true on 2-cycle"),
                expectedValid: true);
        }

        /// <summary>
        /// Constant <c>false</c> against a non-trivial system: every
        /// backend must report Invalid (the formula admits no satisfying
        /// run). Pins the early-out path in <see cref="LtlCheck.Check"/>
        /// against the symbolic NBW-empty-language path.
        /// </summary>
        [Test]
        public void ConstantFalse_AnySystem_InvalidEverywhere()
        {
            var s0 = MakeNode("s0", 0);
            var s1 = MakeNode("s1", 1);
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, LtlFormula.False, fairness: Fairness.None,
                    label: "constant false on 2-cycle"),
                expectedValid: false);
        }

        /// <summary>
        /// Constant <c>true</c> against a deadlock node: every backend
        /// returns Valid. Cross-cuts the constant-formula and terminal-
        /// stutter paths.
        /// </summary>
        [Test]
        public void ConstantTrue_OnDeadlock_ValidEverywhere()
        {
            var s0 = MakeNode("s0", 0);
            AssertAllAgree(
                LtlMultiBackendCrossCheck.Run(s0, LtlFormula.True, fairness: Fairness.None,
                    label: "constant true on deadlock"),
                expectedValid: true);
        }

        #endregion
    }
}
