namespace WorkQueueRefinement;

using System.Linq;
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

    // ---------------------------------------------------------------
    // The same two ledgers, aligned explicitly with .MapTransition(...).
    // ---------------------------------------------------------------

    [Test]
    public void DeclaringTheLedgerActionResolvesTwoActionsForOneStateChange()
    {
        // observe-cancel is the worker honoring cancellation, which settles
        // the entry; cancel-ready on an entry that was already assigned is
        // the queue closing it. The two ledger actions perform the same state
        // change, so only an explicit declaration can separate them.
        var result = WorkQueueRefinementCheck
            .Build(
                ledgerOptions: CloseCancelled,
                transitionMapping: WorkQueueRefinementCheck
                    .LedgerActions(CloseCancelled))
            .CheckTemporal(
                concreteFairness: WorkQueueFairness.Implementation,
                abstractFairness: WorkQueueFairness.LedgerLiveness);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DeclaringTheLedgerActionResolvesAStateNeutralActionAgainstStutter()
    {
        // A failure inside the retry budget is the recorded attempt; every
        // other ledger-invisible queue transition is stutter.
        var result = WorkQueueRefinementCheck
            .Build(
                ledgerOptions: RecordAttempt,
                transitionMapping: WorkQueueRefinementCheck
                    .LedgerActions(RecordAttempt))
            .CheckTemporal(
                concreteFairness: WorkQueueFairness.Implementation,
                abstractFairness: WorkQueueFairness.LedgerLiveness);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void AStateNeutralActionStaysOutsideChangingEdgeFairness()
    {
        // The recorded attempt is now aligned explicitly, and it is still a
        // state-neutral edge. Accordant fairness is defined over changing
        // edges, so asking for weak fairness on that action adds no
        // obligation: it can neither be starved nor discharge anything.
        var withoutConstraint = WorkQueueRefinementCheck
            .Build(
                ledgerOptions: RecordAttempt,
                transitionMapping: WorkQueueRefinementCheck
                    .LedgerActions(RecordAttempt))
            .CheckTemporal(
                concreteFairness: WorkQueueFairness.Implementation,
                abstractFairness: WorkQueueFairness.LedgerLiveness);

        var withConstraint = WorkQueueRefinementCheck
            .Build(
                ledgerOptions: RecordAttempt,
                transitionMapping: WorkQueueRefinementCheck
                    .LedgerActions(RecordAttempt))
            .CheckTemporal(
                concreteFairness: WorkQueueFairness.Implementation,
                abstractFairness: WorkQueueFairness.LedgerLiveness +
                    Fairness.Weak(step => step is LedgerRecordAttemptStep) +
                    Fairness.Strong(step => step is LedgerRecordAttemptStep));

        Assert.That(
            withoutConstraint.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            withConstraint.Status,
            Is.EqualTo(withoutConstraint.Status),
            "a state-neutral abstract edge may be aligned explicitly but " +
            "never raises or discharges a fairness obligation");
    }

    [Test]
    public void TheSameDeclarationsAlsoAlignTheUnambiguousLedger()
    {
        // The default ledger needs no declarations. Adding them changes
        // nothing, because a declaration only narrows responses the state
        // mapping already made state-consistent.
        var declared = WorkQueueRefinementCheck
            .Build(transitionMapping: WorkQueueRefinementCheck.LedgerActions())
            .CheckTemporal(
                concreteFairness: WorkQueueFairness.Implementation,
                abstractFairness: WorkQueueFairness.LedgerLiveness);

        Assert.That(
            WorkQueueRefinementCheck
                .Build(transitionMapping: WorkQueueRefinementCheck.LedgerActions())
                .Check()
                .Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(declared.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DeclarationsAlsoConstrainSafetyRefinement()
    {
        // Safety refinement never had to choose a response, so it accepted
        // the close-cancelled ledger with no complaint. A declaration makes
        // the action-level obligation checkable there too: settling an entry
        // a worker cancelled is not the action reserved for entries nobody
        // ever accepted.
        var result = WorkQueueRefinementCheck
            .Build(
                transitionMapping: WorkQueueRefinementCheck
                    .SettleCancelledAsUnassigned)
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            result.Trace[^1].ConcreteStepFunction,
            Is.TypeOf<ObserveCancelStep>());
        Assert.That(
            result.Trace[^1].DeclaredAbstractResponse.Description,
            Does.Contain(nameof(LedgerCancelUnassignedStep)));
        Assert.That(
            result.Trace[^1].StateConsistentAbstractTransitions
                .Select(response => response.StepFunction?.StepFunctionId),
            Has.Some.StartsWith("ledger-settle-"),
            "the diagnostic names the responses the declaration rejected");
    }

    [Test]
    public void AnOverConstrainedDeclarationIsATransitionMismatch()
    {
        // Declaring stutter everywhere is the common first mistake. Stutter
        // is not state-consistent for a transition that moves the ledger, so
        // the check fails rather than silently passing.
        var result = WorkQueueRefinementCheck
            .Build(transitionMapping: WorkQueueRefinementCheck.AlwaysStutter)
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            result.Trace[^1].DeclaredAbstractResponse,
            Is.EqualTo(AbstractResponse.Stutter));
        Assert.That(result.Trace[^1].AbstractCandidates, Is.Empty);
    }

    [Test]
    public void ADeclarationCannotAdmitAnInconsistentAbstractState()
    {
        // AlwaysStutter asks for stutter at a transition whose mapped ledger
        // state differs. Narrowing cannot create a match, so the answer is a
        // mismatch, never an unsound pass.
        var stateOnly = WorkQueueRefinementCheck.Build().Check();
        var declared = WorkQueueRefinementCheck
            .Build(transitionMapping: WorkQueueRefinementCheck.AlwaysStutter)
            .Check();

        Assert.That(stateOnly.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            declared.Status,
            Is.EqualTo(RefinementCheckingStatus.DoesNotRefine),
            "an explicit declaration adds obligations; it never removes any");
    }
}
