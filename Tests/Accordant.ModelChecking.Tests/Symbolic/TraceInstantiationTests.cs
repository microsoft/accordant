namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests that counterexample traces produced by the symbolic LTL/RLTL
    /// backends carry concrete predicate valuations per state — i.e. each
    /// <see cref="TraceItem.Valuation"/> entry agrees with re-evaluating
    /// the predicate on the trace item's <see cref="StateGraphNode.State"/>.
    /// Also verifies the human-readable rendering surfaces the valuation.
    /// </summary>
    [TestFixture]
    public class TraceInstantiationTests
    {
        #region Test infrastructure (mirrors NestedDfsCheckTests)

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
                clonedMap[this] = new TestState(Label, Value);
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

        private static StateProp Prop(string name, System.Func<IState, bool> eval)
            => new StateProp(name, eval);

        private static Ltl<IStatePredicate> Atom(StateProp p)
            => Ltl<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ltl<IStatePredicate> G(Ltl<IStatePredicate> f)
            => Ltl<IStatePredicate>.Globally(f);

        #endregion

        [Test]
        public void SccBackend_TraceItemsCarryConsistentValuations()
        {
            // Property G(val>0) violated on s2(val=0) — gives a lasso with
            // both prefix and cycle items.
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 1));
            var s2 = MakeNode(new TestState("s2", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s2);
            AddEdge(s2, s2);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.Check(s0, G(Atom(p)));

            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);

            foreach (var item in result.Trace)
            {
                Assert.That(item.Valuation, Is.Not.Null,
                    "symbolic backend should attach a predicate valuation");
                Assert.That(item.Valuation.Count, Is.GreaterThan(0),
                    "registry contains at least the property atom");
                // Each entry must agree with re-evaluating the predicate
                // on the concrete state.
                foreach (var kv in item.Valuation)
                {
                    Assert.That(kv.Value, Is.EqualTo(kv.Key.Eval(item.StateGraphNode.State)),
                        $"valuation for {kv.Key} disagrees with state {item.StateGraphNode.State}");
                }
            }

            // The cycle should be in the s2 region where val>0 is false,
            // i.e. ¬(val>0) is true. The registry holds the negated atom
            // because the property is negated before NBW construction.
            var cycleItem = result.Trace.First(t => t.IsInCycle);
            var negAtom = cycleItem.Valuation.Keys.First(k => k.ToString().Contains("val>0"));
            // Either the atom or its negation must be present and agree with the state.
            Assert.That(cycleItem.Valuation[negAtom],
                Is.EqualTo(negAtom.Eval(cycleItem.StateGraphNode.State)),
                "cycle node valuation should reflect the concrete state");
        }

        [Test]
        public void NdfsBackend_TraceItemsCarryConsistentValuations()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.CheckNDFS(s0, G(Atom(p)));

            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);

            foreach (var item in result.Trace)
            {
                Assert.That(item.Valuation, Is.Not.Null);
                foreach (var kv in item.Valuation)
                {
                    Assert.That(kv.Value, Is.EqualTo(kv.Key.Eval(item.StateGraphNode.State)));
                }
            }
        }

        [Test]
        public void GetTraceString_IncludesValuation()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("val>0", s => ((TestState)s).Value > 0);
            var result = SymbolicLtlCheck.Check(s0, G(Atom(p)));

            Assert.That(result.Valid, Is.False);
            var text = result.GetTraceString();
            Assert.That(text, Does.Contain("val>0"),
                "rendered trace should mention the atom (possibly negated)");
            Assert.That(text, Does.Contain("=true").Or.Contain("=false"),
                "rendered trace should include a boolean valuation");
        }

        [Test]
        public void ExplicitBackend_TraceItemsHaveNullValuation()
        {
            // Sanity: the non-symbolic Check path leaves Valuation = null.
            var s0 = MakeNode(new TestState("s0", 1));
            var item = new TraceItem(null, s0);
            Assert.That(item.Valuation, Is.Null);
        }
    }
}
