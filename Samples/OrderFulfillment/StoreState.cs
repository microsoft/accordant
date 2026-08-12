// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;
using Microsoft.Accordant;

/// <summary>The row a checkout controller writes into the <c>orders</c> table.</summary>
public enum OrderStatus
{
    /// <summary>No row was ever committed for this order id.</summary>
    Missing,

    /// <summary>The order was accepted and a payment is owed.</summary>
    Submitted,

    /// <summary>Risk screening refused the order. No payment is owed.</summary>
    Rejected,

    /// <summary>The gateway charged the customer and the order closed.</summary>
    Paid,

    /// <summary>The gateway permanently declined and the order closed.</summary>
    Failed
}

/// <summary>The row a checkout controller writes into the <c>payment_outbox</c> table.</summary>
public enum OutboxPhase
{
    /// <summary>There is no outbox row for this order.</summary>
    Absent,

    /// <summary>The row is queued and can be picked up by a worker.</summary>
    Pending,

    /// <summary>A worker holds the lease and is running an attempt.</summary>
    Leased
}

/// <summary>
/// What actually happened at the payment gateway, and what the worker saw. The
/// interesting outcome is <see cref="ChargedButTimedOut"/>: the customer's card
/// is charged but the worker observes a retryable failure and will try again.
/// </summary>
public enum GatewayOutcome
{
    /// <summary>Charged; the worker saw success.</summary>
    Charged,

    /// <summary>Charged; the worker saw a retryable failure and will retry.</summary>
    ChargedButTimedOut,

    /// <summary>Not charged; the worker saw a retryable failure and will retry.</summary>
    TransientFailure,

    /// <summary>Not charged; the gateway permanently declined the card.</summary>
    Declined
}

/// <summary>The gateway response a worker is currently holding in memory.</summary>
public enum AttemptResult
{
    /// <summary>The worker has not called the gateway on this attempt yet.</summary>
    None,

    /// <summary>The gateway reported success.</summary>
    Succeeded,

    /// <summary>The gateway reported a retryable failure.</summary>
    Transient,

    /// <summary>The gateway permanently declined.</summary>
    Declined
}

/// <summary>
/// The whole modeled system: the service database (the <c>orders</c> table plus
/// the <c>payment_outbox</c> table), the in-flight context of each background
/// worker, and the external payment gateway's ledger.
///
/// <para>Arrays named per order are indexed by order id; arrays named per worker
/// are indexed by worker id. A database transaction is modeled by mutating
/// several arrays inside <em>one</em> step-function application: the compiled
/// graph has no node between the two writes, which is exactly what makes the
/// commit atomic.</para>
/// </summary>
[State]
public partial class StoreState
{
    /// <summary>The <c>orders</c> table, per order.</summary>
    public OrderStatus[] Orders { get; set; }

    /// <summary>The <c>payment_outbox</c> table, per order.</summary>
    public OutboxPhase[] Outbox { get; set; }

    /// <summary>The worker holding each outbox row, or <see cref="NoWorker"/>.</summary>
    public int[] LeaseHolder { get; set; }

    /// <summary>The order each worker is currently handling, or <see cref="NoOrder"/>.</summary>
    public int[] WorkerOrder { get; set; }

    /// <summary>The gateway response each worker is holding, per worker.</summary>
    public AttemptResult[] WorkerAttempt { get; set; }

    /// <summary>
    /// The external gateway ledger: how many times each order was actually
    /// charged, saturating at <see cref="ChargeCap"/>.
    /// </summary>
    public int[] Charges { get; set; }

    /// <summary>A lease holder value meaning "no worker holds this row".</summary>
    public const int NoWorker = -1;

    /// <summary>A worker context value meaning "this worker is idle".</summary>
    public const int NoOrder = -1;

    /// <summary>
    /// The charge counter saturates here. Charging is monotone and every
    /// property in this sample only distinguishes "never charged", "charged
    /// once", and "charged more than once", so collapsing every count above one
    /// is a sound finite abstraction: it can neither hide a double charge nor
    /// invent one.
    /// </summary>
    public const int ChargeCap = 2;

    /// <summary>Creates the empty database with an untouched gateway ledger.</summary>
    public static StoreState Empty(FulfillmentConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        var leaseHolder = new int[config.Orders];
        for (var order = 0; order < config.Orders; order++)
        {
            leaseHolder[order] = NoWorker;
        }

        var workerOrder = new int[config.Workers];
        for (var worker = 0; worker < config.Workers; worker++)
        {
            workerOrder[worker] = NoOrder;
        }

        return new StoreState
        {
            Orders = new OrderStatus[config.Orders],
            Outbox = new OutboxPhase[config.Orders],
            LeaseHolder = leaseHolder,
            WorkerOrder = workerOrder,
            WorkerAttempt = new AttemptResult[config.Workers],
            Charges = new int[config.Orders]
        };
    }

