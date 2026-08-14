namespace WalRefinement;

using System;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;

/// <summary>
/// The refinement of the write-ahead log by the atomic transaction store: the
/// state mapping, the declaration of what each WAL action is abstractly, and
/// the deliberately wrong versions of both.
///
/// <para>The whole mapping is a total function of one concrete state, with no
/// <c>.Augment(...)</c> and no <c>.WithWitness(...)</c>. That is the point of
/// the sample: choosing the commit-record flush as the linearization point
/// puts everything the specification needs into durable state, so the abstract
/// store can be defined as <em>the store recovery would install right now</em>
/// and the mapping never has to remember the past or predict the future.</para>
/// </summary>
public static class StoreRefinement
{
    // ---------------------------------------------------------------
    // The checks.
    // ---------------------------------------------------------------

    /// <summary>The standard check: the mapping plus the action declarations.</summary>
    public static FunctionalRefinementCheck<WalState, StoreState> Build(
        WalConfig config = null,
        WalOptions options = null,
        Func<WalState, StoreState> map = null,
        bool lazy = true)
        => StateOnly(config, options, map, lazy).MapTransition(Declarations);

    /// <summary>
    /// The same check with state matching alone. Declarations only ever
    /// narrow, so this is the weaker claim; the tests use it to attach a
    /// deliberately wrong declaration.
    /// </summary>
    public static FunctionalRefinementCheck<WalState, StoreState> StateOnly(
        WalConfig config = null,
        WalOptions options = null,
        Func<WalState, StoreState> map = null,
        bool lazy = true)
    {
        config ??= WalConfig.Default;
        return Refinement
            .Between<WalState, StoreState>(
                WriteAheadLog.Explore(config, options, lazy),
                AtomicStore.Explore(config, lazy))
            .Map(map ?? ToStore);
    }

    // ---------------------------------------------------------------
    // The state mapping.
    // ---------------------------------------------------------------

    /// <summary>
    /// One WAL state as one store state.
    ///
    /// <code>
    /// Values[k]   = LogCommit ? LogRecord[k] : Data[k]   // what recovery installs
    /// Phase       = Client = Idle     -> Idle
    ///               LogCommit         -> Committed       // decided, and durably so
    ///               Server = Active   -> Pending         // still in doubt
    ///               otherwise         -> Aborted         // nobody can commit it any more
    /// Request     = the client's outstanding write set
    /// LastOutcome = what the client was told
    /// </code>
    ///
    /// <para>Everything else — dirty pages, the redo record, the restart and
    /// the recovery pass — is dropped by the projection, and is therefore
    /// hidden by construction.</para>
    /// </summary>
    public static StoreState ToStore(WalState wal)
        => new StoreState
        {
            Values = WriteAheadLog.RecoveredSnapshot(wal),
            Phase = PhaseOf(wal),
            Request = wal.Request?.Copy(),
            LastOutcome = wal.Reported
        };

    /// <summary>The client-visible phase of the in-flight transaction.</summary>
    public static TxnPhase PhaseOf(WalState wal)
        => wal.Client == ClientPhase.Idle
            ? TxnPhase.Idle
            : wal.LogCommit
                ? TxnPhase.Committed
                : wal.Server == ServerPhase.Active
                    ? TxnPhase.Pending
                    : TxnPhase.Aborted;

    /// <summary>
    /// A naive mapping that reads the data pages instead of the state recovery
    /// would install. It is what someone writes before choosing a
    /// linearization point, and it fails in the ordinary window between the
    /// commit flush and the write-back.
    /// </summary>
    public static StoreState PagesOnly(WalState wal)
    {
        var store = ToStore(wal);
        store.Values = (int[])wal.Data.Clone();
        return store;
    }

    // ---------------------------------------------------------------
    // The transition mapping: which store action a WAL transition is.
    // ---------------------------------------------------------------

    /// <summary>Declares the store action behind every WAL transition.</summary>
    public static AbstractResponse Declarations(RefinementTransition<WalState> transition)
        => Declare(transition.StepFunction, transition.Source);

    /// <summary>
    /// The declaration itself. It needs the state the transition departs from,
    /// because whether a crash is visible depends on what was in doubt when it
    /// happened.
    /// </summary>
    public static AbstractResponse Declare(IStepFunction step, WalState source)
    {
        var wal = (WalStep)step;
        return wal.Action switch
        {
            // The visible actions.
            WalAction.Submit => StoreStep.Performs(StoreAction.Submit, wal.Subject),
            WalAction.FlushCommit => StoreStep.Performs(StoreAction.Commit),
            WalAction.Abort => StoreStep.Performs(StoreAction.Abort),
            WalAction.AckCommit => StoreStep.Performs(StoreAction.ReportCommit),
            WalAction.AckAbort => StoreStep.Performs(StoreAction.ReportAbort),

            // A crash is not always invisible: losing the server's volatile
            // copy of an in-flight write is exactly the abort of that
            // transaction.
            WalAction.Crash => DoomsInFlightTransaction(source)
                ? StoreStep.Performs(StoreAction.Abort)
                : AbstractResponse.Hidden,

            // Durability plumbing and recovery. The store stands still across
            // all of it, and that claim is checked.
            WalAction.AppendRedo or
            WalAction.InstallData or
            WalAction.TruncateLog or
            WalAction.Restart or
            WalAction.Recover or
            WalAction.Reconnect => AbstractResponse.Hidden,

            _ => AbstractResponse.Unconstrained
        };
    }

