// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Tests for the root→node traversal <c>path</c> that state-graph
/// construction hands to each step function's
/// <see cref="BaseStepFunction.Apply"/>. The path is reconstructed on demand
/// from each node's discovery back-pointers, so:
/// <list type="bullet">
/// <item>eager and lazy exploration hand every node an <em>identical</em>
/// path (the same discovery witness), even when a node is reachable by
/// several routes (a diamond);</item>
/// <item>the path is well formed — root first with a null incoming step,
/// this node last, and every interior element carrying a real step; and</item>
/// <item>a child's path is exactly its discovery parent's path plus the child
/// (the "parent's path + this node" construction).</item>
/// </list>
/// A separate check confirms the reconstructed path is memoized (repeat reads
/// return the same instance rather than recomputing).
/// </summary>
[TestFixture]
public class StateGraphPathTests
{
    #region Diamond model

    // Two independent bits, each flippable 0 -> 1 exactly once. From (0,0)
    // the graph is a diamond: (1,1) is reachable both via A-then-B and
    // B-then-A, so exactly one of those becomes its discovery witness path.
    private sealed class BitState : State
    {
        public int A { get; set; }

        public int B { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new BitState { A = this.A, B = this.B };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths, string path, bool forceRecompute)
            => $"({this.A},{this.B})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    // Flips one bit 0 -> 1, re-adding itself so the step set is stable.
    // Records the path it was handed, keyed by the expanding node, into the
    // currently-installed sink. Returns null once the bit is already 1.
    private sealed class FlipStep : BaseStepFunction
    {
        private readonly string id;
        private readonly bool flipA;

        public FlipStep(string id, bool flipA)
        {
            this.id = id;
            this.flipA = flipA;
        }

        public override string StepFunctionId => this.id;

        // The map recording, per expanding node, the path handed to Apply.
        public Dictionary<string, IReadOnlyList<(IStepFunction, StateGraphNode)>> Sink { get; set; }

        protected override IList<StepResult> ApplyInternal(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            // The node currently being expanded is the last path element.
            var nodeFingerprint = path[path.Count - 1].Item2.GetNodeFingerprint();
            if (!this.Sink.ContainsKey(nodeFingerprint))
            {
                this.Sink[nodeFingerprint] = path;
            }

            var s = (BitState)state;
            var bitIsSet = this.flipA ? s.A == 1 : s.B == 1;
            if (bitIsSet)
            {
                return null;
            }

            var next = (BitState)s.Clone();
            if (this.flipA)
            {
                next.A = 1;
            }
            else
            {
                next.B = 1;
            }

            return new[]
            {
                new StepResult { State = next, StepFunctions = new IStepFunction[] { this } },
            };
        }
    }

    #endregion

    private static string Sig(IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        => string.Join(
            " -> ",
            path.Select(e => (e.Item1?.StepFunctionId ?? "ε") + ":" + e.Item2.GetNodeFingerprint()));

    // Walk the whole graph so every node's edges (and, in lazy mode, its
    // on-demand expansion) are materialized, firing the recording steps.
    private static void MaterializeAll(StateGraphNode root)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<StateGraphNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!seen.Add(n.GetNodeFingerprint()))
            {
                continue;
            }

