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
    /// Integration tests for SymbolicLtlCheck: model program × symbolic NBW.
    /// Uses manually constructed state graphs (no full model program machinery needed).
    /// </summary>
    [TestFixture]
    public class SymbolicLtlCheckTests
    {
        #region Test Infrastructure

        /// <summary>Simple concrete State for testing.</summary>
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

        /// <summary>Simple step function for labeling edges.</summary>
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

        /// <summary>
        /// Builds a state graph node with the given state and step functions.
        /// </summary>
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

        /// <summary>Adds a directed edge between nodes.</summary>
        private static void AddEdge(StateGraphNode from, StateGraphNode to, string sfId = "step")
        {
            from.Edges.Add(new StateGraphEdge
            {
                Target = to,
                StepFunction = new TestStepFunction(sfId)
            });
        }

        #endregion

        #region Helper: build LTL property from StateProp

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

        #region Basic Correctness Tests

        [Test]
        public void Check_Ga_Satisfied_AllStatesHaveA()
        {
            // Linear graph: s0 → s1 → s2 → s2 (self-loop)
            // All states have value > 0 (property: G(val > 0))
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 2));
            var s2 = MakeNode(new TestState("s2", 3));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s2); // self-loop

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var property = G(Atom(p));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.True, "G(val>0) should hold when all values > 0");
        }

        [Test]
        public void Check_Ga_Violated_OneStateDoesNotHaveA()
        {
            // s0(val=1) → s1(val=0) → s1 (self-loop)
            // Property: G(val > 0) — violated at s1
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var property = G(Atom(p));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.False,
                "G(val>0) should be violated when s1 has val=0 in a cycle");
        }

        [Test]
        public void Check_Fa_Satisfied_EventuallyReachesA()
        {
            // s0 → s1 → s2(goal) → s2
            // Property: F(goal) — eventually reach val=99
            var s0 = MakeNode(new TestState("s0", 0));
            var s1 = MakeNode(new TestState("s1", 0));
            var s2 = MakeNode(new TestState("s2", 99));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s2);

            var p = Prop("goal", s => ((TestState)s).Value == 99);
            var property = F(Atom(p));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.True, "F(goal) should hold");
        }

        [Test]
        public void Check_Fa_Violated_NeverReachesA()
        {
            // s0 → s1 → s0 (cycle, never reaches goal)
            // Property: F(goal) — violated because we loop forever
            var s0 = MakeNode(new TestState("s0", 0));
            var s1 = MakeNode(new TestState("s1", 1));
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            var p = Prop("goal", s => ((TestState)s).Value == 99);
            var property = F(Atom(p));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.False,
                "F(goal) should be violated in a cycle without goal");
        }

        [Test]
        public void Check_GFa_Satisfied_InfinitelyOften()
        {
            // s0(a) → s1(¬a) → s0 (cycle visits a infinitely often)
            // Property: GF(a) — a holds infinitely often
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            var p = Prop("a", s => ((TestState)s).Value == 1);
            var property = G(F(Atom(p)));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.True,
                "GF(a) should hold in cycle {s0(a), s1(¬a)}");
        }

        [Test]
        public void Check_GFa_Violated_EventuallyNeverA()
        {
            // s0(a) → s1(¬a) → s1 (s1 self-loop, never sees a again)
            // Property: GF(a) — violated because eventually stuck in s1
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("a", s => ((TestState)s).Value == 1);
            var property = G(F(Atom(p)));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.False,
                "GF(a) violated: cycle at s1 never visits a");
        }

        #endregion

        #region Implication / Response Tests

        [Test]
        public void Check_G_AImplFb_Satisfied()
        {
            // s0(a) → s1(¬a) → s2(b) → s0 (cycle: request always followed by response)
            // Property: G(a → F b)
            var s0 = MakeNode(new TestState("s0_req", 1));
            var s1 = MakeNode(new TestState("s1_mid", 0));
            var s2 = MakeNode(new TestState("s2_resp", 2));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s0);

            var req = Prop("req", s => ((TestState)s).Value == 1);
            var resp = Prop("resp", s => ((TestState)s).Value == 2);
            var property = G(Implies(Atom(req), F(Atom(resp))));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.True,
                "G(req → F resp) should hold: every request is followed by response");
        }

        [Test]
        public void Check_G_AImplFb_Violated()
        {
            // s0(a) → s1(¬a,¬b) → s1 (loop: request never gets response)
            // Property: G(a → F b)
            var s0 = MakeNode(new TestState("s0_req", 1));
            var s1 = MakeNode(new TestState("s1_stuck", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var req = Prop("req", s => ((TestState)s).Value == 1);
            var resp = Prop("resp", s => ((TestState)s).Value == 2);
            var property = G(Implies(Atom(req), F(Atom(resp))));

            var result = SymbolicLtlCheck.Check(s0, property);
            Assert.That(result.Valid, Is.False,
                "G(req → F resp) violated: request at s0 never gets response");
        }

        #endregion

        #region Bounded Depth Tests

        [Test]
        public void Check_BoundedDepth_NoViolationWithinBound()
        {
            // Long chain: s0 → s1 → ... → s10 → bad_cycle
            // Property: G(val >= 0) — violated deep in the chain
            var nodes = new StateGraphNode[12];
            for (int i = 0; i < 11; i++)
                nodes[i] = MakeNode(new TestState($"s{i}", i));
            nodes[11] = MakeNode(new TestState("bad", -1));

            for (int i = 0; i < 11; i++)
                AddEdge(nodes[i], nodes[i + 1]);
            AddEdge(nodes[11], nodes[11]); // self-loop at bad state

            var p = Prop("non_neg", s => ((TestState)s).Value >= 0);
            var property = G(Atom(p));

            // With depth bound 5, we shouldn't reach the violation
            var result = SymbolicLtlCheck.Check(nodes[0], property, maxDepth: 5);
            Assert.That(result.Valid, Is.True,
                "Bounded check (depth 5) shouldn't find violation at depth 11");

            // With unlimited depth, we should find it
            var resultFull = SymbolicLtlCheck.Check(nodes[0], property);
            Assert.That(resultFull.Valid, Is.False,
                "Unbounded check should find violation at depth 11");
        }

        #endregion

        #region Counterexample Trace Tests

        [Test]
        public void Check_Violation_HasTrace()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("a", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.Check(s0, G(Atom(p)));

            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);
            Assert.That(result.Trace.Count, Is.GreaterThan(0));
        }

        #endregion

        #region Edge Cases

        [Test]
        public void Check_SingleState_SelfLoop_PropertyHolds()
        {
            var s0 = MakeNode(new TestState("s0", 42));
            AddEdge(s0, s0);

            var p = Prop("is42", s => ((TestState)s).Value == 42);
            var result = SymbolicLtlCheck.Check(s0, G(Atom(p)));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Check_SingleState_NoEdges_StutterSemantics()
        {
            // Terminal state: stutter self-loop applied.
            // G(a) should hold if a holds in the terminal state.
            var s0 = MakeNode(new TestState("s0", 1));
            // No edges — terminal

            var p = Prop("a", s => ((TestState)s).Value == 1);
            var result = SymbolicLtlCheck.Check(s0, G(Atom(p)));
            Assert.That(result.Valid, Is.True,
                "Terminal state satisfying a: G(a) holds under stutter");
        }

        [Test]
        public void Check_TrueProperty_AlwaysHolds()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            AddEdge(s0, s0);

            var result = SymbolicLtlCheck.Check(s0, Ltl<IStatePredicate>.True());
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Check_FalseProperty_AlwaysFails()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            AddEdge(s0, s0);

            var result = SymbolicLtlCheck.Check(s0, Ltl<IStatePredicate>.False());
            Assert.That(result.Valid, Is.False);
        }

        #endregion
    }
}
