// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    /// <summary>
    /// Tests for lazy (on-the-fly) state-graph construction and its use by the
    /// model-checking emptiness search. Verifies that:
    ///   1. lazy construction defers all step-function evaluation until the graph
    ///      is actually walked, yet ultimately yields the same graph as eager;
    ///   2. lazy and eager roots produce identical model-checking verdicts across
    ///      safety, liveness and fairness properties;
    ///   3. when a counterexample is shallow, the lazy search stops early and
    ///      never explores the (large) unreached remainder of the graph.
    /// </summary>
    [TestFixture]
    public class LazyStateGraphTests
    {
        #region Counter model (finite, bounded [0, max])

        private sealed class CounterState : State
        {
            public int Count { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new CounterState { Count = this.Count };

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"Count={this.Count}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class IncrementStep : BaseStepFunction
        {
            private readonly int max;
            public int ApplyCount;

            public IncrementStep(int max) { this.max = max; }

            public override string StepFunctionId => "Increment";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                this.ApplyCount++;
                var cs = (CounterState)state;
                if (cs.Count >= this.max) return null;
                var next = (CounterState)cs.Clone();
                next.Count++;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private sealed class DecrementStep : BaseStepFunction
        {
            public int ApplyCount;

            public override string StepFunctionId => "Decrement";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                this.ApplyCount++;
                var cs = (CounterState)state;
                if (cs.Count <= 0) return null;
                var next = (CounterState)cs.Clone();
                next.Count--;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private static int CountReachableNodes(StateGraphNode root)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<StateGraphNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!seen.Add(n.GetNodeFingerprint())) continue;
                foreach (var e in n.Edges) stack.Push(e.Target);
            }
            return seen.Count;
        }

        #endregion

        [Test]
        public void Lazy_DefersExpansion_ButYieldsSameGraphWhenWalked()
        {
            const int max = 4;
            var eagerInc = new IncrementStep(max);
            var eagerDec = new DecrementStep();
            var eagerRoot = StateGraph.ExploreStateGraph(
                new IStepFunction[] { eagerInc, eagerDec }, new CounterState { Count = 0 });

            var lazyInc = new IncrementStep(max);
            var lazyDec = new DecrementStep();
            var lazyRoot = StateGraph.ExploreStateGraph(
                new IStepFunction[] { lazyInc, lazyDec }, new CounterState { Count = 0 }, lazy: true);

            // Lazy construction must not have applied any step function yet.
            Assert.That(lazyInc.ApplyCount, Is.Zero, "lazy root should not expand on construction");
            Assert.That(lazyDec.ApplyCount, Is.Zero, "lazy root should not expand on construction");

            // Walking the lazy graph materializes exactly the same set of nodes
            // as the fully eager graph.
            var eagerCount = CountReachableNodes(eagerRoot);
            var lazyCount = CountReachableNodes(lazyRoot);
            Assert.That(lazyCount, Is.EqualTo(eagerCount));
            Assert.That(eagerCount, Is.EqualTo(max + 1), "counter [0..max] has max+1 states");
            Assert.That(lazyInc.ApplyCount, Is.GreaterThan(0), "walking should have driven expansion");
        }

        [Test]
        public void Lazy_And_Eager_AgreeOnAllVerdicts()
        {
            const int max = 3;

            StateGraphNode Build(bool lazy)
                => StateGraph.ExploreStateGraph(
                    new IStepFunction[] { new IncrementStep(max), new DecrementStep() },
                    new CounterState { Count = 0 },
                    lazy: lazy);

            var eagerRoot = Build(lazy: false);
            var lazyRoot = Build(lazy: true);

            var p = Formula.For<CounterState>();
            var inRange = p.Observe(s => s.Count >= 0 && s.Count <= max, "InRange");
            var atTwo = p.Observe(s => s.Count == 2, "AtTwo");
            var atZero = p.Observe(s => s.Count == 0, "AtZero");

            var cases = new (TemporalFormula formula, Fairness fairness, string name)[]
            {
                (p.Always(inRange), null, "safety-holds"),
                (p.Always(!atTwo), null, "safety-fails"),
                (p.Eventually(atTwo), null, "reachability"),
                (p.InfinitelyOften(atZero), Fairness.WeakFairAll, "liveness-fairness"),
            };

            foreach (var (formula, fairness, name) in cases)
            {
                var eager = eagerRoot.Check(formula, fairness: fairness);
                var lazy = lazyRoot.Check(formula, fairness: fairness);
                Assert.That(lazy.Valid, Is.EqualTo(eager.Valid),
                    $"lazy and eager must agree on '{name}'");
            }
        }

        [Test]
        public void Lazy_And_Eager_AgreeOnRandomVerdicts()
        {
            for (var seed = 0; seed < 150; seed++)
            {
                var rnd = new Random(seed);
                var max = rnd.Next(1, 6);

                StateGraphNode Build(bool lazy)
                    => StateGraph.ExploreStateGraph(
                        new IStepFunction[] { new IncrementStep(max), new DecrementStep() },
                        new CounterState { Count = 0 },
                        lazy: lazy);

                var eagerRoot = Build(lazy: false);
                var lazyRoot = Build(lazy: true);

                var target = rnd.Next(0, max + 2);
                var p = Formula.For<CounterState>();
                var atTarget = p.Observe(s => s.Count == target, "AtTarget");
                var atZero = p.Observe(s => s.Count == 0, "AtZero");

                var cases = new (TemporalFormula formula, Fairness fairness, string name)[]
                {
                    (p.Always(atTarget), null, "always"),
                    (p.Always(!atTarget), null, "always-not"),
                    (p.Eventually(atTarget), null, "eventually"),
                    (p.InfinitelyOften(atZero), Fairness.WeakFairAll, "inf-often-fair"),
                };

                foreach (var (formula, fairness, name) in cases)
                {
                    var eager = eagerRoot.Check(formula, fairness: fairness).Valid;
                    var lazy = lazyRoot.Check(formula, fairness: fairness).Valid;
                    Assert.That(lazy, Is.EqualTo(eager),
                        $"seed {seed}: verdict for '{name}' (max={max}, target={target}) differs " +
                        $"(eager={eager}, lazy={lazy})");
                }
            }
        }

        #region Region model (large, with a shallow bad self-loop)

        private sealed class RegionState : State
        {
            public int Region { get; set; }
            public int Count { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new RegionState { Region = this.Region, Count = this.Count };

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"R={this.Region},C={this.Count}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        // "A_ToBad" sorts before "B_Grow", so it is the first edge explored from
        // every region-0 node: it moves to region 1, an absorbing self-loop where
        // the safety property is violated. This guarantees the lazy search finds a
        // shallow accepting lasso before ever descending the large grow chain.
        private sealed class ToBadStep : BaseStepFunction
        {
            public int ApplyCount;

            public override string StepFunctionId => "A_ToBad";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                this.ApplyCount++;
                var s = (RegionState)state;
                var next = (RegionState)s.Clone();
                next.Region = 1;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private sealed class GrowStep : BaseStepFunction
        {
            private readonly int max;
            public int ApplyCount;

            public GrowStep(int max) { this.max = max; }

            public override string StepFunctionId => "B_Grow";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                this.ApplyCount++;
                var s = (RegionState)state;
                if (s.Region != 0 || s.Count >= this.max) return null;
                var next = (RegionState)s.Clone();
                next.Count++;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        #endregion

        [Test]
        public void Lazy_StopsEarly_OnShallowCounterexample()
        {
            const int max = 500; // eager must build ~2*(max+1) nodes

            // Eager: fully explores the graph, so GrowStep is applied at every
            // region-0 node.
            var eagerGrow = new GrowStep(max);
            var eagerRoot = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new ToBadStep(), eagerGrow },
                new RegionState { Region = 0, Count = 0 });

            // Lazy: the search should close the region-1 self-loop lasso after a
            // couple of nodes and never descend the grow chain.
            var lazyGrow = new GrowStep(max);
            var lazyRoot = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new ToBadStep(), lazyGrow },
                new RegionState { Region = 0, Count = 0 },
                lazy: true);

            var p = Formula.For<RegionState>();
            var inRegion0 = p.Observe(s => s.Region == 0, "InRegion0");
            var safety = p.Always(inRegion0);

            var eagerResult = eagerRoot.Check(safety);
            var lazyResult = lazyRoot.Check(safety);

            // Same verdict: the safety property is violated.
            Assert.That(eagerResult.Valid, Is.False);
            Assert.That(lazyResult.Valid, Is.False);

            // Early exit: lazy applied GrowStep far fewer times than eager, which
            // had to expand the entire chain.
            Assert.That(lazyGrow.ApplyCount, Is.LessThan(eagerGrow.ApplyCount),
                "lazy search should not explore the whole graph");
            Assert.That(lazyGrow.ApplyCount, Is.LessThan(20),
                "lazy search should only touch a shallow prefix");
            Assert.That(eagerGrow.ApplyCount, Is.GreaterThan(max),
                "eager exploration touches every region-0 node");
        }

        [Test]
        public void Lazy_RespectsConstructionMaxDepth()
        {
            // Unbounded grow chain, truncated by construction maxDepth. Region-1
            // is never reachable via ToBad within a too-shallow bound because the
            // only violation requires stepping to region 1 (depth 2). With
            // maxDepth = 1, only the root exists, so the safety property holds
            // on the truncated graph.
            var steps = new IStepFunction[] { new ToBadStep(), new GrowStep(int.MaxValue) };

            var shallow = StateGraph.ExploreStateGraph(
                steps, new RegionState { Region = 0, Count = 0 }, maxDepth: 1, lazy: true);
            var deeper = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new ToBadStep(), new GrowStep(int.MaxValue) },
                new RegionState { Region = 0, Count = 0 }, maxDepth: 5, lazy: true);

            var p = Formula.For<RegionState>();
            var safety = p.Always(p.Observe(s => s.Region == 0, "InRegion0"));

            // Depth 1: root only, no edge to region 1 -> property holds.
            Assert.That(shallow.Check(safety).Valid, Is.True,
                "truncated graph has no region-1 state");

            // Depth 5: region 1 reachable at depth 2 -> violation is present.
            Assert.That(deeper.Check(safety).Valid, Is.False);
        }
    }
}
