namespace WalRefinement;

using System;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;

/// <summary>
/// The refinement between the write-ahead log and the atomic transaction
/// store.
///
/// <para>The whole mapping is a total function of one concrete state, with no
/// <c>.Augment(...)</c> and no <c>.WithWitness(...)</c>, and that is the point
/// of the sample: choosing the commit-record flush as the linearization point
/// puts the information the specification needs into durable state. The
/// abstract store is defined to be <em>the store recovery would produce</em>,
/// so the mapping never has to remember the past or predict the future.</para>
/// </summary>
public static class WalRefinementCheck
{
    /// <summary>Builds the standard check, optionally with a broken part.</summary>
    public static FunctionalRefinementCheck<WalState, StoreState> Build(
        WalConfig config = null,
        WalOptions options = null,
        Func<WalState, StoreState> mapping = null,
        Func<RefinementTransition<WalState>, AbstractResponse> transitionMapping = null,
        bool lazy = true)
    {
        config ??= WalConfig.Default;
        var check = Refinement
            .Between<WalState, StoreState>(
                WriteAheadLog.Explore(config, options, lazy),
                TransactionStore.Explore(config, lazy))
            .Map(mapping ?? MapToStore);
        return transitionMapping == null
            ? check
            : check.MapTransition(transitionMapping);
    }

    /// <summary>
    /// Builds the standard check with the action declarations, which is the
    /// form the temporal tests use.
    /// </summary>
    public static FunctionalRefinementCheck<WalState, StoreState> BuildDeclared(
        WalConfig config = null,
        WalOptions options = null)
        => Build(config, options, transitionMapping: StoreActions);

    // ---------------------------------------------------------------
    // The state mapping.
    // ---------------------------------------------------------------

    /// <summary>
    /// Maps one WAL state to one store state.
    ///
    /// <code>
    /// Values[k] = LogCommit ? LogValue : Data[k]     // what recovery installs
    /// Phase     = Client = Idle      -> Idle
    ///             LogCommit          -> Committed    // decided, and durably so
    ///             Server = Active    -> Pending      // still in doubt
    ///             otherwise          -> Aborted      // nobody can commit it any more
    /// Request     = the client's outstanding request
    /// LastOutcome = what the client was told
    /// </code>
    ///
    /// <para>Everything else — dirty pages, the redo record, the restart and
    /// the recovery pass — is dropped by the projection and is therefore
    /// hidden by construction.</para>
    /// </summary>
    public static StoreState MapToStore(WalState wal)
    {
        var values = new int[wal.Data.Length];
        for (var key = 0; key < values.Length; key++)
        {
            values[key] = WriteAheadLog.Recovered(wal, key);
        }

        return new StoreState
        {
            Values = values,
            Phase = MapPhase(wal),
            Request = wal.Request,
            LastOutcome = wal.Reported
        };
    }

    /// <summary>The client-visible phase of the in-flight transaction.</summary>
    public static TxnPhase MapPhase(WalState wal)
        => wal.Client == ClientPhase.Idle
            ? TxnPhase.Idle
            : wal.LogCommit
                ? TxnPhase.Committed
                : wal.Server == ServerPhase.Active
                    ? TxnPhase.Pending
                    : TxnPhase.Aborted;

    /// <summary>
    /// A deliberately naive mapping that reads the data pages instead of the
    /// state recovery would install. It is the mapping written by someone who
    /// has not yet chosen a linearization point, and it fails on the ordinary
    /// window between the commit flush and the write-back.
    /// </summary>
    public static StoreState MapDurablePagesOnly(WalState wal)
    {
        var store = MapToStore(wal);
        for (var key = 0; key < store.Values.Length; key++)
        {
            store.Values[key] = wal.Data[key];
        }

        return store;
    }

    // ---------------------------------------------------------------
    // The transition mapping: which store action a WAL transition is.
    // ---------------------------------------------------------------

    /// <summary>
    /// Declares the store action behind every WAL transition. The durability
    /// and reconnection plumbing is declared
    /// <see cref="AbstractResponse.Hidden"/>; a crash is internal too, unless
    /// it dooms an in-flight transaction.
    /// </summary>
    public static AbstractResponse StoreActions(
        RefinementTransition<WalState> transition)
        => StoreAction(transition.StepFunction, transition.Source);

    /// <summary>
    /// The declaration itself. It needs the state the transition departs
    /// from, because whether a crash is visible depends on what was in doubt
    /// when it happened.
    /// </summary>
    public static AbstractResponse StoreAction(IStepFunction step, WalState source)
        => step switch
        {
            // The visible actions.
            SubmitStep submit => AbstractResponse.Step(candidate =>
                candidate is SubmitTransactionStep spec && spec.Value == submit.Value),
            FlushCommitStep => AbstractResponse.Step<CommitTransactionStep>(),
            AbortStep => AbstractResponse.Step<AbortTransactionStep>(),
            AckCommitStep => AbstractResponse.Step<ReportCommitStep>(),
            AckAbortStep => AbstractResponse.Step<ReportAbortStep>(),

            // A crash is not always invisible: losing the server's volatile
            // copy of an in-flight write is exactly the abort of that
            // transaction.
            CrashStep => DoomsInFlightTransaction(source)
                ? AbstractResponse.Step<AbortTransactionStep>()
                : AbstractResponse.Hidden,

            // Durability plumbing and recovery. The store stands still across
            // all of it, and that claim is checked.
            AppendRedoStep => AbstractResponse.Hidden,
            InstallDataStep => AbstractResponse.Hidden,
            TruncateLogStep => AbstractResponse.Hidden,
            RestartStep => AbstractResponse.Hidden,
            RecoverStep => AbstractResponse.Hidden,
            ReconnectClientStep => AbstractResponse.Hidden,

            _ => AbstractResponse.Unconstrained
        };

