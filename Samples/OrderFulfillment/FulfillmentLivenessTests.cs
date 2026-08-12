// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment.Tests;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Progress, and the fairness assumptions it is stated under. The interesting
/// result is that scheduling the worker is not enough: the model also needs an
/// explicit assumption about the external payment gateway, and it has to be a
/// strong one.
/// </summary>
[TestFixture]
public class FulfillmentLivenessTests
{
    private static StateGraphNode Graph(FulfillmentConfig config)
        => FulfillmentProperties.RequireComplete(
            FulfillmentModel.BuildNativeGraph(config));

    [Test]
    public void WithoutAnyFairnessAnAcceptedOrderCanHangForever()
    {
        var config = FulfillmentConfig.Default;
        var result = Graph(config).Check(
            FulfillmentProperties.SubmittedOrdersResolve(0),
            fairness: Fairness.None);

        Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(result.Trace, Is.Not.Null);
        TestContext.WriteLine(result.GetTraceString());
    }

    [Test]
    public void SchedulingTheWorkerIsNotEnoughBecauseTheGatewayCanKeepFailing()
    {
        var config = FulfillmentConfig.Default;
        var result = Graph(config).Check(
            FulfillmentProperties.SubmittedOrdersResolve(0),
            fairness: FulfillmentProperties.WorkersRun);

        Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Violated));

        var cycle = result.Trace
            .Where(item => item.IsInCycle)
            .Select(item => item.StepFunction?.StepFunctionId ?? string.Empty)
            .ToArray();
        Assert.That(
            cycle,
            Has.Some.StartsWith("call-gateway"),
            "the bad cycle is the worker retrying an endlessly failing gateway");
        Assert.That(cycle, Has.Some.StartsWith("settle"));
    }

    [Test]
    public void TheStatedGatewayAssumptionMakesEveryAcceptedOrderClose()
    {
        var config = FulfillmentConfig.Default;
        var root = Graph(config);

        for (var order = 0; order < config.Orders; order++)
        {
            Assert.That(
                root.Check(
                        FulfillmentProperties.SubmittedOrdersResolve(order),
                        fairness: FulfillmentProperties.ProgressAssumptions)
                    .Valid,
                Is.True,
                $"order {order} should close under the stated assumptions");
        }
    }

    [Test]
    public void WeakGatewayFairnessIsNotEnoughOnTheRetryCycle()
    {
        // The gateway call is not continuously enabled around the retry cycle:
        // the worker spends part of every iteration picking the row up and
        // settling it. Weak fairness never fires, so the assumption has to be
        // strong.
        var config = FulfillmentConfig.Default;
        var weakGateway = FulfillmentProperties.WorkersRun +
            Fairness.Weak<StoreState>(FulfillmentProperties.DefinitiveGatewayAnswer);

        var weak = Graph(config).Check(
            FulfillmentProperties.SubmittedOrdersResolve(0),
            fairness: weakGateway);

        Assert.That(weak.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
    }

    [Test]
    public void ClosingEveryOrderAlsoNeedsTheControllerToRun()
    {
        var config = FulfillmentConfig.Default;
        var root = Graph(config);

        Assert.That(
            root.Check(
                    FulfillmentProperties.EveryOrderResolves(),
                    fairness: FulfillmentProperties.ProgressAssumptions)
                .Status,
            Is.EqualTo(PropertyCheckingStatus.Violated),
            "nothing forces the customer's second order to be submitted at all");
        Assert.That(
            root.Check(
                    FulfillmentProperties.EveryOrderResolves(),
                    fairness: FulfillmentProperties.ProgressAssumptions +
                        FulfillmentProperties.ControllerRuns)
                .Valid,
            Is.True);
    }

    [Test]
    public void TheIdempotentDuplicateSubmitCannotDischargeAFairnessObligation()
    {
        // Re-submitting an existing order id is a real request with a real
        // response, but it writes nothing, so it is a state-neutral edge.
        // Accordant counts only changing edges, so a run that does nothing but
        // re-submit forever leaves the worker's weak-fairness obligation
        // outstanding and is excluded — without the duplicate edge ever being
        // treated as progress.
        var config = FulfillmentConfig.SingleWorker;
        var root = Graph(config);

        var duplicateSelfLoops = FulfillmentModel.Reachable(root)
            .SelectMany(node => node.Edges.Select(edge => (node, edge)))
            .Count(pair =>
                ((FulfillmentAction)pair.edge.Metadata).Response == SubmitOutcome.Duplicate &&
                pair.edge.Target.GetNodeFingerprint() == pair.node.GetNodeFingerprint());

        Assert.That(
            duplicateSelfLoops,
            Is.GreaterThan(0),
            "the model must actually contain state-neutral duplicate submits");
        Assert.That(
            root.Check(
                    FulfillmentProperties.SubmittedOrdersResolve(0),
                    fairness: Fairness.None)
                .Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            root.Check(
                    FulfillmentProperties.SubmittedOrdersResolve(0),
                    fairness: FulfillmentProperties.ProgressAssumptions)
                .Valid,
            Is.True);
    }
}
