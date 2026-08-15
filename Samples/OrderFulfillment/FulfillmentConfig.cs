// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;

/// <summary>
/// The size of one model instance and which deliberate implementation defects
/// are switched on. Every defect is a real mistake a service could make, and
/// each one is expected to produce a counterexample.
/// </summary>
public sealed class FulfillmentConfig
{
    /// <summary>
    /// The default study: two order ids, one payment worker, and a correct
    /// implementation.
    /// </summary>
    public static FulfillmentConfig Default { get; } = new FulfillmentConfig(2, 1);

    /// <summary>Two competing workers on one order id.</summary>
    public static FulfillmentConfig CompetingWorkers { get; } =
        new FulfillmentConfig(1, 2);

    /// <summary>The single-order, single-worker instance the coroutine study uses.</summary>
    public static FulfillmentConfig SingleWorker { get; } = new FulfillmentConfig(1, 1);

    /// <summary>Creates a configuration.</summary>
    public FulfillmentConfig(
        int orders,
        int workers,
        bool atomicSubmit = true,
        bool idempotentCharge = true,
        bool leaseOutboxRows = true)
    {
        if (orders <= 0) throw new ArgumentOutOfRangeException(nameof(orders));
        if (workers <= 0) throw new ArgumentOutOfRangeException(nameof(workers));

        Orders = orders;
        Workers = workers;
        AtomicSubmit = atomicSubmit;
        IdempotentCharge = idempotentCharge;
        LeaseOutboxRows = leaseOutboxRows;
    }

    /// <summary>The number of order ids a customer can submit.</summary>
    public int Orders { get; }

    /// <summary>The number of background payment workers.</summary>
    public int Workers { get; }

    /// <summary>
    /// Whether the controller commits the order row and its outbox row in one
    /// transaction. When false the controller publishes the payment work first
    /// and commits the order row afterwards — the classic dual-write defect.
    /// </summary>
    public bool AtomicSubmit { get; }

    /// <summary>
    /// Whether the worker sends an idempotency key with the charge. When false,
    /// retrying a charge that timed out after succeeding charges twice.
    /// </summary>
    public bool IdempotentCharge { get; }

    /// <summary>
    /// Whether a worker leases the outbox row before working on it. When false,
    /// two workers can deliver the same outbox row concurrently.
    /// </summary>
    public bool LeaseOutboxRows { get; }

    /// <summary>Returns this configuration with a non-atomic submit transaction.</summary>
    public FulfillmentConfig WithDualWriteSubmit()
        => new FulfillmentConfig(
            Orders,
            Workers,
            atomicSubmit: false,
            idempotentCharge: IdempotentCharge,
            leaseOutboxRows: LeaseOutboxRows);

    /// <summary>Returns this configuration with retries that drop the idempotency key.</summary>
    public FulfillmentConfig WithNonIdempotentCharge()
        => new FulfillmentConfig(
            Orders,
            Workers,
            atomicSubmit: AtomicSubmit,
            idempotentCharge: false,
            leaseOutboxRows: LeaseOutboxRows);

    /// <summary>Returns this configuration with an unleased outbox.</summary>
    public FulfillmentConfig WithoutOutboxLease()
        => new FulfillmentConfig(
            Orders,
            Workers,
            atomicSubmit: AtomicSubmit,
            idempotentCharge: IdempotentCharge,
            leaseOutboxRows: false);

    /// <inheritdoc/>
    public override string ToString()
        => $"orders={Orders},workers={Workers}," +
            $"atomicSubmit={AtomicSubmit},idempotentCharge={IdempotentCharge}," +
            $"leaseOutboxRows={LeaseOutboxRows}";
}
