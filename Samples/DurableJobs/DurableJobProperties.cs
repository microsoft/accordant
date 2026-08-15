namespace DurableJobs;

using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// Safety and progress claims for the process design, plus the scheduler and
/// worker assumptions under which the progress claims are made.
/// </summary>
public static class DurableJobProperties
{
    public static FormulaBuilder<DurableJobDesignState> Formula { get; } =
        Microsoft.Accordant.ModelChecking.Formula
            .For<DurableJobDesignState>();

    public static StutterSafeFormula OutcomeShapeIsConsistent()
        => Formula.Always(Formula.Observe(
            state =>
            {
                if (state.Row is null)
                {
                    return state.Status == JobStatus.Missing &&
                        state.Result is null &&
                        state.Error is null;
                }

                return state.Row.Status switch
                {
                    JobStatus.Pending or JobStatus.Cancelled =>
                        state.Row.Result is null && state.Row.Error is null,
                    JobStatus.Succeeded =>
                        state.Row.Result == DurableJobFixture.Result &&
                        state.Row.Error is null,
                    JobStatus.Failed =>
                        state.Row.Result is null &&
                        state.Row.Error == DurableJobFixture.Error,
                    _ => false
                };
            },
            "OutcomeShapeIsConsistent"));

    public static StutterSafeFormula QueueIsBoundedAndEligible()
        => Formula.Always(Formula.Observe(
            state =>
                state.QueueDepth is >= 0 and <= 2 &&
                (state.QueueDepth == 0 ||
                    state.Row?.Status == JobStatus.Pending) &&
                (!IsTerminal(state.Status) || state.QueueDepth == 0) &&
                (state.Row is not null || state.QueueDepth == 0),
            "QueueIsBoundedAndEligible"));

    public static StutterSafeFormula LeaseWorkerAndAttemptsAreConsistent()
        => Formula.Always(Formula.Observe(
            state =>
            {
                if (state.Attempts is < 0 or > DurableJobFixture.MaxAttempts ||
                    state.Crashes is < 0 or > DurableJobFixture.MaxCrashes ||
                    state.AttemptOutcome == AttemptOutcome.Abandoned)
                {
                    return false;
                }

                if (state.Row is null)
                {
                    return state.ActiveLease is null &&
                        state.Attempts == 0 &&
                        state.Worker == WorkerPhase.Idle &&
                        state.AttemptOutcome == AttemptOutcome.None;
                }

                if (state.Row.JobId != DurableJobFixture.JobId ||
                    state.Row.Payload != DurableJobFixture.Payload)
                {
                    return false;
                }

                var ownsLease =
                    state.Worker is WorkerPhase.Running or WorkerPhase.Crashed;
                if (ownsLease != (state.ActiveLease is not null))
                {
                    return false;
                }

                if (state.ActiveLease is not null &&
                    (state.ActiveLease.JobId != state.Row.JobId ||
                        state.ActiveLease.Payload != state.Row.Payload ||
                        state.ActiveLease.Attempt != state.Row.Attempts ||
                        state.ActiveLease.Token != state.Row.Attempts ||
                        state.ActiveLease.Token <= 0))
                {
                    return false;
                }

                if (state.Worker is WorkerPhase.Running or WorkerPhase.Crashed &&
                    state.Row.Status != JobStatus.Pending)
                {
                    return false;
                }

                if (state.Worker == WorkerPhase.Idle &&
                    state.Row.Status == JobStatus.Pending &&
                    state.Row.Attempts >= DurableJobFixture.MaxAttempts)
                {
                    return false;
                }

                if (state.Worker == WorkerPhase.Stopped &&
                    !((state.Row.Status == JobStatus.Pending &&
                            state.Row.Attempts < DurableJobFixture.MaxAttempts) ||
                        (state.Row.Status == JobStatus.Failed &&
                            state.Row.Attempts == DurableJobFixture.MaxAttempts)))
                {
                    return false;
                }

                if (state.AttemptOutcome != AttemptOutcome.None &&
                    state.Worker != WorkerPhase.Running)
                {
                    return false;
                }

                if (IsTerminal(state.Row.Status) &&
                    (state.ActiveLease is not null ||
                        state.AttemptOutcome != AttemptOutcome.None))
                {
                    return false;
                }

                return true;
            },
            "LeaseWorkerAndAttemptsAreConsistent"));

    public static StutterSafeFormula AcceptedRowNeverDisappears()
        => Formula.Always(Formula.ObserveTransition(
            (from, to) => from.Row is null || to.Row is not null,
            "AcceptedRowNeverDisappears"));

    public static StutterSafeFormula AttemptsNeverDecrease()
        => Formula.Always(Formula.ObserveTransition(
            (from, to) =>
                from.Row is null ||
                to.Row is null ||
                to.Attempts >= from.Attempts,
            "AttemptsNeverDecrease"));

    public static StutterSafeFormula TerminalOutcomeIsStable()
        => Formula.Always(Formula.ObserveTransition(
            (from, to) =>
            {
                if (!IsTerminal(from.Status))
                {
                    return true;
                }

                return to.Row is not null &&
                    to.JobId == from.JobId &&
                    to.Payload == from.Payload &&
                    to.Status == from.Status &&
                    to.Result == from.Result &&
                    to.Error == from.Error &&
                    to.Attempts == from.Attempts;
            },
            "TerminalOutcomeIsStable"));

