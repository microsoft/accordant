// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Property-based (randomized/generative) tests asserting that eager and
/// lazy state-graph construction are equivalent — they now share a single
/// successor kernel (<c>StateGraph.GenerateSuccessors</c>), so any divergence
/// is a bug. These tests exercise only the core graph-construction contract
/// (nodes, edges, hooks, constraints, depth bounds); equivalence of the
/// model-checking verdicts built on top of the graph is covered separately
/// in the model-checking test project.
///
/// <para>The <em>invariants</em> asserted, and the one place the two may
/// legitimately differ:</para>
/// <list type="bullet">
/// <item>Unbounded (<c>maxDepth == -1</c>): the eager and lazy graphs are
/// identical as a set of nodes and a set of edges, and they hook exactly the
/// same set of nodes.</item>
/// <item>Bounded (<c>maxDepth &gt;= 0</c>): depth is memoized at first
/// discovery, and discovery order differs (eager DFS stack vs. lazy
/// consumer-driven walk), so the two truncated graphs may genuinely differ.
/// We therefore assert only the order-independent invariant that <em>each</em>
/// bounded graph is a subgraph of the unbounded one — not that they equal
/// each other.</item>
/// </list>
/// </summary>
[TestFixture]
public class LazyEagerEquivalenceTests
{
    private const int Seeds = 200;

    #region Randomized model

    private sealed class VectorState : State
    {
        public int[] Values { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new VectorState { Values = (int[])this.Values.Clone() };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths, string path, bool forceRecompute)
            => "V=[" + string.Join(",", this.Values) + "]";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    // Re-adds itself: moves one component by +/-1 while it stays in [0, bound).
    private sealed class MoveStep : BaseStepFunction
    {
        private readonly string id;
        private readonly int component;
        private readonly int delta;
        private readonly int bound;

        public MoveStep(string id, int component, int delta, int bound)
        {
            this.id = id;
            this.component = component;
            this.delta = delta;
            this.bound = bound;
        }

        public override string StepFunctionId => this.id;

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var s = (VectorState)state;
            var v = s.Values[this.component] + this.delta;
            if (v < 0 || v >= this.bound) return null;
            var next = (VectorState)s.Clone();
            next.Values[this.component] = v;
            return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
        }
    }

    // Re-adds itself, but branches non-deterministically: emits both the +1
    // and -1 in-bounds successors of a component in a single application.
    private sealed class SpawnStep : BaseStepFunction
    {
        private readonly string id;
        private readonly int component;
        private readonly int bound;

        public SpawnStep(string id, int component, int bound)
        {
            this.id = id;
            this.component = component;
            this.bound = bound;
        }

        public override string StepFunctionId => this.id;

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var s = (VectorState)state;
            var results = new List<StepResult>();
            foreach (var delta in new[] { +1, -1 })
            {
                var v = s.Values[this.component] + delta;
                if (v < 0 || v >= this.bound) continue;
                var next = (VectorState)s.Clone();
                next.Values[this.component] = v;
                results.Add(new StepResult { State = next, StepFunctions = new IStepFunction[] { this } });
            }

