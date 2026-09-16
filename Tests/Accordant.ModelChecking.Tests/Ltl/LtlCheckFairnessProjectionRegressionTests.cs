namespace Accordant.ModelChecking.Tests.Ltl
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    /// <summary>
    /// Pins the LtlCheck fairness-projection bug
    /// (todo <c>fix-ltlcheck-fairness-projection</c>).
    ///
    /// <para>
    /// Pre-fix, <see cref="LtlCheck"/>'s product-cycle fairness check
    /// projected the product SCC to a system-level SCC and then asked
    /// <see cref="Fairness.IsFairCycle"/> whether the system SCC was
    /// fair. The "taken in SCC" set was therefore computed from
    /// <em>system</em> edges, but the projected run actually only fires
    /// the step functions that appear on <em>product</em> edges within
    /// the product SCC. A system edge <c>s →[α] s'</c> may exist with
    /// both endpoints in the projected system SCC, yet the corresponding
    /// product edges may all leave the product SCC (because the formula
    /// derivative is incompatible with the cycle). The bug was that the
    /// system-level check would classify the cycle as fair (α is
    /// "taken" in the system projection) while the product run never
    /// actually fires α, leading to spurious counterexamples on LTL
    /// liveness properties under fairness.
    /// </para>
    /// <para>
    /// The synthetic scenario constructed here exhibits exactly this
    /// pattern: one system state with two enabled self-loops α and τ,
    /// and a single product node whose only in-SCC outgoing edge is τ;
    /// the α-edge leaves the SCC. Under <see cref="Fairness.WeakFairAll"/>
    /// α is continuously enabled but never taken by the projected cycle,
    /// so the cycle must be classified as <b>unfair</b>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class LtlCheckFairnessProjectionRegressionTests
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

        [Test]
        public void Unfair_When_AlphaSystemSelfLoop_But_ProductAlphaLeavesSCC()
        {
            var sState = new TestState("s"); sState.Freeze();
            var outState = new TestState("out"); outState.Freeze();

            var alpha = new NamedStep("alpha");
            var tau = new NamedStep("tau");

            var s = new StateGraphNode
            {
                State = sState,
                StepFunctions = new List<IStepFunction> { alpha, tau },
                Edges = new List<StateGraphEdge>(),
            };
            s.Edges.Add(new StateGraphEdge { Target = s, StepFunction = alpha });
            s.Edges.Add(new StateGraphEdge { Target = s, StepFunction = tau });

            var sOut = new StateGraphNode
            {
                State = outState,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>(),
            };

            var p = new ProductNode(s, LtlFormula.True);
            var pOut = new ProductNode(sOut, LtlFormula.True);
            p.Edges.Add(new ProductEdge(p, tau));
            p.Edges.Add(new ProductEdge(pOut, alpha));

            var scc = new ProductSCC();
            scc.Nodes.Add(p);
            // HasCycle is internal-set; not needed by IsFairCycle

            // Sanity-check the pre-fix shape: the system-only projection
            // would classify this SCC as fair (α self-loops s in the
            // system, so "taken in system SCC" ⊇ {α}).
            var systemOnlySCC = new StronglyConnectedComponent();
            systemOnlySCC.Nodes.Add(s);
            typeof(StronglyConnectedComponent)
                .GetProperty(nameof(StronglyConnectedComponent.HasCycle))
                .SetValue(systemOnlySCC, true);
            Assert.That(
                Fairness.WeakFairAll.IsFairCycle(systemOnlySCC), Is.True,
                "Sanity: system-only projection is fair (pre-fix would accept).");

            // Post-fix: product-edge-projected fairness must reject.
            Assert.That(
                LtlCheck.IsFairCycle(scc, Fairness.WeakFairAll), Is.False,
                "α is continuously enabled at s but the product cycle never " +
                "fires α — cycle is unfair under WeakFairAll.");
        }

        [Test]
        public void Fair_When_BothStepsTakenInProduct()
        {
            var sState = new TestState("s"); sState.Freeze();

            var alpha = new NamedStep("alpha");
            var tau = new NamedStep("tau");

            var s = new StateGraphNode
            {
                State = sState,
                StepFunctions = new List<IStepFunction> { alpha, tau },
                Edges = new List<StateGraphEdge>(),
            };
            s.Edges.Add(new StateGraphEdge { Target = s, StepFunction = alpha });
            s.Edges.Add(new StateGraphEdge { Target = s, StepFunction = tau });

            var p = new ProductNode(s, LtlFormula.True);
            p.Edges.Add(new ProductEdge(p, tau));
            p.Edges.Add(new ProductEdge(p, alpha));

            var scc = new ProductSCC();
            scc.Nodes.Add(p);
            // HasCycle is internal-set; not needed by IsFairCycle

            Assert.That(
                LtlCheck.IsFairCycle(scc, Fairness.WeakFairAll), Is.True,
                "Both α and τ taken in product SCC; cycle is fair under WeakFairAll.");
        }

        [Test]
        public void StrongFair_Detects_AlphaNotTakenInProduct()
        {
            var sState = new TestState("s"); sState.Freeze();

            var alpha = new NamedStep("alpha");
            var tau = new NamedStep("tau");

            var s = new StateGraphNode
            {
                State = sState,
                StepFunctions = new List<IStepFunction> { alpha, tau },
                Edges = new List<StateGraphEdge>(),
            };
            s.Edges.Add(new StateGraphEdge { Target = s, StepFunction = alpha });
            s.Edges.Add(new StateGraphEdge { Target = s, StepFunction = tau });

            var outState = new TestState("out"); outState.Freeze();
            var sOut = new StateGraphNode
            {
                State = outState,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>(),
            };

            var p = new ProductNode(s, LtlFormula.True);
            var pOut = new ProductNode(sOut, LtlFormula.True);
            p.Edges.Add(new ProductEdge(p, tau));
            p.Edges.Add(new ProductEdge(pOut, alpha));

            var scc = new ProductSCC();
            scc.Nodes.Add(p);
            // HasCycle is internal-set; not needed by IsFairCycle

            var sf = Fairness.StrongFair(x => x.StepFunctionId == "alpha");
            Assert.That(
                LtlCheck.IsFairCycle(scc, sf), Is.False,
                "Strong fairness on α must reject the cycle since α is enabled at s " +
                "but the product cycle never fires α.");
        }
    }
}