    public static StutterSafeFormula TerminalDecisionIsSingleWinner()
        => Formula.Always(Formula.ObserveTransition(
            (from, to) =>
            {
                if (IsTerminal(from.Status) || !IsTerminal(to.Status))
                {
                    return true;
                }

                return from.Status == JobStatus.Pending &&
                    to.QueueDepth == 0 &&
                    to.ActiveLease is null &&
                    to.AttemptOutcome == AttemptOutcome.None;
            },
            "TerminalDecisionIsSingleWinner"));

    public static StutterSafeFormula PendingEventuallyBecomesTerminal()
        => Formula.LeadsTo(
            Formula.Observe(
                state => state.Status == JobStatus.Pending,
                "Pending"),
            Formula.Observe(
                state => IsTerminal(state.Status),
                "Terminal"));

    public static StutterSafeFormula MissingDispatchIsEventuallyRecovered()
        => Formula.LeadsTo(
            Formula.Observe(IsMissingDispatch, "MissingDispatch"),
            Formula.Observe(
                state => state.QueueDepth > 0 || IsTerminal(state.Status),
                "DispatchedOrTerminal"));

    public static StutterSafeFormula ClaimableWorkIsEventuallyClaimed()
        => Formula.LeadsTo(
            Formula.Observe(state => state.CanClaim, "Claimable"),
            Formula.Observe(
                state => state.ActiveLease is not null || IsTerminal(state.Status),
                "ClaimedOrTerminal"));

    public static StutterSafeFormula CrashedLeaseIsEventuallyReleased()
        => Formula.LeadsTo(
            Formula.Observe(
                state => state.Worker == WorkerPhase.Crashed,
                "Crashed"),
            Formula.Observe(
                state =>
                    state.Worker != WorkerPhase.Crashed &&
                    state.ActiveLease is null,
                "LeaseReleased"));

    public static StutterSafeFormula StoppedWorkerIsEventuallyRestarted()
        => Formula.LeadsTo(
            Formula.Observe(
                state =>
                    state.Status == JobStatus.Pending &&
                    state.Worker == WorkerPhase.Stopped,
                "StoppedPending"),
            Formula.Observe(
                state =>
                    state.Worker == WorkerPhase.Idle ||
                    IsTerminal(state.Status),
                "RestartedOrTerminal"));

    public static StutterSafeFormula CrashedJobEventuallyBecomesTerminal()
        => Formula.LeadsTo(
            Formula.Observe(
                state =>
                    state.Status == JobStatus.Pending &&
                    state.Crashes > 0,
                "PendingAfterCrash"),
            Formula.Observe(
                state => IsTerminal(state.Status),
                "Terminal"));

    public static Fairness RebuildsDispatch { get; } =
        Fairness.WeakAction<ProcessTransition>(
            transition => IsAction(
                transition,
                DesignAction.RebuildDispatch));

    public static Fairness ClaimsWork { get; } =
        Fairness.StrongAction<ProcessTransition>(
            transition => IsAction(
                transition,
                DesignAction.ClaimNext));

    public static Fairness WeakClaimsWork { get; } =
        Fairness.WeakAction<ProcessTransition>(
            transition => IsAction(
                transition,
                DesignAction.ClaimNext));

    /// <summary>
    /// Collective weak fairness for the finite success/failure response family:
    /// when an attempt continuously awaits an outcome, either response may
    /// discharge the single obligation.
    /// </summary>
    public static Fairness RecordsAttemptOutcome { get; } =
        Fairness.WeakAction<ProcessTransition>(
            transition => IsAction(
                transition,
                DesignAction.RecordSuccess,
                DesignAction.RecordFailure));

    public static Fairness CommitsAttempt { get; } =
        Fairness.WeakAction<ProcessTransition>(
            transition => IsAction(
                transition,
                DesignAction.CompleteSuccess,
                DesignAction.FailAttempt));

    public static Fairness ExpiresCrashedLeases { get; } =
        Fairness.WeakAction<ProcessTransition>(
            transition => IsAction(
                transition,
                DesignAction.ExpireLease));

    public static Fairness RestartsWorker { get; } =
        Fairness.WeakAction<ProcessTransition>(
            transition =>
                transition.Control == ProcessControlKind.Restart &&
                transition.Domain == DurableJobRoles.WorkerHost);

    public static Fairness InfrastructureWithoutOutcome { get; } =
        RebuildsDispatch +
        ClaimsWork +
        CommitsAttempt +
        ExpiresCrashedLeases +
        RestartsWorker;

    public static Fairness ProgressWithWeakClaim { get; } =
        RebuildsDispatch +
        WeakClaimsWork +
        RecordsAttemptOutcome +
        CommitsAttempt +
        ExpiresCrashedLeases +
        RestartsWorker;

    public static Fairness Progress { get; } =
        RebuildsDispatch +
        ClaimsWork +
        RecordsAttemptOutcome +
        CommitsAttempt +
        ExpiresCrashedLeases +
        RestartsWorker;

    public static bool IsTerminal(JobStatus status)
        => status is
            JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled;

    private static bool IsMissingDispatch(DurableJobDesignState state)
        => state.Row is
        {
            Status: JobStatus.Pending,
            Attempts: < DurableJobFixture.MaxAttempts
        } &&
        state.Worker == WorkerPhase.Idle &&
        state.ActiveLease is null &&
        state.QueueDepth == 0;

    private static bool IsAction(
        ProcessTransition transition,
        params DesignAction[] actions)
        => transition.Control == ProcessControlKind.None &&
            transition.SemanticAction is DesignAction action &&
            actions.Contains(action);
}
