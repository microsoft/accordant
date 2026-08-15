// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>
/// The structured process frontend, including composition, exact
/// configurations, action-aware fairness, and refinement.
/// </summary>
[TestFixture]
public class ProcessFrontendTests
{
    private const int Order = 0;
    private const int Worker = 0;

    private static readonly FulfillmentConfig Config = FulfillmentConfig.SingleWorker;

    private static StateGraphNode ProcessWorker()
        => ProcessFrontend.BuildWorkerGraph(Config, Order, Worker);

    private static StateGraphNode ManualWorker()
        => FulfillmentModel.BuildNativeWorkerGraph(Config, Order);

    [Test]
    public void StructuredIterationAndCallProduceACompleteExactGraph()
    {
        var process = ProcessWorker();
        var manual = ManualWorker();
        var report = ProcessGraphDiagnostics.Describe(process);

        Assert.Multiple(() =>
        {
            Assert.That(report.Complete, Is.True);
            Assert.That(
                (report.ConfigurationCount, report.TransitionCount),
                Is.EqualTo(FulfillmentModel.Size(manual)));
            Assert.That(report.DomainStateCount, Is.EqualTo(12));
            Assert.That(
                report.ContinuationFormsByRole[ProcessFrontend.WorkerRole(Worker)],
                Has.Some.Contains("foreveriteration:attempt-loop"));
            Assert.That(
                report.ContinuationFormsByRole[ProcessFrontend.WorkerRole(Worker)],
                Has.Some.Contains("call:call-gateway"));
        });

        TestContext.WriteLine(
            $"structured worker: {report.ConfigurationCount} configurations, " +
            $"{report.TransitionCount} edges, {report.DomainStateCount} domain states");
    }

    [Test]
    public void ChooseStepRecordsGatewayOutcomeAndMutationOnOneEdge()
    {
        var root = ProcessWorker();
        var afterPickUp = root.Edges.Single().Target;
        var gatewayEdges = afterPickUp.Edges.ToArray();

        Assert.That(gatewayEdges, Has.Length.EqualTo(4));
        Assert.That(
            gatewayEdges.Select(edge => ProcessFrontend.TransitionOf(edge).CheckpointKind),
            Is.All.EqualTo(ModelCheckpointKind.ChooseStep));
        Assert.That(
            gatewayEdges.Select(edge => ProcessFrontend.TransitionOf(edge).SemanticAction),
            Is.All.EqualTo(FulfillmentProcessAction.CallGateway));
        Assert.That(
            gatewayEdges.Select(edge => ProcessFrontend.TransitionOf(edge).Value),
            Is.EquivalentTo(CallGatewayStep.GatewayOutcomes));
        Assert.That(
            gatewayEdges.Select(edge => ProcessFrontend.TransitionOf(edge).Subject),
            Is.All.EqualTo(Order));
        Assert.That(
            gatewayEdges.All(edge =>
                edge.Target.State.StringRepresentation() !=
                afterPickUp.State.StringRepresentation()),
            Is.True,
            "selection and gateway mutation are one edge, not a neutral Choose");
    }

    [Test]
    public void StructuredWorkerHasTheSameProjectedBehaviorAsTheManualWorker()
    {
        var process = ProcessWorker();
        var manual = ManualWorker();

        Assert.That(
            FulfillmentModel.DomainStates(process),
            Is.EquivalentTo(FulfillmentModel.DomainStates(manual)));
        Assert.That(
            FulfillmentModel.ChangingDomainTransitions(process, ProcessFrontend.Label),
            Is.EquivalentTo(FulfillmentModel.ChangingDomainTransitions(
                manual,
                edge => (FulfillmentAction)edge.Metadata)));
    }

    [Test]
    public void FullyComposedProcessesMatchTheManualDomainModel()
    {
        var config = FulfillmentConfig.Default;
        var process = ProcessFrontend.BuildGraph(config);
        var manual = FulfillmentModel.BuildNativeGraph(config);
        var report = ProcessGraphDiagnostics.Describe(process);

        Assert.Multiple(() =>
        {
            Assert.That(report.Complete, Is.True);
            Assert.That(
                FulfillmentModel.DomainStates(process),
                Is.EquivalentTo(FulfillmentModel.DomainStates(manual)));
            Assert.That(
                FulfillmentModel.ChangingDomainTransitions(process, ProcessFrontend.Label),
                Is.EquivalentTo(FulfillmentModel.ChangingDomainTransitions(
                    manual,
                    edge => (FulfillmentAction)edge.Metadata)));
        });

        TestContext.WriteLine(
            $"structured system: {report.ConfigurationCount} configurations, " +
            $"{report.TransitionCount} edges, {report.DomainStateCount} domain states");
    }

