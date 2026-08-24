namespace Accordant.ModelChecking.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    /// <summary>
    /// Direct unit tests for <see cref="Fairness.IsFairCycle"/> that
    /// exhaustively cover the WeakFair × StrongFair × continuously-enabled
    /// × taken cross-product on hand-built SCCs. Previously this helper
    /// was only exercised indirectly through the sample suites; these
    /// tests pin its semantics with minimal synthetic state graphs.
    ///
    /// <para>
    /// All scenarios use the convention:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>α</c>, <c>β</c>, <c>γ</c>, … — distinct named step functions.
    ///   </description></item>
    ///   <item><description>
    ///     <c>NamedStep(id)</c> — a no-op <see cref="IStepFunction"/>
    ///     whose only purpose is to carry an id on edges.
    ///   </description></item>
    ///   <item><description>
    ///     "Enabled at <c>v</c>" is derived from <c>v.Edges</c> — the
    ///     contract followed by <see cref="Fairness.IsFairCycle"/>.
    ///   </description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class FairnessDirectUnitTests
    {
        private sealed class TestState : State
        {
            public string Label { get; }
            public TestState(string label) { Label = label; }
            protected override void CloneInternal(Dictionary<object, object> map)
                => map[this] = new TestState(Label);
            protected override void LockComponents(HashSet<object> visited) { }
            protected override string StringRepresentationInternal(Dictionary<object, string> paths, string path, bool forceRecompute) => Label;
            protected override void FreezeComponents(HashSet<object> visited) { }
        }

        private sealed class NamedStep : IStepFunction
        {
            public string StepFunctionId { get; }
            public int StepFunctionIdHash { get; }
            public NamedStep(string id) { StepFunctionId = id; StepFunctionIdHash = id.GetHashCode(); }
            public IList<StepResult> Apply(IState s, IReadOnlyList<(IStepFunction, StateGraphNode)> p) => null;
        }

        private static StateGraphNode Node(string label, params IStepFunction[] enabled)
        {
            var st = new TestState(label); st.Freeze();
            return new StateGraphNode
            {
                State = st,
                StepFunctions = new List<IStepFunction>(enabled),
                Edges = new List<StateGraphEdge>(),
            };
        }

        private static void AddEdge(StateGraphNode src, StateGraphNode dst, IStepFunction step)
            => src.Edges.Add(new StateGraphEdge { Target = dst, StepFunction = step });

        private static StronglyConnectedComponent Scc(params StateGraphNode[] nodes)
        {
            var scc = new StronglyConnectedComponent();
            foreach (var n in nodes) scc.Nodes.Add(n);
            return scc;
        }

        // ---------------- single-node SCCs ----------------

        /// <summary>
        /// Single self-loop on α. α is continuously enabled and taken.
        /// </summary>
        [Test]
        public void SingleNode_AlphaSelfLoop_Fair_Under_All_Fairness()
        {
            var alpha = new NamedStep("alpha");
            var v = Node("v", alpha);
            AddEdge(v, v, alpha);
            var scc = Scc(v);

            Assert.That(Fairness.None.IsFairCycle(scc), Is.True);
            Assert.That(Fairness.WeakFairAll.IsFairCycle(scc), Is.True);
            Assert.That(Fairness.WeakFair(_ => true).IsFairCycle(scc), Is.True);
            Assert.That(Fairness.StrongFair(_ => true).IsFairCycle(scc), Is.True);
            Assert.That(
                (Fairness.WeakFair(_ => true) + Fairness.StrongFair(_ => true))
                    .IsFairCycle(scc), Is.True);
        }

        /// <summary>
        /// Single node with two enabled steps α (self-loop) and β
        /// (system edge to a node outside the SCC, so β is NOT taken
        /// inside the SCC). β is continuously enabled here.
        ///
        /// <list type="bullet">
        ///   <item><description>No fairness → fair.</description></item>
        ///   <item><description>WeakFair on β → unfair (continuously enabled, not taken).</description></item>
        ///   <item><description>StrongFair on β → unfair (enabled inf. often, not taken).</description></item>
        ///   <item><description>WeakFair on α only → fair (α taken).</description></item>
        /// </list>
        /// </summary>
        [Test]
        public void SingleNode_BetaEnabledButNotTaken_Unfair_Under_FairnessOnBeta()
        {
            var alpha = new NamedStep("alpha");
            var beta = new NamedStep("beta");
            var v = Node("v", alpha, beta);
            var w = Node("w");
            AddEdge(v, v, alpha);
            AddEdge(v, w, beta);   // β leaves SCC
            var scc = Scc(v);      // single-node SCC = {v}

            Assert.That(Fairness.None.IsFairCycle(scc), Is.True);
            Assert.That(Fairness.WeakFair(sf => sf.StepFunctionId == "beta").IsFairCycle(scc),
                Is.False, "β continuously enabled at v, not taken in SCC");
            Assert.That(Fairness.StrongFair(sf => sf.StepFunctionId == "beta").IsFairCycle(scc),
                Is.False, "β enabled, not taken — strong fairness rejects");
            Assert.That(Fairness.WeakFair(sf => sf.StepFunctionId == "alpha").IsFairCycle(scc),
                Is.True, "α taken — weak fairness satisfied");
            Assert.That(Fairness.WeakFairAll.IsFairCycle(scc), Is.False,
                "WeakFairAll covers β, which is continuously enabled and not taken");
        }

        // ---------------- two-node SCCs ----------------

        /// <summary>
        /// Two-node cycle v ⇄ w via α (v→w) and β (w→v); each step
        /// enabled only at one node — neither is continuously enabled.
        /// Both α and β are taken. Should be fair under all fairness.
        /// </summary>
        [Test]
        public void TwoNode_AlphaBetaCycle_Fair_Under_All_Fairness()
        {
            var alpha = new NamedStep("alpha");
            var beta = new NamedStep("beta");
            var v = Node("v", alpha);
            var w = Node("w", beta);
            AddEdge(v, w, alpha);
            AddEdge(w, v, beta);
            var scc = Scc(v, w);

            Assert.That(Fairness.WeakFairAll.IsFairCycle(scc), Is.True);
            Assert.That(Fairness.StrongFair(_ => true).IsFairCycle(scc), Is.True);
        }

        /// <summary>
        /// Two-node cycle v ⇄ w with α both ways; γ also enabled at v
        /// but γ leads to a node outside the SCC. γ enabled only at v,
        /// so NOT continuously enabled in {v, w}.
        ///
        /// <list type="bullet">
        ///   <item><description>WeakFair on γ → fair (not continuously enabled).</description></item>
        ///   <item><description>StrongFair on γ → unfair (enabled inf. often at v, not taken).</description></item>
        /// </list>
        /// </summary>
        [Test]
        public void TwoNode_GammaEnabledAtOneNodeOnly_DistinguishesWeakStrong()
        {
            var alpha = new NamedStep("alpha");
            var gamma = new NamedStep("gamma");
            var v = Node("v", alpha, gamma);
            var w = Node("w", alpha);
            var outside = Node("outside");
            AddEdge(v, w, alpha);
            AddEdge(w, v, alpha);
            AddEdge(v, outside, gamma);  // γ leaves SCC
            var scc = Scc(v, w);

            Assert.That(Fairness.WeakFair(sf => sf.StepFunctionId == "gamma").IsFairCycle(scc),
                Is.True, "γ enabled only at v, not continuously enabled in {v,w}");
            Assert.That(Fairness.StrongFair(sf => sf.StepFunctionId == "gamma").IsFairCycle(scc),
                Is.False, "γ enabled at v (inf. often) but never taken");
        }

        /// <summary>
        /// Two-node SCC with α as the cycle and δ continuously enabled
        /// at every node (self-loop at v and at w), but δ never taken
        /// in the cycle (cycle uses α only). Both WF and SF reject.
        /// </summary>
        [Test]
        public void TwoNode_DeltaContinuouslyEnabledButNotTaken_Unfair_WF_And_SF()
        {
            var alpha = new NamedStep("alpha");
            var delta = new NamedStep("delta");
            var outside = Node("outside");
            var v = Node("v", alpha, delta);
            var w = Node("w", alpha, delta);
            AddEdge(v, w, alpha);
            AddEdge(w, v, alpha);
            AddEdge(v, outside, delta);  // δ leaves SCC
            AddEdge(w, outside, delta);
            var scc = Scc(v, w);

            Assert.That(Fairness.WeakFair(sf => sf.StepFunctionId == "delta").IsFairCycle(scc),
                Is.False, "δ continuously enabled, never taken");
            Assert.That(Fairness.StrongFair(sf => sf.StepFunctionId == "delta").IsFairCycle(scc),
                Is.False, "δ enabled inf. often, never taken");
        }

        // ---------------- predicate scoping ----------------

        /// <summary>
        /// Fairness only on a step that is neither enabled nor present
        /// in the SCC is trivially satisfied.
        /// </summary>
        [Test]
        public void Fairness_OnAbsentStep_IsAlwaysFair()
        {
            var alpha = new NamedStep("alpha");
            var v = Node("v", alpha);
            AddEdge(v, v, alpha);
            var scc = Scc(v);

            Assert.That(Fairness.WeakFair(sf => sf.StepFunctionId == "absent").IsFairCycle(scc),
                Is.True);
            Assert.That(Fairness.StrongFair(sf => sf.StepFunctionId == "absent").IsFairCycle(scc),
                Is.True);
        }

        /// <summary>
        /// WeakFair + StrongFair combined via <see cref="Fairness.op_Addition"/>:
        /// rejects a cycle that violates either constraint independently.
        /// </summary>
        [Test]
        public void Combined_WF_Plus_SF_RejectsViolationsOfEither()
        {
            var alpha = new NamedStep("alpha");
            var beta = new NamedStep("beta");
            var v = Node("v", alpha, beta);
            var w = Node("w");
            AddEdge(v, v, alpha);
            AddEdge(v, w, beta);   // β leaves SCC
            var scc = Scc(v);

            var fair = Fairness.WeakFair(sf => sf.StepFunctionId == "alpha")
                       + Fairness.StrongFair(sf => sf.StepFunctionId == "beta");

            // α: WF, continuously enabled and taken → ok.
            // β: SF, enabled inf. often, not taken → rejected.
            Assert.That(fair.IsFairCycle(scc), Is.False);
        }

        // ---------------- degenerate inputs ----------------

        /// <summary>
        /// An empty SCC (no nodes) is vacuously fair: no step is
        /// enabled anywhere, so no fairness constraint can be violated.
        /// </summary>
        [Test]
        public void EmptySCC_IsVacuouslyFair()
        {
            var scc = new StronglyConnectedComponent();
            Assert.That(Fairness.WeakFairAll.IsFairCycle(scc), Is.True);
            Assert.That(Fairness.StrongFair(_ => true).IsFairCycle(scc), Is.True);
        }

        /// <summary>
        /// An isolated node with no outgoing edges has empty enabled
        /// and empty taken sets — vacuously fair.
        /// </summary>
        [Test]
        public void IsolatedNode_NoEdges_IsVacuouslyFair()
        {
            var v = Node("v");
            var scc = Scc(v);
            Assert.That(Fairness.WeakFairAll.IsFairCycle(scc), Is.True);
            Assert.That(Fairness.StrongFair(_ => true).IsFairCycle(scc), Is.True);
        }

        // ---------------- WeakFair vs StrongFair semantics distinction --------

        /// <summary>
        /// Step σ enabled at one of two nodes (so enabled inf. often
        /// but NOT continuously enabled) and never taken in the cycle.
        ///
        /// <list type="bullet">
        ///   <item><description>WeakFair on σ → fair.</description></item>
        ///   <item><description>StrongFair on σ → unfair.</description></item>
        /// </list>
        /// This is the textbook distinction between WF and SF.
        /// </summary>
        [Test]
        public void Textbook_WeakFair_vs_StrongFair_Distinction()
        {
            var alpha = new NamedStep("alpha");
            var sigma = new NamedStep("sigma");
            var outside = Node("outside");
            var v = Node("v", alpha, sigma);  // σ enabled at v
            var w = Node("w", alpha);          // σ NOT enabled at w
            AddEdge(v, w, alpha);
            AddEdge(w, v, alpha);
            AddEdge(v, outside, sigma);        // σ leaves SCC
            var scc = Scc(v, w);

            Assert.That(Fairness.WeakFair(sf => sf.StepFunctionId == "sigma").IsFairCycle(scc),
                Is.True, "WF: σ not continuously enabled in {v,w} → vacuously satisfied");
            Assert.That(Fairness.StrongFair(sf => sf.StepFunctionId == "sigma").IsFairCycle(scc),
                Is.False, "SF: σ enabled inf. often at v but never taken → unfair");
        }
    }
}
