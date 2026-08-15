// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>The typed semantic actions emitted by the structured process frontend.</summary>
public enum FulfillmentProcessAction
{
    /// <summary>The controller commits an atomic submit decision.</summary>
    Submit,

    /// <summary>The dual-write defect publishes work before committing the order.</summary>
    PublishWork,

    /// <summary>The dual-write defect accepts after publishing work.</summary>
    CommitAccepted,

    /// <summary>The dual-write defect rejects after publishing work.</summary>
    CommitRejected,

    /// <summary>A worker atomically selects and takes an outbox row.</summary>
    PickUp,

    /// <summary>A worker calls the gateway and records one selected outcome.</summary>
    CallGateway,

    /// <summary>A worker settles its recorded gateway outcome.</summary>
    Settle
}

/// <summary>
/// The controller and payment workers authored as structured
/// <see cref="ProcessSystemModel{TState}"/> processes.
///
/// <para>Each controller is a one-action structured loop. Each worker has an
/// explicit attempt frame, calls a checkpoint-bearing gateway helper through a
/// call frame, and uses <c>ChooseStep</c>
/// so selecting a gateway outcome and recording its effect are one atomic
/// semantic edge. There is no state-neutral choice configuration.</para>
/// </summary>
public static class ProcessFrontend
{
    private const string ControllerPrefix = "controller:o";
    private const string WorkerPrefix = "worker:w";

    /// <summary>Builds the fully composed controller/worker process system.</summary>
    public static ProcessSystemModel<StoreState> Build(FulfillmentConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        var model = new ProcessSystemModel<StoreState>(StoreState.Empty(config));
        for (var order = 0; order < config.Orders; order++)
        {
            var selectedOrder = order;
            if (config.AtomicSubmit)
            {
                model.Process(
                    ControllerRole(order),
                    context => context.Forever(
                        "submit-loop",
                        selectedOrder,
                        AtomicSubmitIteration));
            }
            else
            {
                RegisterDualWriteController(model, selectedOrder);
            }
        }

        for (var worker = 0; worker < config.Workers; worker++)
        {
            var arguments = EncodeWorkerArguments(
                worker,
                config.LeaseOutboxRows,
                config.IdempotentCharge);
            model.Process(
                WorkerRole(worker),
                context => context.Forever(
                    "attempt-loop",
                    arguments,
                    WorkerIteration));
        }

        return model;
    }

    /// <summary>Explores the fully composed structured process system.</summary>
    public static StateGraphNode BuildGraph(
        FulfillmentConfig config,
        bool lazy = false,
        int maxDepth = -1)
        => Build(config).Explore(maxDepth, lazy);

    /// <summary>
    /// Explores one worker from the database state left by an accepted submit.
    /// This closed graph is used for direct refinement against the hand-written
    /// worker sub-model.
    /// </summary>
    public static StateGraphNode BuildWorkerGraph(
        FulfillmentConfig config,
        int order = 0,
        int worker = 0,
        bool lazy = false,
        int maxDepth = -1)
    {
        ValidateCoordinates(config, order, worker);

        var arguments = EncodeWorkerArguments(
            worker,
            config.LeaseOutboxRows,
            config.IdempotentCharge);
        return new ProcessSystemModel<StoreState>(
                StoreState.AfterAcceptedSubmit(config, order))
            .Process(
                WorkerRole(worker),
                context => context.Forever(
                    "attempt-loop",
                    arguments,
                    WorkerIteration))
            .Explore(maxDepth, lazy);
    }

    /// <summary>The stable controller role for an order id.</summary>
    public static string ControllerRole(int order) => $"{ControllerPrefix}{order}";

    /// <summary>The stable worker role for a worker id.</summary>
    public static string WorkerRole(int worker) => $"{WorkerPrefix}{worker}";

    /// <summary>Returns the typed process metadata attached to an edge.</summary>
    public static ProcessTransition TransitionOf(StateGraphEdge edge)
        => edge?.Metadata as ProcessTransition ??
            throw new ArgumentException(
                "The edge is not from the structured fulfillment process model.",
                nameof(edge));

    /// <summary>
    /// Maps structured process metadata to the shared action label used by the
    /// hand-written and Operation frontends.
    /// </summary>
    public static FulfillmentAction Label(StateGraphEdge edge)
        => Label(TransitionOf(edge));

