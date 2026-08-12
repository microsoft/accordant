// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;

/// <summary>
/// Builds the fulfillment state graphs. Every builder returns an ordinary
/// <see cref="StateGraphNode"/>: the three frontends differ only in how the
/// transitions were authored.
/// </summary>
public static class FulfillmentModel
{
    /// <summary>
    /// The complete hand-written model: controller actions and worker actions
    /// as <see cref="IStepFunction"/> implementations, all active from the
    /// start and guarded by the database state.
    /// </summary>
    public static StateGraphNode BuildNativeGraph(FulfillmentConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        return StateGraph.ExploreStateGraph(
            ControllerSteps(config).Concat(WorkerSteps(config)).ToList(),
            StoreState.Empty(config));
    }

    /// <summary>
    /// The worker half of the hand-written model, explored from a database that
    /// a correct controller already committed. This is the closed sub-model the
    /// coroutine frontend is compared against.
    /// </summary>
    public static StateGraphNode BuildNativeWorkerGraph(
        FulfillmentConfig config,
        int order = 0)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        return StateGraph.ExploreStateGraph(
            WorkerSteps(config).ToList(),
            StoreState.AfterAcceptedSubmit(config, order));
    }

    /// <summary>The controller step functions of the hand-written model.</summary>
    public static IEnumerable<IStepFunction> ControllerSteps(FulfillmentConfig config)
    {
        for (var order = 0; order < config.Orders; order++)
        {
            if (config.AtomicSubmit)
            {
                yield return new SubmitOrderStep(config, order);
            }
            else
            {
                yield return new PublishPaymentWorkStep(config, order);
                yield return new CommitOrderRowStep(config, order);
            }
        }
    }

    /// <summary>
    /// The worker step functions of the hand-written model. They are scoped per
    /// (worker, order) so the <c>Operation</c> frontend can queue exactly the
    /// handlers for one order id when the controller accepts it, without ever
    /// adding a duplicate step-function identity to a graph node.
    /// </summary>
    public static IEnumerable<IStepFunction> WorkerSteps(FulfillmentConfig config)
    {
        for (var worker = 0; worker < config.Workers; worker++)
        {
            for (var order = 0; order < config.Orders; order++)
            {
                foreach (var step in WorkerStepsFor(config, worker, order))
                {
                    yield return step;
                }
            }
        }
    }

    /// <summary>The three sequential worker actions for one (worker, order) pair.</summary>
    public static IStepFunction[] WorkerStepsFor(
        FulfillmentConfig config,
        int worker,
        int order)
        => new IStepFunction[]
        {
            new PickUpOutboxRowStep(config, worker, order),
            new CallGatewayStep(config, worker, order),
            new SettleAttemptStep(config, worker, order)
        };

    /// <summary>
    /// The handlers the controller queues for one accepted order, across every
    /// worker.
    /// </summary>
    public static IList<IStepFunction> QueuedWorkFor(FulfillmentConfig config, int order)
    {
        var steps = new List<IStepFunction>();
        for (var worker = 0; worker < config.Workers; worker++)
        {
            steps.AddRange(WorkerStepsFor(config, worker, order));
        }

        return steps;
    }

    /// <summary>Enumerates every node reachable from a fully explored graph.</summary>
    public static IEnumerable<StateGraphNode> Reachable(StateGraphNode root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<StateGraphNode>();
        pending.Push(root);

        while (pending.Count > 0)
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

    /// <summary>The node and edge counts of a fully explored graph.</summary>
    public static (int Nodes, int Edges) Size(StateGraphNode root)
    {
        var nodes = Reachable(root).ToArray();
        return (nodes.Length, nodes.Sum(node => node.Edges.Count));
    }

    /// <summary>
    /// Whether exploration was complete. A depth frontier is not a terminal
    /// state, so a definitive verdict requires there to be none.
    /// </summary>
    public static bool IsComplete(StateGraphNode root)
        => Reachable(root).All(node => !node.IsDepthFrontier);

    /// <summary>
    /// The projected database and gateway state, discarding graph control
    /// information. Two frontends are compared on this projection, never on raw
    /// node identity.
    /// </summary>
    public static string ProjectDomain(StoreState store)
        => store.StringRepresentation();

    /// <summary>
    /// The changing domain transitions of a graph, labelled by the
    /// <see cref="FulfillmentAction"/> each frontend attaches to its edges.
    /// State-neutral edges — the idempotent duplicate submit, and the
    /// coroutine's control choices — are excluded, because the checker's
    /// stutter classification excludes them too.
    /// </summary>
    public static IReadOnlyCollection<(string Source, string Action, string Target)>
        ChangingDomainTransitions(
            StateGraphNode root,
            Func<StateGraphEdge, FulfillmentAction> label)
    {
        if (label == null) throw new ArgumentNullException(nameof(label));

        var transitions = new HashSet<(string, string, string)>();
        foreach (var node in Reachable(root))
        {
            var source = ProjectDomain((StoreState)node.State);
            foreach (var edge in node.Edges)
            {
                var target = ProjectDomain((StoreState)edge.Target.State);
                if (string.Equals(source, target, StringComparison.Ordinal))
                {
                    continue;
                }

                var action = label(edge);
                if (action == null)
                {
                    continue;
                }

                transitions.Add((source, action.ToString(), target));
            }
        }

        return transitions;
    }

    /// <summary>The distinct projected domain states of a graph.</summary>
    public static IReadOnlyCollection<string> DomainStates(StateGraphNode root)
        => Reachable(root)
            .Select(node => ProjectDomain((StoreState)node.State))
            .ToHashSet(StringComparer.Ordinal);
}