            foreach (var e in n.Edges)
            {
                stack.Push(e.Target);
            }
        }
    }

    private static Dictionary<string, IReadOnlyList<(IStepFunction, StateGraphNode)>> RecordPaths(
        FlipStep[] steps, BitState start, bool lazy, out StateGraphNode root)
    {
        var sink = new Dictionary<string, IReadOnlyList<(IStepFunction, StateGraphNode)>>();
        foreach (var s in steps)
        {
            s.Sink = sink;
        }

        root = StateGraph.ExploreStateGraph(steps, (BitState)start.Clone(), lazy: lazy);
        MaterializeAll(root);
        return sink;
    }

    [Test]
    public void EagerAndLazy_HandEachNode_TheIdenticalPath()
    {
        // Shared step instances so the two runs order steps identically and
        // therefore discover nodes in the same order.
        var steps = new[] { new FlipStep("A", flipA: true), new FlipStep("B", flipA: false) };
        var start = new BitState { A = 0, B = 0 };

        var eager = RecordPaths(steps, start, lazy: false, out var eagerRoot);
        var lazy = RecordPaths(steps, start, lazy: true, out _);

        // Sanity: the diamond really merges — all four bit combinations are
        // reached, including the doubly-reachable (1,1).
        Assert.That(eager.Count, Is.EqualTo(4), "expected the 4-node diamond to be fully explored");

        Assert.That(lazy.Keys.ToHashSet().SetEquals(eager.Keys), Is.True,
            "eager and lazy must expand (and record a path for) the same set of nodes");

        foreach (var key in eager.Keys)
        {
            Assert.That(Sig(lazy[key]), Is.EqualTo(Sig(eager[key])),
                $"eager and lazy handed node {key} different paths");
        }
    }

    [Test]
    public void ReconstructedPaths_AreWellFormed_AndPrefixClosed()
    {
        var steps = new[] { new FlipStep("A", flipA: true), new FlipStep("B", flipA: false) };
        var start = new BitState { A = 0, B = 0 };

        var paths = RecordPaths(steps, start, lazy: false, out var root);
        var rootFingerprint = root.GetNodeFingerprint();

        foreach (var (key, path) in paths.Select(kv => (kv.Key, kv.Value)))
        {
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(1), "a path is never empty");

            // Root first, with no incoming step.
            Assert.That(path[0].Item1, Is.Null, "the first path element (root) has no incoming step");
            Assert.That(path[0].Item2.GetNodeFingerprint(), Is.EqualTo(rootFingerprint),
                "every path starts at the root");

            // This node last.
            Assert.That(path[path.Count - 1].Item2.GetNodeFingerprint(), Is.EqualTo(key),
                "a path ends at the node it belongs to");

            // Only the root element lacks an incoming step.
            for (var i = 1; i < path.Count; i++)
            {
                Assert.That(path[i].Item1, Is.Not.Null,
                    "every non-root path element carries the step that reached it");
            }

            // Prefix closure: child path == discovery-parent path + child.
            if (path.Count >= 2)
            {
                var parentFingerprint = path[path.Count - 2].Item2.GetNodeFingerprint();
                Assert.That(paths.ContainsKey(parentFingerprint), Is.True,
                    "the discovery parent must itself have been expanded");

                var expectedParentSig = Sig(path.Take(path.Count - 1).ToList());
                Assert.That(Sig(paths[parentFingerprint]), Is.EqualTo(expectedParentSig),
                    "a node's path must be exactly its discovery parent's path plus itself");
            }
        }
    }

    [Test]
    public void ReconstructedPath_IsMemoized_SoRepeatReadsDoNotRecompute()
    {
        var steps = new[] { new FlipStep("A", flipA: true), new FlipStep("B", flipA: false) };
        foreach (var s in steps)
        {
            s.Sink = new Dictionary<string, IReadOnlyList<(IStepFunction, StateGraphNode)>>();
        }

        var root = StateGraph.ExploreStateGraph(steps, new BitState { A = 0, B = 0 });

        // Path is an internal member; read it twice via reflection and assert
        // the same instance comes back (i.e. it is flattened at most once).
        var pathProperty = typeof(StateGraphNode).GetProperty(
            "Path", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(pathProperty, Is.Not.Null, "StateGraphNode.Path should exist");

        var target = root.Edges[0].Target;
        var first = pathProperty.GetValue(target);
        var second = pathProperty.GetValue(target);

        Assert.That(first, Is.Not.Null);
        Assert.That(ReferenceEquals(first, second), Is.True,
            "the reconstructed path must be memoized and returned by reference");
    }
}
