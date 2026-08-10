// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace CoroutineModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// The executable two-worker equivalence case study for the experimental
/// coroutine front-end.
/// </summary>
public static class WorkerCompetitionCaseStudy
{
    /// <summary>The two finite workers competing for the one task.</summary>
    public static IReadOnlyList<string> Workers { get; } = new[] { "ada", "grace" };

    /// <summary>Builds the direct hand-written state graph.</summary>
    public static StateGraphNode BuildManualGraph()
        => StateGraph.ExploreStateGraph(
            Workers.Select(worker => (IStepFunction)new ClaimWorkerStep(worker)).ToList(),
            new WorkerTaskState());

    /// <summary>Builds the equivalent compiled coroutine state graph.</summary>
    public static StateGraphNode BuildCoroutineGraph()
        => CoroutineModel.Explore(
            "two-worker-task",
            new WorkerTaskState(),
            CoroutineWorkflow);

    /// <summary>Projects a compiled state to the domain state used by the manual graph.</summary>
    public static WorkerTaskState Project(WorkerTaskState state)
        => new WorkerTaskState
        {
            ClaimedBy = state.ClaimedBy,
            FinishedBy = state.FinishedBy
        };

    /// <summary>Gets raw node and edge counts for a complete graph.</summary>
    public static GraphSize GetRawGraphSize(StateGraphNode root)
    {
        var nodes = Reachable(root).ToArray();
        return new GraphSize(nodes.Length, nodes.Sum(node => node.Edges.Count));
    }

    /// <summary>Gets the domain states after discarding graph-control configuration.</summary>
    public static IReadOnlyCollection<WorkerDomainState> ProjectedDomainStates(
        StateGraphNode root)
        => Reachable(root)
            .Select(node => DomainState((WorkerTaskState)node.State))
            .ToHashSet();

    /// <summary>
    /// Gets the manual changing domain transitions. Their edge metadata carries
    /// the manual action directly.
    /// </summary>
    public static IReadOnlyCollection<WorkerDomainTransition> ManualChangingTransitions(
        StateGraphNode root)
        => Reachable(root)
            .SelectMany(node => node.Edges.Select(edge => new { node, edge }))
            .Select(item => new WorkerDomainTransition(
                DomainState((WorkerTaskState)item.node.State),
                (WorkerAction)item.edge.Metadata,
                DomainState((WorkerTaskState)item.edge.Target.State)))
            .ToHashSet();

    /// <summary>
    /// Gets the coroutine changing domain transitions after hiding its
    /// state-neutral Choose edges. Worker identity comes from typed replay
    /// metadata, never from the coroutine step-function ID.
    /// </summary>
    public static IReadOnlyCollection<WorkerDomainTransition>
        CoroutineChangingTransitionsHidingChoose(StateGraphNode root)
        => Reachable(root)
            .SelectMany(node => node.Edges.Select(edge => new { node, edge }))
            .Select(item => new
            {
                item.node,
                item.edge,
                Transition = (CoroutineTransition)item.edge.Metadata
            })
            .Where(item => item.Transition.Kind != ModelCheckpointKind.Choose)
            .Select(item => new WorkerDomainTransition(
                DomainState((WorkerTaskState)item.node.State),
                new WorkerAction(
                    item.Transition.CheckpointName switch
                    {
                        "claim" => WorkerActionKind.Claim,
                        "finish" => WorkerActionKind.Finish,
                        _ => throw new InvalidOperationException(
                            $"Unexpected coroutine checkpoint " +
                            $"'{item.Transition.CheckpointName}'.")
                    },
                    SelectedWorker(item.Transition)),
                DomainState((WorkerTaskState)item.edge.Target.State)))
            .ToHashSet();

    /// <summary>Identifies a manual claim action for ENABLED assertions.</summary>
    public static bool IsManualClaim(IStepFunction step)
        => step is ClaimWorkerStep;

    /// <summary>Identifies a compiled coroutine checkpoint without parsing its ID.</summary>
    public static bool IsCoroutineCheckpoint(
        IStepFunction step,
        ModelCheckpointKind kind,
        string checkpointName)
        => step is ICoroutineCheckpointStep checkpoint &&
            checkpoint.CheckpointKind == kind &&
            checkpoint.CheckpointName == checkpointName;

    /// <summary>
    /// Maps compiled coroutine edges to the corresponding manual action.
    /// Choose is hidden; claim and finish are identified from their typed
    /// replay prefix and mapped to the matching worker action.
    /// </summary>
    public static AbstractResponse MapCoroutineTransition(
        RefinementTransition<WorkerTaskState> transition)
    {
        if (!(transition.Metadata is CoroutineTransition coroutine))
        {
            throw new InvalidOperationException(
                "The worker coroutine graph must retain CoroutineTransition metadata.");
        }

        if (coroutine.Kind == ModelCheckpointKind.Choose)
        {
            return AbstractResponse.Hidden;
        }

        var action = new WorkerAction(
            coroutine.CheckpointName == "claim"
                ? WorkerActionKind.Claim
                : coroutine.CheckpointName == "finish"
                    ? WorkerActionKind.Finish
                    : throw new InvalidOperationException(
                        $"Unexpected coroutine checkpoint '{coroutine.CheckpointName}'."),
            SelectedWorker(coroutine));

        return AbstractResponse.Step(step => step.StepFunctionId == action.StepFunctionId);
    }

