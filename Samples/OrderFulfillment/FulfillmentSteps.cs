// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;
using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>The response the checkout controller returns to the customer.</summary>
public enum SubmitOutcome
{
    /// <summary>Risk screening passed; the order and its payment work are committed.</summary>
    Accepted,

    /// <summary>Risk screening refused; only the order row is committed.</summary>
    Rejected,

    /// <summary>The order id already exists; the controller is idempotent and writes nothing.</summary>
    Duplicate
}

/// <summary>
/// A labelled action in the fulfillment model. Both the hand-written frontend
/// and the <c>Operation</c> frontend attach one of these to every edge, so
/// traces, refinement mappings, and fairness predicates can name actions
/// without parsing step-function identities.
/// </summary>
public sealed record FulfillmentAction(
    string Kind,
    int Order,
    int Worker,
    SubmitOutcome? Response = null,
    GatewayOutcome? Gateway = null)
{
    /// <summary>The controller commits an accept/reject/duplicate decision.</summary>
    public const string Submit = "submit";

    /// <summary>The dual-write defect's first write: publish payment work.</summary>
    public const string PublishWork = "publish-work";

    /// <summary>The dual-write defect's second write: commit the order row.</summary>
    public const string CommitOrder = "commit-order";

    /// <summary>A worker picks up an outbox row.</summary>
    public const string PickUp = "pick-up";

    /// <summary>A worker calls the external payment gateway.</summary>
    public const string CallGateway = "call-gateway";

    /// <summary>A worker commits the gateway response to the database.</summary>
    public const string Settle = "settle";

    /// <inheritdoc/>
    public override string ToString()
        => Kind switch
        {
            Submit => $"submit(o{Order})={Response}",
            PublishWork => $"publish-work(o{Order})",
            CommitOrder => $"commit-order(o{Order})={Response}",
            PickUp => $"pick-up(w{Worker},o{Order})",
            CallGateway => $"call-gateway(w{Worker},o{Order})={Gateway}",
            Settle => $"settle(w{Worker},o{Order})",
            _ => $"{Kind}(w{Worker},o{Order})"
        };
}

/// <summary>
/// Shared scaffolding for the hand-written frontend: guard the source state,
/// clone it once per outcome, mutate the clone, and re-emit this step so the
/// active step-function set — and therefore graph node identity — stays stable.
/// </summary>
public abstract class FulfillmentStep : BaseStepFunction
{
    /// <summary>Creates a step bound to a configuration.</summary>
    protected FulfillmentStep(FulfillmentConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>The configuration this step was built for.</summary>
    protected FulfillmentConfig Config { get; }

    /// <summary>Whether this step can run in the given state.</summary>
    protected abstract bool IsEnabled(StoreState store);

    /// <summary>
    /// The transactional outcomes of this step. Each entry mutates its own
    /// cloned state and carries the action label for the resulting edge.
    /// </summary>
    protected abstract IEnumerable<(Action<StoreState> Commit, FulfillmentAction Action)>
        Outcomes(StoreState store);

    /// <inheritdoc/>
    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var store = (StoreState)state;
        if (!IsEnabled(store))
        {
            return null;
        }

        var results = new List<StepResult>();
        foreach (var (commit, action) in Outcomes(store))
        {
            var next = (StoreState)store.Clone();
            commit(next);
            results.Add(new StepResult
            {
                State = next,
                StepFunctions = new IStepFunction[] { this },
                EdgeMetadata = action
            });
        }

        return results;
    }

    /// <inheritdoc/>
    public override string ToString() => StepFunctionId;
}

/// <summary>
/// The checkout controller action, written by hand. One application is one
/// request: it screens the order and commits the whole decision — including the
/// queued payment work — in a single transaction.
/// </summary>
public sealed class SubmitOrderStep : FulfillmentStep
{
    /// <summary>Creates the controller action for one order id.</summary>
    public SubmitOrderStep(FulfillmentConfig config, int order) : base(config)
    {
        Order = order;
    }

    /// <summary>The order id this controller action submits.</summary>
    public int Order { get; }

    /// <summary>The stable identity shared with the <c>Operation</c> frontend.</summary>
    public static string IdFor(int order) => $"submit(o{order})";

    /// <inheritdoc/>
    public override string StepFunctionId => IdFor(Order);

