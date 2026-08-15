namespace WalRefinement;

using System;
using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// The volatile phase of the server process — all of it is lost at a crash.
/// <see cref="Active"/> is the server holding the client's write in memory:
/// the outcome is still in doubt, and a crash there destroys the only copy of
/// the write and therefore dooms the transaction.
/// </summary>
public enum ServerPhase { Down, Recovering, Idle, Active, Committed, Aborted }

/// <summary>
/// The client is an external process: it outlives every crash and remembers
/// what it asked for and what it was told.
/// </summary>
public enum ClientPhase { Idle, Waiting }

/// <summary>The twelve actions of the implementation.</summary>
public enum WalAction
{
    Submit,
    AckCommit,
    AckAbort,
    AppendRedo,
    FlushCommit,
    TruncateLog,
    Abort,
    InstallData,
    Crash,
    Restart,
    Recover,
    Reconnect
}

/// <summary>
/// Deliberately broken variants of the protocol. Each flag removes exactly
/// one ordering constraint the write-ahead protocol relies on.
/// </summary>
public sealed class WalOptions
{
    /// <summary>The protocol as it is supposed to be implemented.</summary>
    public static WalOptions Correct { get; } = new WalOptions();

    /// <summary>
    /// Uncommitted pages may be written back although this redo-only model has
    /// no undo record. The correct model is no-steal: write-back waits until
    /// the commit record is durable.
    /// </summary>
    public bool InstallUncommittedPages { get; set; }

    /// <summary>
    /// The client is acknowledged once the redo record is durable, before the
    /// commit record is.
    /// </summary>
    public bool AckBeforeCommitIsDurable { get; set; }

    /// <summary>Recovery rolls back instead of rolling a committed transaction forward.</summary>
    public bool RecoveryIgnoresCommitRecord { get; set; }

    /// <summary>The log is reclaimed before the outcome reaches the client.</summary>
    public bool TruncateBeforeAcknowledgement { get; set; }
}

/// <summary>
/// The implementation state: a single-node store with a write-ahead log.
///
/// <list type="table">
/// <item><term>durable</term><description>
/// <see cref="Data"/>, <see cref="LogRedo"/>, <see cref="LogRecord"/> and
/// <see cref="LogCommit"/> survive a crash</description></item>
/// <item><term>volatile</term><description>
/// <see cref="Server"/> is lost at a crash, including the server's in-memory
/// copy of an in-flight write</description></item>
/// <item><term>client</term><description>
/// <see cref="Client"/>, <see cref="Request"/> and <see cref="Reported"/>
/// belong to another process, so they outlive every crash</description></item>
/// </list>
/// </summary>
[State]
public partial class WalState
{
    /// <summary>The durable data page of each key.</summary>
    public int[] Data { get; set; }

    /// <summary>Whether the redo record of the in-flight transaction is durable.</summary>
    public bool LogRedo { get; set; }

    /// <summary>
    /// The payload of the durable redo record: the <em>whole</em> write set,
    /// or <c>null</c> when the log is empty. Logging the whole write set is
    /// what lets one commit record decide the fate of every key at once.
    /// </summary>
    public WriteSet LogRecord { get; set; }

    /// <summary>
    /// Whether the commit record is durable. This is the linearization point:
    /// the instant it becomes true, the transaction has committed.
    /// </summary>
    public bool LogCommit { get; set; }

    /// <summary>The volatile phase of the server process.</summary>
    public ServerPhase Server { get; set; }

    /// <summary>The client's phase.</summary>
    public ClientPhase Client { get; set; }

    /// <summary>The write set the client asked for, or <c>null</c>.</summary>
    public WriteSet Request { get; set; }

    /// <summary>The outcome the client was told.</summary>
    public Outcome Reported { get; set; }
}

/// <summary>An action of the implementation, tagged with the WAL action it is.</summary>
public sealed class WalStep : Step<WalState>
{
    public WalStep(
        WalAction action,
        Func<WalState, bool> when,
        Action<WalState> then,
        string subject = null)
        : base(Step.Id(action, subject), subject, when, then)
        => Action = action;

    /// <summary>The WAL action this step performs.</summary>
    public WalAction Action { get; }

