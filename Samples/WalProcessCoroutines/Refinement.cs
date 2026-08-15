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
/// atomic store. The <em>state mapping alone</em> is the refinement mapping and
/// is enough here; the optional transition declarations are a secondary,
/// checked explanation. Both are written entirely outside the process code: no
/// workflow carries a <c>.Linearizes(...)</c> annotation.
/// </summary>
public static class StoreRefinement
{
    // ---------------------------------------------------------------
    // The checks.
    // ---------------------------------------------------------------

    /// <summary>
    /// The primary check: the <em>state mapping alone</em>, with no transition
    /// declarations. It is enough here because every abstract action is
    /// distinguishable by its mapped endpoints — each concrete transition changes
    /// the mapped store state in a way exactly one abstract edge produces, or
    /// changes nothing at all and aligns with abstract stutter — and there are no
    /// consequential state-neutral abstract actions to disambiguate. Both
    /// <see cref="FunctionalRefinementCheck{TConcrete,TAbstract}.Check"/> and
    /// <see cref="FunctionalRefinementCheck{TConcrete,TAbstract}.CheckTemporal"/>
    /// therefore align every transition deterministically without any
    /// <c>.MapTransition(...)</c>.
    /// </summary>
    public static FunctionalRefinementCheck<WalProcessState, StoreState> Build(
        WalConfig config = null,
        int maxDepth = -1)
    {
        config ??= WalConfig.Default;
        return Refinement
            .Between<WalProcessState, StoreState>(
                WriteAheadLog.Explore(config, maxDepth),
                AtomicStore.Explore(config))
            .Map(ToStore);
    }

    /// <summary>
    /// A secondary check that layers the optional <see cref="Declarations"/> on
    /// top of the state mapping. It is <em>not</em> required for the refinement
    /// to hold — the state mapping alone already proves it — but it demonstrates
    /// the checked action interpretation: it asserts which store action each
    /// meaningful process transition performs, and that a crash outside the
    /// in-doubt window is invisible to the store. A wrong assertion (for example
    /// calling the commit flush an abort) is rejected.
    /// </summary>
    public static FunctionalRefinementCheck<WalProcessState, StoreState> Declared(
        WalConfig config = null,
        int maxDepth = -1)
        => Build(config, maxDepth).MapTransition(Declarations);

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
    // The optional transition declarations (a secondary, checked explanation).
    // ---------------------------------------------------------------

    /// <summary>
    /// The optional action declarations. Each names the store action a meaningful
    /// process transition performs, asserting more than the state mapping infers:
    /// a client submit performs <c>submit(client)</c>, the commit flush performs
    /// <c>commit</c>, and an acknowledgement performs the matching report. A crash
    /// that dooms an in-flight transaction performs <c>abort</c>; any other crash
    /// is an intentional <see cref="AbstractResponse.Hidden"/> claim — a checked
    /// assertion that a crash outside the in-doubt window is invisible to the
    /// store. Everything else (the internal storage steps and pure process
    /// control) is left <see cref="AbstractResponse.Unconstrained"/>: the default
    /// inference already aligns those with abstract stutter, so there is nothing
    /// to assert.
    /// </summary>
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
    /// The declaration itself. A crash is an intentional hidden claim unless it
    /// dooms an in-flight transaction, in which case it performs the abstract
    /// abort. All other transitions are either named by their typed action or
    /// left unconstrained for the state mapping to infer.
    /// </summary>
    public static AbstractResponse Declare(ProcessTransition process, WalProcessState source)
    {
        if (process.IsControl)
        {
            return process.Control switch
            {
                // A crash outside the in-doubt window is intentionally claimed
                // invisible; a crash that dooms the in-flight transaction is its
                // abort. Launch, restart and completion are left to inference.
                ProcessControlKind.Crash => DoomsInFlightTransaction(source)
                    ? StoreStep.Performs(StoreAction.Abort)
                    : AbstractResponse.Hidden,
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

            // The internal storage steps do not move the store; inference aligns
            // them with abstract stutter without an explicit hiding claim.
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
        Fairness.StrongAction<ProcessTransition>(IsRecoveryCompletion);

    /// <summary>
    /// A still-owed client eventually receives its decision — reported either by
    /// the original handler or, if a crash killed it, by the recovery worker that
    /// replaces it. Strong fairness is required because a crash can disable the
    /// acknowledgement (it discards the handler or the recovery worker before it
    /// publishes), so the report edge is only enabled intermittently.
    /// </summary>
    public static Fairness Reports { get; } =
        Fairness.StrongEach<ProcessTransition, object>(
            IsReport,
            transition => transition.Subject);

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
        Fairness.WeakAction<ProcessTransition>(IsRecoveryCompletion);

    /// <summary>The too-weak version of <see cref="Reports"/>.</summary>
    public static Fairness WeakReports { get; } =
        Fairness.WeakEach<ProcessTransition, object>(
            IsReport,
            transition => transition.Subject);

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

    private static bool IsRecoveryCompletion(ProcessTransition transition)
        => transition.ProcessRole == Roles.Recovery &&
            transition.SemanticAction is WalAction action &&
            action is
                WalAction.Recover or
                WalAction.AckCommit or
                WalAction.AckAbort;

    private static bool IsReport(ProcessTransition transition)
        => transition.SemanticAction is WalAction action &&
            (action is WalAction.AckCommit or WalAction.AckAbort) &&
            transition.Subject is ClientId;
}
