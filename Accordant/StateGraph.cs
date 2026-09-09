// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.ExceptionServices;
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
        ValidateExplorationInputs(steps, startingState, maxDepth);

        if (lazy && !generateStateGraph)
        {
            throw new ArgumentException(
                "Lazy exploration builds the graph on demand and therefore requires generateStateGraph = true.",
                nameof(generateStateGraph));
        }

        EnsureStateFrozen(startingState, nameof(startingState));

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
        // Node interning is collision-safe, so node references are the
        // identity used by the traversal. A compact fingerprint is useful
        // for diagnostics but must not decide whether a node was processed.
        var processed = new HashSet<StateGraphNode>();
        var stack = new Stack<StateGraphNode>();

        stack.Push(rootGraphNode);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (!processed.Add(node))
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
                if (!processed.Contains(child))
                {
                    stack.Push(child);
                }
            }
        }

        return generateStateGraph ?
            rootGraphNode :
            null;
    }

    private static void ValidateExplorationInputs(
        IList<IStepFunction> steps,
        IState startingState,
        int maxDepth)
    {
        if (steps == null)
        {
            throw new ArgumentNullException(nameof(steps));
        }

        if (startingState == null)
        {
            throw new ArgumentNullException(nameof(startingState));
        }

        if (maxDepth < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth),
                maxDepth,
                "maxDepth must be -1 for unbounded exploration or a non-negative depth.");
        }

        ValidateStepFunctionList(steps, "The initial step-function set");
    }

    internal static void EnsureStateFrozen(IState state, string context)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (!state.IsFrozen)
        {
            state.Freeze();
        }

        if (!state.IsFrozen)
        {
            throw new InvalidOperationException(
                $"The {context} state did not become frozen after Freeze(). " +
                "IState implementations must make Freeze establish immutability.");
        }
    }

    internal static void ValidateStepFunctionList(
        IEnumerable<IStepFunction> stepFunctions,
        string context)
    {
        if (stepFunctions == null)
        {
            throw new ArgumentNullException(nameof(stepFunctions));
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stepFunction in stepFunctions)
        {
            if (stepFunction == null)
            {
                throw new ArgumentException(
                    $"{context} cannot contain null step functions.",
                    nameof(stepFunctions));
            }

            var stepFunctionId = stepFunction.StepFunctionId;
            if (string.IsNullOrEmpty(stepFunctionId))
            {
                throw new ArgumentException(
                    $"{context} contains a step function with a null or empty StepFunctionId.",
                    nameof(stepFunctions));
            }

            if (!seenIds.Add(stepFunctionId))
            {
                throw new ArgumentException(
                    $"{context} contains duplicate StepFunctionId '{stepFunctionId}'.",
                    nameof(stepFunctions));
            }
        }
    }

    internal static void ValidateStepResult(
        StepResult stepResult,
        IStepFunction sourceStepFunction,
        int resultIndex)
    {
        if (stepResult == null)
        {
            throw new InvalidOperationException(
                $"Step function '{sourceStepFunction.StepFunctionId}' returned a null StepResult at index {resultIndex}.");
        }

        if (stepResult.State == null)
        {
            throw new InvalidOperationException(
                $"Step function '{sourceStepFunction.StepFunctionId}' returned a StepResult with a null State at index {resultIndex}.");
        }

        if (stepResult.StepFunctions != null)
        {
            ValidateStepFunctionList(
                stepResult.StepFunctions,
                $"StepResult {resultIndex} from step function '{sourceStepFunction.StepFunctionId}'");
        }
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
        ValidateStepFunctionList(stepFunctions, "The node's step-function set");

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
                    path,
                    stepFunction);
            }

            if (stepResults == null || stepResults.Count == 0)
            {
                continue;
            }

            var resultIndex = 0;
            foreach (var stepResult in stepResults)
            {
                try
                {
                    ValidateStepResult(stepResult, stepFunction, resultIndex);
                    EnsureStateFrozen(
                        stepResult.State,
                        $"StepResult {resultIndex} from step function '{stepFunction.StepFunctionId}'");
                }
                catch (Exception ex)
                {
                    throw new StepFunctionApplicationException(
                        ex,
                        node,
                        path,
                        stepFunction);
                }

                if (shouldIncludeStepFunctionResult != null &&
                    !shouldIncludeStepFunctionResult(state, stepFunction, stepResult))
                {
                    resultIndex++;
                    continue;
                }

                IList<IStepFunction> orderedStepFunctions;
                try
                {
                    var newStepFunctions = stepFunctions
                        .Where(s => s.StepFunctionId != stepFunction.StepFunctionId)
                        .ToList();
                    if (stepResult.StepFunctions != null)
                    {
                        newStepFunctions.AddRange(stepResult.StepFunctions);
                    }

                    ValidateStepFunctionList(
                        newStepFunctions,
                        $"Successor step-function set from '{stepFunction.StepFunctionId}'");
                    orderedStepFunctions = newStepFunctions
                        .OrderBy(s => s.StepFunctionId)
                        .ToList();
                }
                catch (Exception ex)
                {
                    throw new StepFunctionApplicationException(
                        ex,
                        node,
                        path,
                        stepFunction);
                }

                yield return (
                    stepFunction,
                    stepResult.State,
                    orderedStepFunctions,
                    stepResult.EdgeMetadata);

                resultIndex++;
            }
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

    internal StateGraphNode(
        IState state,
        IList<IStepFunction> stepFunctions,
        StateGraphExpander lazyExpander,
        StateGraphNode discoveredFrom,
        IStepFunction discoveredVia,
        int depth)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        if (stepFunctions == null)
        {
            throw new ArgumentNullException(nameof(stepFunctions));
        }

        var stepFunctionSnapshot = new List<IStepFunction>(stepFunctions);
        StateGraph.ValidateStepFunctionList(stepFunctionSnapshot, "Node step functions");
        StepFunctions = stepFunctionSnapshot.AsReadOnly();
        LazyExpander = lazyExpander;
        DiscoveredFrom = discoveredFrom;
        DiscoveredVia = discoveredVia;
        Depth = depth;
    }

    /// <summary>
    /// The system state represented by this node.
    /// </summary>
    public IState State { get; }

    /// <summary>
    /// The set of step functions that can be applied to this state.
    /// Once a step function is applied, it is _consumed_ and not part of the
    /// updated state (though a step function can produce new step functions that
    /// are included in the step function list for the updated state).
    /// </summary>
    public IReadOnlyList<IStepFunction> StepFunctions { get; }

    private List<StateGraphEdge> edges = new List<StateGraphEdge>();

    private enum ExpansionState
    {
        NotExpanded,
        Expanding,
        Expanded,
        Failed
    }

    private ExpansionState expansionState;
    private ExceptionDispatchInfo expansionFailure;

    /// <summary>
    /// The expander bound to this node in lazy (on-the-fly) exploration, which
    /// computes the node's outgoing edges the first time <see cref="Edges"/> is
    /// accessed. This is the single flag distinguishing the two modes:
    /// <list type="bullet">
    /// <item><c>null</c> ⇒ an <b>eager</b> node. The eager worklist has already
    /// computed and stored its edges via <see cref="SetExpandedEdges"/>, so the
    /// lazy machinery is inert and <see cref="Edges"/> behaves as a plain
    /// read-only list.</item>
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
    public IReadOnlyList<StateGraphEdge> Edges
    {
        get
        {
            EnsureExpanded();
            return edges;
        }
    }

    /// <summary>
    /// Ensures this node's outgoing edges have been computed. A no-op for
    /// eager nodes (<see cref="LazyExpander"/> is <c>null</c>)
    /// and idempotent for lazy nodes (expansion runs at most once).
    /// If lazy expansion fails, the original exception is cached and rethrown
    /// on subsequent accesses rather than exposing a partial empty graph.
    /// </summary>
    internal void EnsureExpanded()
    {
        if (expansionState == ExpansionState.Expanded)
        {
            return;
        }

        if (expansionState == ExpansionState.Failed)
        {
            expansionFailure.Throw();
            return;
        }

        // Preserve the previous re-entrant behavior: an expansion callback
        // reading this node's Edges observes the current backing list instead
        // of recursively expanding the same node.
        if (expansionState == ExpansionState.Expanding)
        {
            return;
        }

        expansionState = ExpansionState.Expanding;
        try
        {
            if (LazyExpander != null)
            {
                edges = LazyExpander.ExpandNode(this);
            }

            expansionState = ExpansionState.Expanded;
        }
        catch (Exception ex)
        {
            expansionFailure = ExceptionDispatchInfo.Capture(ex);
            expansionState = ExpansionState.Failed;
            throw;
        }
    }

    /// <summary>
    /// Stores the eagerly-computed outgoing edges for this node and marks it
    /// expanded so that a later <see cref="Edges"/> access is a no-op rather
    /// than triggering (re)computation. Used by the eager explorer, whose
    /// worklist has already produced the node's edges.
    /// </summary>
    internal void SetExpandedEdges(IReadOnlyList<StateGraphEdge> computedEdges)
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

        if (computedEdges == null)
        {
            throw new ArgumentNullException(nameof(computedEdges));
        }

        var edgeSnapshot = new List<StateGraphEdge>(computedEdges);
        foreach (var edge in edgeSnapshot)
        {
            if (edge == null)
            {
                throw new ArgumentException("Expanded edges cannot contain null entries.", nameof(computedEdges));
            }
        }

        if (expansionState != ExpansionState.NotExpanded)
        {
            throw new InvalidOperationException(
                "Expanded edges can only be assigned to a node that has not started expansion.");
        }

        edges = edgeSnapshot;
        expansionState = ExpansionState.Expanded;
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
            nodeFingerprint = GetNodeFingerprint(State, StepFunctions.ToList());
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
        if (nodeLabelLambda == null)
        {
            nodeLabelLambda = DefaultNodeLabelLambda;
        }

        var edges = new List<(string, string, string)>();
        var nodes = new List<(string, string)>();

        var seenSet = new HashSet<StateGraphNode>();
        var nodeIds = new Dictionary<StateGraphNode, string>();
        var nextNodeId = 0;

        string GetNodeId(StateGraphNode node)
        {
            if (!nodeIds.TryGetValue(node, out var nodeId))
            {
                nodeId = $"N{nextNodeId++}";
                nodeIds[node] = nodeId;
            }

            return nodeId;
        }

        void CollectEdges(StateGraphNode node)
        {
            if (!seenSet.Add(node))
            {
                return;
            }

            var nodeLabel = nodeLabelLambda(node);

            if (showStepFunctionsInNode)
            {
                nodeLabel += "\\n" +
                 (node.StepFunctions == null ? "[]" : ("[" + string.Join(",", node.StepFunctions.Select(s => s.StepFunctionId)) + "]"));
            }

            nodes.Add((
                GetNodeId(node),
                nodeLabel.Replace("\"", "\\\"")));

            foreach (var edge in node.Edges)
            {
                var edgeLabel = edge.Metadata != null ?
                    edge.Metadata.ToString() :
                    edge.StepFunction.StepFunctionId;

                edges.Add((
                    GetNodeId(node),
                    GetNodeId(edge.Target),
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

    /// <summary>
    /// Returns the compact diagnostic fingerprint for a state and its enabled
    /// step functions. This value is intentionally not used as graph identity:
    /// it is a 64-bit display hash and therefore can collide.
    /// </summary>
    public static string GetNodeFingerprint(
        IState state,
        IList<IStepFunction> stepFunctions)
    {
        var fastKey = GetFastNodeKey(state, stepFunctions);
        var bytes = Encoding.UTF8.GetBytes(fastKey);
        var hash = XxHash64.HashToUInt64(bytes);
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns the cheap lookup key used by graph interning. Equal fast keys
    /// are still compared using the canonical state representation before two
    /// nodes are considered identical.
    /// </summary>
    internal static string GetFastNodeKey(
        IState state,
        IList<IStepFunction> stepFunctions)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        StateGraph.ValidateStepFunctionList(stepFunctions, "Fingerprint step functions");

        var stepFunctionSignature = new StringBuilder();
        foreach (var stepFunction in stepFunctions.OrderBy(
            s => s.StepFunctionId,
            StringComparer.Ordinal))
        {
            var id = stepFunction.StepFunctionId;
            stepFunctionSignature
                .Append(id.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(id);
        }

        return state.GetStateHash().ToString(CultureInfo.InvariantCulture) +
            "-" +
            stepFunctionSignature;
    }

    /// <summary>
    /// Returns the exact logical state representation required to resolve a
    /// collision in <see cref="GetFastNodeKey"/>.
    /// </summary>
    internal static string GetCanonicalStateRepresentation(IState state)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var representation = state.StringRepresentation();
        if (representation == null)
        {
            throw new InvalidOperationException(
                $"State of type '{state.GetType().Name}' returned a null StringRepresentation().");
        }

        return representation;
    }

    /// <summary>
    /// Compares two candidate nodes after their cheap hash/signature keys have
    /// matched. The canonical representation is computed only on this slow
    /// collision path.
    /// </summary>
    internal static bool HasSameNodeIdentity(
        StateGraphNode existingNode,
        IState state,
        IList<IStepFunction> stepFunctions)
    {
        if (existingNode == null)
        {
            throw new ArgumentNullException(nameof(existingNode));
        }

        if (GetFastNodeKey(existingNode.State, existingNode.StepFunctions.ToList()) !=
            GetFastNodeKey(state, stepFunctions))
        {
            return false;
        }

        return string.Equals(
            GetCanonicalStateRepresentation(existingNode.State),
            GetCanonicalStateRepresentation(state),
            StringComparison.Ordinal);
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
    internal StateGraphEdge(
        StateGraphNode target,
        IStepFunction stepFunction,
        object metadata)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        StepFunction = stepFunction ?? throw new ArgumentNullException(nameof(stepFunction));
        Metadata = metadata;
    }

    /// <summary>
    /// The target state graph node.
    /// </summary>
    public StateGraphNode Target { get; }

    /// <summary>
    /// The step function that takes the system to the target
    /// state graph node.
    /// </summary>
    public IStepFunction StepFunction { get; }

    /// <summary>
    /// Metadata associated with the edge.
    /// </summary>
    public object Metadata { get; }
}
