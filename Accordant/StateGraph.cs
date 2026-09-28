// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Text;

/// <summary>
/// This class contains methods a number of methods related to state graph.
/// </summary>
public static class StateGraph
{
    /// <summary>
    /// This method explores the state graph starting from the given state
    /// and applying the step functions in some order, then exploring the updated
    /// states and any step functions produced by the earlier set, and so on.
    /// It can either exhaustively explore the complete state graph (which can be _very_
    /// large, even infinite) or explore up to a given depth.
    /// It can be instructed to either generate the state graph and return it so the
    /// caller can inspect it and use it for further processing, or it can just traverse
    /// the state graph w/o returning it. In the latter case, the caller probably also gives
    /// a hook to run at each node of the state graph (though a hook can also be given if
    /// a state graph is requested as well),
    /// </summary>
    public static StateGraphNode ExploreStateGraph(
        IList<IStepFunction> steps,
        IState startingState,
        int maxDepth = -1,
        bool generateStateGraph = true,
        Action<StateGraphNode> hook = null,
        Action<StateGraphNode> postHook = null,
        Func<IState, bool> stateConstraint = null,
        Func<IState, IStepFunction, StepResult, bool> shouldIncludeStepFunctionResult = null,
        bool lazy = false)
    {
        if (steps == null)
        {
            throw new ArgumentNullException(nameof(steps));
        }

        if (startingState == null)
        {
            throw new ArgumentNullException(nameof(startingState));
        }

        if (steps.Any(s => s == null))
        {
            throw new ArgumentException(
                "The step function list must not contain null entries.",
                nameof(steps));
        }

        if (maxDepth < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth),
                maxDepth,
                "maxDepth must be -1 (unbounded) or a non-negative depth bound.");
        }

        if (lazy && !generateStateGraph)
        {
            throw new ArgumentException(
                "Lazy exploration builds the graph on demand and therefore requires generateStateGraph = true.",
                nameof(generateStateGraph));
        }

        var expander = new StateGraphExpander(
            maxDepth,
            stateConstraint,
            shouldIncludeStepFunctionResult,
            hook,
            postHook,
            lazy);

        var rootGraphNode = expander.GetOrCreateNode(
            startingState,
            steps.OrderBy(s => s.StepFunctionId).ToList(),
            discoveredFrom: null,
            discoveredVia: null,
            depth: 1);

        if (lazy)
        {
            // Return a root whose outgoing edges (and, transitively, the
            // whole reachable graph) materialize on demand as the graph is
            // walked — e.g. by the model-checking emptiness search, which
            // then stops at the first counterexample without ever building
            // the unreached remainder of the graph.
            return rootGraphNode;
        }

        // Eager exploration: drive the same per-node expansion over a
        // worklist so every reachable node is materialized up front. Each
        // node is expanded at most once (its first pop); the resulting edges
        // are stored on the node and their targets are queued for expansion.
        // Both the traversal path and the depth handed to expansion are read
        // from each node (reconstructed from its discovery back-pointers),
        // exactly as in lazy mode, so the worklist carries only the nodes.
        var processed = new HashSet<string>();
        var stack = new Stack<StateGraphNode>();

        stack.Push(rootGraphNode);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (!processed.Add(node.GetNodeFingerprint()))
            {
                continue;
            }

            var edges = expander.ExpandNode(node);

            if (generateStateGraph)
            {
                node.SetExpandedEdges(edges);
            }

            foreach (var edge in edges)
            {
                var child = edge.Target;
                if (!processed.Contains(child.GetNodeFingerprint()))
                {
                    stack.Push(child);
                }
            }
        }

        return generateStateGraph ?
            rootGraphNode :
            null;
    }

    /// <summary>
    /// Generates the raw successors of a node — the single source of truth
    /// for successor generation shared by eager
    /// (<see cref="ExploreStateGraph"/>) and lazy
    /// (<see cref="StateGraphExpander.ExpandNode"/>) construction.
    ///
    /// <para>For each step function attached to the node it invokes
    /// <see cref="IStepFunction.Apply"/> (wrapping failures in a
    /// <see cref="StepFunctionApplicationException"/>), skips empty results,
    /// applies the optional <paramref name="shouldIncludeStepFunctionResult"/>
    /// filter, and computes the successor's step-function set (the current
    /// step consumed, any newly-produced steps added, ordered by id).</para>
    ///
    /// <para>It deliberately does <em>not</em> apply the
    /// <c>stateConstraint</c> or <c>maxDepth</c> bounds, perform node
    /// interning, or build edges: those differ between the eager and lazy
    /// drivers and remain each driver's responsibility. Keeping only the
    /// path-independent per-step logic here guarantees the two drivers can
    /// never silently diverge on how a successor state and its step-function
    /// set are derived.</para>
    /// </summary>
    internal static IEnumerable<(
        IStepFunction stepFunction,
        IState childState,
        IList<IStepFunction> childStepFunctions,
        object edgeMetadata)> GenerateSuccessors(
        StateGraphNode node,
        IReadOnlyList<(IStepFunction, StateGraphNode)> path,
        Func<IState, IStepFunction, StepResult, bool> shouldIncludeStepFunctionResult)
    {
        var state = node.State;
        var stepFunctions = node.StepFunctions;

        foreach (var stepFunction in stepFunctions)
        {
            IList<StepResult> stepResults;

            try
            {
                stepResults = stepFunction.Apply(state, path);
            }
            catch (Exception ex)
            {
                throw new StepFunctionApplicationException(
                    ex,
                    node,
                    path.ToList(),
                    stepFunction);
            }

            if (stepResults == null || stepResults.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < stepResults.Count; i++)
            {
                var stepResult = stepResults[i];
                ValidateStepResult(stepFunction, i, stepResult);

                if (shouldIncludeStepFunctionResult != null &&
                    !shouldIncludeStepFunctionResult(state, stepFunction, stepResult))
                {
                    continue;
                }

                var newStepFunctions = stepFunctions
                    .Where(s => s.StepFunctionId != stepFunction.StepFunctionId)
                    .ToList();
                if (stepResult.StepFunctions != null)
                {
                    newStepFunctions.AddRange(stepResult.StepFunctions);
                }

                yield return (
                    stepFunction,
                    stepResult.State,
                    newStepFunctions.OrderBy(s => s.StepFunctionId).ToList(),
                    stepResult.EdgeMetadata);
            }
        }
    }

    /// <summary>
    /// Validates that a step function honored its contract: every returned
    /// <see cref="StepResult"/> is non-null, carries a non-null
    /// <see cref="StepResult.State"/>, and lists no null step functions.
    /// A violation is a bug in the step function (or the step function it
    /// produced), so fail loudly and name the offender instead of letting the
    /// null surface as an obscure failure deep inside hashing or fingerprinting.
    /// </summary>
    private static void ValidateStepResult(
        IStepFunction stepFunction,
        int index,
        StepResult stepResult)
    {
        if (stepResult == null)
        {
            throw new InvalidOperationException(
                $"Step function '{stepFunction.StepFunctionId}' returned a null step result at index {index}.");
        }

        if (stepResult.State == null)
        {
            throw new InvalidOperationException(
                $"Step function '{stepFunction.StepFunctionId}' returned a step result with a null state at index {index}.");
        }

        if (stepResult.StepFunctions != null && stepResult.StepFunctions.Any(sf => sf == null))
        {
            throw new InvalidOperationException(
                $"Step function '{stepFunction.StepFunctionId}' returned a null step function in the step result at index {index}.");
        }
    }
}

