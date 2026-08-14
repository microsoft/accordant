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
            Pending = wal.Exchange.Pending?.Copy(),
            Replies = (Outcome[])wal.Exchange.Replies.Clone()
        };

    /// <summary>
    /// The client-visible phase of the in-flight transaction, derived from
    /// durable, exchange, and mode state — never from a stored protocol phase.
    /// </summary>
    public static TxnPhase PhaseOf(WalProcessState wal)
        => wal.Exchange.Pending == null
            ? TxnPhase.Idle
            : wal.Wal.LogCommit
                ? TxnPhase.Committed
                : wal.Server.Mode == ServerMode.Running
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

        // A client's guarded submission claims the slot.
        if (process.SemanticAction is ClientAction clientAction)
        {
            return clientAction switch
            {
                ClientAction.Submit => StoreStep.Performs(
                    StoreAction.Submit, process.Subject.ToString()),
                _ => AbstractResponse.Unconstrained
            };
        }

        if (!(process.SemanticAction is WalAction action))
        {
            throw new InvalidOperationException(
                $"Step transition '{process}' has no typed WAL or client action.");
        }

        return action switch
        {
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
    /// window of a transaction the slot still holds: a crash from a running
    /// server that has admitted a request but not yet flushed its commit.
    /// </summary>
    public static bool DoomsInFlightTransaction(WalProcessState source)
        => source.Exchange.Pending != null &&
            source.Server.Mode == ServerMode.Running &&
            !source.Wal.LogCommit;
}

/// <summary>The fairness constraints the two models are checked under.</summary>
public static class WalFairness
{
    /// <summary>
    /// A restarted server eventually finishes its recovery and comes back up.
    /// Strong fairness is required: a crash during recovery resets the server to
    /// <see cref="ServerMode.Down"/> and disables the analysis, so returning to
    /// <see cref="ServerMode.Running"/> is only enabled intermittently.
    /// </summary>
    public static Fairness Recovers { get; } =
        Fairness.Strong<WalProcessState>((source, target) =>
            source.Server.Mode == ServerMode.Recovering &&
            target.Server.Mode == ServerMode.Running);

    /// <summary>
    /// A still-owed client eventually receives its decision — reported either by
    /// the original handler or, if a crash killed it, by the recovery worker that
    /// replaces it. Strong fairness is required because a crash can disable the
    /// acknowledgement (it discards the handler or the recovery worker before it
    /// publishes), so the report edge is only enabled intermittently.
    /// </summary>
    public static Fairness Reports { get; } =
        Fairness.Strong<WalProcessState>((source, target) =>
            source.Exchange.Pending != null && target.Exchange.Pending == null);

    /// <summary>Everything the implementation is assumed to guarantee.</summary>
    ///
    /// <remarks>
    /// Because recovery completes and publishes the owed reply in one atomic
    /// step, that single transition is at once the server returning to
    /// <see cref="ServerMode.Running"/> and the request slot being cleared, so
    /// <see cref="Recovers"/> and <see cref="Reports"/> match the same recovery
    /// edge from two angles. Strong fairness on either alone already closes the
    /// crash loop; the bundle asks for both to mirror the hand-written WAL's
    /// separate recover and report obligations and to remain honest if the two
    /// steps were ever split.
    /// </remarks>
    public static Fairness Implementation { get; } = Recovers + Reports;

    /// <summary>The too-weak version of <see cref="Recovers"/>.</summary>
    public static Fairness WeakRecovers { get; } =
        Fairness.Weak<WalProcessState>((source, target) =>
            source.Server.Mode == ServerMode.Recovering &&
            target.Server.Mode == ServerMode.Running);

    /// <summary>The too-weak version of <see cref="Reports"/>.</summary>
    public static Fairness WeakReports { get; } =
        Fairness.Weak<WalProcessState>((source, target) =>
            source.Exchange.Pending != null && target.Exchange.Pending == null);

    /// <summary>
    /// Both obligations at weak strength. This is too weak: a crash resets the
    /// server before either recovery or a report is <em>continuously</em>
    /// enabled, so weak fairness never fires and the crash loop survives.
    /// </summary>
    public static Fairness WeakBoth { get; } = WeakRecovers + WeakReports;

    /// <summary>
    /// Every submitted transaction is eventually decided and reported. Weak
    /// fairness on each decision; there is deliberately no obligation to submit,
    /// so no client is required to win admission.
    /// </summary>
    public static Fairness StoreLiveness { get; } =
        Fairness.Weak(StoreStep.Any(StoreAction.Commit, StoreAction.Abort)) +
        Fairness.Weak(StoreStep.Any(StoreAction.ReportCommit, StoreAction.ReportAbort));
}
