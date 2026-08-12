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
/// The experimental coroutine frontend authoring the worker's sequential logic,
/// and the boundary of what it can soundly be used for.
///
/// <para>The coroutine worker is compared with the hand-written worker in a
/// <em>closed</em> sub-model — one worker, one already-committed order, nothing
/// else active. It is not composed with the controller or with a second worker,
/// and it is not used to establish progress. Both restrictions are executable
/// findings rather than caution:
/// <see cref="TheCompiledCoroutineStepIsNotAFunctionOfItsInputState"/> and
/// <see cref="TheGatewayFairnessAssumptionCannotBeStatedOnTheCompiledGraph"/>.</para>
/// </summary>
[TestFixture]
public class CoroutineFrontendTests
{
    private const int Order = 0;
    private const int Worker = 0;

    private static readonly FulfillmentConfig Config = FulfillmentConfig.SingleWorker;

    private static StateGraphNode Coroutine()
        => PaymentWorkerCoroutine.BuildGraph(Config, Order, Worker);

    private static StateGraphNode Manual()
        => FulfillmentModel.BuildNativeWorkerGraph(Config, Order);

    [Test]
    public void TheLoopBoundaryMakesTheRetryCycleAFiniteGraph()
    {
        var coroutine = Coroutine();
        var manual = Manual();

        Assert.That(
            FulfillmentModel.IsComplete(coroutine),
            Is.True,
            "a depth frontier would make every verdict below bounded, not definitive");

        var coroutineSize = FulfillmentModel.Size(coroutine);
        var manualSize = FulfillmentModel.Size(manual);
        TestContext.WriteLine(
            $"manual worker {manualSize}, coroutine worker {coroutineSize}");

        Assert.That(
            coroutineSize.Nodes,
            Is.GreaterThan(manualSize.Nodes),
            "the compiled graph carries the coroutine's control configuration too");

        // The retry loop is a real cycle in the compiled graph, not a finite
        // unrolling: some node is reachable from itself.
        Assert.That(
            FulfillmentModel.Reachable(coroutine)
                .Any(node => Reaches(node, node.GetNodeFingerprint())),
            Is.True);
    }

    [Test]
    public void HidingChooseMakesTheDomainStatesAndTransitionsMatchTheHandWrittenWorker()
    {
        var coroutine = Coroutine();
        var manual = Manual();

        Assert.That(
            FulfillmentModel.DomainStates(coroutine),
            Is.EquivalentTo(FulfillmentModel.DomainStates(manual)));
        Assert.That(
            FulfillmentModel.ChangingDomainTransitions(
                coroutine,
                edge => PaymentWorkerCoroutine.Label(edge, Worker, Order)),
            Is.EquivalentTo(FulfillmentModel.ChangingDomainTransitions(
                manual,
                edge => (FulfillmentAction)edge.Metadata)));
    }

    [Test]
    public void ChooseIsVisibleButStateNeutralAndThereforeNotEnabled()
    {
        var coroutine = Coroutine();
        var afterPickUp = coroutine.Edges.Single().Target;

        Assert.That(
            coroutine.Edges.Select(edge =>
                ((CoroutineTransition)edge.Metadata).CheckpointName).Single(),
            Is.EqualTo(FulfillmentAction.PickUp));

        var chooseEdges = afterPickUp.Edges
            .Where(edge =>
                ((CoroutineTransition)edge.Metadata).Kind == ModelCheckpointKind.Choose)
            .ToArray();
        Assert.That(chooseEdges, Has.Length.EqualTo(4));
        Assert.That(
            chooseEdges.Select(edge =>
                edge.Target.State.StringRepresentation()).Distinct().Single(),
            Is.EqualTo(afterPickUp.State.StringRepresentation()),
            "picking a gateway outcome changes no domain state");

        Assert.That(
            afterPickUp.Check(FulfillmentProperties.ExactFormula.Enabled(
                step => IsCheckpoint(step, ModelCheckpointKind.Choose, "gateway"))).Valid,
            Is.False,
            "state-neutral edges are never ENABLED");
        Assert.That(
            chooseEdges[0].Target.Check(FulfillmentProperties.ExactFormula.Enabled(
                    step => IsCheckpoint(
                        step,
                        ModelCheckpointKind.Step,
                        FulfillmentAction.CallGateway)))
                .Valid,
            Is.True,
            "after the selection, the gateway call is a changing compiled action");
    }

    [Test]
    public void TheCoroutineWorkerSatisfiesTheSameSafetyProperties()
    {
        var coroutine = FulfillmentProperties.RequireComplete(Coroutine());

        Assert.That(
            coroutine.Check(FulfillmentProperties.ChargedAtMostOnce(Config.Orders)).Valid,
            Is.True);
        Assert.That(
            coroutine.Check(FulfillmentProperties.OutboxMatchesOrder(Config.Orders)).Valid,
            Is.True);
        Assert.That(
            coroutine.Check(FulfillmentProperties.ResolutionIsFinal(Config.Orders)).Valid,
            Is.True);
        Assert.That(
            coroutine.Check(FulfillmentProperties.NoPaidOrderWithoutCharge(Config.Orders))
                .Valid,
            Is.True);
    }

