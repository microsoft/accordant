namespace WalRefinement;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// The volatile phase of the server process. Everything in this enum is lost
/// at a crash: a process that comes back can only read the durable log and
/// the durable data pages.
/// </summary>
public enum ServerPhase
{
    /// <summary>Crashed. Nothing is running.</summary>
    Down,

    /// <summary>Restarted and running recovery analysis.</summary>
    Recovering,

    /// <summary>Up with no transaction in flight.</summary>
    Idle,

    /// <summary>
    /// Up and holding the client's write in volatile memory. The outcome is
    /// still in doubt: the server can still commit it, and a crash here loses
    /// the write and therefore dooms the transaction.
    /// </summary>
    Active,

    /// <summary>
    /// Up and knowing the transaction committed, either because this process
    /// flushed the commit record or because recovery found one.
    /// </summary>
    Committed,

    /// <summary>Up and knowing the transaction aborted.</summary>
    Aborted
}

/// <summary>
/// The client, which is an external process: it outlives every crash of the
/// server and remembers what it asked for and what it was told.
/// </summary>
public enum ClientPhase
{
    /// <summary>Nothing outstanding.</summary>
    Idle,

    /// <summary>Waiting for the outcome of a submitted transaction.</summary>
    Waiting
}

/// <summary>
/// Deliberately broken variants of the protocol. Each flag removes exactly
/// one ordering constraint that the write-ahead protocol relies on.
/// </summary>
public sealed class WalOptions
{
    /// <summary>The protocol as it is supposed to be implemented.</summary>
    public static WalOptions Correct { get; } = new WalOptions();

    /// <summary>
    /// Uncommitted pages may be written back even though this redo-only model
    /// has no undo record. The correct model uses a no-steal policy: write-back
    /// waits until the commit record is durable.
    /// </summary>
    public bool InstallUncommittedPages { get; set; }

    /// <summary>
    /// The client is acknowledged once the redo record is durable, before the
    /// commit record is. A crash in that window loses a transaction the
    /// client was told had committed.
    /// </summary>
    public bool AckBeforeCommitIsDurable { get; set; }

    /// <summary>
    /// Recovery rolls back unconditionally instead of rolling a committed
    /// transaction forward.
    /// </summary>
    public bool RecoveryIgnoresCommitRecord { get; set; }

    /// <summary>
    /// The log is reclaimed as soon as the data is installed, without waiting
    /// for the outcome to be delivered to the client.
    /// </summary>
    public bool TruncateBeforeAcknowledgement { get; set; }
}

/// <summary>
/// The implementation: a single-node store with a write-ahead log.
///
/// <para><b>Durable</b> — <see cref="Data"/>, <see cref="LogRedo"/>,
/// <see cref="LogValue"/> and <see cref="LogCommit"/> survive a crash.</para>
///
/// <para><b>Volatile</b> — <see cref="Server"/> is lost at a crash. The
/// server's in-memory copy of the client's write is represented by
/// <see cref="ServerPhase.Active"/>: a crash leaves that phase, so the write
/// is gone.</para>
///
/// <para><b>Client</b> — <see cref="Client"/>, <see cref="Request"/> and
/// <see cref="Reported"/> belong to the external client and outlive every
/// crash.</para>
/// </summary>
[State]
public partial class WalState
{
    /// <summary>The durable data page of each key.</summary>
    public int[] Data { get; set; }

    /// <summary>Whether the redo record of the in-flight transaction is durable.</summary>
    public bool LogRedo { get; set; }

    /// <summary>
    /// The payload of the durable redo record, or
    /// <see cref="TransactionStore.NoValue"/> when the log is empty.
    /// </summary>
    public int LogValue { get; set; }

    /// <summary>
    /// Whether the commit record is durable. This is the linearization point:
    /// the instant this becomes true, the transaction has committed.
    /// </summary>
    public bool LogCommit { get; set; }

    /// <summary>The volatile phase of the server process.</summary>
    public ServerPhase Server { get; set; }

