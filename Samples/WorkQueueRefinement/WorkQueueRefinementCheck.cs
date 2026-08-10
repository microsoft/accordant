namespace WorkQueueRefinement;

using System;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;

/// <summary>
/// Deterministic checker-local memory of a fact the concrete past decided and
/// the concrete state then forgot: which worker first accepted each task. A
/// retry can hand the task to the other worker, and an expired lease erases
/// the holder altogether, so this cannot be recovered from
/// <see cref="QueueState"/>.
/// </summary>
[State]
public partial class ClaimHistory
{
    /// <summary>The first claimant of each task, or <see cref="Ledger.NoOwner"/>.</summary>
    public int[] FirstOwner { get; set; }
}

/// <summary>The predicted final result of a pending queue entry.</summary>
[State]
public partial class OutcomeWitness
{
    public LedgerResult Result { get; set; }
}

/// <summary>
/// The refinement between the leased work queue and the client ledger:
/// <c>.Augment(...)</c> recovers the first claimant from the concrete past,
/// <c>.WithWitness(...)</c> predicts the result the ledger commits to at
/// assignment time, and <c>.Map(...)</c> combines both with the concrete
/// present.
/// </summary>
public static class WorkQueueRefinementCheck
{
    /// <summary>The witness operation identity of a task — a model identity, never invented.</summary>
    public static string OperationId(int task) => $"outcome-t{task}";