    /// <summary>
    /// Whether a transition leaving <paramref name="source"/> ends the
    /// in-doubt window of a transaction the client is still waiting for.
    /// </summary>
    public static bool DoomsInFlightTransaction(WalState source)
        => source.Client == ClientPhase.Waiting &&
            source.Server == ServerPhase.Active &&
            !source.LogCommit;

    /// <summary>
    /// A deliberately over-confident declaration: it treats every crash as an
    /// internal action, which is true for every crash except the one that
    /// loses an in-flight transaction.
    /// </summary>
    public static AbstractResponse CrashIsAlwaysHidden(
        RefinementTransition<WalState> transition)
        => transition.StepFunction is CrashStep
            ? AbstractResponse.Hidden
            : StoreActions(transition);

    /// <summary>
    /// A deliberately wrong declaration: it calls the commit flush a store
    /// abort. The mapped store state already says otherwise, so this is a
    /// mismatch rather than a way to relabel the protocol.
    /// </summary>
    public static AbstractResponse CommitFlushIsAnAbort(
        RefinementTransition<WalState> transition)
        => transition.StepFunction is FlushCommitStep
            ? AbstractResponse.Step<AbortTransactionStep>()
            : StoreActions(transition);
}

/// <summary>The fairness constraints the two models are checked under.</summary>
public static class WalFairness
{
    /// <summary>
    /// An intentionally redundant restart assumption. Every down state has
    /// only the restart edge, so no infinite graph path can remain down and
    /// fairness is unnecessary. It is retained to demonstrate that weak
    /// fairness is evaluated over a complete cycle, not a temporary sojourn.
    /// </summary>
    public static Fairness Restarts { get; } = Fairness.Weak<RestartStep>();

    /// <summary>
    /// A restarted process eventually finishes recovery analysis.
    /// <b>Strong</b> fairness is required: a crash during recovery disables
    /// the analysis, so a process that crashes on every recovery attempt only
    /// enables it intermittently and weak fairness is powerless.
    /// </summary>
    public static Fairness Recovers { get; } = Fairness.Strong<RecoverStep>();

    /// <summary>The too-weak version of <see cref="Recovers"/>.</summary>
    public static Fairness WeakRecovers { get; } = Fairness.Weak<RecoverStep>();

    /// <summary>
    /// A waiting client eventually reconnects and receives the decision.
    /// <b>Strong</b> fairness is required because a crash can disable both
    /// reconnection and acknowledgement.
    /// </summary>
    public static Fairness Reports { get; } = Fairness.Strong(
        step => step is ReconnectClientStep ||
            step is AckCommitStep ||
            step is AckAbortStep);

    /// <summary>The too-weak version of <see cref="Reports"/>.</summary>
    public static Fairness WeakReports { get; } = Fairness.Weak(
        step => step is ReconnectClientStep ||
            step is AckCommitStep ||
            step is AckAbortStep);

    /// <summary>
    /// A running server eventually pushes an in-doubt transaction towards a
    /// decision. Weak fairness is enough while the server stays up, and a
    /// crash decides the transaction by dooming it. This bundle is
    /// <em>not</em> part of <see cref="Implementation"/>: the model has no
    /// infinite behavior that stays inside the in-doubt window, so no
    /// assumption is needed to leave it.
    /// </summary>
    public static Fairness Decides { get; } = Fairness.Weak(
        step => step is AppendRedoStep || step is FlushCommitStep);

    /// <summary>
    /// Everything the implementation is assumed to guarantee. Note what is
    /// <em>not</em> here: nothing constrains <c>crash</c>, so crashing forever
    /// remains a behavior of this model, and nothing constrains write-back or
    /// truncation, which no client-visible obligation depends on.
    /// </summary>
    public static Fairness Implementation { get; } = Recovers + Reports;

    /// <summary>The same bundle with weak acknowledgement fairness.</summary>
    public static Fairness ImplementationWithWeakReports { get; } =
        Recovers + WeakReports;

    /// <summary>The same bundle with weak recovery fairness.</summary>
    public static Fairness ImplementationWithWeakRecovery { get; } =
        WeakRecovers + Reports;

    /// <summary>
    /// A submitted transaction is eventually decided. Weak fairness on each
    /// decision: taking one disables the other, so this is the obligation
    /// "leave <see cref="TxnPhase.Pending"/>", not "commit".
    /// </summary>
    public static Fairness StoreDecides { get; } = Fairness.Weak(
        step => step is CommitTransactionStep || step is AbortTransactionStep);

    /// <summary>A decided transaction is eventually reported.</summary>
    public static Fairness StoreReports { get; } = Fairness.Weak(
        step => step is ReportCommitStep || step is ReportAbortStep);

    /// <summary>
    /// Every submitted transaction is eventually decided and reported. There
    /// is deliberately no obligation to submit: a quiet client is allowed.
    /// </summary>
    public static Fairness StoreLiveness { get; } = StoreDecides + StoreReports;
}