    /// <summary>
    /// Whether a transition leaving <paramref name="source"/> ends the in-doubt
    /// window of a transaction the client is still waiting for.
    /// </summary>
    public static bool DoomsInFlightTransaction(WalState source)
        => source.Client == ClientPhase.Waiting &&
            source.Server == ServerPhase.Active &&
            !source.LogCommit;

    /// <summary>
    /// A deliberately over-confident declaration: every crash is internal,
    /// which is true for every crash except the one that loses an in-flight
    /// transaction.
    /// </summary>
    public static AbstractResponse CrashIsAlwaysHidden(
        RefinementTransition<WalState> transition)
        => WalStep.ActionOf(transition.StepFunction) == WalAction.Crash
            ? AbstractResponse.Hidden
            : Declarations(transition);

    /// <summary>
    /// A deliberately wrong declaration: the commit flush is called a store
    /// abort. The mapped state already says otherwise, so this is a mismatch
    /// rather than a way to relabel the protocol.
    /// </summary>
    public static AbstractResponse CommitFlushIsAnAbort(
        RefinementTransition<WalState> transition)
        => WalStep.ActionOf(transition.StepFunction) == WalAction.FlushCommit
            ? StoreStep.Performs(StoreAction.Abort)
            : Declarations(transition);
}

/// <summary>The fairness constraints the two models are checked under.</summary>
public static class WalFairness
{
    /// <summary>
    /// A restarted process eventually finishes recovery analysis.
    /// <b>Strong</b> fairness is required: a crash during recovery disables the
    /// analysis, so a process that crashes on every attempt only enables it
    /// intermittently and weak fairness is powerless.
    /// </summary>
    public static Fairness Recovers { get; } =
        Fairness.Strong(WalStep.Any(WalAction.Recover));

    /// <summary>
    /// A waiting client eventually reconnects and receives the decision.
    /// <b>Strong</b> fairness is required because a crash can disable both
    /// reconnection and acknowledgement.
    /// </summary>
    public static Fairness Reports { get; } = Fairness.Strong(
        WalStep.Any(WalAction.Reconnect, WalAction.AckCommit, WalAction.AckAbort));

    /// <summary>
    /// Everything the implementation is assumed to guarantee. Note what is
    /// <em>not</em> here: nothing constrains <c>crash</c>, so crashing forever
    /// remains a behavior of this model, and nothing constrains write-back or
    /// truncation, which no client-visible obligation depends on.
    /// </summary>
    public static Fairness Implementation { get; } = Recovers + Reports;

    /// <summary>The too-weak version of <see cref="Recovers"/>.</summary>
    public static Fairness WeakRecovers { get; } =
        Fairness.Weak(WalStep.Any(WalAction.Recover));

    /// <summary>The too-weak version of <see cref="Reports"/>.</summary>
    public static Fairness WeakReports { get; } = Fairness.Weak(
        WalStep.Any(WalAction.Reconnect, WalAction.AckCommit, WalAction.AckAbort));

    /// <summary>The bundle with weak recovery fairness.</summary>
    public static Fairness WithWeakRecovery { get; } = WeakRecovers + Reports;

    /// <summary>The bundle with weak acknowledgement fairness.</summary>
    public static Fairness WithWeakReports { get; } = Recovers + WeakReports;

    /// <summary>
    /// An intentionally redundant restart assumption: every down state has the
    /// restart edge as its only successor, so no infinite path can remain down.
    /// A test shows that adding it changes no verdict.
    /// </summary>
    public static Fairness Restarts { get; } =
        Fairness.Weak(WalStep.Any(WalAction.Restart));

    /// <summary>
    /// An in-doubt transaction is eventually pushed towards a decision. This is
    /// <em>not</em> part of <see cref="Implementation"/>: the in-doubt window
    /// contains no cycle, so no assumption is needed to leave it.
    /// </summary>
    public static Fairness Decides { get; } =
        Fairness.Weak(WalStep.Any(WalAction.AppendRedo, WalAction.FlushCommit));

    /// <summary>
    /// Every submitted transaction is eventually decided and reported. Weak
    /// fairness on each decision: taking one disables the other, so this is the
    /// obligation "leave <see cref="TxnPhase.Pending"/>", not "commit". There
    /// is deliberately no obligation to submit: a quiet client is allowed.
    /// </summary>
    public static Fairness StoreLiveness { get; } =
        Fairness.Weak(StoreStep.Any(StoreAction.Commit, StoreAction.Abort)) +
        Fairness.Weak(StoreStep.Any(StoreAction.ReportCommit, StoreAction.ReportAbort));
}
