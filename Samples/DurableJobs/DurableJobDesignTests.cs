namespace DurableJobs.Tests;

using DurableJobs;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>Structure and control-flow checks for the process design.</summary>
[TestFixture]
public class DurableJobDesignTests
{
    [Test]
    public void TheDetailedDesignIsACompleteExactProcessGraph()
    {
        var root = DurableJobDesign.Explore();
        var report = ProcessGraphDiagnostics.Describe(root);
        var scheduler = (IProcessSchedulerStep)root.StepFunctions.Single();
        var actions = AllEdges(root)
            .Select(item => DurableJobDesign.ActionOf(item.Transition))
            .Where(action => action is not null)
            .ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(ModelGraph.IsComplete(root), Is.True);
            Assert.That(
                (report.ConfigurationCount, report.TransitionCount),
                Is.EqualTo((61, 293)));
            Assert.That(report.DomainStateCount, Is.EqualTo(56));
            Assert.That(report.Complete, Is.True);
            Assert.That(root.StepFunctions, Has.Count.EqualTo(1));
            Assert.That(
                scheduler.LiveProcesses.Select(process => process.Role),
                Is.EquivalentTo(DurableJobRoles.Processes));
            Assert.That(
                report.ContinuationFormsByRole.Keys,
                Is.EquivalentTo(DurableJobRoles.Correct));
            Assert.That(
                DurableJobRoles.RepeatedActions.Select(role =>
                    report.ContinuationFormsByRole[role].Single()),
                Has.All.EqualTo("<stateless recurring action>"));
            Assert.That(
                report.ContinuationFormsByRole[DurableJobRoles.Worker],
                Has.Some.Contains("foreveriteration:worker-loop"));
            Assert.That(actions, Does.Contain(DesignAction.EnqueueDuplicate));
            Assert.That(actions, Does.Contain(DesignAction.LoseDispatch));
            Assert.That(actions, Does.Contain(DesignAction.RebuildDispatch));
            Assert.That(actions, Does.Contain(DesignAction.ClaimNext));
            Assert.That(actions, Does.Contain(DesignAction.RecordSuccess));
            Assert.That(actions, Does.Contain(DesignAction.RecordFailure));
            Assert.That(actions, Does.Contain(DesignAction.CompleteSuccess));
            Assert.That(actions, Does.Contain(DesignAction.FailAttempt));
            Assert.That(actions, Does.Contain(DesignAction.CrashWorker));
            Assert.That(actions, Does.Contain(DesignAction.ExpireLease));
            Assert.That(actions, Does.Contain(DesignAction.RestartWorker));
        });

        TestContext.WriteLine(
            $"process design: {report.ConfigurationCount} configurations, " +
            $"{report.TransitionCount} edges, {report.DomainStateCount} domain states");
    }

    [Test]
    public void TheWorkerProcessReadsLikeClaimWaitBranchAndCommit()
    {
        var edges = AllEdges(DurableJobDesign.Explore()).ToArray();

        var claims = edges
            .Where(item =>
                DurableJobDesign.ActionOf(item.Transition) ==
                    DesignAction.ClaimNext)
            .ToArray();
        var successes = edges
            .Where(item =>
                DurableJobDesign.ActionOf(item.Transition) ==
                    DesignAction.CompleteSuccess)
            .ToArray();
        var failures = edges
            .Where(item =>
                DurableJobDesign.ActionOf(item.Transition) ==
                    DesignAction.FailAttempt)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(claims, Is.Not.Empty);
            Assert.That(successes, Is.Not.Empty);
            Assert.That(failures, Is.Not.Empty);
            Assert.That(
                claims.Select(item => item.Transition.ProcessRole),
                Has.All.EqualTo(DurableJobRoles.Worker));
            Assert.That(
                successes.Select(item => item.Transition.ProcessRole),
                Has.All.EqualTo(DurableJobRoles.Worker));
            Assert.That(
                failures.Select(item => item.Transition.ProcessRole),
                Has.All.EqualTo(DurableJobRoles.Worker));
        });

        foreach (var item in claims)
        {
            var before = (DurableJobDesignState)item.Node.State;
            var after = (DurableJobDesignState)item.Edge.Target.State;
            Assert.Multiple(() =>
            {
                Assert.That(before.CanClaim, Is.True);
                Assert.That(after.ActiveLease, Is.Not.Null);
                Assert.That(after.Attempts, Is.EqualTo(before.Attempts + 1));
                Assert.That(after.Worker, Is.EqualTo(WorkerPhase.Running));
            });
        }

        Assert.That(
            successes.Select(item =>
                ((DurableJobDesignState)item.Node.State).AttemptOutcome),
            Has.All.EqualTo(AttemptOutcome.Succeeded));
        Assert.That(
            failures.Select(item =>
                ((DurableJobDesignState)item.Node.State).AttemptOutcome),
            Has.All.EqualTo(AttemptOutcome.Failed));
    }

    [Test]
    public void ACrashDiscardsWorkerHostContinuationsAndRestartRelaunchesThem()
    {
        var root = DurableJobDesign.Explore();
        var crash = AllEdges(root).First(item =>
            item.Transition.Control == ProcessControlKind.Crash);
        var restart = AllEdges(root).First(item =>
            item.Transition.Control == ProcessControlKind.Restart);

        var beforeCrash = LiveRoles(crash.Node);
        var afterCrash = LiveRoles(crash.Edge.Target);
        var afterRestart = LiveRoles(restart.Edge.Target);

        Assert.Multiple(() =>
        {
            Assert.That(beforeCrash, Does.Contain(DurableJobRoles.Worker));
            Assert.That(afterCrash, Does.Not.Contain(DurableJobRoles.Worker));
            Assert.That(afterRestart, Does.Contain(DurableJobRoles.Worker));
            Assert.That(
                crash.Edge.Target.Edges
                    .Select(edge => DurableJobDesign.TransitionOf(edge).ProcessRole),
                Does.Not.Contain(DurableJobRoles.SuccessSource));
            Assert.That(
                crash.Edge.Target.Edges
                    .Select(edge => DurableJobDesign.TransitionOf(edge).ProcessRole),
                Does.Not.Contain(DurableJobRoles.FailureSource));
        });
    }

    [Test]
    public void StateNeutralWorkerControlStillChangesTheExactConfiguration()
    {
        var abandoned = AllEdges(DurableJobDesign.Explore()).First(item =>
            DurableJobDesign.ActionOf(item.Transition) ==
                DesignAction.AbandonAttempt);

        Assert.Multiple(() =>
        {
            Assert.That(
                abandoned.Edge.Target.State.StringRepresentation(),
                Is.EqualTo(abandoned.Node.State.StringRepresentation()));
            Assert.That(
                abandoned.Edge.Target.GetNodeFingerprint(),
                Is.Not.EqualTo(abandoned.Node.GetNodeFingerprint()),
                "the worker moved to a different continuation configuration");
        });
    }

    private static IEnumerable<(
        StateGraphNode Node,
        StateGraphEdge Edge,
        ProcessTransition Transition)> AllEdges(StateGraphNode root)
        => ModelGraph.Nodes(root)
            .SelectMany(node => node.Edges.Select(edge =>
                (node, edge, DurableJobDesign.TransitionOf(edge))));

    private static IReadOnlyCollection<string> LiveRoles(StateGraphNode node)
        => ((IProcessSchedulerStep)node.StepFunctions.Single())
            .LiveProcesses
            .Select(process => process.Role)
            .ToArray();
}