    /// <summary>The client's phase.</summary>
    public ClientPhase Client { get; set; }

    /// <summary>
    /// The value the client asked for, or
    /// <see cref="TransactionStore.NoValue"/> when nothing is outstanding.
    /// </summary>
    public int Request { get; set; }

    /// <summary>The outcome the client was told.</summary>
    public Outcome Reported { get; set; }
}

/// <summary>Builds the implementation model.</summary>
public static class WriteAheadLog
{
    /// <summary>
    /// The value recovery would install for <paramref name="key"/> from the
    /// durable state alone: the logged payload once the commit record is
    /// durable, and the durable data page otherwise. This function is the
    /// whole durability contract, and the refinement mapping is built on it.
    /// </summary>
    public static int Recovered(WalState wal, int key)
        => wal.LogCommit ? wal.LogValue : wal.Data[key];

    /// <summary>Whether every data page already holds the logged payload.</summary>
    public static bool FullyInstalled(WalState wal)
    {
        for (var key = 0; key < wal.Data.Length; key++)
        {
            if (wal.Data[key] != wal.LogValue)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Creates the initial state: empty log, nothing in flight.</summary>
    public static WalState InitialState(WalConfig config)
    {
        var data = new int[config.Keys];
        for (var key = 0; key < config.Keys; key++)
        {
            data[key] = TransactionStore.InitialValue;
        }

        return new WalState
        {
            Data = data,
            LogRedo = false,
            LogValue = TransactionStore.NoValue,
            LogCommit = false,
            Server = ServerPhase.Idle,
            Client = ClientPhase.Idle,
            Request = TransactionStore.NoValue,
            Reported = Outcome.None
        };
    }

    /// <summary>Creates every step function of the implementation model.</summary>
    public static IList<IStepFunction> Steps(
        WalConfig config,
        WalOptions options = null)
    {
        options ??= WalOptions.Correct;
        var steps = new List<IStepFunction>();
        foreach (var value in config.Values)
        {
            steps.Add(new SubmitStep(value));
        }

        for (var key = 0; key < config.Keys; key++)
        {
            steps.Add(new InstallDataStep(key, options));
        }

        steps.Add(new AppendRedoStep());
        steps.Add(new FlushCommitStep());
        steps.Add(new TruncateLogStep(options));
        steps.Add(new AbortStep());
        steps.Add(new CrashStep());
        steps.Add(new RestartStep());
        steps.Add(new RecoverStep(options));
        steps.Add(new ReconnectClientStep());
        steps.Add(new AckCommitStep(options));
        steps.Add(new AckAbortStep());
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

/// <summary>
/// Shared scaffolding: guard the source state, clone it, mutate the clone,
/// and re-emit this step so the step-function set — and therefore the graph
/// node identity — stays stable. Every step changes the state, so no
/// implementation action is invisible to changing-edge fairness.
/// </summary>
public abstract class WalStep : BaseStepFunction
{
    protected abstract bool IsEnabled(WalState wal);

    protected abstract void Advance(WalState next);

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var wal = (WalState)state;
        if (!IsEnabled(wal))
        {
            return null;
        }

        var next = (WalState)wal.Clone();
        Advance(next);
        return new[]
        {
            new StepResult
            {
                State = next,
                StepFunctions = new IStepFunction[] { this }
            }
        };
    }
}

/// <summary>
/// The client submits a transaction. The server takes the write into volatile
/// memory; nothing durable has happened yet.
/// </summary>
public sealed class SubmitStep : WalStep
{
    public SubmitStep(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public override string StepFunctionId => $"submit-v{Value}";

    protected override bool IsEnabled(WalState wal)
        => wal.Client == ClientPhase.Idle && wal.Server == ServerPhase.Idle;

    protected override void Advance(WalState next)
    {
        next.Client = ClientPhase.Waiting;
        next.Request = Value;
        next.Reported = Outcome.None;
        next.Server = ServerPhase.Active;
    }
}

/// <summary>
/// The redo record reaches durable storage. Recovery ignores an uncommitted
/// log, so this changes nothing a client can observe.
/// </summary>
public sealed class AppendRedoStep : WalStep
{
    public override string StepFunctionId => "append-redo";

    protected override bool IsEnabled(WalState wal)
        => wal.Server == ServerPhase.Active &&
            !wal.LogRedo &&
            wal.Request != TransactionStore.NoValue;

    protected override void Advance(WalState next)
    {
        next.LogRedo = true;
        next.LogValue = next.Request;
    }
}

/// <summary>
/// The commit record reaches durable storage. This is the linearization
/// point: before it, recovery would discard the transaction; after it,
/// recovery replays it. The <see cref="WalState.LogRedo"/> guard is the force
/// rule at commit: the commit record may not become durable before the redo
/// record it commits.
/// </summary>
public sealed class FlushCommitStep : WalStep
{
    public override string StepFunctionId => "flush-commit";

    protected override bool IsEnabled(WalState wal)
        => wal.Server == ServerPhase.Active && wal.LogRedo && !wal.LogCommit;

    protected override void Advance(WalState next)
    {
        next.LogCommit = true;
        next.Server = ServerPhase.Committed;
    }
}

/// <summary>
/// A dirty page is written back to durable storage. On the normal path this
/// happens lazily after the commit; during recovery the same action is the
/// redo replay, which is why it is idempotent and why a crash in the middle
/// of it is harmless.
/// </summary>
public sealed class InstallDataStep : WalStep
{
    private readonly WalOptions options;

    public InstallDataStep(int key, WalOptions options)
    {
        Key = key;
        this.options = options ?? WalOptions.Correct;
    }

    public int Key { get; }

    public override string StepFunctionId => $"install-data-k{Key}";

    protected override bool IsEnabled(WalState wal)
    {
        if (wal.Server == ServerPhase.Committed &&
            wal.LogCommit &&
            wal.Data[Key] != wal.LogValue)
        {
            return true;
        }

        // Broken: steal an uncommitted page although there is no undo log.
        return options.InstallUncommittedPages &&
            wal.Server == ServerPhase.Active &&
            wal.Request != TransactionStore.NoValue &&
            wal.Data[Key] != wal.Request;
    }

    protected override void Advance(WalState next)
        => next.Data[Key] = next.Server == ServerPhase.Committed
            ? next.LogValue
            : next.Request;
}

/// <summary>
/// The log is reclaimed once its effect is fully installed and the outcome
/// has been delivered. Recovery would produce the same store from the data
/// pages alone, so the record is no longer needed.
/// </summary>
public sealed class TruncateLogStep : WalStep
{
    private readonly WalOptions options;

    public TruncateLogStep(WalOptions options)
    {
        this.options = options ?? WalOptions.Correct;
    }

    public override string StepFunctionId => "truncate-log";

    protected override bool IsEnabled(WalState wal)
        => wal.Server == ServerPhase.Committed &&
            wal.LogCommit &&
            WriteAheadLog.FullyInstalled(wal) &&
            (options.TruncateBeforeAcknowledgement ||
                wal.Client == ClientPhase.Idle);

    protected override void Advance(WalState next)
    {
        next.LogRedo = false;
        next.LogCommit = false;
        next.LogValue = TransactionStore.NoValue;
        next.Server = ServerPhase.Idle;
    }
}

/// <summary>
/// The server rolls back an in-doubt transaction. No undo of the data pages
/// is needed: nothing uncommitted was ever installed.
/// </summary>
public sealed class AbortStep : WalStep
{
    public override string StepFunctionId => "abort-txn";

    protected override bool IsEnabled(WalState wal)
        => wal.Server == ServerPhase.Active && wal.Client == ClientPhase.Waiting;

    protected override void Advance(WalState next)
    {
        next.LogRedo = false;
        next.LogValue = TransactionStore.NoValue;
        next.Server = ServerPhase.Aborted;
    }
}

/// <summary>
/// The process crashes. Durable state is untouched and volatile state is
/// gone — including the server's copy of an in-flight write, which is why a
/// crash during <see cref="ServerPhase.Active"/> is the abort of that
/// transaction rather than an invisible event.
/// </summary>
public sealed class CrashStep : WalStep
{
    public override string StepFunctionId => "crash";

    protected override bool IsEnabled(WalState wal)
        => wal.Server != ServerPhase.Down;

    protected override void Advance(WalState next)
        => next.Server = ServerPhase.Down;
}

/// <summary>The process restarts and begins recovery.</summary>
public sealed class RestartStep : WalStep
{
    public override string StepFunctionId => "restart";

    protected override bool IsEnabled(WalState wal)
        => wal.Server == ServerPhase.Down;

    protected override void Advance(WalState next)
        => next.Server = ServerPhase.Recovering;
}

/// <summary>
/// Recovery analysis reads only the durable log. A durable commit record rolls
/// the transaction forward — the replay itself is the ordinary
/// <see cref="InstallDataStep"/>. No commit record discards the redo record and
/// returns the server to idle. A separate client reconnection then re-establishes
/// the volatile knowledge needed to report an abort.
/// </summary>
public sealed class RecoverStep : WalStep
{
    private readonly WalOptions options;

    public RecoverStep(WalOptions options)
    {
        this.options = options ?? WalOptions.Correct;
    }

    public override string StepFunctionId => "recover";

    protected override bool IsEnabled(WalState wal)
        => wal.Server == ServerPhase.Recovering;

    protected override void Advance(WalState next)
    {
        if (next.LogCommit && !options.RecoveryIgnoresCommitRecord)
        {
            next.Server = ServerPhase.Committed;
            return;
        }

        next.LogRedo = false;
        next.LogCommit = false;
        next.LogValue = TransactionStore.NoValue;
        next.Server = ServerPhase.Idle;
    }
}

/// <summary>
/// The waiting client reconnects after an uncommitted request was lost. This
/// is a client/server interaction, deliberately separate from recovery so
/// recovery itself depends only on durable server state.
/// </summary>
public sealed class ReconnectClientStep : WalStep
{
    public override string StepFunctionId => "reconnect-client";

    protected override bool IsEnabled(WalState wal)
        => wal.Server == ServerPhase.Idle &&
            wal.Client == ClientPhase.Waiting &&
            !wal.LogCommit;

    protected override void Advance(WalState next)
        => next.Server = ServerPhase.Aborted;
}

/// <summary>
/// The client is told the transaction committed. The durability contract is
/// the <see cref="ServerPhase.Committed"/> guard: the commit record is
/// durable before anybody is told.
/// </summary>
public sealed class AckCommitStep : WalStep
{
    private readonly WalOptions options;

    public AckCommitStep(WalOptions options)
    {
        this.options = options ?? WalOptions.Correct;
    }

    public override string StepFunctionId => "ack-commit";

    protected override bool IsEnabled(WalState wal)
    {
        if (wal.Client != ClientPhase.Waiting)
        {
            return false;
        }

        if (wal.Server == ServerPhase.Committed)
        {
            return true;
        }

        // Broken: acknowledging on the redo flush instead of the commit flush.
        return options.AckBeforeCommitIsDurable &&
            wal.Server == ServerPhase.Active &&
            wal.LogRedo;
    }

    protected override void Advance(WalState next)
    {
        next.Client = ClientPhase.Idle;
        next.Request = TransactionStore.NoValue;
        next.Reported = Outcome.Committed;
    }
}

/// <summary>The client is told the transaction aborted.</summary>
public sealed class AckAbortStep : WalStep
{
    public override string StepFunctionId => "ack-abort";

    protected override bool IsEnabled(WalState wal)
        => wal.Client == ClientPhase.Waiting && wal.Server == ServerPhase.Aborted;

    protected override void Advance(WalState next)
    {
        next.Client = ClientPhase.Idle;
        next.Request = TransactionStore.NoValue;
        next.Reported = Outcome.Aborted;
        next.Server = ServerPhase.Idle;
    }
}
