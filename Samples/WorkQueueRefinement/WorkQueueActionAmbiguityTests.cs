namespace WorkQueueRefinement;

using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Two ledgers that a state-valued refinement mapping cannot align. Both are
/// reasonable specifications; neither can be checked temporally today,
/// because the correspondence the modeler has in mind is between
/// <em>actions</em>, not between states.
/// </summary>
[TestFixture]
public class WorkQueueActionAmbiguityTests
{
    private static readonly LedgerOptions CloseCancelled =
        new LedgerOptions { IncludeCloseCancelled = true };

    private static readonly LedgerOptions RecordAttempt =
        new LedgerOptions { IncludeRecordAttempt = true };

    [Test]
    public void TwoLedgerActionsForOneStateChangeAreAmbiguous()
    {
        // The intended correspondence is
        //     observe-cancel-w{w}-t{t} -> ledger-settle-t{t}
        //     cancel-ready-t{t}        -> ledger-close-cancelled-t{t}
        // but both ledger actions perform the same state change, so the
        // mapping cannot say which one a concrete transition took.
        Assert.That(
            () => WorkQueueRefinementCheck
                .Build(ledgerOptions: CloseCancelled)
                .CheckTemporal(),
            Throws.TypeOf<AmbiguousTemporalRefinementException>()
                .With.Property(nameof(
                    AmbiguousTemporalRefinementException.MatchCount)).EqualTo(2)
                .And.Property(nameof(
                    AmbiguousTemporalRefinementException.ConcreteStep))
                .Property(nameof(IStepFunction.StepFunctionId))
                .StartsWith("observe-cancel-"));
    }

    [Test]
    public void AStateNeutralLedgerActionIsIndistinguishableFromStutter()
    {
        // Recording an attempt is a real ledger action with no state
        // footprint. Every concrete transition that leaves the ledger state
        // alone now has two abstract responses: stutter, or that action.
        Assert.That(
            () => WorkQueueRefinementCheck
                .Build(ledgerOptions: RecordAttempt)
                .CheckTemporal(),
            Throws.TypeOf<AmbiguousTemporalRefinementException>()
                .With.Property(nameof(
                    AmbiguousTemporalRefinementException.MatchCount)).EqualTo(2)
                .And.Property(nameof(
                    AmbiguousTemporalRefinementException.ConcreteStep))
                .Property(nameof(IStepFunction.StepFunctionId))
                .StartsWith("expire-"));
    }

    [Test]
    public void BothAmbiguousLedgersStillRefineForSafety()
    {
        // Safety refinement carries a set of coherent abstract
        // configurations, so it never has to choose between the responses.
        // The ambiguity is specific to deterministic temporal alignment.
        Assert.That(
            WorkQueueRefinementCheck.Build(ledgerOptions: CloseCancelled).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            WorkQueueRefinementCheck.Build(ledgerOptions: RecordAttempt).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void FairnessCannotResolveAStateNeutralActionAmbiguity()
    {
        // Accordant fairness deliberately ignores state-neutral edges, and
        // alignment fails before fairness analysis anyway. Supplying this
        // constraint therefore cannot distinguish the action from stutter.
        Assert.That(
            () => WorkQueueRefinementCheck
                .Build(ledgerOptions: RecordAttempt)
                .CheckTemporal(
                    concreteFairness: WorkQueueFairness.Implementation,
                    abstractFairness: WorkQueueFairness.LedgerLiveness +
                        Fairness.Weak(
                            step => step is LedgerRecordAttemptStep)),
            Throws.TypeOf<AmbiguousTemporalRefinementException>()
                .With.Property(nameof(
                    AmbiguousTemporalRefinementException.ConcreteStep))
                .Property(nameof(IStepFunction.StepFunctionId))
                .StartsWith("expire-"),
            "state-neutral actions are outside changing-edge fairness, and " +
            "explicit action mapping is required to distinguish one from stutter");
    }
}