    /// <inheritdoc/>
    protected override bool IsEnabled(StoreState store) => true;

    /// <inheritdoc/>
    protected override IEnumerable<(Action<StoreState>, FulfillmentAction)> Outcomes(
        StoreState store)
    {
        if (store.Orders[Order] != OrderStatus.Missing)
        {
            // The controller is idempotent: a repeated submit writes nothing.
            // This is a real request that returns a real response, and it is a
            // state-neutral edge, so it is neither ENABLED nor able to
            // discharge a fairness obligation.
            yield return (
                _ => { },
                new FulfillmentAction(
                    FulfillmentAction.Submit,
                    Order,
                    StoreState.NoWorker,
                    SubmitOutcome.Duplicate));
            yield break;
        }

        yield return (
            next => next.CommitAcceptedOrder(Order),
            new FulfillmentAction(
                FulfillmentAction.Submit,
                Order,
                StoreState.NoWorker,
                SubmitOutcome.Accepted));

        yield return (
            next => next.CommitRejectedOrder(Order),
            new FulfillmentAction(
                FulfillmentAction.Submit,
                Order,
                StoreState.NoWorker,
                SubmitOutcome.Rejected));
    }
}

/// <summary>
/// The dual-write defect, first half: the controller publishes the payment work
/// before it has committed the order row.
/// </summary>
public sealed class PublishPaymentWorkStep : FulfillmentStep
{
    /// <summary>Creates the publish half of the dual write.</summary>
    public PublishPaymentWorkStep(FulfillmentConfig config, int order) : base(config)
    {
        Order = order;
    }

    /// <summary>The order id whose payment work is published.</summary>
    public int Order { get; }

    /// <inheritdoc/>
    public override string StepFunctionId => $"publish-work(o{Order})";

    /// <inheritdoc/>
    protected override bool IsEnabled(StoreState store)
        => store.Orders[Order] == OrderStatus.Missing &&
            store.Outbox[Order] == OutboxPhase.Absent;

    /// <inheritdoc/>
    protected override IEnumerable<(Action<StoreState>, FulfillmentAction)> Outcomes(
        StoreState store)
    {
        yield return (
            next => next.PublishPaymentWorkOnly(Order),
            new FulfillmentAction(
                FulfillmentAction.PublishWork,
                Order,
                StoreState.NoWorker));
    }
}

/// <summary>
/// The dual-write defect, second half: the controller commits the order row in
/// a later transaction, leaving a window in which published payment work has no
/// committed order behind it.
/// </summary>
public sealed class CommitOrderRowStep : FulfillmentStep
{
    /// <summary>Creates the commit half of the dual write.</summary>
    public CommitOrderRowStep(FulfillmentConfig config, int order) : base(config)
    {
        Order = order;
    }

    /// <summary>The order id whose row is committed.</summary>
    public int Order { get; }

    /// <inheritdoc/>
    public override string StepFunctionId => $"commit-order(o{Order})";

    /// <inheritdoc/>
    protected override bool IsEnabled(StoreState store)
        => store.Orders[Order] == OrderStatus.Missing &&
            store.Outbox[Order] != OutboxPhase.Absent;

    /// <inheritdoc/>
    protected override IEnumerable<(Action<StoreState>, FulfillmentAction)> Outcomes(
        StoreState store)
    {
        yield return (
            next => next.Orders[Order] = OrderStatus.Submitted,
            new FulfillmentAction(
                FulfillmentAction.CommitOrder,
                Order,
                StoreState.NoWorker,
                SubmitOutcome.Accepted));

        yield return (
            next => next.Orders[Order] = OrderStatus.Rejected,
            new FulfillmentAction(
                FulfillmentAction.CommitOrder,
                Order,
                StoreState.NoWorker,
                SubmitOutcome.Rejected));
    }
}

/// <summary>
/// The worker's first sequential step: read the outbox and take the row. This
/// is the hand-written counterpart of the coroutine's
/// <c>Read</c> plus <c>Step("pick-up")</c>.
/// </summary>
public sealed class PickUpOutboxRowStep : FulfillmentStep
{
    /// <summary>Creates the pick-up step for one worker and order id.</summary>
    public PickUpOutboxRowStep(FulfillmentConfig config, int worker, int order)
        : base(config)
    {
        Worker = worker;
        Order = order;
    }

