namespace DurableJobs.Tests;

using DurableJobs;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

[TestFixture]
public class DurableJobRefinementTests
{
    [Test]
    public void TheProcessDesignSafetyRefinesTheAtomicContract()
    {
        var result = DurableJobRefinement.Build().Check();

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            result.GetTraceString());
    }

    [Test]
    public void EveryProcessActionHasACheckedVisibleOrHiddenInterpretation()
    {
        var declarations = ModelGraph
            .Edges(DurableJobDesign.Explore())
            .Select(pair =>
            {
                var process = DurableJobDesign.TransitionOf(pair.Edge);
                return new
                {
                    Action = DurableJobDesign.ActionOf(process),
                    Process = process,
                    Response = DurableJobRefinement.Declare(
                        process,
                        (DurableJobDesignState)pair.Source.State)
                };
            })
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                declarations
                    .Where(item => item.Action is
                        DesignAction.EnqueueDuplicate or
                        DesignAction.LoseDispatch or
                        DesignAction.RebuildDispatch or
                        DesignAction.ClaimNext or
                        DesignAction.RecordSuccess or
                        DesignAction.RecordFailure or
                        DesignAction.AbandonAttempt or
                        DesignAction.CrashWorker or
                        DesignAction.RestartWorker)
                    .Select(item => item.Response.HidesConcreteAction),
                Is.Not.Empty.And.All.True);
            Assert.That(
                declarations.Any(item =>
                    item.Action == DesignAction.Submit &&
                    !item.Response.HidesConcreteAction),
                Is.True);
            Assert.That(
                declarations.Any(item =>
                    item.Action == DesignAction.CompleteSuccess &&
                    !item.Response.HidesConcreteAction),
                Is.True);
            Assert.That(
                declarations.Any(item =>
                    item.Action == DesignAction.FailAttempt &&
                    !item.Response.HidesConcreteAction),
                Is.True,
                "the final FailAttempt is the durable Failed linearization point");
            Assert.That(
                declarations.Any(item =>
                    item.Action == DesignAction.ExpireLease &&
                    !item.Response.HidesConcreteAction),
                Is.True,
                "final-attempt expiry is also a durable Failed linearization point");
        });
    }

    [Test]
    public void FirstFailureIsHiddenButRetryExhaustionIsVisible()
    {
        var failures = ModelGraph
            .Edges(DurableJobDesign.Explore())
            .Select(pair => new
            {
                Source = (DurableJobDesignState)pair.Source.State,
                Process = DurableJobDesign.TransitionOf(pair.Edge)
            })
            .Where(item =>
                DurableJobDesign.ActionOf(item.Process) ==
                    DesignAction.FailAttempt)
            .ToArray();

        Assert.That(
            failures
                .Where(item => item.Source.Attempts == 1)
                .Select(item => DurableJobRefinement
                    .Declare(item.Process, item.Source)
                    .HidesConcreteAction),
            Is.Not.Empty.And.All.True);
        Assert.That(
            failures
                .Where(item =>
                    item.Source.Attempts == DurableJobFixture.MaxAttempts)
                .Select(item => DurableJobRefinement
                    .Declare(item.Process, item.Source)
                    .HidesConcreteAction),
            Is.Not.Empty.And.All.False);
    }

    [Test]
    public void CompletionAfterCancellationProducesAShortCounterexample()
    {
        var result = DurableJobRefinement
            .Build(new DesignOptions
            {
                AllowCompletionAfterCancellation = true
            })
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch),
            result.GetTraceString());
        Assert.That(
            LastAction(result),
            Is.EqualTo(DesignAction.CompleteSuccess));
        Assert.That(
            ((AtomicJobState)result.Trace[^2].MappedAbstractState).Status,
            Is.EqualTo(JobStatus.Cancelled));
        Assert.That(
            ((AtomicJobState)result.Trace[^1].MappedAbstractState).Status,
            Is.EqualTo(JobStatus.Succeeded));
    }

    [Test]
    public void LosingAnAcceptedDurableRowDoesNotRefine()
    {
        var result = DurableJobRefinement
            .Build(new DesignOptions { LoseAcceptedJob = true })
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch),
            result.GetTraceString());
        Assert.That(
            LastAction(result),
            Is.EqualTo(DesignAction.LoseAcceptedJob));
        Assert.That(
            ((AtomicJobState)result.Trace[^2].MappedAbstractState).Status,
            Is.EqualTo(JobStatus.Pending));
        Assert.That(
            ((AtomicJobState)result.Trace[^1].MappedAbstractState).Status,
            Is.EqualTo(JobStatus.Missing));
    }

    private static DesignAction? LastAction(
        RefinementCheckingResult result)
        => DurableJobDesign.ActionOf(
            (ProcessTransition)result.Trace[^1].ConcreteEdgeMetadata);
}
