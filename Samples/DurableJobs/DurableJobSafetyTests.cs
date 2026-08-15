namespace DurableJobs.Tests;

using DurableJobs;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>State and transition invariants of the process design.</summary>
[TestFixture]
public class DurableJobSafetyTests
{
    [Test]
    public void DurableRowsAndTerminalOutcomesStayCanonical()
    {
        var root = DurableJobDesign.Explore();

        Assert.Multiple(() =>
        {
            Holds(root, DurableJobProperties.OutcomeShapeIsConsistent());
            Holds(root, DurableJobProperties.AcceptedRowNeverDisappears());
            Holds(root, DurableJobProperties.TerminalOutcomeIsStable());
            Holds(root, DurableJobProperties.AttemptsNeverDecrease());
        });
    }

    [Test]
    public void QueueLeaseWorkerAndAttemptStateStayCanonical()
    {
        var root = DurableJobDesign.Explore();

        Assert.Multiple(() =>
        {
            Holds(root, DurableJobProperties.QueueIsBoundedAndEligible());
            Holds(root, DurableJobProperties.LeaseWorkerAndAttemptsAreConsistent());
        });
    }

    [Test]
    public void CancellationSuccessAndFailureHaveOneDurableWinner()
    {
        var root = DurableJobDesign.Explore();

        Holds(root, DurableJobProperties.TerminalDecisionIsSingleWinner());

        var race = ModelGraph.Nodes(root).First(node =>
        {
            var actions = node.Edges
                .Select(edge =>
                    DurableJobDesign.ActionOf(
                        DurableJobDesign.TransitionOf(edge)))
                .ToHashSet();
            return actions.Contains(DesignAction.Cancel) &&
                actions.Contains(DesignAction.CompleteSuccess);
        });

        var terminalTargets = race.Edges
            .Where(edge =>
            {
                var action = DurableJobDesign.ActionOf(
                    DurableJobDesign.TransitionOf(edge));
                return action is
                    DesignAction.Cancel or DesignAction.CompleteSuccess;
            })
            .Select(edge => ((DurableJobDesignState)edge.Target.State).Status)
            .ToHashSet();

        Assert.That(
            terminalTargets,
            Is.EquivalentTo(new[]
            {
                JobStatus.Cancelled,
                JobStatus.Succeeded
            }));
    }

    [Test]
    public void TheGraphExercisesRetryExhaustionAndBothLeaseExpiryPaths()
    {
        var edges = ModelGraph
            .Nodes(DurableJobDesign.Explore())
            .SelectMany(node => node.Edges.Select(edge => new
            {
                Before = (DurableJobDesignState)node.State,
                After = (DurableJobDesignState)edge.Target.State,
                Action = DurableJobDesign.ActionOf(
                    DurableJobDesign.TransitionOf(edge))
            }))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                edges.Any(item =>
                    item.Action == DesignAction.FailAttempt &&
                    item.Before.Attempts == 1 &&
                    item.After.Status == JobStatus.Pending &&
                    item.After.QueueDepth > 0),
                Is.True,
                "the first failed attempt must redispatch");
            Assert.That(
                edges.Any(item =>
                    item.Action == DesignAction.FailAttempt &&
                    item.Before.Attempts == DurableJobFixture.MaxAttempts &&
                    item.After.Status == JobStatus.Failed),
                Is.True,
                "the final failed attempt must close the row");
            Assert.That(
                edges.Any(item =>
                    item.Action == DesignAction.ExpireLease &&
                    item.Before.Attempts == 1 &&
                    item.After.Status == JobStatus.Pending &&
                    item.After.Worker == WorkerPhase.Stopped),
                Is.True,
                "an early crashed lease must redispatch before restart");
            Assert.That(
                edges.Any(item =>
                    item.Action == DesignAction.ExpireLease &&
                    item.Before.Attempts == DurableJobFixture.MaxAttempts &&
                    item.After.Status == JobStatus.Failed),
                Is.True,
                "a final crashed lease must exhaust the retry budget");
        });
    }

    private static void Holds(
        StateGraphNode root,
        StutterSafeFormula property)
    {
        var result = root.Check(property);
        Assert.That(
            result.Valid,
            Is.True,
            () => $"{property.Name}: {result.GetTraceString()}");
    }
}
