// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment.Tests;

using System;
using System.Linq;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Safety of the correct implementation, checked on the hand-written
/// (native <c>IState</c>/<c>IStepFunction</c>) frontend. These are the claims
/// the whole case study is about: an external charge implies a committed
/// request, the outbox never drifts from the order table, and no order is
/// charged or closed twice.
/// </summary>
[TestFixture]
public class FulfillmentSafetyTests
{
    [Test]
    public void TheDefaultInstanceIsFullyExploredAndBounded()
    {
        var root = FulfillmentModel.BuildNativeGraph(FulfillmentConfig.Default);
        var (nodes, edges) = FulfillmentModel.Size(root);

        Assert.That(FulfillmentModel.IsComplete(root), Is.True);
        Assert.That(nodes, Is.GreaterThan(20));
        Assert.That(edges, Is.GreaterThan(nodes));
        TestContext.WriteLine($"native default: {nodes} nodes, {edges} edges");
    }

    [Test]
    public void NoOrderIsChargedWithoutACommittedRequest()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        Assert.That(
            root.Check(FulfillmentProperties.NoChargeWithoutCommittedOrder(config.Orders))
                .Valid,
            Is.True);
    }

    [Test]
    public void TheOutboxNeverDriftsFromTheOrderTable()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        Assert.That(
            root.Check(FulfillmentProperties.OutboxMatchesOrder(config.Orders)).Valid,
            Is.True);
    }

    [Test]
    public void RetriesNeverChargeTwiceAndClosedOrdersStayClosed()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

        Assert.That(
            root.Check(FulfillmentProperties.ChargedAtMostOnce(config.Orders)).Valid,
            Is.True);
        Assert.That(
            root.Check(FulfillmentProperties.ResolutionIsFinal(config.Orders)).Valid,
            Is.True);
        Assert.That(
            root.Check(FulfillmentProperties.NoPaidOrderWithoutCharge(config.Orders)).Valid,
            Is.True);
    }

    [Test]
    public void CompetingWorkersOnOneOrderStaySafe()
    {
        var config = FulfillmentConfig.CompetingWorkers;
        var root = FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));
        var (nodes, edges) = FulfillmentModel.Size(root);
        TestContext.WriteLine($"two workers, one order: {nodes} nodes, {edges} edges");

        Assert.That(
            root.Check(FulfillmentProperties.OutboxMatchesOrder(config.Orders)).Valid,
            Is.True);
        Assert.That(
            root.Check(FulfillmentProperties.ChargedAtMostOnce(config.Orders)).Valid,
            Is.True);
        Assert.That(
            root.Check(FulfillmentProperties.ResolutionIsFinal(config.Orders)).Valid,
            Is.True);
    }

    [Test]
    public void TheModelReallyExercisesTheAmbiguousGatewayCall()
    {
        var root = FulfillmentModel.BuildNativeGraph(FulfillmentConfig.SingleWorker);

        var chargedButTimedOut = FulfillmentModel.Reachable(root)
            .SelectMany(node => node.Edges)
            .Select(edge => (FulfillmentAction)edge.Metadata)
            .Any(action => action.Gateway == GatewayOutcome.ChargedButTimedOut);
        var retried = FulfillmentModel.Reachable(root)
            .Count(node =>
            {
                var store = (StoreState)node.State;
                return store.Charges[0] > 0 && store.Outbox[0] == OutboxPhase.Pending;
            });

        Assert.That(chargedButTimedOut, Is.True);
        Assert.That(
            retried,
            Is.GreaterThan(0),
            "a charged order must be re-queued for retry somewhere, or the study is vacuous");
    }
}
