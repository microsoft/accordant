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
    /// Audit tests for the <c>maxDepth</c> frontier-stutter semantics
    /// across the three symbolic backends. The audit task asks whether
    /// the implicit self-loop added at the depth frontier preserves
    /// sound Büchi semantics (no false counterexamples).
    ///
    /// <para>
    /// Two distinct semantics existed pre-fix:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>NestedDfsCheck</b>: at frontier, system stutters but
    ///   the NBW makes a real transition on the current system state.
    ///   If the NBW has no valid outgoing transition (e.g. q is
    ///   waiting for <c>¬p</c> and the frontier state has <c>p=true</c>),
    ///   the run cuts off and no accepting cycle is reported.</item>
    ///   <item><b>ExploreProduct / SccProductCheck</b>: at frontier,
    ///   add a pure product self-loop without consulting the NBW.
    ///   This pretends the NBW can self-loop at <c>(sys, q)</c> on
    ///   <c>state(sys)</c> regardless of whether the NBW actually has
    ///   such a transition.</item>
    /// </list>
    ///
    /// <para>
    /// The pure-product-self-loop is unsound: it can fabricate an
    /// accepting cycle where no real Büchi-accepting run exists,
    /// producing a false counterexample for an LTL property that
    /// actually holds.
    /// </para>
    ///
    /// <para>
    /// The discriminating scenario:
    /// </para>
    /// <list type="bullet">
    ///   <item>System: linear chain <c>s0 → s1 → s2</c> with <c>p</c>
    ///   true only at <c>s2</c>.</item>
    ///   <item>Property: <c>F p</c>. <c>¬F p = G ¬p</c>; NBW is single
    ///   accepting state <c>q0</c> with self-loop on <c>¬p</c>.</item>
    ///   <item><c>maxDepth=2</c>: <c>s2</c> is the frontier node and
    ///   <c>p</c> is true there.</item>
    /// </list>
    ///
    /// <para>
    /// At <c>(s2, q0)</c> the NBW transition <c>q0 -¬p-> q0</c> cannot
    /// fire because <c>p</c> is true. The correct semantics yields no
    /// accepting cycle, so <c>F p</c> should be reported as holding.
    /// All three backends must agree.
    /// </para>
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
            AddEdge(s0, s1, "a");
            AddEdge(s1, s2, "b");
            return s0;
        }

        /// <summary>p ≡ Value == 1.</summary>
        private static StateProp PProp =>
            new StateProp("p", s => ((TestState)s).Value == 1);

        /// <summary>
        /// <see cref="SymbolicLtlCheck.Check"/> (ExploreProduct path,
        /// no fairness) must report <c>F p</c> as holding when the
        /// frontier state already satisfies <c>p</c>.
        /// </summary>
        [Test]
        public void ExploreProduct_FrontierAtSatisfyingState_FPHolds()
        {
            var s0 = BuildChain();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var result = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2);

            Assert.That(result.Valid, Is.True,
                "F p holds because p is true at the frontier state s2; " +
                "a pure product self-loop at the frontier would fabricate " +
                "an accepting cycle that the real NBW cannot produce.");
        }

        /// <summary>
        /// <see cref="SymbolicLtlCheck.CheckNDFS"/> (NestedDfsCheck path)
        /// must agree.
        /// </summary>
        [Test]
        public void NDFS_FrontierAtSatisfyingState_FPHolds()
        {
            var s0 = BuildChain();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var result = SymbolicLtlCheck.CheckNDFS(s0, phi, maxDepth: 2);
            Assert.That(result.Valid, Is.True);
        }

        /// <summary>
        /// <see cref="SymbolicLtlCheck.Check"/> with non-trivial
        /// fairness routes through <see cref="SccProductCheck"/>; it
        /// too must report <c>F p</c> as holding.
        /// </summary>
        [Test]
        public void SccProductCheck_FrontierAtSatisfyingState_FPHolds()
        {
            var s0 = BuildChain();
            var phi = Ltl<IStatePredicate>.Eventually(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var result = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2,
                fairness: Fairness.WeakFairAll);
            Assert.That(result.Valid, Is.True);
        }

        /// <summary>
        /// Negative-direction sanity check on the same chain. If
        /// <c>p</c> is true at <em>every</em> frontier system state,
        /// the unbounded property <c>G p</c> must still fail under all
        /// three backends because the chain visits states where <c>p</c>
        /// is false before reaching the frontier.
        /// </summary>
        [Test]
        public void Frontier_GP_PFailsBeforeFrontier_InvalidEverywhere()
        {
            var s0 = BuildChain();
            var phi = Ltl<IStatePredicate>.Globally(
                Ltl<IStatePredicate>.Atom(new StatePredAtom(PProp)));

            var r1 = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2);
            var r2 = SymbolicLtlCheck.CheckNDFS(s0, phi, maxDepth: 2);
            var r3 = SymbolicLtlCheck.Check(s0, phi, maxDepth: 2,
                fairness: Fairness.WeakFairAll);

            Assert.That(r1.Valid, Is.False, "ExploreProduct: G p must fail (p false at s0).");
            Assert.That(r2.Valid, Is.False, "NDFS: G p must fail (p false at s0).");
            Assert.That(r3.Valid, Is.False, "SccProductCheck: G p must fail (p false at s0).");
        }
    }
}
