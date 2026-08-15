namespace WorkQueueRefinement;

using System.Linq;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Hiding and projection in the case study. Most queue actions are internal
/// to the implementation: the ledger never sees an expiring lease, an arriving
/// cancellation request or a retry. Declaring them hidden is the checked claim
/// that the ledger stands still, and it changes nothing about what the queue
/// can do.
/// </summary>
[TestFixture]
public class WorkQueueHidingTests
{
    [Test]
    public void InternalQueueActionsAreDeclaredHidden()
    {
        var result = Check(Fairness.None);

        Assert.That(
            result.Trace.Where(item =>
                item.ConcreteStepFunction is ExpireLeaseStep),
            Is.Not.Empty.And.All.Property(
                nameof(RefinementTraceItem.DeclaredAbstractResponse))
                .EqualTo(AbstractResponse.Hidden));
        Assert.That(
            result.Trace
                .Where(item => item.ConcreteStepFunction is ExpireLeaseStep)
                .Select(item => item.ProjectionKind)
                .Distinct(),
            Is.EquivalentTo(new[] { AbstractProjectionKind.HiddenAction }));
        Assert.That(
            AbstractResponse.Hidden.HidesConcreteAction,
            Is.True,
            "hiding admits abstract stutter only, and that is checked");
    }

    [Test]
    public void HidingIsNotSuppression()
    {
        // Expiry and retry leasing are hidden, and the queue can still do
        // them forever. Hiding declares that the ledger does not move; it
        // never removes a concrete behavior, so ledger liveness still needs
        // concrete fairness to exclude the loop.
        var diverges = Check(Fairness.None);

        Assert.That(
            diverges.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(CycleSteps(diverges), Has.Some.StartsWith("expire-"));
        Assert.That(
            diverges.Trace
                .Where(item => item.IsInCycle)
                .Skip(1)
                .Select(item => item.ProjectionKind)
                .Distinct(),
            Is.EquivalentTo(new[] { AbstractProjectionKind.HiddenAction }),
            "the queue recycles leases forever while the ledger stutters");
        Assert.That(
            diverges.GetTraceString(),
            Does.Contain("Every concrete transition in the repeating part is hidden"));
        Assert.That(
            diverges.GetTraceString(),
            Does.Contain("never discharge an abstract fairness obligation"));
        Assert.That(
            Check(WorkQueueFairness.Settling).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "concrete fairness, not the declaration, excludes the hidden loop");
    }

    [Test]
    public void TheProjectionViewNamesWhatEachQueueStepBecame()
    {
        var projection = Check(Fairness.None).GetProjectionString();

        Assert.That(
            projection,
            Does.Contain("Concrete behavior projected onto the abstract model"));
        Assert.That(projection, Does.Contain("hidden action (abstract stutter)"));
        Assert.That(projection, Does.Contain("abstract step ledger-accept-t"));
        Assert.That(projection, Does.Contain("[cycle]"));
    }

    [Test]
    public void HidingAnActionTheLedgerRecordsIsAMismatch()
    {
        // Hiding is checked against the state mapping, so claiming that every
        // queue transition leaves the ledger alone fails at the first one
        // that does not.
        var result = WorkQueueRefinementCheck
            .Build(transitionMapping: WorkQueueRefinementCheck.AlwaysStutter)
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            result.Trace[^1].ProjectionKind,
            Is.EqualTo(AbstractProjectionKind.Unaligned));
        Assert.That(
            result.GetTraceString(),
            Does.Contain("The declaration hides this concrete transition"));
    }

    private static RefinementCheckingResult Check(Fairness concreteFairness)
        => WorkQueueRefinementCheck
            .Build(transitionMapping: WorkQueueRefinementCheck.LedgerActions())
            .CheckTemporal(concreteFairness, WorkQueueFairness.LedgerSettles);

    private static string[] CycleSteps(RefinementCheckingResult result)
        => result.Trace
            .Where(item => item.IsInCycle)
            .Select(item => item.ConcreteStepFunction?.StepFunctionId ?? string.Empty)
            .ToArray();
}