/// <summary>
/// A state graph node represents a unique state along with the set of
/// step functions that can be applied to that state (if enabled).
/// The state and set of step functions lead to a unique node fingerprint.
/// </summary>
public class StateGraphNode
{
    private string nodeFingerprint = null;

    /// <summary>
    /// The system state represented by this node.
    /// </summary>
    public IState State { get; set; }

    /// <summary>
    /// The set of step functions that can be applied to this state.
    /// Once a step function is applied, it is _consumed_ and not part of the
    /// updated state (though a step function can produce new step functions that
    /// are included in the step function list for the updated state).
    /// </summary>
    public IList<IStepFunction> StepFunctions { get; set; }

    private List<StateGraphEdge> edges = new List<StateGraphEdge>();

    private bool expanded;

    /// <summary>
    /// The expander bound to this node in lazy (on-the-fly) exploration, which
    /// computes the node's outgoing edges the first time <see cref="Edges"/> is
    /// accessed. This is the single flag distinguishing the two modes:
    /// <list type="bullet">
    /// <item><c>null</c> ⇒ an <b>eager</b> (or manually constructed) node. The
    /// eager worklist has already computed and stored its edges via
    /// <see cref="SetExpandedEdges"/>, so the lazy machinery is inert and
    /// <see cref="Edges"/> behaves as a plain list.</item>
    /// <item>non-<c>null</c> ⇒ a <b>lazy</b> node
    /// (<c>StateGraph.ExploreStateGraph(..., lazy: true)</c>) that materializes
    /// its edges on first <see cref="Edges"/> access.</item>
    /// </list>
    /// </summary>
    internal StateGraphExpander LazyExpander { get; set; }

