// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;

/// <summary>
/// The properties every fulfillment frontend is checked against, and the
/// fairness assumptions the progress properties are stated under. They are
/// written once and reused by the hand-written, <c>Operation</c>, and coroutine
/// graphs, which is the point: the frontends differ, the backend does not.
/// </summary>
public static class FulfillmentProperties
{
    /// <summary>The stutter-invariant formula builder for the store state.</summary>
    public static FormulaBuilder<StoreState> Formula { get; } =
        Microsoft.Accordant.ModelChecking.Formula.For<StoreState>();

    /// <summary>The complete formula language, used only where ENABLED is needed.</summary>
    public static StutterSensitiveFormulaBuilder<StoreState> ExactFormula { get; } =
        Formula.AllowStutterSensitiveFormulas();

    /// <summary>
    /// <c>□ (charged(o) ⇒ the order row says a payment is owed)</c>.
    /// The gateway is never called on behalf of an order whose accepting
    /// transaction has not committed, and never on behalf of a rejected order.
    /// </summary>
    public static StutterSafeFormula NoChargeWithoutCommittedOrder(int orders)
        => Formula.Always(Formula.Observe(
            store =>
            {
                for (var order = 0; order < orders; order++)
                {
                    if (store.Charges[order] == 0)
                    {
                        continue;
                    }

                    var status = store.Orders[order];
                    if (status == OrderStatus.Missing || status == OrderStatus.Rejected)
                    {
                        return false;
                    }
                }

                return true;
            },
            "NoChargeWithoutCommittedOrder"));

    /// <summary>
    /// <c>□ (outbox row exists ⇔ the order is awaiting payment)</c>.
    /// This is the transactional-outbox invariant: the queued work and the
    /// domain row are written and cleared by the same transaction, so no
    /// reachable state has one without the other.
    /// </summary>
    public static StutterSafeFormula OutboxMatchesOrder(int orders)
        => Formula.Always(Formula.Observe(
            store =>
            {
                for (var order = 0; order < orders; order++)
                {
                    var queued = store.Outbox[order] != OutboxPhase.Absent;
                    var awaitingPayment = store.Orders[order] == OrderStatus.Submitted;
                    if (queued != awaitingPayment)
                    {
                        return false;
                    }
                }

                return true;
            },
            "OutboxMatchesOrder"));

    /// <summary>
    /// <c>□ (charges(o) ≤ 1)</c>. Retrying an ambiguous gateway call must not
    /// charge the customer a second time.
    /// </summary>
    public static StutterSafeFormula ChargedAtMostOnce(int orders)
        => Formula.Always(Formula.Observe(
            store =>
            {
                for (var order = 0; order < orders; order++)
                {
                    if (store.Charges[order] > 1)
                    {
                        return false;
                    }
                }

                return true;
            },
            "ChargedAtMostOnce"));

    /// <summary>
    /// <c>□ (paid(o) ⇒ charged(o))</c>. An order is never reported as paid
    /// unless the external system really charged it.
    /// </summary>
    public static StutterSafeFormula NoPaidOrderWithoutCharge(int orders)
        => Formula.Always(Formula.Observe(
            store =>
            {
                for (var order = 0; order < orders; order++)
                {
                    if (store.Orders[order] == OrderStatus.Paid &&
                        store.Charges[order] == 0)
                    {
                        return false;
                    }
                }

                return true;
            },
            "NoPaidOrderWithoutCharge"));

    /// <summary>
    /// <c>□ (a resolved order never changes status again)</c>, as a transition
    /// property. A closed order must not be reopened or closed a second time
    /// with a different answer.
    /// </summary>
    public static StutterSafeFormula ResolutionIsFinal(int orders)
        => Formula.Always(Formula.ObserveTransition(
            (from, to) =>
            {
                for (var order = 0; order < orders; order++)
                {
                    if (from.IsResolved(order) && to.Orders[order] != from.Orders[order])
                    {
                        return false;
                    }
                }

                return true;
            },
            "ResolutionIsFinal"));

    /// <summary>
    /// <c>□ (submitted(o) ⇒ ◇ resolved(o))</c>. Every accepted order eventually
    /// closes. This needs the fairness assumptions below.
    /// </summary>
    public static StutterSafeFormula SubmittedOrdersResolve(int order)
        => Formula.LeadsTo(
            Formula.Observe(
                store => store.Orders[order] == OrderStatus.Submitted,
                $"Submitted(o{order})"),
            Formula.Observe(
                store => store.IsResolved(order),
                $"Resolved(o{order})"));

    /// <summary><c>◇ (every order id is closed)</c>.</summary>
    public static StutterSafeFormula EveryOrderResolves()
        => Formula.Eventually(Formula.Observe(
            store => store.AllResolved(),
            "AllResolved"));

    /// <summary>
    /// Weak fairness for the background workers: a worker action that stays
    /// continuously enabled is eventually taken. This is the scheduler
    /// assumption — the worker process is not stopped forever.
    /// </summary>
    public static Fairness WorkersRun { get; } =
        Fairness.Weak<PickUpOutboxRowStep>() +
        Fairness.Weak<CallGatewayStep>() +
        Fairness.Weak<SettleAttemptStep>();

    /// <summary>
    /// Weak fairness for the customer-facing controller: a submit that stays
    /// enabled is eventually taken. Only needed for
    /// <see cref="EveryOrderResolves"/>, which requires the orders to be
    /// submitted at all.
    /// </summary>
    public static Fairness ControllerRuns { get; } =
        Fairness.Weak(step => step is SubmitOrderStep);

    /// <summary>
    /// The gateway assumption, stated explicitly: <em>the payment gateway does
    /// not fail transiently forever</em>. Formally, strong fairness on the
    /// gateway call that produces a definitive answer — if a definitive answer
    /// is available infinitely often, it is eventually given.
    ///
    /// <para>It has to be strong, not weak: on the retry cycle the gateway call
    /// is not continuously enabled, because the worker spends part of every
    /// iteration picking the row up and settling it.</para>
    /// </summary>
    public static Fairness GatewayAnswersDefinitively { get; } =
        Fairness.Strong<StoreState>(DefinitiveGatewayAnswer);

    /// <summary>
    /// The full set of assumptions the progress properties are stated under.
    /// </summary>
    public static Fairness ProgressAssumptions { get; } =
        WorkersRun + GatewayAnswersDefinitively;

    /// <summary>
    /// A transition in which some worker obtains a definitive gateway answer —
    /// success or a permanent decline — rather than a retryable failure.
    /// </summary>
    public static bool DefinitiveGatewayAnswer(StoreState from, StoreState to)
    {
        for (var worker = 0; worker < from.WorkerAttempt.Length; worker++)
        {
            if (from.WorkerAttempt[worker] != AttemptResult.None)
            {
                continue;
            }

            var answer = to.WorkerAttempt[worker];
            if (answer == AttemptResult.Succeeded || answer == AttemptResult.Declined)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Asserts a graph is complete enough for a definitive verdict and returns
    /// it, so a study never reports <c>Holds</c> from a truncated graph.
    /// </summary>
    public static StateGraphNode RequireComplete(StateGraphNode root)
    {
        if (!FulfillmentModel.IsComplete(root))
        {
            throw new InvalidOperationException(
                "Exploration reached a depth frontier; no definitive verdict is available.");
        }

        return root;
    }
}
