namespace WorkQueueRefinement;

using System.Linq;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Temporal refinement under fairness. Two obligations of the ledger need
/// different strengths of concrete fairness, and the model shows why: an
/// action a competing worker only sometimes has enabled cannot be forced by
/// weak fairness, while an action that stays enabled can.
/// </summary>
[TestFixture]
public class WorkQueueTemporalFairnessTests
{
    [Test]
    public void RecyclingLeasesForeverBreaksLedgerLiveness()
    {
        // Lease, expire, lease, expire ... forever. No attempt is ever spent,
        // so the ledger entry stays assigned and is never closed.
        var result = Check(Fairness.None, WorkQueueFairness.LedgerSettles);

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(CycleSteps(result), Has.Some.StartsWith("expire-"));
    }

    [Test]
    public void WeakSettlingFairnessIsNotEnough()
    {
        // Completion is enabled only while a worker holds the lease, and the
        // lasso passes through states where nobody does. The action is not
        // continuously enabled, so weak fairness never forces it.
        var result = Check(
            WorkQueueFairness.WeakSettling,
            WorkQueueFairness.LedgerSettles);

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(CycleSteps(result), Has.Some.StartsWith("expire-"));
        Assert.That(CycleSteps(result), Has.Some.StartsWith("lease-"));
    }

    [Test]
    public void StrongSettlingFairnessClosesEveryAssignedEntry()
    {
        // Strong fairness only needs the action to be enabled infinitely
        // often, which lease recycling guarantees.
        var result = Check(
            WorkQueueFairness.Settling,
            WorkQueueFairness.LedgerSettles);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void WeakLeasingFairnessDoesNotPreventStarvation()
    {
        // Both workers ping-pong on task 0. Each of them is idle only some of
        // the time, so leasing task 1 is not continuously enabled and the
        // ledger entry for task 1 is never assigned.
        var result = Check(
            WorkQueueFairness.WeakLeasing(1) +
                WorkQueueFairness.CancelSweep,
            WorkQueueFairness.LedgerAssigns(1));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(CycleSteps(result), Has.Some.StartsWith("lease-w0-t0"));
        Assert.That(
            CycleSteps(result),
            Has.None.StartsWith("request-cancel-"),
            "cancel sweep fairness isolates intermittent lease enabledness");
    }

    [Test]
    public void StrongLeasingStillMissesTheCancelledEntry()
    {
        // A cancellation request disables leasing altogether, so the strong
        // obligation is vacuous. The ledger still expects the entry to be
        // assigned, and the concrete alternative — sweeping the cancelled
        // entry — has no fairness constraint yet.
        var result = Check(
            WorkQueueFairness.StrongLeasing(1),
            WorkQueueFairness.LedgerAssigns(1));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(CycleSteps(result), Has.Some.StartsWith("request-cancel-t1"));
    }

    [Test]
    public void WeakCancelSweepFairnessClosesTheRemainingGap()
    {
        // Sweeping a cancelled unleased entry stays enabled once enabled, so
        // plain weak fairness closes it. Assignment then becomes disabled
        // rather than remaining continuously enabled and untaken.
        var result = Check(
            WorkQueueFairness.StrongLeasing(1) + WorkQueueFairness.CancelSweep,
            WorkQueueFairness.LedgerAssigns(1));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void ImplementationRefinesTheLedgerTemporally()
    {
        var result = Check(
            WorkQueueFairness.Implementation,
            WorkQueueFairness.LedgerLiveness);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    private static RefinementCheckingResult Check(
        Fairness concreteFairness,
        Fairness abstractFairness)
        => WorkQueueRefinementCheck
            .Build()
            .CheckTemporal(concreteFairness, abstractFairness);

    private static string[] CycleSteps(RefinementCheckingResult result)
        => result.Trace
            .Where(item => item.IsInCycle)
            .Select(item => item.ConcreteStepFunction?.StepFunctionId ?? string.Empty)
            .ToArray();
}
