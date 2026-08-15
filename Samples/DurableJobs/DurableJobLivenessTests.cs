namespace DurableJobs.Tests;

using DurableJobs;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Progress checks and negative controls that document every required fairness
/// assumption rather than treating scheduler activity as implicit.
/// </summary>
[TestFixture]
public class DurableJobLivenessTests
{
    [Test]
    public void PendingCanStutterForeverWithoutFairness()
    {
        Refuted(
            DurableJobProperties.PendingEventuallyBecomesTerminal(),
            Fairness.None);
    }

    [Test]
    public void InfrastructureFairnessAloneCannotInventAWorkerOutcome()
    {
        Refuted(
            DurableJobProperties.PendingEventuallyBecomesTerminal(),
            DurableJobProperties.InfrastructureWithoutOutcome);
    }

    [Test]
    public void WeakClaimFairnessCannotBeatRepeatedDispatchLoss()
    {
        Refuted(
            DurableJobProperties.PendingEventuallyBecomesTerminal(),
            DurableJobProperties.ProgressWithWeakClaim);
    }

    [Test]
    public void BoundedRetriesEventuallyReachOneTerminalOutcomeUnderProgressFairness()
    {
        Holds(
            DurableJobProperties.PendingEventuallyBecomesTerminal(),
            DurableJobProperties.Progress);
    }

    [Test]
    public void ADeletedDispatchIsRebuiltUnderItsExplicitFairness()
    {
        var property = DurableJobProperties.MissingDispatchIsEventuallyRecovered();

        Refuted(property, Fairness.None);
        Holds(property, DurableJobProperties.RebuildsDispatch);
    }

    [Test]
    public void ACrashNeedsLeaseExpiryAndAStoppedWorkerNeedsRestart()
    {
        var expires = DurableJobProperties.CrashedLeaseIsEventuallyReleased();
        var restarts = DurableJobProperties.StoppedWorkerIsEventuallyRestarted();

        Refuted(expires, Fairness.None);
        Holds(expires, DurableJobProperties.ExpiresCrashedLeases);
        Refuted(restarts, Fairness.None);
        Holds(restarts, DurableJobProperties.RestartsWorker);
    }

    [Test]
    public void AJobThatHasCrashedStillEventuallyTerminatesUnderFullFairness()
    {
        Holds(
            DurableJobProperties.CrashedJobEventuallyBecomesTerminal(),
            DurableJobProperties.Progress);
    }

    [Test]
    public void IntermittentlyClaimableWorkNeedsStrongClaimFairness()
    {
        var property = DurableJobProperties.ClaimableWorkIsEventuallyClaimed();

        Refuted(property, DurableJobProperties.ProgressWithWeakClaim);
        Holds(property, DurableJobProperties.Progress);
    }

    private static StateGraphNode Graph() => DurableJobDesign.Explore();

    private static void Holds(
        StutterSafeFormula property,
        Fairness fairness)
    {
        var result = Graph().Check(property, fairness: fairness);
        Assert.That(
            result.Valid,
            Is.True,
            () => $"{property.Name}: {result.GetTraceString()}");
    }

    private static void Refuted(
        StutterSafeFormula property,
        Fairness fairness)
    {
        var result = Graph().Check(property, fairness: fairness);
        Assert.That(
            result.Status,
            Is.EqualTo(PropertyCheckingStatus.Violated),
            $"{property.Name} should have a fair counterexample");
    }
}
