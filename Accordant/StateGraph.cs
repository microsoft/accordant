// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
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
        Func<IState, IStepFunction, StepResult, bool> shouldIncludeStepFunctionResult = null)
    {
        var processedNodeMap = new Dictionary<string, StateGraphNode>();
        var nodeMap = new Dictionary<string, StateGraphNode>();
        var parentChildMap = new Dictionary<
            string,
            List<(StateGraphNode child, IStepFunction step, object edgeMetadata)>>();
        var stack = new Stack<(
            StateGraphNode node,
            int depth,
            StateGraphNode parent,
            IStepFunction parentStep,
            object edgeMetadata,
            ImmutableList<(IStepFunction, StateGraphNode)> path)>();

        // Helper method to return a state graph node if it's already been seen,
        // or create a new one otherwise.
        StateGraphNode GetOrCreateStateGraphNode(
            IState state,
            IList<IStepFunction> stepFunctions)
        {
            var nodeFingerprint = StateGraphNode.GetNodeFingerprint(
                state,
                stepFunctions);

            if (!nodeMap.ContainsKey(nodeFingerprint))
            {
                nodeMap[nodeFingerprint] = new StateGraphNode()
                {
                    State = state,
                    StepFunctions = stepFunctions
                };
            }

            return nodeMap[nodeFingerprint];
        }

        var rootGraphNode = GetOrCreateStateGraphNode(
            startingState,
            steps.OrderBy(s => s.StepFunctionId).ToList());

        stack.Push((
            rootGraphNode,
            1,
            null,
            null,
            null,
            ImmutableList<(IStepFunction, StateGraphNode)>.Empty.Add((null, rootGraphNode))));

        while (stack.Count > 0)
        {
            var (node, depth, parent, parentStep, edgeMetadata, path) = stack.Pop();

            if (maxDepth != -1 && depth > maxDepth)
            {
                continue;
            }

            if (stateConstraint != null && !stateConstraint(node.State))
            {
                continue;
            }

            if (generateStateGraph && parent != null)
            {
                var parentFingerprint = parent.GetNodeFingerprint();

                if (!parentChildMap.ContainsKey(parentFingerprint))
                {
                    parentChildMap[parentFingerprint] =
                        new List<(StateGraphNode child, IStepFunction step, object edgeMetadata)>();
                }

                parentChildMap[parentFingerprint].Add((node, parentStep, edgeMetadata));
            }

            var fingerprint = node.GetNodeFingerprint();
            if (processedNodeMap.ContainsKey(fingerprint))
            {
                continue;
            }

            processedNodeMap[fingerprint] = node;

            var state = node.State;
            var stepFunctions = node.StepFunctions;

            if (hook != null)
            {
                hook(node);
            }

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

                foreach (var stepResult in stepResults)
                {
                    if (shouldIncludeStepFunctionResult != null &&
                        !shouldIncludeStepFunctionResult(
                        state,
                        stepFunction,
                        stepResult))
                    {
                        continue;
                    }

                    var newStepFunctions = stepFunctions.Where(s => s.StepFunctionId != stepFunction.StepFunctionId).ToList();
                    if (stepResult.StepFunctions != null)
                    {
                        newStepFunctions.AddRange(stepResult.StepFunctions);
                    }

                    var nextGraphNode = GetOrCreateStateGraphNode(
                        stepResult.State,
                        newStepFunctions.OrderBy(s => s.StepFunctionId).ToList());

                    stack.Push((
                        nextGraphNode,
                        depth + 1,
                        node,
                        stepFunction,
                        stepResult.EdgeMetadata,
                        path.Add((stepFunction, nextGraphNode))));
                }
            }

            if (postHook != null)
            {
                postHook(node);
            }
        }

        if (generateStateGraph)
        {
            foreach (var kvp in parentChildMap)
            {
                var parentFingerprint = kvp.Key;
                var parent = processedNodeMap[parentFingerprint];

                foreach (var (child, step, edgeMetadata) in kvp.Value)
                {
                    var childFingerprint = child.GetNodeFingerprint();
                    var exists = parent.Edges.Any(e =>
                        e.StepFunction.StepFunctionId == step.StepFunctionId &&
                        e.Target.GetNodeFingerprint() == childFingerprint);

                    if (!exists)
                    {
                        parent.Edges.Add(new StateGraphEdge()
                        {
                            StepFunction = step,
                            Target = child,
                            Metadata = edgeMetadata
                        });
                    }
                }
            }
        }

        return generateStateGraph ?
            rootGraphNode :
            null;
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

    private static SHA256 SHA256 = SHA256.Create();

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

    /// <summary>
    /// Edges which lead to the outgoing set of state graph nodes.
    /// </summary>
    public List<StateGraphEdge> Edges { get; set; } = new List<StateGraphEdge>();

    /// <summary>
    /// Returns the node fingerprint which is a hash computed over the state hash
    /// along with the set of step functions that can be applied to this state (if enabled).
    /// </summary>
    /// <returns></returns>
    public string GetNodeFingerprint()
    {
        if (nodeFingerprint == null)
        {
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
        // Combine state hash with step function IDs for node fingerprint
        var nodeState =
            state.GetStateHash().ToString() + "-" +
            string.Join(string.Empty, stepFunctions.OrderBy(s => s.StepFunctionId).Select(s => s.StepFunctionId));

        // Use XxHash64 for fast node fingerprinting
        var bytes = Encoding.UTF8.GetBytes(nodeState);
        var hash = XxHash64.HashToUInt64(bytes);
        return hash.ToString("x16");
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
