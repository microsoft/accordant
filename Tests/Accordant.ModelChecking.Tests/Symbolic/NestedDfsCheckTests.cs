namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for the Nested DFS (Algorithm B) LTL emptiness check.
    /// Mirrors the cases in <see cref="SymbolicLtlCheckTests"/> to assert
    /// equivalent semantics between the Tarjan-based <c>Check</c> and the
    /// nested-DFS-based <c>CheckNDFS</c>.
    /// </summary>
    [TestFixture]
    public class NestedDfsCheckTests
    {
        #region Test Infrastructure

        private sealed class TestState : State
        {
            public string Label { get; set; }
            public int Value { get; set; }

            public TestState(string label, int value = 0)
            {
                Label = label;
                Value = value;
            }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
            {
                var clone = new TestState(Label, Value);
                clonedMap[this] = clone;
            }

            protected override void LockComponents(HashSet<object> visited) { }

            protected override string StringRepresentationInternal(Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"{Label}({Value})";
            protected override void FreezeComponents(HashSet<object> visited) { }
        }

        private sealed class TestStepFunction : IStepFunction
        {
            public string StepFunctionId { get; }
            public int StepFunctionIdHash { get; }

            public TestStepFunction(string id)
            {
                StepFunctionId = id;
                StepFunctionIdHash = id.GetHashCode();
            }

            public IList<StepResult> Apply(IState state,
                IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        private static StateGraphNode MakeNode(TestState state, params string[] sfIds)
        {
            state.Freeze();
            return new StateGraphNode
            {
                State = state,
                StepFunctions = sfIds.Select(id => (IStepFunction)new TestStepFunction(id)).ToList(),
                Edges = new List<StateGraphEdge>()
            };
        }

        private static void AddEdge(StateGraphNode from, StateGraphNode to, string sfId = "step")
        {
            from.Edges.Add(new StateGraphEdge
            {
                Target = to,
                StepFunction = new TestStepFunction(sfId)
            });
        }

        private static StateProp Prop(string name, Func<IState, bool> eval)
            => new StateProp(name, eval);

        private static Ltl<IStatePredicate> Atom(StateProp p)
            => Ltl<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ltl<IStatePredicate> G(Ltl<IStatePredicate> f)
            => Ltl<IStatePredicate>.Globally(f);

        private static Ltl<IStatePredicate> F(Ltl<IStatePredicate> f)
            => Ltl<IStatePredicate>.Eventually(f);

        private static Ltl<IStatePredicate> Implies(Ltl<IStatePredicate> a, Ltl<IStatePredicate> b)
            => LtlAlgebra.Default.Implies(a, b);

        #endregion

        #region Safety properties

        [Test]
        public void NDFS_Ga_Satisfied_AllStatesHaveA()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 2));
            var s2 = MakeNode(new TestState("s2", 3));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s2);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(Atom(p)));
            Assert.That(result.Valid, Is.True, "G(val>0) should hold");
        }

        [Test]
        public void NDFS_Ga_Violated_OneStateDoesNotHaveA()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(Atom(p)));
            Assert.That(result.Valid, Is.False, "G(val>0) should be violated");
            Assert.That(result.Trace, Is.Not.Null);
            Assert.That(result.Trace.Any(t => t.IsInCycle), Is.True,
                "Counterexample should have a cycle portion");
        }

        #endregion

        #region Liveness properties

        [Test]
        public void NDFS_Fa_Satisfied_EventuallyReachesA()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            var s1 = MakeNode(new TestState("s1", 0));
            var s2 = MakeNode(new TestState("s2", 99));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s2);

            var p = Prop("goal", s => ((TestState)s).Value == 99);
            var result = SymbolicLtlCheck.CheckNDFS(s0, F(Atom(p)));
            Assert.That(result.Valid, Is.True, "F(goal) should hold");
        }

        [Test]
        public void NDFS_Fa_Violated_NeverReachesA()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            var s1 = MakeNode(new TestState("s1", 1));
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            var p = Prop("goal", s => ((TestState)s).Value == 99);
            var result = SymbolicLtlCheck.CheckNDFS(s0, F(Atom(p)));
            Assert.That(result.Valid, Is.False, "F(goal) should be violated in a goal-free cycle");
            Assert.That(result.Trace.Any(t => t.IsInCycle), Is.True);
        }

        [Test]
        public void NDFS_GFa_Satisfied_InfinitelyOften()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            var p = Prop("a", s => ((TestState)s).Value == 1);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(F(Atom(p))));
            Assert.That(result.Valid, Is.True, "GF(a) should hold in cycle visiting a");
        }

        [Test]
        public void NDFS_GFa_Violated_EventuallyNeverA()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("a", s => ((TestState)s).Value == 1);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(F(Atom(p))));
            Assert.That(result.Valid, Is.False,
                "GF(a) should be violated when system eventually loops at ¬a forever");
        }

        [Test]
        public void NDFS_G_AImplFb_Satisfied()
        {
            // s0(a,¬b) → s1(¬a,¬b) → s2(¬a,b) → s2
            // Property G(a → Fb)
            var s0 = MakeNode(new TestState("s0", 10));
            var s1 = MakeNode(new TestState("s1", 0));
            var s2 = MakeNode(new TestState("s2", 1));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s2);

            var a = Prop("a", s => ((TestState)s).Value >= 10);
            var b = Prop("b", s => ((TestState)s).Value == 1);
            var prop = G(Implies(Atom(a), F(Atom(b))));

            var result = SymbolicLtlCheck.CheckNDFS(s0, prop);
            Assert.That(result.Valid, Is.True, "G(a→Fb) should hold");
        }

        [Test]
        public void NDFS_G_AImplFb_Violated()
        {
            // s0(a) → s1(¬a,¬b) → s1: a occurs but b never does after.
            var s0 = MakeNode(new TestState("s0", 10));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var a = Prop("a", s => ((TestState)s).Value >= 10);
            var b = Prop("b", s => ((TestState)s).Value == 1);
            var prop = G(Implies(Atom(a), F(Atom(b))));

            var result = SymbolicLtlCheck.CheckNDFS(s0, prop);
            Assert.That(result.Valid, Is.False, "G(a→Fb) should be violated");
        }

        #endregion

        #region Counterexample structure

        [Test]
        public void NDFS_Violation_TraceHasPrefixAndCycle()
        {
            // s0 → s1 → s2 → s2 with property G(val>0) and s2 has val=0
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 1));
            var s2 = MakeNode(new TestState("s2", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s2);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(Atom(p)));

            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);
            Assert.That(result.Trace.Count, Is.GreaterThan(0));
            Assert.That(result.Trace.Any(t => !t.IsInCycle), Is.True, "should have prefix");
            Assert.That(result.Trace.Any(t => t.IsInCycle), Is.True, "should have cycle");
        }

        #endregion

        #region Edge cases

        [Test]
        public void NDFS_SingleState_SelfLoop_PropertyHolds()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            AddEdge(s0, s0);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(Atom(p)));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void NDFS_SingleState_NoEdges_StutterSemantics()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            // No outgoing edges → NDFS adds stutter self-loop.

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(Atom(p)));
            Assert.That(result.Valid, Is.True, "G(val>0) holds under stutter at a state where val>0");
        }

        [Test]
        public void NDFS_TrueProperty_AlwaysHolds()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            AddEdge(s0, s0);

            var result = SymbolicLtlCheck.CheckNDFS(s0, Ltl<IStatePredicate>.True());
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void NDFS_FalseProperty_AlwaysFails()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            AddEdge(s0, s0);

            var result = SymbolicLtlCheck.CheckNDFS(s0, Ltl<IStatePredicate>.False());
            Assert.That(result.Valid, Is.False);
        }

        [Test]
        public void NDFS_BoundedDepth_NoViolationWithinBound()
        {
            // Linear path of depth > maxDepth where violation is beyond the bound.
            var nodes = new List<StateGraphNode>();
            for (int i = 0; i < 8; i++)
                nodes.Add(MakeNode(new TestState($"s{i}", i < 5 ? 1 : 0)));
            for (int i = 0; i < nodes.Count - 1; i++)
                AddEdge(nodes[i], nodes[i + 1]);
            AddEdge(nodes[^1], nodes[^1]);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            // With maxDepth=3, violation at s5 is unreachable; stutter at s3 (val=1) is OK.
            var result = SymbolicLtlCheck.CheckNDFS(nodes[0], G(Atom(p)), maxDepth: 3);
            Assert.That(result.Valid, Is.True);
        }

        #endregion

        #region Cross-validation with Tarjan implementation

        [Test]
        public void NDFS_Agrees_With_Tarjan_OnSeveralCases()
        {
            // A small battery of structurally distinct graphs/properties to
            // confirm CheckNDFS and Check produce the same Valid verdict.

            StateGraphNode BuildLinearSelfLoop(int[] vals)
            {
                var ns = vals.Select((v, i) => MakeNode(new TestState($"n{i}", v))).ToList();
                for (int i = 0; i < ns.Count - 1; i++) AddEdge(ns[i], ns[i + 1]);
                AddEdge(ns[^1], ns[^1]);
                return ns[0];
            }

            StateGraphNode BuildTwoCycle(int v0, int v1)
            {
                var a = MakeNode(new TestState("a", v0));
                var b = MakeNode(new TestState("b", v1));
                AddEdge(a, b); AddEdge(b, a);
                return a;
            }

            var pVpos = Prop("v>0", s => ((TestState)s).Value > 0);
            var pGoal = Prop("g", s => ((TestState)s).Value == 99);
            var pA = Prop("a", s => ((TestState)s).Value == 1);

            AssertAgree(BuildLinearSelfLoop(new[] { 1 }),         G(Atom(pVpos)), "Ga-sat-single");
            AssertAgree(BuildLinearSelfLoop(new[] { 1, 0 }),      G(Atom(pVpos)), "Ga-vio");
            AssertAgree(BuildLinearSelfLoop(new[] { 0, 0, 99 }),  F(Atom(pGoal)), "Fa-sat");
            AssertAgree(BuildTwoCycle(0, 1),                       F(Atom(pGoal)), "Fa-vio");
            AssertAgree(BuildTwoCycle(1, 0),                       G(F(Atom(pA))), "GFa-sat");
            AssertAgree(BuildLinearSelfLoop(new[] { 1, 0 }),       G(F(Atom(pA))), "GFa-vio");
        }

        private static void AssertAgree(StateGraphNode root, Ltl<IStatePredicate> phi, string label)
        {
            // Re-clone the graph isn't necessary: Check is read-only over the graph.
            var tarjan = SymbolicLtlCheck.Check(root, phi);
            var ndfs = SymbolicLtlCheck.CheckNDFS(root, phi);
            Assert.That(ndfs.Valid, Is.EqualTo(tarjan.Valid),
                $"Case '{label}': NDFS and Tarjan must agree on validity");
        }

        #endregion
    }
}
