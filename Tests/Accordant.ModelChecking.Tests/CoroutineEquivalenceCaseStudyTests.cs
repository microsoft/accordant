// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests;

using System.Linq;
using CoroutineModelChecking;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>
/// Executable checks for the two-worker coroutine equivalence case study.
/// </summary>
[TestFixture]
public class CoroutineEquivalenceCaseStudyTests
{
    [Test]
    public void RawGraphsHaveStableIdentitiesAndExpectedControlOverhead()
    {
        var manual = WorkerCompetitionCaseStudy.BuildManualGraph();
        var coroutine = WorkerCompetitionCaseStudy.BuildCoroutineGraph();
        var repeatedCoroutine = WorkerCompetitionCaseStudy.BuildCoroutineGraph();

        Assert.That(
            WorkerCompetitionCaseStudy.GetRawGraphSize(manual),
            Is.EqualTo(new GraphSize(Nodes: 5, Edges: 4)));
        Assert.That(
            WorkerCompetitionCaseStudy.GetRawGraphSize(coroutine),
            Is.EqualTo(new GraphSize(Nodes: 7, Edges: 6)),
            "one state-neutral Choose edge and one selected control configuration per worker");
        Assert.That(
            coroutine.GetNodeFingerprint(),
            Is.EqualTo(repeatedCoroutine.GetNodeFingerprint()));
        Assert.That(
            manual.Edges.Select(edge => edge.StepFunction.StepFunctionId),
            Is.EqualTo(new[] { "claim(ada)", "claim(grace)" }));
        Assert.That(
            coroutine.Edges.Select(edge => ((CoroutineTransition)edge.Metadata).ToString()),
            Is.EqualTo(new[] { "choose:worker=ada", "choose:worker=grace" }));
        Assert.That(
            coroutine.StepFunctions.Single(),
            Is.InstanceOf<ICoroutineCheckpointStep>());
    }

    [Test]
    public void HidingChooseMakesTheProjectedDomainStateAndTransitionSetsMatch()
    {
        var manual = WorkerCompetitionCaseStudy.BuildManualGraph();
        var coroutine = WorkerCompetitionCaseStudy.BuildCoroutineGraph();

        Assert.That(
            WorkerCompetitionCaseStudy.ProjectedDomainStates(coroutine),
            Is.EquivalentTo(WorkerCompetitionCaseStudy.ProjectedDomainStates(manual)));
        Assert.That(
            WorkerCompetitionCaseStudy.CoroutineChangingTransitionsHidingChoose(coroutine),
            Is.EquivalentTo(WorkerCompetitionCaseStudy.ManualChangingTransitions(manual)));
        Assert.That(
            WorkerCompetitionCaseStudy.CoroutineChangingTransitionsHidingChoose(coroutine),
            Has.Count.EqualTo(4));
    }

    [Test]
    public void FiniteGraphsSatisfyTheSamePropertiesAndWeakAllIsVacuous()
    {
        var formula = Formula.For<WorkerTaskState>();
        var completed = formula.Observe(state => state.IsCompleted, "Completed");
        var correctFinisher = formula.Observe(
            state => state.FinishedBy == null || state.FinishedBy == state.ClaimedBy,
            "FinisherIsClaimer");
        var safety = formula.Always(correctFinisher);
        var liveness = formula.Eventually(completed);

        foreach (var root in new[]
        {
            WorkerCompetitionCaseStudy.BuildManualGraph(),
            WorkerCompetitionCaseStudy.BuildCoroutineGraph()
        })
        {
            Assert.That(root.Check(safety).Valid, Is.True);
            Assert.That(root.Check(liveness).Valid, Is.True);
            Assert.That(
                root.Check(liveness, fairness: Fairness.WeakAll).Valid,
                Is.True,
                "all finite paths reach completion; this does not establish " +
                "fairness preservation because there is no nonterminal cycle");
        }
    }

    [Test]
    public void EnabledUsesTheFullCompiledGraphAndIgnoresStateNeutralChoose()
    {
        var manual = WorkerCompetitionCaseStudy.BuildManualGraph();
        var coroutine = WorkerCompetitionCaseStudy.BuildCoroutineGraph();
        var formula = Formula.For<WorkerTaskState>().AllowStutterSensitiveFormulas();

        Assert.That(
            manual.Check(formula.Enabled(WorkerCompetitionCaseStudy.IsManualClaim)).Valid,
            Is.True,
            "manual claim(worker) is a changing action at the root");
        Assert.That(
            coroutine.Check(formula.Enabled(
                step => WorkerCompetitionCaseStudy.IsCoroutineCheckpoint(
                    step,
                    ModelCheckpointKind.Choose,
                    "worker"))).Valid,
            Is.False,
            "Choose has visible control edges but they leave the domain state unchanged");

        var afterChoose = coroutine.Edges.First().Target;
        Assert.That(
            afterChoose.State.StringRepresentation(),
            Is.EqualTo(coroutine.State.StringRepresentation()));
        Assert.That(
            afterChoose.Check(formula.Enabled(
                step => WorkerCompetitionCaseStudy.IsCoroutineCheckpoint(
                    step,
                    ModelCheckpointKind.Step,
                    "claim"))).Valid,
            Is.True,
            "after selection, claim is a changing compiled graph action");
    }

    [Test]
    public void TypedReplayMetadataLetsTheCoroutineRefineTheManualGraph()
    {
        var concrete = WorkerCompetitionCaseStudy.BuildCoroutineGraph();
        var abstraction = WorkerCompetitionCaseStudy.BuildManualGraph();

        var safety = Microsoft.Accordant.ModelChecking.Refinement
            .Between<WorkerTaskState, WorkerTaskState>(concrete, abstraction)
            .Map(WorkerCompetitionCaseStudy.Project)
            .MapTransition(WorkerCompetitionCaseStudy.MapCoroutineTransition)
            .Check();
        var temporal = Microsoft.Accordant.ModelChecking.Refinement
            .Between<WorkerTaskState, WorkerTaskState>(concrete, abstraction)
            .Map(WorkerCompetitionCaseStudy.Project)
            .MapTransition(WorkerCompetitionCaseStudy.MapCoroutineTransition)
            .CheckTemporal(
                concreteFairness: Fairness.WeakAll,
                abstractFairness: Fairness.WeakAll);

        Assert.That(safety.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(temporal.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }
}