    /// <summary>
    /// Discovery depth of this node (root = 1), set once when the node is
    /// first created. Both eager and lazy expansion read it to create
    /// successors at <c>Depth + 1</c> and to honor the construction-time
    /// <c>maxDepth</c> bound.
    /// </summary>
    internal int Depth { get; set; }

    /// <summary>
    /// The node from which this node was first discovered (its parent in the
    /// discovery tree), or <c>null</c> for the root. Together with
    /// <see cref="DiscoveredVia"/> this forms an immutable, prefix-shared
    /// chain from any node back to the root — the single source of truth for
    /// the traversal <see cref="Path"/>, used identically by eager and lazy
    /// exploration. Set exactly once, when the node is first created.
    /// </summary>
    internal StateGraphNode DiscoveredFrom { get; set; }

    /// <summary>
    /// The step function whose edge first reached this node, or <c>null</c>
    /// for the root. See <see cref="DiscoveredFrom"/>.
    /// </summary>
    internal IStepFunction DiscoveredVia { get; set; }

    private IReadOnlyList<(IStepFunction, StateGraphNode)> materializedPath;

    /// <summary>
    /// The root→this traversal path handed to step functions during
    /// expansion, in the shape <c>[(null, root), …, (DiscoveredVia, this)]</c>
    /// (root first, this node last).
    ///
    /// <para>The path is <em>not</em> stored eagerly: it is reconstructed on
    /// demand by walking the <see cref="DiscoveredFrom"/>/<see cref="DiscoveredVia"/>
    /// back-pointer chain — which is O(1) memory per node and structurally
    /// shared across descendants — and then flattened once and memoized here,
    /// so repeat readers never recompute. Eager and lazy exploration derive
    /// the path the same way, so a given node is handed an identical path in
    /// either mode.</para>
    ///
    /// <para>This is the <em>discovery</em> witness path: interning means a
    /// node is expanded at most once, so exactly one of the (possibly many)
    /// root→node paths is materialized. Because successor generation is
    /// path-independent, the path only feeds step functions that read history
    /// (e.g. to prune), never the computed successor set.</para>
    /// </summary>
    internal IReadOnlyList<(IStepFunction, StateGraphNode)> Path
    {
        get
        {
            if (materializedPath == null)
            {
                var reversed = new List<(IStepFunction, StateGraphNode)>();
                for (var node = this; node != null; node = node.DiscoveredFrom)
                {
                    reversed.Add((node.DiscoveredVia, node));
                }

                reversed.Reverse();
                materializedPath = reversed;
            }

            return materializedPath;
        }
    }

    /// <summary>
    /// Edges which lead to the outgoing set of state graph nodes.
    ///
    /// <para>For lazily explored nodes the outgoing edges are computed and
    /// memoized on first access via <see cref="EnsureExpanded"/>. This makes
    /// on-the-fly model checking transparent to every consumer that walks
    /// the graph through <see cref="Edges"/> (emptiness checkers, dot
    /// visualization, BFS traversals) — the graph materializes only as far
    /// as it is actually walked.</para>
    /// </summary>
    public List<StateGraphEdge> Edges
    {
        get
        {
            EnsureExpanded();
            return edges;
        }

        set => edges = value;
    }