    /// <summary>Maps structured process metadata to the shared action label.</summary>
    public static FulfillmentAction Label(ProcessTransition transition)
    {
        if (transition == null) throw new ArgumentNullException(nameof(transition));
        if (transition.IsControl ||
            !(transition.SemanticAction is FulfillmentProcessAction action))
        {
            throw new InvalidOperationException(
                $"Unexpected process transition '{transition}'.");
        }

        var order = Convert.ToInt32(transition.Subject);
        return action switch
        {
            FulfillmentProcessAction.Submit => new FulfillmentAction(
                FulfillmentAction.Submit,
                order,
                StoreState.NoWorker,
                (SubmitOutcome)transition.Value),
            FulfillmentProcessAction.PublishWork => new FulfillmentAction(
                FulfillmentAction.PublishWork,
                order,
                StoreState.NoWorker),
            FulfillmentProcessAction.CommitAccepted => new FulfillmentAction(
                FulfillmentAction.CommitOrder,
                order,
                StoreState.NoWorker,
                SubmitOutcome.Accepted),
            FulfillmentProcessAction.CommitRejected => new FulfillmentAction(
                FulfillmentAction.CommitOrder,
                order,
                StoreState.NoWorker,
                SubmitOutcome.Rejected),
            FulfillmentProcessAction.PickUp => new FulfillmentAction(
                FulfillmentAction.PickUp,
                order,
                WorkerFromRole(transition.ProcessRole)),
            FulfillmentProcessAction.CallGateway => new FulfillmentAction(
                FulfillmentAction.CallGateway,
                order,
                WorkerFromRole(transition.ProcessRole),
                Gateway: (GatewayOutcome)transition.Value),
            FulfillmentProcessAction.Settle => new FulfillmentAction(
                FulfillmentAction.Settle,
                order,
                WorkerFromRole(transition.ProcessRole)),
            _ => throw new InvalidOperationException(
                $"Unknown fulfillment process action '{action}'.")
        };
    }

    /// <summary>Maps a concrete process edge to its matching hand-written edge.</summary>
    public static AbstractResponse MapToManual(
        RefinementTransition<StoreState> transition)
    {
        if (transition == null) throw new ArgumentNullException(nameof(transition));
        var expected = Label((ProcessTransition)transition.Metadata);
        return AbstractResponse.Matching(
            candidate => !candidate.IsStutter && Equals(candidate.Metadata, expected));
    }

    /// <summary>
    /// Weak scheduling fairness for every worker action and stable
    /// worker/order subject. One busy worker cannot discharge another worker's
    /// obligation.
    /// </summary>
    public static Fairness WorkersRun { get; } =
        WeakWorkerAction(FulfillmentProcessAction.PickUp) +
        WeakWorkerAction(FulfillmentProcessAction.CallGateway) +
        WeakWorkerAction(FulfillmentProcessAction.Settle);

    /// <summary>
    /// Collective strong action fairness for a definitive gateway answer.
    /// The selected <c>ChooseStep</c> value is carried by
    /// <see cref="ProcessTransition.Value"/>, so the environmental assumption
    /// is stated directly on the changing gateway-call edge.
    /// </summary>
    public static Fairness GatewayAnswersDefinitively { get; } =
        Fairness.StrongAction<ProcessTransition>(IsDefinitiveGatewayAnswer);

    /// <summary>The deliberately insufficient weak form of gateway fairness.</summary>
    public static Fairness WeakGatewayAnswersDefinitively { get; } =
        Fairness.WeakAction<ProcessTransition>(IsDefinitiveGatewayAnswer);

    /// <summary>Worker scheduling plus the external gateway assumption.</summary>
    public static Fairness ProgressAssumptions { get; } =
        WorkersRun + GatewayAnswersDefinitively;

    /// <summary>Weak action fairness for each order's controller process.</summary>
    public static Fairness ControllerRuns { get; } =
        Fairness.WeakEach<ProcessTransition, object>(
            transition =>
                transition.SemanticAction is FulfillmentProcessAction.Submit,
            transition => transition.Subject);

    /// <summary>Whether an edge is a definitive gateway answer.</summary>
    public static bool IsDefinitiveGatewayAnswer(ProcessTransition transition)
        => transition?.SemanticAction is FulfillmentProcessAction.CallGateway &&
            transition.Value is GatewayOutcome outcome &&
            (outcome == GatewayOutcome.Charged ||
                outcome == GatewayOutcome.Declined);

    private static Fairness WeakWorkerAction(FulfillmentProcessAction action)
        => Fairness.WeakEach<ProcessTransition, string>(
            transition => transition.SemanticAction is FulfillmentProcessAction actual &&
                actual == action,
            transition => $"{transition.ProcessRole}:o{transition.Subject}");

