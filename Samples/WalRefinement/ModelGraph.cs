namespace WalRefinement;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>Breadth-first walk over an explored graph, used by the tests.</summary>
internal static class ModelGraph
{
    /// <summary>Every reachable node, each visited once.</summary>
    internal static IEnumerable<StateGraphNode> Nodes(StateGraphNode root)
    {
        var seen = new HashSet<string> { root.GetNodeFingerprint() };
        var queue = new Queue<StateGraphNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;
            foreach (var edge in node.Edges)
            {
                if (seen.Add(edge.Target.GetNodeFingerprint()))
                {
                    queue.Enqueue(edge.Target);
                }
            }
        }
    }

    /// <summary>Every reachable edge.</summary>
    internal static IEnumerable<(StateGraphNode Source, StateGraphEdge Edge)> Edges(
        StateGraphNode root)
    {
        foreach (var node in Nodes(root))
        {
            foreach (var edge in node.Edges)
            {
                yield return (node, edge);
            }
        }
    }

    /// <summary>The node and edge counts of an explored graph.</summary>
    internal static (int Nodes, int Edges) Measure(StateGraphNode root)
    {
        var nodes = 0;
        var edges = 0;
        foreach (var node in Nodes(root))
        {
            nodes++;
            edges += node.Edges.Count;
        }

        return (nodes, edges);
    }
}