    /// <summary>
    /// Ensures this node's outgoing edges have been computed. A no-op for
    /// eager / manually built nodes (<see cref="LazyExpander"/> is <c>null</c>)
    /// and idempotent for lazy nodes (expansion runs at most once).
    /// </summary>
    internal void EnsureExpanded()
    {
        if (expanded)
        {
            return;
        }

        // Mark expanded before invoking the expander so that any re-entrant
        // access to this node's Edges during expansion returns the
        // (currently empty) backing list rather than recursing.
        expanded = true;

        if (LazyExpander != null)
        {
            edges = LazyExpander.ExpandNode(this);
        }
    }

    /// <summary>
    /// Stores the eagerly-computed outgoing edges for this node and marks it
    /// expanded so that a later <see cref="Edges"/> access is a no-op rather
    /// than triggering (re)computation. Used by the eager explorer, whose
    /// worklist has already produced the node's edges.
    /// </summary>
    internal void SetExpandedEdges(List<StateGraphEdge> computedEdges)
    {
        // Eager and lazy are mutually exclusive per node: an eager node must
        // never be lazy-bound, or a later Edges access would re-expand it
        // (with an empty path) and overwrite these edges. Guard explicitly so
        // the invariant fails loudly in every build, not just DEBUG.
        if (LazyExpander != null)
        {
            throw new InvalidOperationException(
                "SetExpandedEdges is the eager store path and must not be called on a " +
                "lazy-bound node (LazyExpander != null).");
        }

        edges = computedEdges;
        expanded = true;
    }

    /// <summary>
    /// Returns the node fingerprint which is a hash computed over the state hash
    /// along with the set of step functions that can be applied to this state (if enabled).
    /// </summary>
    /// <returns></returns>
    public string GetNodeFingerprint()
    {
        if (nodeFingerprint == null)
        {
            Invariant.Assert(State != null, "StateGraphNode.State must not be null.");
            Invariant.Assert(StepFunctions != null, "StateGraphNode.StepFunctions must not be null.");
            nodeFingerprint = GetNodeFingerprint(State, StepFunctions);
        }

        return nodeFingerprint;
    }

    public string GenerateDotFileContent(
        Func<StateGraphNode, string> nodeLabelLambda = null,
        bool showStepFunctionsInNode = true)
    {
        return GenerateDotFileContent(
            this,
            nodeLabelLambda,
            showStepFunctionsInNode);
    }

    /// <summary>
    /// This method generates a GraphViz dot file to help visualize the state graph.
    /// </summary>
    public static string GenerateDotFileContent(
        StateGraphNode rootNode,
        Func<StateGraphNode, string> nodeLabelLambda = null,
        bool showStepFunctionsInNode = true)
    {
        if (rootNode == null)
        {
            throw new ArgumentNullException(nameof(rootNode));
        }

        if (nodeLabelLambda == null)
        {
            nodeLabelLambda = DefaultNodeLabelLambda;
        }

        var edges = new List<(string, string, string)>();
        var nodes = new List<(string, string)>();

        var seenSet = new HashSet<string>();
        void CollectEdges(StateGraphNode node)
        {
            if (seenSet.Contains(node.GetNodeFingerprint()))
            {
                return;
            }

            seenSet.Add(node.GetNodeFingerprint());

            var nodeLabel = nodeLabelLambda(node);

            if (showStepFunctionsInNode)
            {
                nodeLabel += "\\n" +
                 (node.StepFunctions == null ? "[]" : ("[" + string.Join(",", node.StepFunctions.Select(s => s.StepFunctionId)) + "]"));
            }

            nodes.Add((
                node.GetNodeFingerprint().Substring(0, 5),
                nodeLabel.Replace("\"", "\\\"")));

            foreach (var edge in node.Edges)
            {
                var edgeLabel = edge.Metadata != null ?
                    edge.Metadata.ToString() :
                    edge.StepFunction.StepFunctionId;

                edges.Add((
                    node.GetNodeFingerprint().Substring(0, 5),
                    edge.Target.GetNodeFingerprint().Substring(0, 5),
                    edgeLabel.Replace("\"", "\\\"")));

                CollectEdges(edge.Target);
            }
        }

        CollectEdges(rootNode);

        var lines = new List<string>();
        lines.Add("digraph G {");

        foreach (var (node, label) in nodes)
        {
            lines.Add($"\"{node}\" [label=\"{label}\"];");
        }

        // Group edges between the same pair of nodes and combine their labels
        var groupedEdges = edges
            .GroupBy(e => (e.Item1, e.Item2))
            .Select(g => (
                source: g.Key.Item1,
                target: g.Key.Item2,
                label: string.Join("\\n", g.Select(e => e.Item3))));

        foreach (var (source, target, label) in groupedEdges)
        {
            lines.Add($"\"{source}\" -> \"{target}\" [label=\"{label}\"];");
        }

        lines.Add("}");

        return string.Join("\r\n", lines);
    }

