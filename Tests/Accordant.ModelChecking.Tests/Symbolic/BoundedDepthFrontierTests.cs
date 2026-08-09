namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Ensures depth frontiers remain distinct from real terminal states.
    /// Unknown continuations produce a bounded-inconclusive result; real
    /// explored cycles and genuine terminal stutter remain conclusive.
    /// </summary>
    [TestFixture]
    public class BoundedDepthFrontierTests
    {
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

        private sealed class AdvanceStep : IStepFunction
        {
            public string StepFunctionId => "advance";

            public IList<StepResult> Apply(
                IState state,
                IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            {
                var current = (TestState)state;
                return new[]
                {
                    new StepResult
                    {
                        State = new TestState($"s{current.Value + 1}", current.Value + 1),
                        StepFunctions = new IStepFunction[] { this }
                    }
                };
            }
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

        private static void AddEdge(StateGraphNode from, StateGraphNode to, string stepId)
            => from.Edges.Add(new StateGraphEdge { Target = to, StepFunction = new NoopStep(stepId) });

        /// <summary>
        /// Build the discriminating chain <c>s0 → s1 → s2</c> with
        /// <c>p</c> true only at <c>s2</c>.
        /// </summary>
        private static StateGraphNode BuildChain()
        {
            var s0 = MakeNode("s0", 0);
            var s1 = MakeNode("s1", 0);
            var s2 = MakeNode("s2", 1);
            var s3 = MakeNode("s3", 2);
            AddEdge(s0, s1, "a");
            AddEdge(s1, s2, "b");
            AddEdge(s2, s3, "c");
            AddEdge(s3, s3, "loop");
            return s0;
        }

        /// <summary>p ≡ Value == 1.</summary>
        private static StateProp PProp =>
            new StateProp("p", s => ((TestState)s).Value == 1);

        private static StateProp GoalProp =>
            new StateProp("goal", s => ((TestState)s).Value == 99);

        [Test]
        public void CheckerDepthFrontier_IsInconclusiveAcrossSymbolicBackends()
        {
            var s0 = BuildChain();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(GoalProp)));

            var r1 = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2);
            var r2 = SymbolicLtlCheck.CheckNDFS(s0, phi, maxDepth: 2);
            var r3 = SymbolicLtlCheck.Check(
                s0, phi, maxDepth: 2, fairness: Fairness.WeakAll);

            Assert.That(r1.Status, Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
            Assert.That(r2.Status, Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
            Assert.That(r3.Status, Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
            Assert.That(r1.Valid, Is.Null);
        }

        [Test]
        public void CheckerDepthFrontier_AlreadySatisfiedEventuallyStillHolds()
        {
            var s0 = BuildChain();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var r1 = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2);
            var r2 = SymbolicLtlCheck.CheckNDFS(s0, phi, maxDepth: 2);
            var r3 = SymbolicLtlCheck.Check(
                s0, phi, maxDepth: 2, fairness: Fairness.WeakAll);

            Assert.That(r1.Status, Is.EqualTo(PropertyCheckingStatus.Holds));
            Assert.That(r2.Status, Is.EqualTo(PropertyCheckingStatus.Holds));
            Assert.That(r3.Status, Is.EqualTo(PropertyCheckingStatus.Holds));
        }

        [Test]
        public void FiniteInvariantViolationBeforeCheckerFrontier_RemainsDefinitive()
        {
            var s0 = BuildChain();
            var phi = Ltl<IStatePredicate>.Globally(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var r1 = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2);
            var r2 = SymbolicLtlCheck.CheckNDFS(s0, phi, maxDepth: 2);

            Assert.That(r1.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(r2.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(r1.Trace, Has.Count.EqualTo(1));
        }

        [Test]
        public void ConstructionDepthFrontier_IsMarkedForEagerAndLazyGraphs()
        {
            StateGraphNode Build(bool lazy) => StateGraph.ExploreStateGraph(
                new IStepFunction[] { new AdvanceStep() },
                new TestState("s0", 0),
                maxDepth: 2,
                lazy: lazy);

            foreach (var root in new[] { Build(false), Build(true) })
            {
                var frontier = root.Edges.Single().Target;
                Assert.That(root.IsDepthFrontier, Is.False);
                Assert.That(frontier.Edges, Is.Empty);
                Assert.That(frontier.IsDepthFrontier, Is.True);

                var result = SymbolicLtlCheck.Check(
                    root,
                    Ltl<IStatePredicate>.Eventually(
                        Ltl<IStatePredicate>.Atom(new StatePredAtom(GoalProp))));
                Assert.That(
                    result.Status,
                    Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
            }
        }

        [Test]
        public void ConstructionDepthFrontier_IsInconclusiveInExplicitBackend()
        {
            var root = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new AdvanceStep() },
                new TestState("s0", 0),
                maxDepth: 2);
            var phi = LtlFormula.Eventually(
                LtlFormula.Prop(s => ((TestState)s).Value == 99, "goal"));

            var result = LtlCheck.Check(root, phi);

            Assert.That(
                result.Status,
                Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
        }

        [Test]
        public void ConstructionDepthFrontier_ExplicitBackendObservesBoundaryState()
        {
            var root = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new AdvanceStep() },
                new TestState("s0", 0),
                maxDepth: 2);
            var phi = LtlFormula.Eventually(
                LtlFormula.Prop(s => ((TestState)s).Value == 1, "p"));

            var result = LtlCheck.Check(root, phi);

            Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Holds));
        }

        [Test]
        public void ConstructionDepthFrontier_UserFacingInvariantViolationIsDefinitive()
        {
            var root = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new AdvanceStep() },
                new TestState("s0", 0),
                maxDepth: 2);
            var f = Formula.For<TestState>();
            var positive = f.Observe(s => s.Value > 0, "positive");

            var result = root.Check(f.Always(positive));

            Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(result.Trace, Has.Count.EqualTo(1));
        }

        [Test]
        public void RealCounterexampleCycleBeforeFrontier_RemainsDefinitive()
        {
            var s0 = MakeNode("s0", 0);
            AddEdge(s0, s0, "loop");
            var phi = Ltl<IStatePredicate>.Globally(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var r1 = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2);
            var r2 = SymbolicLtlCheck.CheckNDFS(s0, phi, maxDepth: 2);
            var r3 = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2,
                fairness: Fairness.WeakAll);

            Assert.That(r1.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(r2.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(r3.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
        }

        [Test]
        public void ActualTerminalState_RemainsConclusive()
        {
            var terminal = MakeNode("terminal", 1);
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var r1 = SymbolicLtlCheck.Check(terminal, phi, maxDepth: 1);
            var r2 = SymbolicLtlCheck.CheckNDFS(terminal, phi, maxDepth: 1);
            var r3 = SymbolicLtlCheck.Check(
                terminal, phi, maxDepth: 1, fairness: Fairness.WeakAll);

            Assert.That(r1.Status, Is.EqualTo(PropertyCheckingStatus.Holds));
            Assert.That(r2.Status, Is.EqualTo(PropertyCheckingStatus.Holds));
            Assert.That(r3.Status, Is.EqualTo(PropertyCheckingStatus.Holds));
        }

        [Test]
        public void ActualTerminalInvariantViolation_RemainsDefinitive()
        {
            var terminal = MakeNode("terminal", 0);
            var phi = Ltl<IStatePredicate>.Globally(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var r1 = SymbolicLtlCheck.Check(terminal, phi, maxDepth: 1);
            var r2 = SymbolicLtlCheck.CheckNDFS(terminal, phi, maxDepth: 1);
            var r3 = SymbolicLtlCheck.Check(
                terminal, phi, maxDepth: 1, fairness: Fairness.WeakAll);

            Assert.That(r1.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(r2.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(r3.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
        }
    }
}
