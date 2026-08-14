// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines;

using System;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// The refinement of the process-oriented write-ahead log by the guarded-action
/// atomic store. The state mapping and the transition declarations are written
/// entirely outside the process code: no coroutine carries a
/// <c>.Linearizes(...)</c> annotation.
/// </summary>
public static class StoreRefinement
{
    // ---------------------------------------------------------------
    // The checks.
    // ---------------------------------------------------------------

    /// <summary>The standard check: the mapping plus the action declarations.</summary>
    public static FunctionalRefinementCheck<WalProcessState, StoreState> Build(
        WalConfig config = null,
        Func<WalProcessState, StoreState> map = null,
        int maxDepth = -1)
    {
        config ??= WalConfig.Default;
        return Refinement
            .Between<WalProcessState, StoreState>(
                WriteAheadLog.Explore(config, maxDepth),
                AtomicStore.Explore(config))
            .Map(map ?? ToStore)
            .MapTransition(Declarations);
    }

    // ---------------------------------------------------------------
    // The state mapping.
    // ---------------------------------------------------------------

    /// <summary>One WAL process state as one store state.</summary>
    public static StoreState ToStore(WalProcessState wal)
        => new StoreState
        {
            Values = WriteAheadLog.RecoveredSnapshot(wal),
            Phase = PhaseOf(wal),
            Request = wal.Request?.Copy(),
            LastOutcome = wal.Reported
        };

    /// <summary>The client-visible phase of the in-flight transaction.</summary>
    public static TxnPhase PhaseOf(WalProcessState wal)
        => wal.Client == ClientPhase.Idle
            ? TxnPhase.Idle
            : wal.LogCommit
                ? TxnPhase.Committed
                : wal.Server == ServerPhase.Active
                    ? TxnPhase.Pending
                    : TxnPhase.Aborted;

    // ---------------------------------------------------------------
    // The transition mapping.
    // ---------------------------------------------------------------

    /// <summary>Declares the store action behind every process transition.</summary>
    public static AbstractResponse Declarations(RefinementTransition<WalProcessState> transition)
    {
        if (!(transition.Metadata is ProcessTransition process))
        {
            throw new InvalidOperationException(
                "The compiled process graph must retain ProcessTransition metadata.");
        }

        return Declare(process, transition.Source);
    }

    /// <summary>
    /// The declaration itself. Control transitions (launch, restart, process
    /// completion) are hidden; a crash is hidden unless it dooms an in-flight
    /// transaction. Coroutine checkpoints are named by their typed action.
    /// </summary>
    public static AbstractResponse Declare(ProcessTransition process, WalProcessState source)
    {
        if (process.IsControl)
        {
            return process.Control switch
            {
                ProcessControlKind.Crash => DoomsInFlightTransaction(source)
                    ? StoreStep.Performs(StoreAction.Abort)
                    : AbstractResponse.Hidden,
                ProcessControlKind.Launch or
                ProcessControlKind.Restart or
                ProcessControlKind.Completion => AbstractResponse.Hidden,
                _ => AbstractResponse.Unconstrained
            };
        }

        // A visible Choose is a state-neutral control branch (which transaction
        // the client will submit); the store does not move when it is taken.
        if (process.CheckpointKind == ModelCheckpointKind.Choose)
        {
            return AbstractResponse.Hidden;
        }

        if (!(process.SemanticAction is WalAction action))
        {
            throw new InvalidOperationException(
                $"Step transition '{process}' has no typed WAL action.");
        }

        return action switch
        {
            WalAction.Submit => StoreStep.Performs(StoreAction.Submit, (string)process.Subject),
            WalAction.FlushCommit => StoreStep.Performs(StoreAction.Commit),
            WalAction.AckCommit => StoreStep.Performs(StoreAction.ReportCommit),
            WalAction.AckAbort => StoreStep.Performs(StoreAction.ReportAbort),

            WalAction.AppendRedo or
            WalAction.InstallData or
            WalAction.TruncateLog or
            WalAction.Recover => AbstractResponse.Hidden,

            _ => AbstractResponse.Unconstrained
        };
    }

    /// <summary>
    /// Whether a transition leaving <paramref name="source"/> ends the in-doubt
    /// window of a transaction the client is still waiting for.
    /// </summary>
    public static bool DoomsInFlightTransaction(WalProcessState source)
        => source.Client == ClientPhase.Waiting &&
            source.Server == ServerPhase.Active &&
            !source.LogCommit;
}

/// <summary>The fairness constraints the two models are checked under.</summary>
public static class WalFairness
{
    /// <summary>
    /// A restarted server eventually finishes its recovery analysis and reaches a
    /// decided phase. Strong fairness is required: a crash during recovery resets
    /// the server to <see cref="ServerPhase.Down"/> and disables the analysis, so
    /// the <c>Recover</c> step is only enabled intermittently.
    /// </summary>
    public static Fairness Recovers { get; } =
        Fairness.Strong<WalProcessState>((source, target) =>
            source.Server == ServerPhase.Recovering &&
            target.Server != ServerPhase.Recovering &&
            target.Server != ServerPhase.Down);

    /// <summary>
    /// A waiting client eventually receives its decision and goes idle — reported
    /// either by the original handler or, if a crash killed it, by the recovery
    /// worker that replaces it. Strong fairness is required because a crash can
    /// disable the acknowledgement (it discards the handler or the recovery
    /// worker before it reports), so the report edge is only enabled
    /// intermittently.
    /// </summary>
    public static Fairness Reports { get; } =
        Fairness.Strong<WalProcessState>((source, target) =>
            source.Client == ClientPhase.Waiting && target.Client == ClientPhase.Idle);

    /// <summary>Everything the implementation is assumed to guarantee.</summary>
    public static Fairness Implementation { get; } = Recovers + Reports;

    /// <summary>The too-weak version of <see cref="Recovers"/>.</summary>
    public static Fairness WeakRecovers { get; } =
        Fairness.Weak<WalProcessState>((source, target) =>
            source.Server == ServerPhase.Recovering &&
            target.Server != ServerPhase.Recovering &&
            target.Server != ServerPhase.Down);

    /// <summary>The too-weak version of <see cref="Reports"/>.</summary>
    public static Fairness WeakReports { get; } =
        Fairness.Weak<WalProcessState>((source, target) =>
            source.Client == ClientPhase.Waiting && target.Client == ClientPhase.Idle);

    /// <summary>The bundle with weak recovery fairness.</summary>
    public static Fairness WithWeakRecovery { get; } = WeakRecovers + Reports;

    /// <summary>The bundle with weak acknowledgement fairness.</summary>
    public static Fairness WithWeakReports { get; } = Recovers + WeakReports;

    /// <summary>
    /// Every submitted transaction is eventually decided and reported. Weak
    /// fairness on each decision; there is deliberately no obligation to submit.
    /// </summary>
    public static Fairness StoreLiveness { get; } =
        Fairness.Weak(StoreStep.Any(StoreAction.Commit, StoreAction.Abort)) +
        Fairness.Weak(StoreStep.Any(StoreAction.ReportCommit, StoreAction.ReportAbort));
}