    public static string GetNodeFingerprint(
        IState state,
        IList<IStepFunction> stepFunctions)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (stepFunctions == null)
        {
            throw new ArgumentNullException(nameof(stepFunctions));
        }

        // Hash the state hash and the ordered step-function ids directly into a
        // single XxHash64. The previous implementation built a joined id string, the
        // combined string and a UTF-8 byte[] of the whole thing before hashing;
        // appending the pieces incrementally yields an equally stable fingerprint
        // with a fraction of the allocations.
        var hasher = new XxHash64();

        Span<byte> stateHashBytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(stateHashBytes, state.GetStateHash());
        hasher.Append(stateHashBytes);

        if (IsOrderedByStepFunctionId(stepFunctions))
        {
            // The common case: step functions handed out by the graph are already
            // ordered, so skip the LINQ sort entirely.
            for (var i = 0; i < stepFunctions.Count; i++)
            {
                AppendUtf8(hasher, stepFunctions[i].StepFunctionId);
            }
        }
        else
        {
            foreach (var stepFunction in stepFunctions.OrderBy(s => s.StepFunctionId))
            {
                AppendUtf8(hasher, stepFunction.StepFunctionId);
            }
        }

        return hasher.GetCurrentHashAsUInt64().ToString("x16");
    }

    private static readonly IComparer<string> StepFunctionIdComparer =
        Comparer<string>.Default;

    private static readonly byte[] Utf8Separator = { 0 };

    private static bool IsOrderedByStepFunctionId(IList<IStepFunction> stepFunctions)
    {
        for (var i = 1; i < stepFunctions.Count; i++)
        {
            if (StepFunctionIdComparer.Compare(
                    stepFunctions[i - 1].StepFunctionId,
                    stepFunctions[i].StepFunctionId) > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Appends a UTF-8 string followed by a separator byte, so the hash is not
    /// ambiguous across field boundaries (e.g. "ab"+"c" vs "a"+"bc").
    /// </summary>
    private static void AppendUtf8(XxHash64 hasher, string value)
    {
        hasher.Append(Encoding.UTF8.GetBytes(value ?? string.Empty));
        hasher.Append(Utf8Separator);
    }

    /// <summary>
    /// This method prints out the literal contents of the node in the
    /// state graph node.
    /// </summary>
    public static string DefaultNodeLabelLambda(StateGraphNode node)
    {
        return node.State.ToString();
    }

    /// <summary>
    /// This method creates a count based node label lambda, where it returns a
    /// a label with a monotonically increasing count each time it's called,
    /// so a sequence like N1, N2, N3, ... and so on.
    /// </summary>
    /// <returns></returns>
    public static Func<StateGraphNode, string> CreateCountBasedNodeLabelLambda()
    {
        int count = 0;

        return _ =>
        {
            return $"N{++count}";
        };
    }
}

/// <summary>
/// The edge of the state graph. The edge includes the target state graph node
/// and the step function whose application takes the system to the target state
/// graph node.
/// </summary>
public class StateGraphEdge
{
    /// <summary>
    /// The target state graph node.
    /// </summary>
    public StateGraphNode Target { get; set; }

    /// <summary>
    /// The step function that takes the system to the target
    /// state graph node.
    /// </summary>
    public IStepFunction StepFunction { get; set; }

    /// <summary>
    /// Metadata associated with the edge.
    /// </summary>
    public object Metadata { get; set; }
}