    /// <summary>The action a transition took.</summary>
    public static WalAction ActionOf(IStepFunction step) => ((WalStep)step).Action;

    /// <summary>Selects WAL actions, for a fairness assumption.</summary>
    public static Func<IStepFunction, bool> Any(params WalAction[] actions)
        => step => step is WalStep wal && Array.IndexOf(actions, wal.Action) >= 0;
}

/// <summary>Builds the implementation model.</summary>
public static class WriteAheadLog
{
    /// <summary>
    /// The value recovery would install for <paramref name="key"/> from the
    /// durable state alone: the logged value once the commit record is
    /// durable, the durable data page otherwise. This function <em>is</em> the
    /// recovery algorithm, and the refinement mapping is built on it.
    /// </summary>
    public static int Recovered(WalState wal, int key)
        => wal.LogCommit ? wal.LogRecord[key] : wal.Data[key];

    /// <summary>The whole store recovery would install right now.</summary>
    public static int[] RecoveredSnapshot(WalState wal)
    {
        var values = new int[wal.Data.Length];
        for (var key = 0; key < values.Length; key++)
        {
            values[key] = Recovered(wal, key);
        }

        return values;
    }

    /// <summary>Whether every data page already holds the logged write set.</summary>
    public static bool FullyInstalled(WalState wal)
        => wal.LogRecord != null && wal.LogRecord.Matches(wal.Data);

    /// <summary>The initial state: empty log, nothing in flight.</summary>
    public static WalState InitialState(WalConfig config)
        => new WalState
        {
            Data = config.Initial.ToValues(),
            LogRedo = false,
            LogRecord = null,
            LogCommit = false,
            Server = ServerPhase.Idle,
            Client = ClientPhase.Idle,
            Request = null,
            Reported = Outcome.None
        };