            return results.Count == 0 ? null : results;
        }
    }

    // Fires once when a component hits a target value, then consumes itself
    // (adds no step functions), so downstream nodes carry a smaller
    // step-function set. Exercises the successor step-set computation.
    private sealed class ConsumeStep : BaseStepFunction
    {
        private readonly string id;
        private readonly int component;
        private readonly int target;

        public ConsumeStep(string id, int component, int target)
        {
            this.id = id;
            this.component = component;
            this.target = target;
        }

        public override string StepFunctionId => this.id;

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var s = (VectorState)state;
            if (s.Values[this.component] != this.target) return null;
            var next = (VectorState)s.Clone();
            // No StepFunctions -> this step is consumed at the successor node.
            return new[] { new StepResult { State = next, StepFunctions = Array.Empty<IStepFunction>() } };
        }
    }

    private static (IStepFunction[] steps, VectorState start) BuildModel(Random rnd)
    {
        var dims = rnd.Next(1, 3);       // 1..2
        var bound = rnd.Next(2, 4);      // 2..3
        var numSteps = rnd.Next(2, 5);   // 2..4

        var steps = new List<IStepFunction>();
        var hasMovement = false;
        for (var i = 0; i < numSteps; i++)
        {
            var id = "s" + i;
            var component = rnd.Next(0, dims);
            switch (rnd.Next(0, 3))
            {
                case 0:
                    steps.Add(new MoveStep(id, component, rnd.Next(0, 2) == 0 ? +1 : -1, bound));
                    hasMovement = true;
                    break;
                case 1:
                    steps.Add(new SpawnStep(id, component, bound));
                    hasMovement = true;
                    break;
                default:
                    steps.Add(new ConsumeStep(id, component, rnd.Next(0, bound)));
                    break;
            }
        }

        // Guarantee a non-trivial graph.
        if (!hasMovement)
        {
            steps.Add(new MoveStep("sMove", 0, +1, bound));
        }

        return (steps.ToArray(), new VectorState { Values = new int[dims] });
    }

    #endregion

    #region Graph signatures

    // Fingerprints of every node reachable from the root by walking Edges.
    private static HashSet<string> NodeSet(StateGraphNode root)
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

        return seen;
    }

    // "source|stepId|target" for every edge reachable from the root.
    private static HashSet<string> EdgeSet(StateGraphNode root)
    {
        var seenNodes = new HashSet<string>();
        var edges = new HashSet<string>();
        var stack = new Stack<StateGraphNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            var nf = n.GetNodeFingerprint();
            if (!seenNodes.Add(nf)) continue;
            foreach (var e in n.Edges)
            {
                edges.Add(nf + "|" + e.StepFunction.StepFunctionId + "|" + e.Target.GetNodeFingerprint());
                stack.Push(e.Target);
            }
        }

        return edges;
    }

    #endregion

    [Test]
    public void EagerAndLazy_ProduceIdenticalGraphs_WhenUnbounded()
    {
        for (var seed = 0; seed < Seeds; seed++)
        {
            var (steps, start) = BuildModel(new Random(seed));

            var eagerHooks = new HashSet<string>();
            var eagerRoot = StateGraph.ExploreStateGraph(
                steps, (VectorState)start.Clone(),
                hook: n => eagerHooks.Add(n.GetNodeFingerprint()));

            var lazyHooks = new HashSet<string>();
            var lazyRoot = StateGraph.ExploreStateGraph(
                steps, (VectorState)start.Clone(),
                hook: n => lazyHooks.Add(n.GetNodeFingerprint()),
                lazy: true);

            var eagerNodes = NodeSet(eagerRoot);
            var lazyNodes = NodeSet(lazyRoot);      // full walk materializes lazy graph
            var eagerEdges = EdgeSet(eagerRoot);
            var lazyEdges = EdgeSet(lazyRoot);

            Assert.That(lazyNodes.SetEquals(eagerNodes), Is.True,
                $"seed {seed}: node sets differ (eager={eagerNodes.Count}, lazy={lazyNodes.Count})");
            Assert.That(lazyEdges.SetEquals(eagerEdges), Is.True,
                $"seed {seed}: edge sets differ (eager={eagerEdges.Count}, lazy={lazyEdges.Count})");

            // Hook alignment: a full walk of the lazy graph fires the hook on
            // exactly the same set of nodes the eager explorer hooks.
            Assert.That(lazyHooks.SetEquals(eagerHooks), Is.True,
                $"seed {seed}: hooked node sets differ");
            Assert.That(eagerHooks.SetEquals(eagerNodes), Is.True,
                $"seed {seed}: eager should hook every reachable node exactly once");
        }
    }

    [Test]
    public void BoundedGraphs_AreSubgraphsOfUnbounded_ForBothDrivers()
    {
        for (var seed = 0; seed < Seeds; seed++)
        {
            var rnd = new Random(seed);
            var (steps, start) = BuildModel(rnd);
            var maxDepth = rnd.Next(1, 6);

            var unboundedNodes = NodeSet(
                StateGraph.ExploreStateGraph(steps, (VectorState)start.Clone()));
            var unboundedEdges = EdgeSet(
                StateGraph.ExploreStateGraph(steps, (VectorState)start.Clone()));

            foreach (var lazy in new[] { false, true })
            {
                var root = StateGraph.ExploreStateGraph(
                    steps, (VectorState)start.Clone(), maxDepth: maxDepth, lazy: lazy);

                var boundedNodes = NodeSet(root);
                var boundedEdges = EdgeSet(root);

                Assert.That(boundedNodes.IsSubsetOf(unboundedNodes), Is.True,
                    $"seed {seed} lazy={lazy}: bounded nodes must be a subset of the unbounded graph");
                Assert.That(boundedEdges.IsSubsetOf(unboundedEdges), Is.True,
                    $"seed {seed} lazy={lazy}: bounded edges must be a subset of the unbounded graph");
            }
        }
    }

    [Test]
    public void ConstraintFailingRoot_FiresNoHooks_InBothDrivers()
    {
        var steps = new IStepFunction[] { new MoveStep("s0", 0, +1, 3) };
        Func<IState, bool> rejectAll = _ => false;

        var eagerHooks = new List<string>();
        var eagerPostHooks = new List<string>();
        StateGraph.ExploreStateGraph(
            steps, new VectorState { Values = new int[1] },
            hook: n => eagerHooks.Add(n.GetNodeFingerprint()),
            postHook: n => eagerPostHooks.Add(n.GetNodeFingerprint()),
            stateConstraint: rejectAll);

        var lazyHooks = new List<string>();
        var lazyPostHooks = new List<string>();
        var lazyRoot = StateGraph.ExploreStateGraph(
            steps, new VectorState { Values = new int[1] },
            hook: n => lazyHooks.Add(n.GetNodeFingerprint()),
            postHook: n => lazyPostHooks.Add(n.GetNodeFingerprint()),
            stateConstraint: rejectAll,
            lazy: true);

        // Force lazy expansion of the (constraint-failing) root.
        var edges = lazyRoot.Edges;

        Assert.That(edges, Is.Empty, "constraint-failing root has no successors");
        Assert.That(eagerHooks, Is.Empty, "eager must not hook a constraint-failing node");
        Assert.That(eagerPostHooks, Is.Empty, "eager must not post-hook a constraint-failing node");
        Assert.That(lazyHooks, Is.Empty, "lazy must not hook a constraint-failing node");
        Assert.That(lazyPostHooks, Is.Empty, "lazy must not post-hook a constraint-failing node");
    }
}