    /// <summary>
    /// Creates the state a correct controller leaves behind after accepting
    /// <paramref name="order"/>: the order row and its outbox row are both
    /// already committed. This is the starting point of the worker-only studies.
    /// </summary>
    public static StoreState AfterAcceptedSubmit(FulfillmentConfig config, int order)
    {
        var state = Empty(config);
        state.CommitAcceptedOrder(order);
        return state;
    }

    /// <summary>Whether the customer's order reached a terminal status.</summary>
    public bool IsResolved(int order)
        => Orders[order] == OrderStatus.Rejected ||
            Orders[order] == OrderStatus.Paid ||
            Orders[order] == OrderStatus.Failed;

    /// <summary>Whether every order id reached a terminal status.</summary>
    public bool AllResolved()
    {
        for (var order = 0; order < Orders.Length; order++)
        {
            if (!IsResolved(order))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The controller's accepting transaction: the order row and the outbox row
    /// are committed together.
    /// </summary>
    public void CommitAcceptedOrder(int order)
    {
        Orders[order] = OrderStatus.Submitted;
        Outbox[order] = OutboxPhase.Pending;
    }

    /// <summary>The controller's rejecting transaction: no payment work is queued.</summary>
    public void CommitRejectedOrder(int order)
        => Orders[order] = OrderStatus.Rejected;

    /// <summary>
    /// The first half of the dual-write defect: the payment work is published
    /// before the order row exists.
    /// </summary>
    public void PublishPaymentWorkOnly(int order)
        => Outbox[order] = OutboxPhase.Pending;

    /// <summary>
    /// A worker picks up an outbox row. With leasing the row is marked
    /// <see cref="OutboxPhase.Leased"/> so no other worker can take it; without
    /// leasing the row stays queued and can be delivered again.
    /// </summary>
    public void PickUpOutboxRow(int order, int worker, bool lease)
    {
        if (lease)
        {
            Outbox[order] = OutboxPhase.Leased;
            LeaseHolder[order] = worker;
        }

        WorkerOrder[worker] = order;
        WorkerAttempt[worker] = AttemptResult.None;
    }

    /// <summary>
    /// One external gateway call: the gateway's ledger effect and the response
    /// the worker ends up holding.
    /// </summary>
    /// <param name="worker">The calling worker.</param>
    /// <param name="outcome">The nondeterministic gateway outcome.</param>
    /// <param name="idempotent">
    /// Whether the worker sends the order id as an idempotency key. When false
    /// this reproduces the classic retry defect: a charge that timed out after
    /// succeeding is charged again.
    /// </param>
    public void RecordGatewayCall(int worker, GatewayOutcome outcome, bool idempotent)
    {
        var order = WorkerOrder[worker];
        if (order == NoOrder)
        {
            throw new InvalidOperationException("An idle worker cannot call the gateway.");
        }

        if (outcome == GatewayOutcome.Charged || outcome == GatewayOutcome.ChargedButTimedOut)
        {
            Charges[order] = idempotent && Charges[order] > 0
                ? Charges[order]
                : Math.Min(Charges[order] + 1, ChargeCap);
        }

        WorkerAttempt[worker] = outcome switch
        {
            GatewayOutcome.Charged => AttemptResult.Succeeded,
            GatewayOutcome.ChargedButTimedOut => AttemptResult.Transient,
            GatewayOutcome.TransientFailure => AttemptResult.Transient,
            GatewayOutcome.Declined => AttemptResult.Declined,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }

    /// <summary>
    /// The worker's settling transaction: the order row and the outbox row move
    /// together, so the outbox is never out of step with the order.
    /// </summary>
    public void SettleAttempt(int worker)
    {
        var order = WorkerOrder[worker];
        if (order == NoOrder)
        {
            throw new InvalidOperationException("An idle worker has nothing to settle.");
        }

        switch (WorkerAttempt[worker])
        {
            case AttemptResult.Succeeded:
                Orders[order] = OrderStatus.Paid;
                RemoveOutboxRow(order);
                break;

            case AttemptResult.Declined:
                Orders[order] = OrderStatus.Failed;
                RemoveOutboxRow(order);
                break;

            case AttemptResult.Transient:
                Outbox[order] = OutboxPhase.Pending;
                LeaseHolder[order] = NoWorker;
                break;

            default:
                throw new InvalidOperationException(
                    "Settling requires a gateway response.");
        }

        WorkerOrder[worker] = NoOrder;
        WorkerAttempt[worker] = AttemptResult.None;
    }

    private void RemoveOutboxRow(int order)
    {
        Outbox[order] = OutboxPhase.Absent;
        LeaseHolder[order] = NoWorker;
    }
}