    /// <summary>The actions, in four groups: client, log, data pages, crash and recovery.</summary>
    public static IList<IStepFunction> Steps(WalConfig config, WalOptions options = null)
    {
        options ??= WalOptions.Correct;
        var steps = new List<IStepFunction>();

        // ---- the client -------------------------------------------------
        // Submitting hands the write to volatile server memory; nothing
        // durable has happened yet. An acknowledgement may only be sent from
        // a decided server phase, and that guard is the durability contract.

        foreach (var transaction in config.Transactions)
        {
            steps.Add(new WalStep(
                WalAction.Submit,
                subject: transaction.Name,
                when: wal => wal.Client == ClientPhase.Idle && wal.Server == ServerPhase.Idle,
                then: wal =>
                {
                    wal.Client = ClientPhase.Waiting;
                    wal.Request = transaction.Copy();
                    wal.Reported = Outcome.None;
                    wal.Server = ServerPhase.Active;
                }));
        }

        steps.Add(new WalStep(
            WalAction.AckCommit,
            when: wal => wal.Client == ClientPhase.Waiting &&
                (wal.Server == ServerPhase.Committed ||
                    // Broken: acknowledging on the redo flush, which recovery
                    // would discard.
                    (options.AckBeforeCommitIsDurable &&
                        wal.Server == ServerPhase.Active &&
                        wal.LogRedo)),
            then: wal =>
            {
                wal.Client = ClientPhase.Idle;
                wal.Request = null;
                wal.Reported = Outcome.Committed;
            }));

        steps.Add(new WalStep(
            WalAction.AckAbort,
            when: wal => wal.Client == ClientPhase.Waiting && wal.Server == ServerPhase.Aborted,
            then: wal =>
            {
                wal.Client = ClientPhase.Idle;
                wal.Request = null;
                wal.Reported = Outcome.Aborted;
                wal.Server = ServerPhase.Idle;
            }));

        // ---- the log ----------------------------------------------------
        // The redo record carries the whole write set and is forced before the
        // commit record. Flushing the commit record is the linearization
        // point. Truncation gives the log back once nothing depends on it.

        steps.Add(new WalStep(
            WalAction.AppendRedo,
            when: wal => wal.Server == ServerPhase.Active && !wal.LogRedo && wal.Request != null,
            then: wal =>
            {
                wal.LogRedo = true;
                wal.LogRecord = wal.Request.Copy();
            }));

        steps.Add(new WalStep(
            WalAction.FlushCommit,
            when: wal => wal.Server == ServerPhase.Active && wal.LogRedo && !wal.LogCommit,
            then: wal =>
            {
                wal.LogCommit = true;
                wal.Server = ServerPhase.Committed;
            }));

        steps.Add(new WalStep(
            WalAction.TruncateLog,
            when: wal => wal.Server == ServerPhase.Committed &&
                wal.LogCommit &&
                FullyInstalled(wal) &&
                // Broken: reclaiming the last durable evidence of the outcome
                // while a client is still waiting for it.
                (options.TruncateBeforeAcknowledgement || wal.Client == ClientPhase.Idle),
            then: wal =>
            {
                wal.LogRedo = false;
                wal.LogCommit = false;
                wal.LogRecord = null;
                wal.Server = ServerPhase.Idle;
            }));

        steps.Add(new WalStep(
            WalAction.Abort,
            when: wal => wal.Server == ServerPhase.Active && wal.Client == ClientPhase.Waiting,
            then: wal =>
            {
                // No undo of the data pages is needed: nothing uncommitted was
                // ever installed.
                wal.LogRedo = false;
                wal.LogRecord = null;
                wal.Server = ServerPhase.Aborted;
            }));

        // ---- the data pages ---------------------------------------------
        // One page at a time, after the commit. The same action is the redo
        // replay during recovery, which is why it is idempotent and why a
        // crash in the middle of a replay is harmless.

        for (var index = 0; index < config.Keys; index++)
        {
            var key = index;
            steps.Add(new WalStep(
                WalAction.InstallData,
                subject: $"k{key}",
                when: wal => (wal.Server == ServerPhase.Committed &&
                        wal.LogCommit &&
                        wal.Data[key] != wal.LogRecord[key]) ||
                    // Broken: stealing an uncommitted page although there is
                    // no undo record.
                    (options.InstallUncommittedPages &&
                        wal.Server == ServerPhase.Active &&
                        wal.Request != null &&
                        wal.Data[key] != wal.Request[key]),
                then: wal => wal.Data[key] = wal.Server == ServerPhase.Committed
                    ? wal.LogRecord[key]
                    : wal.Request[key]));
        }

        // ---- crash and recovery ------------------------------------------
        // A crash keeps durable state and loses everything else. Recovery
        // analysis reads only the durable log: a commit record rolls the
        // transaction forward and leaves the replay to install-data, and its
        // absence discards the redo record. Reconnection is a separate
        // client/server interaction, so recovery itself depends on durable
        // state alone.

        steps.Add(new WalStep(
            WalAction.Crash,
            when: wal => wal.Server != ServerPhase.Down,
            then: wal => wal.Server = ServerPhase.Down));

        steps.Add(new WalStep(
            WalAction.Restart,
            when: wal => wal.Server == ServerPhase.Down,
            then: wal => wal.Server = ServerPhase.Recovering));

        steps.Add(new WalStep(
            WalAction.Recover,
            when: wal => wal.Server == ServerPhase.Recovering,
            then: wal =>
            {
                if (wal.LogCommit && !options.RecoveryIgnoresCommitRecord)
                {
                    wal.Server = ServerPhase.Committed;
                    return;
                }

                wal.LogRedo = false;
                wal.LogCommit = false;
                wal.LogRecord = null;
                wal.Server = ServerPhase.Idle;
            }));

        steps.Add(new WalStep(
            WalAction.Reconnect,
            when: wal => wal.Server == ServerPhase.Idle &&
                wal.Client == ClientPhase.Waiting &&
                !wal.LogCommit,
            then: wal => wal.Server = ServerPhase.Aborted));

        return steps;
    }

    /// <summary>Explores the implementation graph.</summary>
    public static StateGraphNode Explore(
        WalConfig config = null,
        WalOptions options = null,
        bool lazy = true)
    {
        config ??= WalConfig.Default;
        return StateGraph.ExploreStateGraph(
            Steps(config, options),
            InitialState(config),
            lazy: lazy);
    }
}
