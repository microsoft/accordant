// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment.Tests;

using System;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Operations;
using NUnit.Framework;

/// <summary>
/// The <c>Operation</c> frontend authoring the controller's request/response
/// behavior, composed in one graph with hand-written background workers.
///
/// <para>This is the composition claim of the case study: an
/// <see cref="OperationModelStep"/> is an ordinary step function whose outcome
/// is a function of the state it is applied to, so the ordinary exploration
/// rules interleave it with independently active steps with no special
/// support. The evidence is that the composed graph satisfies the same
/// invariants, exposes the same changing domain transitions as the hand-written
/// model, and refines it.</para>
/// </summary>
[TestFixture]
public class OperationsFrontendTests
{
    [Test]
    public void ResponseDependentSubmitOutcomesBecomeOrdinaryGraphBranches()
    {
        var config = FulfillmentConfig.SingleWorker;
        var root = OperationsFrontend.BuildGraph(config);

        var responses = root.Edges
            .Select(edge => (OperationModelTransition)edge.Metadata)
            .Select(transition => (SubmitOutcome)transition.Response)
            .ToArray();

        Assert.That(
            responses,
            Is.EquivalentTo(new[] { SubmitOutcome.Accepted, SubmitOutcome.Rejected }));
        Assert.That(
            root.Edges.Select(edge => edge.StepFunction.StepFunctionId).Distinct().Single(),
            Is.EqualTo("operation:submit(o0)"));
    }

    [Test]
    public void AnAcceptedSubmitCommitsStateAndQueuesTheBackgroundHandlers()
    {
        var config = FulfillmentConfig.SingleWorker;
        var root = OperationsFrontend.BuildGraph(config);

        var accepted = root.Edges.Single(edge =>
            (SubmitOutcome)((OperationModelTransition)edge.Metadata).Response ==
                SubmitOutcome.Accepted);
        var rejected = root.Edges.Single(edge =>
            (SubmitOutcome)((OperationModelTransition)edge.Metadata).Response ==
                SubmitOutcome.Rejected);

        var acceptedStore = (StoreState)accepted.Target.State;
        Assert.That(acceptedStore.Orders[0], Is.EqualTo(OrderStatus.Submitted));
        Assert.That(acceptedStore.Outbox[0], Is.EqualTo(OutboxPhase.Pending));
        Assert.That(
            accepted.Target.StepFunctions.Select(step => step.StepFunctionId),
            Is.SupersetOf(new[]
            {
                PickUpOutboxRowStep.IdFor(0, 0),
                CallGatewayStep.IdFor(0, 0),
                SettleAttemptStep.IdFor(0, 0)
            }),
            "the accepting transaction commits domain state and queues work together");

        var rejectedStore = (StoreState)rejected.Target.State;
        Assert.That(rejectedStore.Orders[0], Is.EqualTo(OrderStatus.Rejected));
        Assert.That(rejectedStore.Outbox[0], Is.EqualTo(OutboxPhase.Absent));
        Assert.That(
            rejected.Target.StepFunctions.Select(step => step.StepFunctionId),
            Has.None.StartsWith("pick-up"),
            "a rejected submit queues no payment work at all");
    }

    [Test]
    public void TheComposedGraphSatisfiesTheSameSafetyProperties()
    {
        var config = FulfillmentConfig.Default;

        foreach (var root in new[]
        {
            OperationsFrontend.BuildGraph(config),
            OperationsFrontend.BuildCoActiveGraph(config)
        })
        {
            FulfillmentProperties.RequireComplete(root);
            Assert.That(
                root.Check(FulfillmentProperties.NoChargeWithoutCommittedOrder(config.Orders))
                    .Valid,
                Is.True);
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
    }

    [Test]
    public void TheComposedGraphNeedsTheSameFairnessForProgress()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentProperties.RequireComplete(
            OperationsFrontend.BuildCoActiveGraph(config));

        Assert.That(
            root.Check(
                    FulfillmentProperties.SubmittedOrdersResolve(0),
                    fairness: FulfillmentProperties.WorkersRun)
                .Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            root.Check(
                    FulfillmentProperties.SubmittedOrdersResolve(0),
                    fairness: FulfillmentProperties.ProgressAssumptions)
                .Valid,
            Is.True);
    }

