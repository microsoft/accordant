// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace CoroutineModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// Executable measurements showing why an explicit loop rebase is required
/// for ordinary graph-cycle recognition.
/// </summary>
public static class LoopReplayCaseStudy
{
    /// <summary>Builds the bounded graph for a natural loop without a rebase.</summary>
    public static StateGraphNode BuildNaiveGraph(int maxDepth = 4)
        => CoroutineModel.Explore(
            "naive-toggle-loop",
            new LoopToggleState(),
            NaiveWorkflow,
            maxDepth: maxDepth);

    /// <summary>Builds the same loop with an internal replay rebase.</summary>
    public static StateGraphNode BuildLoopGraph()
        => CoroutineModel.Explore(
            "rebased-toggle-loop",
            new LoopToggleState(),
            RebasedWorkflow);

    /// <summary>Measures the bounded naive replay graph.</summary>
    public static ReplayGraphMeasurement MeasureNaive(int maxDepth = 4)
        => Measure(BuildNaiveGraph(maxDepth));

    /// <summary>Measures the finite graph compiled with <see cref="ModelContext{TState}.Loop(string)"/>.</summary>
    public static ReplayGraphMeasurement MeasureRebased()
        => Measure(BuildLoopGraph());

    private static ReplayGraphMeasurement Measure(StateGraphNode root)
    {
        var nodes = Reachable(root).ToArray();
        var active = nodes
            .Where(node => node.StepFunctions.Count != 0)
            .Select(node => (ICoroutineCheckpointStep)node.StepFunctions.Single())
            .ToArray();
        return new ReplayGraphMeasurement(
            Nodes: nodes.Length,
            Edges: nodes.Sum(node => node.Edges.Count),
            DistinctContinuationIdentities: active
                .Select(step => step.StepFunctionId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            LongestVisibleReplayTape: active
                .Select(step => step.ReplayPrefix.Count)
                .DefaultIfEmpty(0)
                .Max(),
            HasDepthFrontier: nodes.Any(node => node.IsDepthFrontier),
            HasGraphCycle: HasCycle(root));
    }

    private static async ModelTask NaiveWorkflow(ModelContext<LoopToggleState> context)
    {
        while (true)
        {
            await context.Step("toggle", state => state.On = !state.On);
        }
    }

    private static async ModelTask RebasedWorkflow(ModelContext<LoopToggleState> context)
    {
        while (true)
        {
            await context.Loop("iteration");
            await context.Step("toggle", state => state.On = !state.On);
        }
    }

    private static IEnumerable<StateGraphNode> Reachable(StateGraphNode root)
    {
        var seen = new HashSet<string>();
        var pending = new Stack<StateGraphNode>();
        pending.Push(root);

        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.GetNodeFingerprint()))
            {
                continue;
            }

            yield return node;
            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }
    }

    private static bool HasCycle(StateGraphNode root)
    {
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();

        bool Visit(StateGraphNode node)
        {
            var fingerprint = node.GetNodeFingerprint();
            if (visiting.Contains(fingerprint))
            {
                return true;
            }
            if (!visited.Add(fingerprint))
            {
                return false;
            }
            visiting.Add(fingerprint);

            foreach (var edge in node.Edges)
            {
                if (Visit(edge.Target))
                {
                    return true;
                }
            }

            visiting.Remove(fingerprint);
            return false;
        }

        return Visit(root);
    }
}

/// <summary>Measured graph and replay-control characteristics.</summary>
public sealed record ReplayGraphMeasurement(
    int Nodes,
    int Edges,
    int DistinctContinuationIdentities,
    int LongestVisibleReplayTape,
    bool HasDepthFrontier,
    bool HasGraphCycle);

/// <summary>Domain state for the loop replay measurement.</summary>
public sealed class LoopToggleState : State
{
    /// <summary>Current toggle value.</summary>
    public bool On { get; set; }

    protected override void CloneInternal(Dictionary<object, object> clonedMap)
        => clonedMap[this] = new LoopToggleState { On = On };

    protected override string StringRepresentationInternal(
        Dictionary<object, string> objectPaths,
        string path,
        bool forceRecompute)
        => $"On={On}";

    protected override void FreezeComponents(HashSet<object> visited)
    {
    }
}
