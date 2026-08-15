namespace TemporalRefinement;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class TemporalRefinementTests
{
    [Test]
    public void FairWorkerRefinesFairJob()
    {
        var result = BuildCheck().CheckTemporal(
            concreteFairness: Fairness.Weak<CompleteWork>(),
            abstractFairness: Fairness.Weak<CompleteJob>());

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void PollingForeverViolatesAbstractFairnessWithoutWorkerFairness()
    {
        var result = BuildCheck().CheckTemporal(
            concreteFairness: Fairness.None,
            abstractFairness: Fairness.Weak<CompleteJob>());

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(
            result.Trace
                .Where(item => item.IsInCycle)
                .Select(item => item.ConcreteStepFunction?.StepFunctionId),
            Does.Contain("poll-work"));
    }

    private static FunctionalRefinementCheck<WorkerState, JobState> BuildCheck()
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[]
            {
                new SubmitWork(),
                new StartWork(),
                new PollWork(),
                new CompleteWork()
            },
            new WorkerState { Stage = WorkerStage.Idle },
            lazy: true);
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[]
            {
                new SubmitJob(),
                new CompleteJob()
            },
            new JobState { Stage = JobStage.Idle },
            lazy: true);

        return Refinement
            .Between<WorkerState, JobState>(concrete, abstraction)
            .Map(worker => new JobState
            {
                Stage = worker.Stage == WorkerStage.Idle
                    ? JobStage.Idle
                    : worker.Stage == WorkerStage.Completed
                        ? JobStage.Completed
                        : JobStage.Pending
            });
    }
}