    private static async ModelTask CoroutineWorkflow(ModelContext<WorkerTaskState> context)
    {
        var worker = await context.Choose("worker", Workers);
        await context.Step("claim", state => state.ClaimedBy = worker);
        await context.Step("finish", state => state.FinishedBy = worker);
    }

    private static string SelectedWorker(CoroutineTransition transition)
    {
        var selection = transition.ReplayPrefix.SingleOrDefault(entry =>
            entry.Kind == ModelCheckpointKind.Choose && entry.Name == "worker");
        if (selection?.Value is string worker && Workers.Contains(worker))
        {
            return worker;
        }

        throw new InvalidOperationException(
            $"Coroutine transition '{transition}' has no valid worker replay selection.");
    }

    private static WorkerDomainState DomainState(WorkerTaskState state)
        => new WorkerDomainState(state.ClaimedBy, state.FinishedBy);

    private static IEnumerable<StateGraphNode> Reachable(StateGraphNode root)
    {
        var seen = new HashSet<string>();
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

    private sealed class ClaimWorkerStep : BaseStepFunction
    {
        private readonly string worker;

        internal ClaimWorkerStep(string worker)
        {
            this.worker = worker;
        }

        public override string StepFunctionId => new WorkerAction(
            WorkerActionKind.Claim,
            worker).StepFunctionId;

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var task = (WorkerTaskState)state;
            if (task.ClaimedBy != null)
            {
                return null;
            }

            var next = (WorkerTaskState)task.Clone();
            next.ClaimedBy = worker;
            return new[]
            {
                new StepResult
                {
                    State = next,
                    StepFunctions = new IStepFunction[] { new FinishWorkerStep(worker) },
                    EdgeMetadata = new WorkerAction(WorkerActionKind.Claim, worker)
                }
            };
        }
    }

    private sealed class FinishWorkerStep : BaseStepFunction
    {
        private readonly string worker;

        internal FinishWorkerStep(string worker)
        {
            this.worker = worker;
        }

        public override string StepFunctionId => new WorkerAction(
            WorkerActionKind.Finish,
            worker).StepFunctionId;

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var task = (WorkerTaskState)state;
            if (task.ClaimedBy != worker || task.FinishedBy != null)
            {
                return null;
            }

            var next = (WorkerTaskState)task.Clone();
            next.FinishedBy = worker;
            return new[]
            {
                new StepResult
                {
                    State = next,
                    EdgeMetadata = new WorkerAction(WorkerActionKind.Finish, worker)
                }
            };
        }
    }
}

/// <summary>The domain state for the one-task worker model.</summary>
public sealed class WorkerTaskState : State
{
    /// <summary>The worker that claimed the task, if any.</summary>
    public string ClaimedBy { get; set; }

    /// <summary>The worker that completed the task, if any.</summary>
    public string FinishedBy { get; set; }

    /// <summary>Whether the one task has been completed.</summary>
    public bool IsCompleted => FinishedBy != null;

    protected override void CloneInternal(Dictionary<object, object> clonedMap)
        => clonedMap[this] = new WorkerTaskState
        {
            ClaimedBy = ClaimedBy,
            FinishedBy = FinishedBy
        };

    protected override string StringRepresentationInternal(
        Dictionary<object, string> objectPaths,
        string path,
        bool forceRecompute)
        => $"ClaimedBy={ClaimedBy ?? "none"},FinishedBy={FinishedBy ?? "none"}";

    protected override void FreezeComponents(HashSet<object> visited)
    {
    }
}

/// <summary>The two domain actions exposed by the hand-written graph.</summary>
public enum WorkerActionKind
{
    /// <summary>Claim the task.</summary>
    Claim,

    /// <summary>Finish the claimed task.</summary>
    Finish
}

/// <summary>A stable domain action label.</summary>
public sealed record WorkerAction(WorkerActionKind Kind, string Worker)
{
    /// <summary>The stable hand-written step-function identity.</summary>
    public string StepFunctionId => $"{Kind.ToString().ToLowerInvariant()}({Worker})";

    /// <inheritdoc/>
    public override string ToString() => StepFunctionId;
}

/// <summary>A domain-state projection that intentionally excludes control state.</summary>
public sealed record WorkerDomainState(string ClaimedBy, string FinishedBy);

/// <summary>One changing projected domain transition.</summary>
public sealed record WorkerDomainTransition(
    WorkerDomainState Source,
    WorkerAction Action,
    WorkerDomainState Target);

/// <summary>Raw compiled graph counts.</summary>
public sealed record GraphSize(int Nodes, int Edges);
