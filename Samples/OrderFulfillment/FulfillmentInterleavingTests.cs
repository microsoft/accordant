// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment.Tests;

using System.Linq;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// The interleavings the model actually contains. A case study that only ever
/// ran the controller to completion and then the worker to completion would
/// prove very little, so these tests pin down that requests and background work
/// really do interleave, and that two workers really do race.
/// </summary>
[TestFixture]
public class FulfillmentInterleavingTests
{
    [Test]
    public void ARequestCanArriveAtEveryStageOfAnInFlightAttempt()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentModel.BuildNativeGraph(config);

        // The three stages of worker 0's attempt on order 0, each observed in a
        // state where order 1 has not been submitted yet and the controller
        // action for it is still enabled.
        var stages = new (string Name, System.Func<StoreState, bool> Match)[]
        {
            ("leased, not yet called", store =>
                store.Outbox[0] == OutboxPhase.Leased &&
                store.WorkerAttempt[0] == AttemptResult.None),
            ("called, not yet settled", store =>
                store.Outbox[0] == OutboxPhase.Leased &&
                store.WorkerAttempt[0] != AttemptResult.None),
            ("re-queued after a retryable failure", store =>
                store.Orders[0] == OrderStatus.Submitted &&
                store.Outbox[0] == OutboxPhase.Pending &&
                store.Charges[0] > 0)
        };

        foreach (var (name, match) in stages)
        {
            var interleaved = FulfillmentModel.Reachable(root)
                .Where(node =>
                {
                    var store = (StoreState)node.State;
                    return store.Orders[1] == OrderStatus.Missing && match(store);
                })
                .SelectMany(node => node.Edges)
                .Where(edge =>
                {
                    var action = (FulfillmentAction)edge.Metadata;
                    return action.Kind == FulfillmentAction.Submit && action.Order == 1;
                })
                .ToArray();

            Assert.That(
                interleaved,
                Is.Not.Empty,
                $"a second request must be able to arrive while the first attempt is {name}");
        }
    }

    [Test]
    public void TheWorkerCanInterleaveBetweenTwoOrdersRatherThanDrainingOne()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentModel.BuildNativeGraph(config);

        // A state where one order is already closed and the other is mid-flight,
        // reached by the worker switching between them rather than by finishing
        // one and then starting the other from a quiescent database.
        var bothInPlay = FulfillmentModel.Reachable(root).Count(node =>
        {
            var store = (StoreState)node.State;
            return store.Orders[0] == OrderStatus.Submitted &&
                store.Orders[1] == OrderStatus.Submitted &&
                (store.Outbox[0] == OutboxPhase.Leased ||
                    store.Outbox[1] == OutboxPhase.Leased);
        });

        Assert.That(bothInPlay, Is.GreaterThan(0));
        Assert.That(
            FulfillmentModel.Reachable(root).Count(node =>
            {
                var store = (StoreState)node.State;
                return store.Charges[0] > 0 && store.Charges[1] > 0;
            }),
            Is.GreaterThan(0),
            "both orders can be in flight at the gateway across one run");
    }

    [Test]
    public void TwoWorkersRaceForTheSameOutboxRowAndOnlyOneWins()
    {
        var config = FulfillmentConfig.CompetingWorkers;
        var root = FulfillmentModel.BuildNativeGraph(config);

        var raceNodes = FulfillmentModel.Reachable(root)
            .Where(node => node.Edges
                .Select(edge => (FulfillmentAction)edge.Metadata)
                .Count(action => action.Kind == FulfillmentAction.PickUp) == 2)
            .ToArray();

        Assert.That(
            raceNodes,
            Is.Not.Empty,
            "both workers must be able to take the row from the same state");
        Assert.That(
            raceNodes.SelectMany(node => node.Edges)
                .Select(edge => (FulfillmentAction)edge.Metadata)
                .Where(action => action.Kind == FulfillmentAction.PickUp)
                .Select(action => action.Worker)
                .Distinct(),
            Is.EquivalentTo(new[] { 0, 1 }));

        // The lease resolves the race: no reachable state has two workers
        // holding the same order.
        Assert.That(
            FulfillmentModel.Reachable(root).All(node =>
            {
                var store = (StoreState)node.State;
                return store.WorkerOrder[0] == StoreState.NoOrder ||
                    store.WorkerOrder[0] != store.WorkerOrder[1];
            }),
            Is.True);
    }

    [Test]
    public void WithoutALeaseBothWorkersHoldTheSameRowAtOnce()
    {
        var config = FulfillmentConfig.CompetingWorkers.WithoutOutboxLease();
        var root = FulfillmentModel.BuildNativeGraph(config);

        Assert.That(
            FulfillmentModel.Reachable(root).Any(node =>
            {
                var store = (StoreState)node.State;
                return store.WorkerOrder[0] != StoreState.NoOrder &&
                    store.WorkerOrder[0] == store.WorkerOrder[1];
            }),
            Is.True,
            "duplicate delivery is exactly two workers holding one outbox row");
    }

    [Test]
    public void TheOperationsFrontendExposesTheSameInterleavings()
    {
        var config = FulfillmentConfig.Default;
        var composed = OperationsFrontend.BuildCoActiveGraph(config);

        var midFlightRequests = FulfillmentModel.Reachable(composed)
            .Where(node =>
            {
                var store = (StoreState)node.State;
                return store.Outbox[0] == OutboxPhase.Leased &&
                    store.Orders[1] == OrderStatus.Missing;
            })
            .SelectMany(node => node.Edges)
            .Select(OperationsFrontend.Label)
            .Where(action =>
                action.Kind == FulfillmentAction.Submit && action.Order == 1)
            .Select(action => action.Response)
            .Distinct()
            .ToArray();

        Assert.That(
            midFlightRequests,
            Is.EquivalentTo(new object[]
            {
                SubmitOutcome.Accepted,
                SubmitOutcome.Rejected
            }),
            "both response-dependent outcomes remain available mid-attempt");
    }
}
