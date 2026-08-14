// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>A breadth-first walk used by the tests.</summary>
public static class ModelGraph
{
    /// <summary>Every reachable node, each visited once.</summary>
    public static IReadOnlyList<StateGraphNode> Reachable(StateGraphNode root)
    {
        var seen = new HashSet<string>();
        var order = new List<StateGraphNode>();
        var pending = new Stack<StateGraphNode>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.GetNodeFingerprint()))
            {
                continue;
            }

            order.Add(node);
            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }

        return order;
    }

    /// <summary>The node and edge counts of a complete graph.</summary>
    public static (int Nodes, int Edges) Size(StateGraphNode root)
    {
        var nodes = Reachable(root);
        return (nodes.Count, nodes.Sum(node => node.Edges.Count));
    }

    /// <summary>Whether no reachable node was cut off by the depth bound.</summary>
    public static bool IsComplete(StateGraphNode root)
        => Reachable(root).All(node => !node.IsDepthFrontier);

    /// <summary>The distinct domain states, discarding process control configuration.</summary>
    public static IReadOnlyCollection<string> DomainStates(StateGraphNode root)
        => Reachable(root)
            .Select(node => node.State.StringRepresentation())
            .ToHashSet();

    /// <summary>The live process roles at a node.</summary>
    public static IReadOnlyList<string> LiveRoles(StateGraphNode node)
        => ((IProcessSchedulerStep)node.StepFunctions.Single())
            .LiveProcesses
            .Select(process => process.Role)
            .OrderBy(role => role)
            .ToList();

    /// <summary>The live processes at a node, with their failure-domain names.</summary>
    public static IReadOnlyList<ProcessInstance> LiveProcesses(StateGraphNode node)
        => ((IProcessSchedulerStep)node.StepFunctions.Single()).LiveProcesses;

    /// <summary>The process transition metadata on an edge.</summary>
    public static ProcessTransition Transition(StateGraphEdge edge)
        => (ProcessTransition)edge.Metadata;
}