    /// <summary>Builds the standard check, optionally with deliberately altered parts.</summary>
    public static AugmentedWitnessFunctionalRefinementCheck<
        QueueState,
        LedgerState,
        ClaimHistory> Build(
            WorkQueueConfig config = null,
            LedgerOptions ledgerOptions = null,
            Func<ClaimHistory, RefinementTransition<QueueState>, ClaimHistory> augment = null,
            Func<PendingWitnesses, RefinementTransition<QueueState>, WitnessChanges> lifecycle = null,
            Func<QueueState, ClaimHistory, WitnessCollection, LedgerState> mapping = null)
    {
        config ??= WorkQueueConfig.Default;
        return Refinement
            .Between<QueueState, LedgerState>(
                WorkQueue.Explore(config),
                Ledger.Explore(config, ledgerOptions))
            .Augment(
                initial: _ => InitialHistory(config),
                next: augment ?? RememberFirstClaimant)
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: lifecycle ?? TrackOutcomes)
            .Map(mapping ?? MapToLedger);
    }

    /// <summary>
    /// Builds the check without witnesses. The mapping then has to guess the
    /// result the ledger already committed to.
    /// </summary>
    public static AugmentedFunctionalRefinementCheck<
        QueueState,
        LedgerState,
        ClaimHistory> BuildWithoutWitnesses(WorkQueueConfig config = null)
    {
        config ??= WorkQueueConfig.Default;
        return Refinement
            .Between<QueueState, LedgerState>(
                WorkQueue.Explore(config),
                Ledger.Explore(config))
            .Augment(
                initial: _ => InitialHistory(config),
                next: RememberFirstClaimant)
            .Map((queue, history) =>
                MapGuessingCompletion(queue, history, witnesses: null));
    }

    /// <summary>
    /// Builds the check without augmentation. The mapping then has to read
    /// the owner from the concrete lease, which a retry or an expiry erases.
    /// </summary>
    public static WitnessFunctionalRefinementCheck<QueueState, LedgerState>
        BuildWithoutAugmentation(WorkQueueConfig config = null)
    {
        config ??= WorkQueueConfig.Default;
        var noHistory = InitialHistory(config);
        return Refinement
            .Between<QueueState, LedgerState>(
                WorkQueue.Explore(config),
                Ledger.Explore(config))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: TrackOutcomes)
            .Map((queue, witnesses) =>
                MapOwnerFromCurrentHolder(queue, noHistory, witnesses));
    }

    // ---------------------------------------------------------------
    // Augmentation: determined by the concrete past.
    // ---------------------------------------------------------------

    /// <summary>Nobody has accepted anything yet.</summary>
    public static ClaimHistory InitialHistory(WorkQueueConfig config)
    {
        var firstOwner = new int[config.Tasks];
        for (var task = 0; task < config.Tasks; task++)
        {
            firstOwner[task] = Ledger.NoOwner;
        }

        return new ClaimHistory { FirstOwner = firstOwner };
    }

    /// <summary>Records the worker that took the very first lease on a task.</summary>
    public static ClaimHistory RememberFirstClaimant(
        ClaimHistory history,
        RefinementTransition<QueueState> transition)
    {
        if (!(transition.StepFunction is LeaseStep lease) ||
            history.FirstOwner[lease.Task] != Ledger.NoOwner)
        {
            return history;
        }

        var next = (ClaimHistory)history.Clone();
        next.FirstOwner[lease.Task] = lease.Worker;
        return next;
    }

    /// <summary>
    /// A deliberately wrong augmentation: it remembers the most recent
    /// claimant, so a retry taken by the other worker rewrites an audit field
    /// the ledger already committed.
    /// </summary>
    public static ClaimHistory RememberLatestClaimant(
        ClaimHistory history,
        RefinementTransition<QueueState> transition)
    {
        if (!(transition.StepFunction is LeaseStep lease))
        {
            return history;
        }

        var next = (ClaimHistory)history.Clone();
        next.FirstOwner[lease.Task] = lease.Worker;
        return next;
    }

    // ---------------------------------------------------------------
    // Witnesses: validated by the concrete future.
    //
    // Trust boundary: the checker validates that a resolving value belongs
    // to the introduced domain, that identities are not resolved twice, and
    // that nothing is mutated. It cannot validate that the value is the one
    // the transition actually reveals. Every Resolve below reads its value
    // from the transition's step function and target state; resolving with a
    // guess would silently prune the sibling copies and could prove a wrong
    // ledger, so that is a modeling obligation, not a checked property.
    // ---------------------------------------------------------------

    /// <summary>
    /// Introduces a prediction at the first lease of a task, keeps it across
    /// expiries and retries, resolves it from the transition that reveals the
    /// result, and cancels it when a purge erases the ledger commitment.
    /// </summary>
    public static WitnessChanges TrackOutcomes(
        PendingWitnesses pending,
        RefinementTransition<QueueState> transition)
    {
        switch (transition.StepFunction)
        {
            case LeaseStep lease:
                // A retry lease finds the prediction still pending.
                return pending.IsPending(OperationId(lease.Task))
                    ? WitnessChanges.None
                    : Predict(lease.Task);

            case CompleteStep complete:
                return Resolve(complete.Task, LedgerResult.Completed);

            case FailStep fail:
                // A failure inside the retry budget reveals nothing.
                return transition.Target.Phases[fail.Task] == TaskPhase.Dropped
                    ? Resolve(fail.Task, LedgerResult.Dropped)
                    : WitnessChanges.None;

            case ObserveCancelStep observe:
                return Resolve(observe.Task, LedgerResult.Cancelled);

            case CancelReadyStep cancelReady:
                // A task cancelled before it was ever leased has no prediction.
                return pending.IsPending(OperationId(cancelReady.Task))
                    ? Resolve(cancelReady.Task, LedgerResult.Cancelled)
                    : WitnessChanges.None;

            case PurgeStep purge:
                // The ledger entry disappears, so the commitment is never
                // validated: every copy survives and identical copies merge.
                return pending.IsPending(OperationId(purge.Task))
                    ? WitnessChanges.Cancel(OperationId(purge.Task))
                    : WitnessChanges.None;

            default:
                // expire and request-cancel reveal nothing.
                return WitnessChanges.None;
        }
    }

    /// <summary>
    /// The same lifecycle without the purge cancellation. It still refines,
    /// but three refuted copies are carried forever instead of merging.
    /// </summary>
    public static WitnessChanges TrackOutcomesKeepingPurgedPredictions(
        PendingWitnesses pending,
        RefinementTransition<QueueState> transition)
        => transition.StepFunction is PurgeStep
            ? WitnessChanges.None
            : TrackOutcomes(pending, transition);

    /// <summary>
    /// A deliberately wrong lifecycle that forgets a prediction survives a
    /// retry and introduces the same identity a second time.
    /// </summary>
    public static WitnessChanges TrackOutcomesReintroducingOnEveryLease(
        PendingWitnesses pending,
        RefinementTransition<QueueState> transition)
        => transition.StepFunction is LeaseStep lease
            ? Predict(lease.Task)
            : TrackOutcomes(pending, transition);

    /// <summary>
    /// A deliberately wrong lifecycle that resolves with a value the
    /// introduced domain does not contain.
    /// </summary>
    public static WitnessChanges TrackOutcomesResolvingOutsideTheDomain(
        PendingWitnesses pending,
        RefinementTransition<QueueState> transition)
        => transition.StepFunction is CompleteStep complete
            ? Resolve(complete.Task, LedgerResult.Unknown)
            : TrackOutcomes(pending, transition);

    private static WitnessChanges Predict(int task)
        => WitnessChanges.Introduce(
            OperationId(task),
            new OutcomeWitness { Result = LedgerResult.Completed },
            new OutcomeWitness { Result = LedgerResult.Dropped },
            new OutcomeWitness { Result = LedgerResult.Cancelled });

    private static WitnessChanges Resolve(int task, LedgerResult result)
        => WitnessChanges.Resolve(
            OperationId(task),
            new OutcomeWitness { Result = result });

    // ---------------------------------------------------------------
    // The mapping.
    // ---------------------------------------------------------------

    /// <summary>
    /// Maps one queue state to one ledger state. The mapping is total: it
    /// reads a prediction only through <c>TryGet</c>, so positions where the
    /// operation is not pending are handled by the concrete state alone.
    /// </summary>
    public static LedgerState MapToLedger(
        QueueState queue,
        ClaimHistory history,
        WitnessCollection witnesses)
    {
        var tasks = queue.Phases.Length;
        var ledger = new LedgerState
        {
            Phases = new LedgerPhase[tasks],
            Owner = new int[tasks],
            Results = new LedgerResult[tasks]
        };

        for (var task = 0; task < tasks; task++)
        {
            var owner = history.FirstOwner[task];
            ledger.Owner[task] = owner;
            ledger.Results[task] = LedgerResult.Unknown;

            switch (queue.Phases[task])
            {
                case TaskPhase.Ready when owner == Ledger.NoOwner:
                    ledger.Phases[task] = LedgerPhase.Enqueued;
                    break;

                case TaskPhase.Ready:
                case TaskPhase.Leased:
                    ledger.Phases[task] = LedgerPhase.Assigned;
                    ledger.Results[task] = Predicted(witnesses, task);
                    break;

                case TaskPhase.Completed:
                    ledger.Phases[task] = LedgerPhase.Settled;
                    ledger.Results[task] = LedgerResult.Completed;
                    break;

                case TaskPhase.Dropped:
                    ledger.Phases[task] = LedgerPhase.Settled;
                    ledger.Results[task] = LedgerResult.Dropped;
                    break;

                case TaskPhase.Cancelled:
                    ledger.Phases[task] = LedgerPhase.Settled;
                    ledger.Results[task] = LedgerResult.Cancelled;
                    break;

                default:
                    ledger.Phases[task] = LedgerPhase.Purged;
                    break;
            }
        }

        return ledger;
    }

    /// <summary>
    /// A deliberately optimistic mapping that guesses the committed result
    /// instead of predicting it. It is the mapping a modeler writes before
    /// reaching for <c>.WithWitness(...)</c>.
    /// </summary>
    public static LedgerState MapGuessingCompletion(
        QueueState queue,
        ClaimHistory history,
        WitnessCollection witnesses)
    {
        var ledger = MapToLedger(queue, history, witnesses);
        for (var task = 0; task < ledger.Phases.Length; task++)
        {
            if (ledger.Phases[task] == LedgerPhase.Assigned)
            {
                ledger.Results[task] = LedgerResult.Completed;
            }
        }

        return ledger;
    }

    /// <summary>
    /// A deliberately naive mapping that reads the owner from the concrete
    /// state instead of the augmentation, and therefore loses it as soon as
    /// the lease moves or expires.
    /// </summary>
    public static LedgerState MapOwnerFromCurrentHolder(
        QueueState queue,
        ClaimHistory history,
        WitnessCollection witnesses)
    {
        var ledger = MapToLedger(queue, history, witnesses);
        for (var task = 0; task < ledger.Phases.Length; task++)
        {
            ledger.Owner[task] = CurrentHolder(queue, task);
        }

        return ledger;
    }

    private static LedgerResult Predicted(WitnessCollection witnesses, int task)
        => witnesses != null &&
            witnesses.TryGet<OutcomeWitness>(OperationId(task), out var prediction)
            ? prediction.Result
            : LedgerResult.Unknown;

    private static int CurrentHolder(QueueState queue, int task)
    {
        for (var worker = 0; worker < queue.WorkerTask.Length; worker++)
        {
            if (queue.WorkerTask[worker] == task)
            {
                return worker;
            }
        }

        return Ledger.NoOwner;
    }
}