    private static async ModelTask AtomicSubmitIteration(
        ModelContext<StoreState> context,
        int order)
    {
        await context.ChooseStep(
            "submit-decision",
            FulfillmentProcessAction.Submit,
            state => state.Orders[order] == OrderStatus.Missing
                ? new[] { SubmitOutcome.Accepted, SubmitOutcome.Rejected }
                : new[] { SubmitOutcome.Duplicate },
            (state, outcome) =>
            {
                switch (outcome)
                {
                    case SubmitOutcome.Accepted:
                        state.CommitAcceptedOrder(order);
                        break;
                    case SubmitOutcome.Rejected:
                        state.CommitRejectedOrder(order);
                        break;
                    case SubmitOutcome.Duplicate:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(outcome));
                }
            },
            subject: _ => order);
    }

    private static void RegisterDualWriteController(
        ProcessSystemModel<StoreState> model,
        int order)
    {
        model.RepeatedAction(
            $"publisher:o{order}",
            state => state.Orders[order] == OrderStatus.Missing &&
                state.Outbox[order] == OutboxPhase.Absent,
            FulfillmentProcessAction.PublishWork,
            state => state.PublishPaymentWorkOnly(order),
            subject: order);
        model.RepeatedAction(
            $"commit-accepted:o{order}",
            state => state.Orders[order] == OrderStatus.Missing &&
                state.Outbox[order] != OutboxPhase.Absent,
            FulfillmentProcessAction.CommitAccepted,
            state => state.Orders[order] = OrderStatus.Submitted,
            subject: order);
        model.RepeatedAction(
            $"commit-rejected:o{order}",
            state => state.Orders[order] == OrderStatus.Missing &&
                state.Outbox[order] != OutboxPhase.Absent,
            FulfillmentProcessAction.CommitRejected,
            state => state.Orders[order] = OrderStatus.Rejected,
            subject: order);
    }

    private static async ModelTask WorkerIteration(
        ModelContext<StoreState> context,
        long arguments)
    {
        var worker = DecodeWorker(arguments);
        var lease = DecodeLease(arguments);
        var idempotent = DecodeIdempotent(arguments);

        await context.When(
            "work-available",
            state => state.WorkerOrder[worker] == StoreState.NoOrder &&
                state.Outbox.Any(phase => phase == OutboxPhase.Pending));

        var order = await context.ChooseStep(
            "pick-order",
            FulfillmentProcessAction.PickUp,
            state => PendingOrders(state, worker),
            (state, selected) => state.PickUpOutboxRow(selected, worker, lease),
            subject: selected => selected);

        await context.Call(
            "call-gateway",
            EncodeGatewayArguments(worker, order, idempotent),
            CallGateway);

        await context.Step(
            FulfillmentProcessAction.Settle,
            state => state.SettleAttempt(worker),
            subject: order);
    }

    private static async ModelTask CallGateway(
        ModelContext<StoreState> context,
        long arguments)
    {
        var worker = DecodeGatewayWorker(arguments);
        var order = DecodeGatewayOrder(arguments);
        var idempotent = DecodeGatewayIdempotent(arguments);

        await context.ChooseStep(
            "gateway-outcome",
            FulfillmentProcessAction.CallGateway,
            CallGatewayStep.GatewayOutcomes,
            (state, outcome) =>
                state.RecordGatewayCall(worker, outcome, idempotent),
            subject: _ => order);
    }

    private static IEnumerable<int> PendingOrders(StoreState state, int worker)
    {
        if (state.WorkerOrder[worker] != StoreState.NoOrder)
        {
            yield break;
        }

        for (var order = 0; order < state.Outbox.Length; order++)
        {
            if (state.Outbox[order] == OutboxPhase.Pending)
            {
                yield return order;
            }
        }
    }

    private static long EncodeWorkerArguments(
        int worker,
        bool lease,
        bool idempotent)
        => ((long)worker << 2) |
            (lease ? 2L : 0L) |
            (idempotent ? 1L : 0L);

    private static int DecodeWorker(long arguments) => (int)(arguments >> 2);

    private static bool DecodeLease(long arguments) => (arguments & 2L) != 0;

    private static bool DecodeIdempotent(long arguments) => (arguments & 1L) != 0;

    private static long EncodeGatewayArguments(
        int worker,
        int order,
        bool idempotent)
        => ((long)worker << 33) |
            ((long)order << 1) |
            (idempotent ? 1L : 0L);

    private static int DecodeGatewayWorker(long arguments)
        => (int)(arguments >> 33);

    private static int DecodeGatewayOrder(long arguments)
        => (int)((arguments >> 1) & uint.MaxValue);

    private static bool DecodeGatewayIdempotent(long arguments)
        => (arguments & 1L) != 0;

    private static int WorkerFromRole(string role)
    {
        if (role == null ||
            !role.StartsWith(WorkerPrefix, StringComparison.Ordinal) ||
            !int.TryParse(role.Substring(WorkerPrefix.Length), out var worker))
        {
            throw new InvalidOperationException($"Unexpected worker role '{role}'.");
        }

        return worker;
    }

    private static void ValidateCoordinates(
        FulfillmentConfig config,
        int order,
        int worker)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (order < 0 || order >= config.Orders)
        {
            throw new ArgumentOutOfRangeException(nameof(order));
        }

        if (worker < 0 || worker >= config.Workers)
        {
            throw new ArgumentOutOfRangeException(nameof(worker));
        }
    }
}