    [Test]
    public void BothFrontendsProduceTheSameChangingDomainBehavior()
    {
        var config = FulfillmentConfig.Default;
        var native = FulfillmentModel.BuildNativeGraph(config);
        var queued = OperationsFrontend.BuildGraph(config);
        var coActive = OperationsFrontend.BuildCoActiveGraph(config);

        var nativeStates = FulfillmentModel.DomainStates(native);
        var nativeTransitions = FulfillmentModel.ChangingDomainTransitions(
            native,
            edge => (FulfillmentAction)edge.Metadata);

        foreach (var root in new[] { queued, coActive })
        {
            Assert.That(
                FulfillmentModel.DomainStates(root),
                Is.EquivalentTo(nativeStates));
            Assert.That(
                FulfillmentModel.ChangingDomainTransitions(root, OperationsFrontend.Label),
                Is.EquivalentTo(nativeTransitions));
        }

        TestContext.WriteLine(
            $"native {FulfillmentModel.Size(native)}, " +
            $"operations+queued work {FulfillmentModel.Size(queued)}, " +
            $"operations+co-active workers {FulfillmentModel.Size(coActive)}");
    }

    [Test]
    public void CoActiveCompositionNeverDuplicatesAnActiveStepIdentity()
    {
        var root = OperationsFrontend.BuildCoActiveGraph(FulfillmentConfig.Default);

        var duplicate = FulfillmentModel.Reachable(root)
            .Select(node => node.StepFunctions
                .GroupBy(step => step.StepFunctionId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1))
            .FirstOrDefault(group => group != null);

        Assert.That(
            duplicate,
            Is.Null,
            "a controller must not trigger a worker that is already independently active");
    }

    [Test]
    public void TheComposedGraphRefinesTheHandWrittenModel()
    {
        var config = FulfillmentConfig.Default;
        var concrete = OperationsFrontend.BuildCoActiveGraph(config);
        var abstraction = FulfillmentModel.BuildNativeGraph(config);

        var safety = Refinement
            .Between<StoreState, StoreState>(concrete, abstraction)
            .Map(store => (StoreState)store.Clone())
            .MapTransition(MapComposedTransition)
            .Check();
        var temporal = Refinement
            .Between<StoreState, StoreState>(concrete, abstraction)
            .Map(store => (StoreState)store.Clone())
            .MapTransition(MapComposedTransition)
            .CheckTemporal(
                concreteFairness: FulfillmentProperties.ProgressAssumptions,
                abstractFairness: FulfillmentProperties.ProgressAssumptions);

        Assert.That(
            safety.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            safety.GetTraceString());
        Assert.That(
            temporal.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            temporal.GetTraceString());
    }

    [Test]
    public void ComposingTwoStepsWithTheSameIdentityIsRejected()
    {
        var config = FulfillmentConfig.SingleWorker;

        var collision = Assert.Throws<ArgumentException>(() => OperationModel.Explore(
            StoreState.Empty(config),
            OperationsFrontend.ControllerSteps(config),
            additionalSteps: FulfillmentModel.WorkerSteps(config)
                .Concat(FulfillmentModel.WorkerSteps(config))));

        Assert.That(collision.Message, Does.Contain("is not unique in the composed model"));
    }

    private static AbstractResponse MapComposedTransition(
        RefinementTransition<StoreState> transition)
    {
        // Both graphs label their edges with the same FulfillmentAction record,
        // so the declared abstract response can be narrowed all the way down to
        // the action — including which gateway outcome occurred. Naming only the
        // step function would be ambiguous: two gateway outcomes can produce the
        // same next state once the order has already been charged.
        var expected = ExpectedAbstractAction(transition.Metadata);
        return AbstractResponse.Matching(
            candidate => !candidate.IsStutter && Equals(candidate.Metadata, expected));
    }

    private static FulfillmentAction ExpectedAbstractAction(object metadata)
    {
        if (metadata is FulfillmentAction worker)
        {
            return worker;
        }

        if (metadata is OperationModelTransition submit)
        {
            var request = (SubmitOrderRequest)submit.Request;
            return new FulfillmentAction(
                FulfillmentAction.Submit,
                request.Order,
                StoreState.NoWorker,
                (SubmitOutcome)submit.Response);
        }

        throw new InvalidOperationException(
            $"Unexpected composed edge metadata '{metadata}'.");
    }
}