    [Test]
    public void ComposedProcessesSatisfyTheSameSafetyProperties()
    {
        var config = FulfillmentConfig.Default;
        var root = FulfillmentProperties.RequireComplete(
            ProcessFrontend.BuildGraph(config));

        Assert.Multiple(() =>
        {
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
                root.Check(FulfillmentProperties.NoPaidOrderWithoutCharge(config.Orders))
                    .Valid,
                Is.True);
            Assert.That(
                root.Check(FulfillmentProperties.ResolutionIsFinal(config.Orders)).Valid,
                Is.True);
        });
    }

    [Test]
    public void StructuredProcessesPreserveTheThreeDefectCounterexamples()
    {
        var dualWrite = FulfillmentConfig.Default.WithDualWriteSubmit();
        var nonIdempotent = FulfillmentConfig.SingleWorker.WithNonIdempotentCharge();
        var noLease = FulfillmentConfig.CompetingWorkers.WithoutOutboxLease();

        Assert.Multiple(() =>
        {
            Assert.That(
                ProcessFrontend.BuildGraph(dualWrite)
                    .Check(FulfillmentProperties.OutboxMatchesOrder(dualWrite.Orders))
                    .Status,
                Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(
                ProcessFrontend.BuildGraph(nonIdempotent)
                    .Check(FulfillmentProperties.ChargedAtMostOnce(nonIdempotent.Orders))
                    .Status,
                Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(
                ProcessFrontend.BuildGraph(noLease)
                    .Check(FulfillmentProperties.ResolutionIsFinal(noLease.Orders))
                    .Status,
                Is.EqualTo(PropertyCheckingStatus.Violated));
        });
    }

    [Test]
    public void StructuredDefectVariantsPreserveManualDomainBehavior()
    {
        var configurations = new[]
        {
            FulfillmentConfig.Default.WithDualWriteSubmit(),
            FulfillmentConfig.SingleWorker.WithNonIdempotentCharge(),
            FulfillmentConfig.CompetingWorkers.WithoutOutboxLease()
        };

        foreach (var config in configurations)
        {
            var process = ProcessFrontend.BuildGraph(config);
            var manual = FulfillmentModel.BuildNativeGraph(config);

            Assert.That(
                FulfillmentModel.DomainStates(process),
                Is.EquivalentTo(FulfillmentModel.DomainStates(manual)),
                config.ToString());
            Assert.That(
                FulfillmentModel.ChangingDomainTransitions(process, ProcessFrontend.Label),
                Is.EquivalentTo(FulfillmentModel.ChangingDomainTransitions(
                    manual,
                    edge => (FulfillmentAction)edge.Metadata)),
                config.ToString());
        }
    }

    [Test]
    public void ActionAwareGatewayFairnessPreservesTheProgressLadder()
    {
        var root = FulfillmentProperties.RequireComplete(ProcessWorker());
        var resolves = FulfillmentProperties.SubmittedOrdersResolve(Order);

        Assert.That(
            root.Check(resolves, fairness: Fairness.None).Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            root.Check(resolves, fairness: ProcessFrontend.WorkersRun).Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            root.Check(
                    resolves,
                    fairness: ProcessFrontend.WorkersRun +
                        ProcessFrontend.WeakGatewayAnswersDefinitively)
                .Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            root.Check(resolves, fairness: ProcessFrontend.ProgressAssumptions).Valid,
            Is.True);
    }

    [Test]
    public void EveryOrderAlsoNeedsPerOrderControllerFairness()
    {
        var root = FulfillmentProperties.RequireComplete(
            ProcessFrontend.BuildGraph(FulfillmentConfig.Default));

        Assert.That(
            root.Check(
                    FulfillmentProperties.EveryOrderResolves(),
                    fairness: ProcessFrontend.ProgressAssumptions)
                .Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            root.Check(
                    FulfillmentProperties.EveryOrderResolves(),
                    fairness: ProcessFrontend.ProgressAssumptions +
                        ProcessFrontend.ControllerRuns)
                .Valid,
            Is.True);
    }

    [Test]
    public void StructuredWorkerRefinesTheHandWrittenWorker()
    {
        var concrete = ProcessWorker();
        var abstraction = ManualWorker();
        var check = Refinement
            .Between<StoreState, StoreState>(concrete, abstraction)
            .Map(store => (StoreState)store.Clone())
            .MapTransition(ProcessFrontend.MapToManual);

        var safety = check.Check();
        var temporal = check.CheckTemporal(
            concreteFairness: ProcessFrontend.ProgressAssumptions,
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
    public void FullyComposedProcessesRefineTheHandWrittenSystem()
    {
        var config = FulfillmentConfig.Default;
        var result = Refinement
            .Between<StoreState, StoreState>(
                ProcessFrontend.BuildGraph(config),
                FulfillmentModel.BuildNativeGraph(config))
            .Map(store => (StoreState)store.Clone())
            .MapTransition(ProcessFrontend.MapToManual)
            .Check();

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            result.GetTraceString());
    }

    [Test]
    public void EveryProcessEdgeDeclaresAnExactManualAction()
    {
        var process = ProcessFrontend.BuildGraph(FulfillmentConfig.Default);
        var actions = FulfillmentModel.Reachable(process)
            .SelectMany(node => node.Edges)
            .Select(ProcessFrontend.Label)
            .ToArray();

        Assert.That(actions, Is.Not.Empty);
        Assert.That(actions, Has.Some.Property(nameof(FulfillmentAction.Kind))
            .EqualTo(FulfillmentAction.Submit));
        Assert.That(actions, Has.Some.Property(nameof(FulfillmentAction.Kind))
            .EqualTo(FulfillmentAction.PickUp));
        Assert.That(actions, Has.Some.Property(nameof(FulfillmentAction.Kind))
            .EqualTo(FulfillmentAction.CallGateway));
        Assert.That(actions, Has.Some.Property(nameof(FulfillmentAction.Kind))
            .EqualTo(FulfillmentAction.Settle));
    }

    [Test]
    public void TheRetryLoopIsARealCycleRatherThanABoundedUnrolling()
    {
        var root = ProcessWorker();

        Assert.That(
            FulfillmentModel.Reachable(root)
                .Any(node => Reaches(node, node.GetNodeFingerprint())),
            Is.True);
    }

    private static bool Reaches(StateGraphNode from, string targetFingerprint)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<StateGraphNode>(from.Edges.Select(edge => edge.Target));

        while (pending.Count != 0)
        {
            var node = pending.Pop();
            var fingerprint = node.GetNodeFingerprint();
            if (fingerprint == targetFingerprint)
            {
                return true;
            }

            if (!seen.Add(fingerprint))
            {
                continue;
            }

            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }

        return false;
    }
}
