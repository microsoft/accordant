namespace DurableJobs;

using Microsoft.Accordant;

internal static class ModelGraph
{
    internal static IEnumerable<StateGraphNode> Nodes(StateGraphNode root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            root.GetNodeFingerprint()
        };
        var pending = new Queue<StateGraphNode>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            yield return node;

            foreach (var edge in node.Edges)
            {
                if (seen.Add(edge.Target.GetNodeFingerprint()))
                {
                    pending.Enqueue(edge.Target);
                }
            }
        }
    }

    internal static IEnumerable<(StateGraphNode Source, StateGraphEdge Edge)> Edges(
        StateGraphNode root)
    {
        foreach (var source in Nodes(root))
        {
            foreach (var edge in source.Edges)
            {
                yield return (source, edge);
            }
        }
    }

    internal static (int Nodes, int Edges) Measure(StateGraphNode root)
    {
        var nodes = Nodes(root).ToArray();
        return (nodes.Length, nodes.Sum(node => node.Edges.Count));
    }

    internal static bool IsComplete(StateGraphNode root)
        => Nodes(root).All(node => !node.IsDepthFrontier);
}
