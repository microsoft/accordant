// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The single driver for state-graph construction, shared by both the eager
/// and the lazy (on-the-fly) exploration modes so that the two apply the
/// state constraint, depth bound, node interning, edge de-duplication and
/// pre/post hooks in exactly the same way. Every node — in either mode — is
/// expanded through <see cref="ExpandNode"/>.
///
/// <para>The modes differ in only three, deliberate, ways:</para>
/// <list type="number">
/// <item><b>Who drives expansion.</b> The eager explorer
/// (<see cref="StateGraph.ExploreStateGraph"/>) walks a worklist and expands
/// every reachable node up front. The lazy driver expands a node the first
/// time its <see cref="StateGraphNode.Edges"/> are accessed
/// (<see cref="StateGraphNode.EnsureExpanded"/>), which is what lets model
/// checking stop at the first counterexample without materializing the
/// unreached remainder of the graph.</item>
/// <item><b>The traversal path handed to step functions.</b> Both modes
/// hand step functions the same reconstructed root→node
/// <see cref="StateGraphNode.Path"/> (walked from the node's discovery
/// back-pointers), so a given node is expanded with an identical path in
/// either mode. Successor generation is path-independent for model
/// programs; the path only feeds step functions that read history.</item>
/// <item><b>Whether created nodes self-expand.</b> Lazy nodes carry a back
/// reference to this expander (<see cref="StateGraphNode.LazyExpander"/>) so
/// their edges materialize on demand; eager nodes do not, since the worklist
/// has already computed and stored their edges.</item>
/// </list>
/// </summary>
internal sealed class StateGraphExpander
{
    private readonly int maxDepth;
    private readonly Func<IState, bool> stateConstraint;
    private readonly Func<IState, IStepFunction, StepResult, bool> shouldIncludeStepFunctionResult;
    private readonly Action<StateGraphNode> hook;
    private readonly Action<StateGraphNode> postHook;
    private readonly bool lazy;

    // Shared fingerprint -> node intern map. Guarantees that two paths
    // reaching the same (state, step-functions) fingerprint resolve to the
    // same node object, so a state's edges are computed at most once and the
    // graph stays a proper DAG-with-cycles.
    private readonly Dictionary<string, StateGraphNode> nodeMap =
        new Dictionary<string, StateGraphNode>();

    public StateGraphExpander(
        int maxDepth,
        Func<IState, bool> stateConstraint,
        Func<IState, IStepFunction, StepResult, bool> shouldIncludeStepFunctionResult,
        Action<StateGraphNode> hook,
        Action<StateGraphNode> postHook,
        bool lazy)
    {
        this.maxDepth = maxDepth;
        this.stateConstraint = stateConstraint;
        this.shouldIncludeStepFunctionResult = shouldIncludeStepFunctionResult;
        this.hook = hook;
        this.postHook = postHook;
        this.lazy = lazy;
    }

    /// <summary>
    /// Returns the interned node for a given (state, step-functions) pair,
    /// creating a fresh node if it has not been seen before. A newly created
    /// node records the parent and edge it was first reached through
    /// (<see cref="StateGraphNode.DiscoveredFrom"/>/<see cref="StateGraphNode.DiscoveredVia"/>)
    /// so its traversal <see cref="StateGraphNode.Path"/> can be reconstructed
    /// on demand. In lazy mode the new node is bound to this expander so it
    /// expands on first <see cref="StateGraphNode.Edges"/> access; in eager
    /// mode it is left unbound because the worklist expands it directly. An
    /// existing node keeps its original discovery back-pointer and
    /// <see cref="StateGraphNode.Depth"/>.
    /// </summary>
    internal StateGraphNode GetOrCreateNode(
        IState state,
        IList<IStepFunction> stepFunctions,
        StateGraphNode discoveredFrom,
        IStepFunction discoveredVia,
        int depth)
    {
        var fingerprint = StateGraphNode.GetNodeFingerprint(state, stepFunctions);

        if (!nodeMap.TryGetValue(fingerprint, out var node))
        {
            node = new StateGraphNode
            {
                State = state,
                StepFunctions = stepFunctions,
                LazyExpander = this.lazy ? this : null,
                DiscoveredFrom = discoveredFrom,
                DiscoveredVia = discoveredVia,
                Depth = depth
            };

            nodeMap[fingerprint] = node;
        }

        return node;
    }

    /// <summary>
    /// Expands a single node — the one place both modes compute a node's
    /// outgoing edges. It is the eager worklist's per-node step and, via
    /// <see cref="StateGraphNode.EnsureExpanded"/>, the lazy on-demand entry
    /// point invoked the first time a node's edges are accessed.
    /// A node failing the state constraint contributes no
    /// edges and fires no hook. Otherwise the pre-hook runs, successors are
    /// drawn from the path-independent
    /// <see cref="StateGraph.GenerateSuccessors"/> kernel (handed the node's
    /// reconstructed root→node <see cref="StateGraphNode.Path"/>), each
    /// successor is filtered by the state constraint and the depth bound, the
    /// surviving target states are interned — recording the discovery
    /// back-pointer and depth — and recorded as de-duplicated edges, and the
    /// post-hook runs.
    ///
    /// <para>Both the traversal path and the depth are read from the node
    /// itself (<see cref="StateGraphNode.Path"/> / <see cref="StateGraphNode.Depth"/>),
    /// set once at discovery, so eager and lazy expand a given node
    /// identically without either driver having to thread that context.</para>
    /// </summary>
    /// <param name="node">The node to expand; successors are created at
    /// <see cref="StateGraphNode.Depth"/> + 1.</param>
    internal List<StateGraphEdge> ExpandNode(StateGraphNode node)
    {
        var edges = new List<StateGraphEdge>();

        // A node failing the state constraint is treated as having no
        // successors and is not hooked.
        if (stateConstraint != null && !stateConstraint(node.State))
        {
            return edges;
        }

        hook?.Invoke(node);

        var childDepth = node.Depth + 1;
        var withinDepth = maxDepth == -1 || childDepth <= maxDepth;

        foreach (var (stepFunction, childState, childStepFunctions, edgeMetadata) in
            StateGraph.GenerateSuccessors(node, node.Path, shouldIncludeStepFunctionResult))
        {
            // Drop edges leading to constraint-violating states or beyond the
            // depth bound; such targets never become nodes or edges.
            if (stateConstraint != null && !stateConstraint(childState))
            {
                continue;
            }

            if (!withinDepth)
            {
                node.IsDepthFrontier = true;
                continue;
            }

            var child = GetOrCreateNode(childState, childStepFunctions, node, stepFunction, childDepth);

            var childFingerprint = child.GetNodeFingerprint();
            var alreadyPresent = edges.Any(e =>
                e.StepFunction.StepFunctionId == stepFunction.StepFunctionId &&
                e.Target.GetNodeFingerprint() == childFingerprint &&
                Equals(e.Metadata, edgeMetadata));

            if (!alreadyPresent)
            {
                edges.Add(new StateGraphEdge
                {
                    StepFunction = stepFunction,
                    Target = child,
                    Metadata = edgeMetadata
                });
            }
        }

        postHook?.Invoke(node);
        return edges;
    }
}
