namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for fairness-aware checking via
    /// <see cref="SccProductCheck"/> reached through
    /// <see cref="SymbolicLtlCheck.Check"/> and
    /// <see cref="SymbolicRltlCheck.Check"/>.
    ///
    /// The classical scenario used throughout is a one-state system with
    /// two outgoing actions:
    /// <list type="bullet">
    ///   <item><c>step_loop</c>: self-loop at <c>s0</c>.</item>
    ///   <item><c>step_progress</c>: takes <c>s0 → s1</c> where the goal
    ///   holds.</item>
    /// </list>
    /// Without fairness, the self-loop run is a counterexample to
    /// <c>F goal</c>. Under weak fairness on <c>step_progress</c> the
    /// self-loop cycle is unfair (step_progress is continuously enabled at
    /// <c>s0</c> but never taken inside the cycle), so it is no longer a
    /// valid counterexample and <c>F goal</c> holds.
    /// </summary>
    [TestFixture]
    public class FairnessTests
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

        private sealed class StepLoop : IStepFunction
        {
            public string StepFunctionId => "step_loop";
            public IList<StepResult> Apply(IState s, IReadOnlyList<(IStepFunction, StateGraphNode)> p) => null;
        }

        private sealed class StepProgress : IStepFunction
        {
            public string StepFunctionId => "step_progress";
            public IList<StepResult> Apply(IState s, IReadOnlyList<(IStepFunction, StateGraphNode)> p) => null;
        }

        /// <summary>
        /// Build the two-state system described in the class summary.
        /// Returns <c>s0</c>.
        /// </summary>
        private static StateGraphNode BuildSystem()
        {
            var s0State = new TestState("s0", 0); s0State.Freeze();
            var s1State = new TestState("s1", 99); s1State.Freeze();

            var s0 = new StateGraphNode
            {
                State = s0State,
                StepFunctions = new List<IStepFunction> { new StepLoop(), new StepProgress() },
                Edges = new List<StateGraphEdge>()
            };
            var s1 = new StateGraphNode
            {
                State = s1State,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>()
            };

            s0.Edges.Add(new StateGraphEdge { Target = s0, StepFunction = new StepLoop() });
            s0.Edges.Add(new StateGraphEdge { Target = s1, StepFunction = new StepProgress() });

            return s0;
        }

        private static StateProp Goal => new StateProp(
            "goal", s => ((TestState)s).Value == 99);

        #endregion

        #region SymbolicLtlCheck + Fairness

        [Test]
        public void Ltl_F_Goal_Violated_Without_Fairness()
        {
            var s0 = BuildSystem();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(Goal)));

            var result = SymbolicLtlCheck.Check(s0, phi);
            Assert.That(result.Valid, Is.False,
                "Without fairness, the s0 self-loop violates F goal.");
        }

        [Test]
        public void Ltl_F_Goal_Holds_Under_WeakFairAll()
        {
            var s0 = BuildSystem();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(Goal)));

            var result = SymbolicLtlCheck.Check(s0, phi, maxDepth: 0,
                fairness: Fairness.WeakFairAll);
            Assert.That(result.Valid, Is.True,
                "Under weak fairness, step_progress is continuously enabled at s0 " +
                "and must be taken, so F goal holds.");
        }

        [Test]
        public void Ltl_F_Goal_Holds_Under_WeakFair_On_StepProgress_Only()
        {
            var s0 = BuildSystem();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(Goal)));

            var fairness = Fairness.WeakFair<StepProgress>();
            var result = SymbolicLtlCheck.Check(s0, phi, maxDepth: 0, fairness: fairness);
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Ltl_F_Goal_Violated_Under_WeakFair_On_StepLoop_Only()
        {
            // Fairness only on step_loop says nothing about step_progress;
            // the self-loop run still takes step_loop infinitely often and
            // is therefore fair w.r.t. this constraint — still a counterexample.
            var s0 = BuildSystem();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(Goal)));

            var fairness = Fairness.WeakFair<StepLoop>();
            var result = SymbolicLtlCheck.Check(s0, phi, maxDepth: 0, fairness: fairness);
            Assert.That(result.Valid, Is.False);
        }

        #endregion

        #region SymbolicRltlCheck + Fairness

        [Test]
        public void Rltl_F_Goal_Holds_Under_WeakFairAll()
        {
            var s0 = BuildSystem();
            var phi = RltlFormula.Eventually(RltlFormula.Prop(s => ((TestState)s).Value == 99, "goal"));

            var result = RltlCheck.Check(s0, phi, maxDepth: 0, fairness: Fairness.WeakFairAll);
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Rltl_F_Goal_Violated_Without_Fairness()
        {
            var s0 = BuildSystem();
            var phi = RltlFormula.Eventually(RltlFormula.Prop(s => ((TestState)s).Value == 99, "goal"));

            var result = RltlCheck.Check(s0, phi);
            Assert.That(result.Valid, Is.False);
        }

        [Test]
        public void Rltl_InfinitelyOften_Goal_Holds_Under_StrongFair_On_StepProgress()
        {
            // ◇□¬goal = ¬□◇ goal. Under strong fairness on step_progress,
            // step_progress is always enabled at s0, so it must be taken
            // infinitely often, hence we visit s1 (goal) infinitely often.
            // BUT s1 has no outgoing edges so the run will stutter at s1
            // forever — goal holds infinitely often trivially.
            var s0 = BuildSystem();
            var phi = RltlFormula.InfinitelyOften(
                RltlFormula.Prop(s => ((TestState)s).Value == 99, "goal"));

            var fairness = Fairness.StrongFair<StepProgress>();
            var result = RltlCheck.Check(s0, phi, maxDepth: 0, fairness: fairness);
            Assert.That(result.Valid, Is.True);
        }

        #endregion

        #region BadCycle population on symbolic counterexamples

        /// <summary>
        /// SccProductCheck (fairness path) must attach a system-level
        /// <see cref="StronglyConnectedComponent"/> to
        /// <see cref="PropertyCheckingResult.BadCycle"/> so the
        /// enabled-but-not-taken hint fires on parity with the
        /// explicit-LTL backend.
        /// </summary>
        [Test]
        public void SccProductCheck_Failure_Populates_BadCycle_With_System_Nodes()
        {
            var s0 = BuildSystem();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(Goal)));

            // Fairness only on StepLoop leaves the self-loop run fair → counterexample.
            var result = SymbolicLtlCheck.Check(s0, phi, maxDepth: 0,
                fairness: Fairness.WeakFair<StepLoop>());

            Assert.That(result.Valid, Is.False);
            Assert.That(result.BadCycle, Is.Not.Null,
                "SccProductCheck should attach the system-projected SCC to BadCycle.");
            Assert.That(result.BadCycle.Nodes, Is.Not.Empty);
            Assert.That(result.BadCycle.HasCycle, Is.True);
            Assert.That(result.BadCycle.Nodes.Any(n => ReferenceEquals(n, s0)), Is.True,
                "Projected SCC must include the s0 self-loop node.");
        }

        /// <summary>
        /// NestedDfsCheck (no-fairness path through SymbolicRltlCheck) must
        /// likewise populate <see cref="PropertyCheckingResult.BadCycle"/>.
        /// </summary>
        [Test]
        public void NestedDfsCheck_Failure_Populates_BadCycle_With_System_Nodes()
        {
            var s0 = BuildSystem();
            var phi = RltlFormula.Eventually(
                RltlFormula.Prop(s => ((TestState)s).Value == 99, "goal"));

            var result = RltlCheck.Check(s0, phi);

            Assert.That(result.Valid, Is.False);
            Assert.That(result.BadCycle, Is.Not.Null,
                "NestedDfsCheck should reconstruct the cycle and populate BadCycle.");
            Assert.That(result.BadCycle.Nodes, Is.Not.Empty);
            Assert.That(result.BadCycle.HasCycle, Is.True);
            Assert.That(result.BadCycle.Nodes.Any(n => ReferenceEquals(n, s0)), Is.True);
        }

        #endregion
    }
}