    [Test]
    public void TheCoroutineWorkerRefinesTheHandWrittenWorkerOnSafetyAndFairBehavior()
    {
        var concrete = Coroutine();
        var abstraction = Manual();

        var safety = Build(concrete, abstraction).Check();
        var temporal = Build(concrete, abstraction).CheckTemporal(
            concreteFairness: Fairness.WeakAll,
            abstractFairness: Fairness.None);

        Assert.That(
            safety.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            safety.GetTraceString());
        Assert.That(
            temporal.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            temporal.GetTraceString());

        // Every control choice is declared hidden, and hiding is a checked
        // claim rather than a suppression: declaring that the hand-written
        // worker takes an action when the coroutine merely picks an outcome is
        // rejected.
        Assert.That(
            FulfillmentModel.Reachable(concrete)
                .SelectMany(node => node.Edges)
                .Where(edge =>
                    ((CoroutineTransition)edge.Metadata).Kind ==
                        ModelCheckpointKind.Choose)
                .Select(edge => PaymentWorkerCoroutine.MapToManualWorker(
                    edge.Metadata,
                    Worker,
                    Order)),
            Is.Not.Empty.And.All.EqualTo(AbstractResponse.Hidden));

        var overclaimed = Refinement
            .Between<StoreState, StoreState>(concrete, abstraction)
            .Map(store => (StoreState)store.Clone())
            .MapTransition(_ => AbstractResponse.Step(step => true))
            .Check();
        Assert.That(
            overclaimed.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch),
            "claiming the abstract worker moves on a state-neutral Choose is rejected");
    }

    [Test]
    public void TheGatewayFairnessAssumptionCannotBeStatedOnTheCompiledGraph()
    {
        // The hand-written worker offers all four gateway outcomes as changing
        // edges of one action, so "the gateway eventually answers definitively"
        // is expressible as strong fairness and it closes the retry cycle.
        //
        // The coroutine decides the outcome at a Choose, which is visible but
        // state-neutral. Accordant's fairness — deliberately — counts changing
        // edges only, so on the compiled retry cycle there is no changing edge
        // producing a definitive answer to attach an obligation to. The
        // assumption is therefore not statable on this graph at all, and the
        // progress property stays violated however much fairness is added.
        var resolves = FulfillmentProperties.SubmittedOrdersResolve(Order);
        var manual = FulfillmentProperties.RequireComplete(Manual());
        var coroutine = FulfillmentProperties.RequireComplete(Coroutine());

        Assert.That(
            manual.Check(resolves, fairness: FulfillmentProperties.WorkersRun).Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            manual.Check(resolves, fairness: FulfillmentProperties.ProgressAssumptions)
                .Valid,
            Is.True);

        var everything = Fairness.WeakAll +
            Fairness.Strong(_ => true) +
            FulfillmentProperties.GatewayAnswersDefinitively;
        var coroutineResult = coroutine.Check(resolves, fairness: everything);

        Assert.That(
            coroutineResult.Status,
            Is.EqualTo(PropertyCheckingStatus.Violated),
            "no fairness constraint can exclude the compiled retry cycle");

        // The precise reason, checked rather than argued in prose: no node of
        // the bad cycle has a changing outgoing edge producing a definitive
        // gateway answer, while the hand-written retry cycle does.
        Assert.That(coroutineResult.BadCycle, Is.Not.Null);
        Assert.That(
            coroutineResult.BadCycle.Nodes.Any(DefinitiveAnswerEnabledAt),
            Is.False);
        Assert.That(
            FulfillmentModel.Reachable(manual).Any(node =>
                DefinitiveAnswerEnabledAt(node) &&
                Reaches(node, node.GetNodeFingerprint())),
            Is.True,
            "the hand-written retry cycle keeps the definitive answer enabled");
    }

    [Test]
    public void AddingTheGatewayAssumptionToBothSidesMakesRefinementFail()
    {
        // The same gap, seen through refinement: the compiled graph has a fair
        // behavior the hand-written model excludes, so the coroutine does not
        // temporally refine it under the assumption that makes the hand-written
        // model live.
        var result = Build(Coroutine(), Manual()).CheckTemporal(
            concreteFairness: Fairness.WeakAll +
                FulfillmentProperties.GatewayAnswersDefinitively,
            abstractFairness: FulfillmentProperties.ProgressAssumptions);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
    }

    [Test]
    public void TheDeterminismAuditAcceptsTheWorkflow()
    {
        // Opt-in, roughly doubles exploration cost, and is sampling rather than
        // proof — but it is the strongest check the runtime offers.
        var audited = PaymentWorkerCoroutine.BuildAuditedGraph(Config, Order, Worker);

        Assert.That(FulfillmentModel.IsComplete(audited), Is.True);
        Assert.That(
            FulfillmentModel.DomainStates(audited),
            Is.EquivalentTo(FulfillmentModel.DomainStates(Coroutine())));
    }

    [Test]
    public void AuditingAColdDelegateReportsTheCompilersLambdaCache()
    {
        // A finding of this study, and a real usability limit of the audit: the
        // C# compiler caches a workflow's non-escaping lambdas in <>9__ fields
        // on the closure that captured worker and order. Those fields are
        // written the first time each lambda is evaluated, and the audit
        // compares captured variables around the body, so a freshly created
        // workflow delegate fails its own audit with a diagnostic about
        // compiler-generated state rather than about the model.
        var cold = PaymentWorkerCoroutine.Workflow(Worker, Order, Config.IdempotentCharge);

        var failure = Assert.Throws<ModelDefinitionException>(() =>
            CoroutineModel.Explore(
                PaymentWorkerCoroutine.WorkflowName,
                StoreState.AfterAcceptedSubmit(Config, Order),
                cold,
                maxDepth: 64,
                verifyDeterminism: true));

        Assert.That(failure.Message, Does.Contain("captured external variable"));
        Assert.That(failure.Message, Does.Contain("<>9__"));
        TestContext.WriteLine(failure.Message);
    }

    [Test]
    public void EveryDeclaredWorkflowInputIsAnImmutableScalar()
    {
        var captured = CoroutineModel.DescribeCapturedInputs(
            PaymentWorkerCoroutine.Workflow(Worker, Order, Config.IdempotentCharge));

        foreach (var input in captured)
        {
            TestContext.WriteLine($"captured input: {input}");
        }

        // The report also lists the compiler's own <>9__ lambda-cache fields;
        // only the workflow's declared captures are the model's business.
        var declared = captured
            .Where(input => !input.Name.StartsWith("<>", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            declared.Select(input => input.Name),
            Is.EquivalentTo(new[] { "worker", "order", "idempotentCharge" }));
        Assert.That(
            declared.Select(input => input.Monitoring),
            Is.All.EqualTo(CoroutineCapturedInputMonitoring.ImmutableScalar),
            "capturing a mutable object would only be monitored by reference identity");
    }

    [Test]
    public void TheCompiledCoroutineStepIsNotAFunctionOfItsInputState()
    {
        // This is why the coroutine worker is studied in a closed sub-model
        // rather than composed with the controller or a second worker: the
        // pending checkpoint of a compiled step was computed from the state the
        // coroutine arrived at, so an interleaved write between arrival and
        // selection is invisible to it.
        var (offered, selectorWouldAllow) =
            PaymentWorkerCoroutine.CompiledStepIsNotAFunctionOfTheStateItIsAppliedTo();

        Assert.That(
            selectorWouldAllow,
            Is.EquivalentTo(new[] { GatewayOutcome.Charged }),
            "in the interfered-with state the workflow's own selector allows one outcome");
        Assert.That(
            offered,
            Is.EquivalentTo(CallGatewayStep.GatewayOutcomes),
            "but the compiled step still offers the choice set it computed earlier");
        Assert.That(
            offered.Count,
            Is.GreaterThan(selectorWouldAllow.Count),
            "the extra branches are behavior no execution of the workflow could take");
    }

    private static FunctionalRefinementCheck<StoreState, StoreState> Build(
        StateGraphNode concrete,
        StateGraphNode abstraction)
        => Refinement
            .Between<StoreState, StoreState>(concrete, abstraction)
            .Map(store => (StoreState)store.Clone())
            .MapTransition(transition =>
                PaymentWorkerCoroutine.MapToManualWorker(transition, Worker, Order));

    private static bool DefinitiveAnswerEnabledAt(StateGraphNode node)
        => node.Edges.Any(edge =>
            !string.Equals(
                node.State.StringRepresentation(),
                edge.Target.State.StringRepresentation(),
                StringComparison.Ordinal) &&
            FulfillmentProperties.DefinitiveGatewayAnswer(
                (StoreState)node.State,
                (StoreState)edge.Target.State));

    private static bool IsCheckpoint(
        IStepFunction step,
        ModelCheckpointKind kind,
        string name)
        => step is ICoroutineCheckpointStep checkpoint &&
            checkpoint.CheckpointKind == kind &&
            checkpoint.CheckpointName == name;

    private static bool Reaches(StateGraphNode from, string targetFingerprint)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<StateGraphNode>();
        foreach (var edge in from.Edges)
        {
            pending.Push(edge.Target);
        }

        while (pending.Count > 0)
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