    /// <summary>The worker running this step.</summary>
    public int Worker { get; }

    /// <summary>The order id this step handles.</summary>
    public int Order { get; }

    /// <summary>The stable identity shared by every frontend.</summary>
    public static string IdFor(int worker, int order) => $"pick-up(w{worker},o{order})";

    /// <inheritdoc/>
    public override string StepFunctionId => IdFor(Worker, Order);

    /// <inheritdoc/>
    protected override bool IsEnabled(StoreState store)
        => store.WorkerOrder[Worker] == StoreState.NoOrder &&
            store.Outbox[Order] == OutboxPhase.Pending;

    /// <inheritdoc/>
    protected override IEnumerable<(Action<StoreState>, FulfillmentAction)> Outcomes(
        StoreState store)
    {
        yield return (
            next => next.PickUpOutboxRow(Order, Worker, Config.LeaseOutboxRows),
            new FulfillmentAction(FulfillmentAction.PickUp, Order, Worker));
    }
}

/// <summary>
/// The worker's external call. The gateway's behavior is the model's
/// nondeterminism: it can charge and answer, charge and time out, fail without
/// charging, or decline permanently.
/// </summary>
public sealed class CallGatewayStep : FulfillmentStep
{
    /// <summary>Every gateway outcome the model considers.</summary>
    public static IReadOnlyList<GatewayOutcome> GatewayOutcomes { get; } = new[]
    {
        GatewayOutcome.Charged,
        GatewayOutcome.ChargedButTimedOut,
        GatewayOutcome.TransientFailure,
        GatewayOutcome.Declined
    };

    /// <summary>Creates the gateway call for one worker and order id.</summary>
    public CallGatewayStep(FulfillmentConfig config, int worker, int order) : base(config)
    {
        Worker = worker;
        Order = order;
    }

    /// <summary>The worker running this step.</summary>
    public int Worker { get; }

    /// <summary>The order id this step handles.</summary>
    public int Order { get; }

    /// <summary>The stable identity shared by every frontend.</summary>
    public static string IdFor(int worker, int order)
        => $"call-gateway(w{worker},o{order})";

    /// <inheritdoc/>
    public override string StepFunctionId => IdFor(Worker, Order);

    /// <inheritdoc/>
    protected override bool IsEnabled(StoreState store)
        => store.WorkerOrder[Worker] == Order &&
            store.WorkerAttempt[Worker] == AttemptResult.None;

    /// <inheritdoc/>
    protected override IEnumerable<(Action<StoreState>, FulfillmentAction)>
        Outcomes(StoreState store)
    {
        foreach (var outcome in GatewayOutcomes)
        {
            var selected = outcome;
            yield return (
                next => next.RecordGatewayCall(Worker, selected, Config.IdempotentCharge),
                new FulfillmentAction(
                    FulfillmentAction.CallGateway,
                    Order,
                    Worker,
                    Gateway: selected));
        }
    }
}

/// <summary>
/// The worker's settling transaction: one commit that moves the order row and
/// the outbox row together, or returns the row to the queue for a retry.
/// </summary>
public sealed class SettleAttemptStep : FulfillmentStep
{
    /// <summary>Creates the settle step for one worker and order id.</summary>
    public SettleAttemptStep(FulfillmentConfig config, int worker, int order) : base(config)
    {
        Worker = worker;
        Order = order;
    }

    /// <summary>The worker running this step.</summary>
    public int Worker { get; }

    /// <summary>The order id this step handles.</summary>
    public int Order { get; }

    /// <summary>The stable identity shared by every frontend.</summary>
    public static string IdFor(int worker, int order) => $"settle(w{worker},o{order})";

    /// <inheritdoc/>
    public override string StepFunctionId => IdFor(Worker, Order);

    /// <inheritdoc/>
    protected override bool IsEnabled(StoreState store)
        => store.WorkerOrder[Worker] == Order &&
            store.WorkerAttempt[Worker] != AttemptResult.None;

    /// <inheritdoc/>
    protected override IEnumerable<(Action<StoreState>, FulfillmentAction)> Outcomes(
        StoreState store)
    {
        yield return (
            next => next.SettleAttempt(Worker),
            new FulfillmentAction(FulfillmentAction.Settle, Order, Worker));
    }
}