/// <summary>The fairness constraints the two models are checked under.</summary>
public static class WorkQueueFairness
{
    /// <summary>
    /// A worker that keeps reacquiring a task must eventually finish an
    /// attempt by succeeding, failing, or honoring cancellation. Weak fairness
    /// is not enough because each action is only intermittently enabled.
    /// </summary>
    public static Fairness Settling { get; } = Fairness.Strong(
        step => step is CompleteStep ||
            step is FailStep ||
            step is ObserveCancelStep);

    /// <summary>A worker that can repeatedly take a ready task eventually does.</summary>
    public static Fairness Leasing { get; } = Fairness.Strong(
        step => step is LeaseStep);

    /// <summary>
    /// A cancelled unleased task is eventually closed. Weak fairness is
    /// enough: the action stays enabled once it becomes enabled.
    /// </summary>
    public static Fairness CancelSweep { get; } = Fairness.Weak(
        step => step is CancelReadyStep);

    /// <summary>Everything the implementation must guarantee.</summary>
    public static Fairness Implementation { get; } =
        Settling + Leasing + CancelSweep;

    /// <summary>
    /// Assignment and settlement cannot remain continuously enabled without
    /// occurring. An entry may instead be cancelled or purged, disabling the
    /// corresponding action.
    /// </summary>
    public static Fairness LedgerLiveness { get; } = Fairness.Weak(
        step => step is LedgerAcceptStep || step is LedgerSettleStep);

    /// <summary>
    /// An assigned entry cannot remain continuously settleable without being
    /// settled.
    /// </summary>
    public static Fairness LedgerSettles { get; } = Fairness.Weak(
        step => step is LedgerSettleStep);

    /// <summary>
    /// Entry <paramref name="task"/> cannot remain continuously assignable
    /// without assignment. Cancellation may close it and disable assignment.
    /// </summary>
    public static Fairness LedgerAssigns(int task) => Fairness.Weak(
        step => step is LedgerAcceptStep accept && accept.Task == task);

    /// <summary>Weak fairness for the settling actions, which is too weak.</summary>
    public static Fairness WeakSettling { get; } = Fairness.Weak(
        step => step is CompleteStep ||
            step is FailStep ||
            step is ObserveCancelStep);

    /// <summary>Weak fairness for leasing task <paramref name="task"/>.</summary>
    public static Fairness WeakLeasing(int task) => Fairness.Weak(
        step => step is LeaseStep lease && lease.Task == task);

    /// <summary>Strong fairness for leasing task <paramref name="task"/>.</summary>
    public static Fairness StrongLeasing(int task) => Fairness.Strong(
        step => step is LeaseStep lease && lease.Task == task);
}
