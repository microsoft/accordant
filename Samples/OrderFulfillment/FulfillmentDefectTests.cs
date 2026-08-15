// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment.Tests;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Three deliberately broken variants of the same implementation. Each defect
/// is a mistake a real service makes, and each one has to produce a definitive
/// counterexample — otherwise the invariants above would be vacuous.
/// </summary>
[TestFixture]
public class FulfillmentDefectTests
{
    [Test]
    public void PublishingPaymentWorkBeforeCommittingTheOrderBreaksTheOutboxInvariant()
    {
        // The dual write: the controller publishes the payment work in one
        // transaction and commits the order row in another. Between the two,
        // queued work exists with no order behind it.
        var config = FulfillmentConfig.Default.WithDualWriteSubmit();
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        var result = root.Check(FulfillmentProperties.OutboxMatchesOrder(config.Orders));

        Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            result.Trace.Select(item => item.StepFunction?.StepFunctionId),
            Has.Some.StartsWith("publish-work"));
        TestContext.WriteLine(result.GetTraceString());
    }

    [Test]
    public void TheDualWriteAlsoChargesACustomerWhoseOrderWasNeverCommitted()
    {
        var config = FulfillmentConfig.Default.WithDualWriteSubmit();
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        var result = root.Check(
            FulfillmentProperties.NoChargeWithoutCommittedOrder(config.Orders));

        Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
        TestContext.WriteLine(result.GetTraceString());
        Assert.That(
            result.Trace.Select(item => item.StepFunction?.StepFunctionId ?? "start"),
            Has.Some.StartsWith("call-gateway"));

        var violating = result.Trace
            .Select(item => (StoreState)item.StateGraphNode.State)
            .Where(store => Enumerable.Range(0, config.Orders).Any(order =>
                store.Charges[order] > 0 &&
                (store.Orders[order] == OrderStatus.Missing ||
                    store.Orders[order] == OrderStatus.Rejected)))
            .ToArray();
        Assert.That(
            violating,
            Is.Not.Empty,
            "the counterexample must pass through a state that charges an order " +
            "the service never owed");

        // Both shapes of the defect are reachable: the payment can be taken
        // before any order row exists, and it can be taken for an order that
        // risk screening went on to reject.
        var chargedWhileMissing = FulfillmentModel.Reachable(root).Any(node =>
            Enumerable.Range(0, config.Orders).Any(order =>
                ((StoreState)node.State).Charges[order] > 0 &&
                ((StoreState)node.State).Orders[order] == OrderStatus.Missing));
        var chargedAfterReject = FulfillmentModel.Reachable(root).Any(node =>
            Enumerable.Range(0, config.Orders).Any(order =>
                ((StoreState)node.State).Charges[order] > 0 &&
                ((StoreState)node.State).Orders[order] == OrderStatus.Rejected));

        Assert.That(chargedWhileMissing, Is.True);
        Assert.That(chargedAfterReject, Is.True);
        TestContext.WriteLine(result.GetTraceString());
    }

    [Test]
    public void TheAtomicControllerHasNoSuchWindow()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        Assert.That(
            root.Check(FulfillmentProperties.OutboxMatchesOrder(config.Orders)).Valid,
            Is.True);
        Assert.That(
            root.Check(FulfillmentProperties.NoChargeWithoutCommittedOrder(config.Orders))
                .Valid,
            Is.True);
    }

    [Test]
    public void DroppingTheIdempotencyKeyChargesTheCustomerTwice()
    {
        // The gateway charged the card and then timed out. The worker retries,
        // and without an idempotency key the second attempt charges again.
        var config = FulfillmentConfig.SingleWorker.WithNonIdempotentCharge();
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        var result = root.Check(FulfillmentProperties.ChargedAtMostOnce(config.Orders));

        Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Violated));

        var gatewayOutcomes = result.Trace
            .Select(item => item.StepFunction?.StepFunctionId ?? "start")
            .Count(id => id.StartsWith("call-gateway"));
        Assert.That(
            gatewayOutcomes,
            Is.GreaterThanOrEqualTo(2),
            "a double charge needs at least two gateway calls");
        Assert.That(
            FulfillmentModel.Reachable(root)
                .Any(node => ((StoreState)node.State).Charges[0] > 1),
            Is.True);
        TestContext.WriteLine(result.GetTraceString());
    }

    [Test]
    public void TheIdempotentWorkerSurvivesTheSameAmbiguity()
    {
        var config = FulfillmentConfig.SingleWorker;
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        Assert.That(
            root.Check(FulfillmentProperties.ChargedAtMostOnce(config.Orders)).Valid,
            Is.True);
        Assert.That(
            FulfillmentModel.Reachable(root)
                .All(node => ((StoreState)node.State).Charges[0] <= 1),
            Is.True);
    }

    [Test]
    public void DeliveringAnUnleasedOutboxRowTwiceReopensAClosedOrder()
    {
        // Without a lease the same outbox row is delivered to both workers.
        // One of them settles a success and closes the order; the other is
        // still holding a stale retryable response and re-queues work for an
        // order that is already closed.
        var config = FulfillmentConfig.CompetingWorkers.WithoutOutboxLease();
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        var finality = root.Check(FulfillmentProperties.ResolutionIsFinal(config.Orders));
        var outbox = root.Check(FulfillmentProperties.OutboxMatchesOrder(config.Orders));

        Assert.That(finality.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(outbox.Status, Is.EqualTo(PropertyCheckingStatus.Violated));

        var pickUps = finality.Trace
            .Select(item => item.StepFunction?.StepFunctionId ?? "start")
            .Where(id => id.StartsWith("pick-up"))
            .Distinct()
            .ToArray();
        Assert.That(
            pickUps,
            Has.Length.EqualTo(2),
            "both workers must have taken the same outbox row");
        TestContext.WriteLine(finality.GetTraceString());
    }

    [Test]
    public void LeasingTheOutboxRowRemovesTheDuplicateDelivery()
    {
        var config = FulfillmentConfig.CompetingWorkers;
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        Assert.That(
            root.Check(FulfillmentProperties.ResolutionIsFinal(config.Orders)).Valid,
            Is.True);
        Assert.That(
            root.Check(FulfillmentProperties.OutboxMatchesOrder(config.Orders)).Valid,
            Is.True);
    }
}
