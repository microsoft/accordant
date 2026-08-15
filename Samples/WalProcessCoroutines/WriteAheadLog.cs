// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// The only lifecycle state the server process needs. Everything else that used
/// to be a bespoke server phase — idle, active, committed, aborted, recovered —
/// is now <em>derived</em> from durable and exchange state plus this mode, so
/// there are no artificial shared phases the protocol has to keep in sync.
/// </summary>
public enum ServerMode { Running, Down, Recovering }

/// <summary>The semantic action a client takes.</summary>
public enum ClientAction { Submit }

/// <summary>
/// The semantic action each server-side process step performs, kept separate
/// from the process role and from the generated runtime step-function id.
/// </summary>
public enum WalAction
{
    AppendRedo,
    FlushCommit,
    InstallData,
    TruncateLog,
    AckCommit,
    AckAbort,
    Recover
}

/// <summary>
/// The durable decision recovery captures in its call/iteration frames before
/// it recovers state. A crash kills those frames; a restart recomputes it.
/// </summary>
internal enum RecoveryDecision { None, Commit, Abort }

/// <summary>The stable process roles of the system.</summary>
public static class Roles
{
    public const string Handler = "request-handler";
    public const string PageWriter = "page-writer";
    public const string Recovery = "recovery";

    /// <summary>The stable process role of a client (its lower-cased name).</summary>
    public static string Of(ClientId client) => client.ToString().ToLowerInvariant();

    /// <summary>The stable process roles of every possible client.</summary>
    public static IReadOnlyList<string> Clients { get; } =
        Enum.GetValues(typeof(ClientId)).Cast<ClientId>().Select(Of).ToArray();
}

// ---------------------------------------------------------------------
// The encapsulated implementation state.
// ---------------------------------------------------------------------

/// <summary>
/// The durable write-ahead log: the data pages, the redo record and the commit
/// record. All of it survives a server crash. Its focused methods read like the
/// storage operations they model.
/// </summary>
[State]
public partial class DurableWal
{
    /// <summary>The durable data page of each key.</summary>
    public int[] Data { get; set; }

    /// <summary>Whether the redo record of the in-flight transaction is durable.</summary>
    public bool LogRedo { get; set; }

    /// <summary>The payload of the durable redo record: the whole write set, or <c>null</c>.</summary>
    public WriteSet LogRecord { get; set; }

    /// <summary>Whether the commit record is durable. This is the linearization point.</summary>
    public bool LogCommit { get; set; }

    /// <summary>Whether the log holds no in-flight transaction at all.</summary>
    public bool IsClean => !LogRedo && !LogCommit && LogRecord == null;

    /// <summary>Appends the whole redo record for a request.</summary>
    public void Append(WriteSet request)
    {
        LogRedo = true;
        LogRecord = request.Copy();
    }

    /// <summary>Flushes the commit record — the linearization point.</summary>
    public void Commit() => LogCommit = true;

    /// <summary>Discards an uncommitted redo record (an abort during recovery).</summary>
    public void DiscardRedo()
    {
        LogRedo = false;
        LogRecord = null;
    }

    /// <summary>Installs the committed write of one key into its data page.</summary>
    public void Install(int key) => Data[key] = LogRecord[key];

    /// <summary>Truncates a fully-installed committed log.</summary>
    public void Truncate()
    {
        LogRedo = false;
        LogCommit = false;
        LogRecord = null;
    }

    /// <summary>The value recovery would install for <paramref name="key"/>.</summary>
    public int Recovered(int key) => LogCommit ? LogRecord[key] : Data[key];
}

/// <summary>
/// The external request/reply table: a shared mailbox that survives every server
/// crash. It holds the capacity-one <see cref="Pending"/> request and one
/// persistent <see cref="Replies"/> outcome per client. The pending row survives
/// a crash so recovery knows which client is still owed a result; the per-client
/// replies persist because clients are one-shot.
/// </summary>
[State]
public partial class Exchange
{
    /// <summary>The request in the capacity-one slot, or <c>null</c> when empty.</summary>
    public RequestEnvelope Pending { get; set; }

    /// <summary>The persistent outcome told to each client, indexed by client.</summary>
    public Outcome[] Replies { get; set; }

    /// <summary>
    /// Whether <paramref name="client"/> may claim the slot: it is free and the
    /// client has not already been given a result (clients are one-shot).
    /// </summary>
    public bool CanSubmit(ClientId client)
        => Pending == null && Replies[WalConfig.IndexOf(client)] == Outcome.None;

    /// <summary>Claims the slot for a client's request.</summary>
    public void Submit(ClientId client, WriteSet request)
        => Pending = new RequestEnvelope { Client = client, TransactionName = request.Name };

    /// <summary>Whether a client already has its persistent result.</summary>
    public bool HasReply(ClientId client)
        => Replies[WalConfig.IndexOf(client)] != Outcome.None;

    /// <summary>The persistent result a client was given.</summary>
    public Outcome ReplyOf(ClientId client) => Replies[WalConfig.IndexOf(client)];

    /// <summary>
    /// Publishes a client's result and clears the slot. Whichever of the handler
    /// or recovery publishes first empties the slot, so the other can no longer.
    /// </summary>
    public void Publish(ClientId client, Outcome outcome)
    {
        Replies[WalConfig.IndexOf(client)] = outcome;
        Pending = null;
    }
}

/// <summary>The lifecycle of the actual server process — nothing more.</summary>
[State]
public partial class ServerState
{
    /// <summary>The server lifecycle mode.</summary>
    public ServerMode Mode { get; set; }

    /// <summary>Marks the server as crashed. Assigns lifecycle state only; the
    /// crash transition itself is owned by failure-domain scheduling.</summary>
    public void MarkCrashed() => Mode = ServerMode.Down;

    /// <summary>Marks the server as restarted into recovery.</summary>
    public void MarkRecovering() => Mode = ServerMode.Recovering;

    /// <summary>Marks the server as up and serving.</summary>
    public void MarkRunning() => Mode = ServerMode.Running;
}

/// <summary>
/// The implementation state: a durable write-ahead log, a shared request/reply
/// exchange, and the server lifecycle. Process continuations are <em>not</em>
/// here — they live in the scheduler configuration, so refinement's state
/// semantics see only this domain state.
/// </summary>
[State]
public partial class WalProcessState
{
    /// <summary>The durable log and data pages; survive a crash.</summary>
    public DurableWal Wal { get; set; }

    /// <summary>The shared request/reply table; survives a crash.</summary>
    public Exchange Exchange { get; set; }

    /// <summary>The server lifecycle; reset by a crash.</summary>
    public ServerState Server { get; set; }

    /// <summary>
    /// Whether a client may be admitted right now: the server is up, the log is
    /// clean (the previous transaction fully drained), and the client can claim
    /// the free slot. Admitting one transaction at a time is what serializes the
    /// two concurrent clients through the capacity-one WAL.
    /// </summary>
    public bool CanAdmit(ClientId client)
        => Server.Mode == ServerMode.Running && Wal.IsClean && Exchange.CanSubmit(client);
}

/// <summary>
/// The process-oriented write-ahead log: two concurrent one-shot clients
/// contending for a capacity-one request slot, and a server failure domain whose
/// crash discards its process continuations. Only one transaction is admitted at
/// a time, matching the one redo record and one in-flight transaction the server
/// models — not a fundamental single-writer assumption.
/// </summary>
public static class WriteAheadLog
{
    // ---- durable recovery function ---------------------------------------

    /// <summary>The value recovery would install for <paramref name="key"/> from durable state.</summary>
    public static int Recovered(WalProcessState wal, int key) => wal.Wal.Recovered(key);

    /// <summary>The whole store recovery would install right now.</summary>
    public static int[] RecoveredSnapshot(WalProcessState wal)
    {
        var values = new int[wal.Wal.Data.Length];
        for (var key = 0; key < values.Length; key++)
        {
            values[key] = wal.Wal.Recovered(key);
        }

        return values;
    }

    /// <summary>Whether every durable data page already holds the logged write set.</summary>
    public static bool FullyInstalled(WalProcessState wal)
        => wal.Wal.LogRecord != null && wal.Wal.LogRecord.Matches(wal.Wal.Data);

    private static int FirstDirtyKey(WalProcessState wal)
    {
        if (wal.Wal.LogRecord == null)
        {
            return -1;
        }

        for (var key = 0; key < wal.Wal.Data.Length; key++)
        {
            if (wal.Wal.Data[key] != wal.Wal.LogRecord[key])
            {
                return key;
            }
        }

        return -1;
    }

    private static bool HasCommittedDirtyKey(WalProcessState wal)
        => wal.Wal.LogCommit && FirstDirtyKey(wal) >= 0;

    private static bool CanTruncate(WalProcessState wal)
        => wal.Wal.LogCommit &&
            FullyInstalled(wal) &&
            wal.Exchange.Pending == null;

    // ---- the initial state -----------------------------------------------

    /// <summary>The initial state: empty log, empty slot, no replies, server up.</summary>
    public static WalProcessState InitialState(WalConfig config)
        => new WalProcessState
        {
            Wal = new DurableWal
            {
                Data = config.Initial.ToValues(),
                LogRedo = false,
                LogRecord = null,
                LogCommit = false
            },
            Exchange = new Exchange
            {
                Pending = null,
                Replies = Enumerable.Repeat(Outcome.None, WalConfig.ReplyCount).ToArray()
            },
            Server = new ServerState { Mode = ServerMode.Running }
        };

    // ---- the composition root --------------------------------------------

    /// <summary>
    /// Registers the process system structurally: two external clients outside
    /// every failure domain, and a persistent page-writer, a persistent recovery
    /// worker and a guarded request-handler launch inside the <c>server</c>
    /// failure domain. Ownership is expressed by <em>where</em> each process is
    /// registered — through the model (survives a crash) or through the domain
    /// object (discarded and relaunched) — never by a boolean flag.
    /// </summary>
    public static ProcessSystemModel<WalProcessState> Build(WalConfig config = null)
    {
        config ??= WalConfig.Default;

        var model = new ProcessSystemModel<WalProcessState>(InitialState(config));

        // The server failure domain. A crash may interleave between any two
        // atomic checkpoints while the server is up (including during recovery);
        // it resets the volatile server mode and discards every continuation
        // registered through this domain object.
        var server = model.FailureDomain(
            "server",
            crashEnabled: s => s.Server.Mode != ServerMode.Down,
            onCrash: s => s.Server.MarkCrashed(),
            restartEnabled: s => s.Server.Mode == ServerMode.Down,
            onRestart: s => s.Server.MarkRecovering());

        // The two clients are independently active processes outside the
        // failure domain: they survive every crash with their continuation
        // intact. Each reads like one-shot request/reply implementation code.
        foreach (var client in config.Clients)
        {
            var owner = client;
            model.Process(Roles.Of(owner), ctx => Client(ctx, config, owner));
        }

        // Persistent server-domain workers: discarded at a crash, relaunched
        // fresh at the next restart.
        server.Process(
            Roles.PageWriter,
            ctx => ctx.Forever("page-writer-loop", PageWriterIteration));
        server.Process(
            Roles.Recovery,
            ctx => ctx.Forever("recovery-loop", RecoveryIteration));

        // A guarded request-handler launch in the server domain: at most one
        // handler per in-flight transaction, and never a duplicate while the
        // launch condition remains true. A crash discards it; recovery — not an
        // automatic relaunch — replaces it.
        server.On(
            Roles.Handler,
            guard: s => s.Server.Mode == ServerMode.Running &&
                s.Exchange.Pending != null &&
                !s.Wal.LogRedo,
            workflow: ctx => Handler(ctx, config));

        return model;
    }

    /// <summary>Explores the compiled process-system graph.</summary>
    public static StateGraphNode Explore(WalConfig config = null, int maxDepth = -1, bool lazy = false)
        => Build(config).Explore(maxDepth, lazy);

    // ---- the processes ---------------------------------------------------

    /// <summary>
    /// One external, one-shot client. It atomically claims the capacity-one slot
    /// for its fixed request when it can — a guarded resource claim, not a
    /// defensive precondition — waits for its persistent reply, and completes.
    /// The reply remains as an observable result; the client never resubmits.
    /// </summary>
    private static async ModelTask Client(
        ModelContext<WalProcessState> ctx,
        WalConfig config,
        ClientId client)
    {
        var request = config.RequestOf(client);

        await ctx.StepWhen(
            ClientAction.Submit,
            when: s => s.CanAdmit(client),
            then: s => s.Exchange.Submit(client, request),
            subject: client);

        await ctx.When("reply-ready", s => s.Exchange.HasReply(client));

        // One-shot: the client completes. Its reply persists in the exchange.
    }

    /// <summary>
    /// The request handler, launched atomically once per in-flight transaction.
    /// It reads like ordinary local implementation code — three sequential atomic
    /// steps with no scheduling guards between them: append the whole redo
    /// record, flush the commit record (the linearization point), and acknowledge
    /// the commit to the client. Interleavings may occur between the awaits, but
    /// nothing another process can legitimately do here invalidates the next
    /// step; the only thing that removes this handler is a crash, which
    /// atomically discards its continuation so it never resumes. There is no
    /// implementation abort — a precommit crash kills this handler and recovery
    /// produces the abstract abort instead.
    /// </summary>
    private static async ModelTask Handler(ModelContext<WalProcessState> ctx, WalConfig config)
    {
        // An intentional local snapshot of the accepted request, resolved through
        // immutable configuration — not a fresh read of shared state at each
        // action. The handler carries scalar identity (client and transaction
        // name) rather than a mutable closure over the write set.
        var client = await ctx.Read("owner", s => s.Exchange.Pending.Client);
        var name = await ctx.Read("transaction", s => s.Exchange.Pending.TransactionName);
        var request = config.Find(name);

        await ctx.Step(
            WalAction.AppendRedo,
            s => s.Wal.Append(request),
            subject: client);
        await ctx.Step(
            WalAction.FlushCommit,
            s => s.Wal.Commit(),
            subject: client);
        await ctx.Step(
            WalAction.AckCommit,
            s => s.Exchange.Publish(client, Outcome.Committed),
            subject: client);
    }

    /// <summary>
    /// The page writer: wait for a committed dirty key, install one page, and
    /// loop. Once every page is installed and no client still depends on the log
    /// (the slot is empty), the same worker truncates it in one guarded atomic
    /// action, which lets the next client be admitted.
    /// </summary>
    private static async ModelTask PageWriterIteration(ModelContext<WalProcessState> ctx)
    {
        await ctx.When(
            "installable-or-truncatable",
            s => HasCommittedDirtyKey(s) || CanTruncate(s));

        var key = await ctx.Read("dirty-key", FirstDirtyKey);
        if (key >= 0)
        {
            await ctx.Step(WalAction.InstallData, s => s.Wal.Install(key), subject: key);
        }
        else
        {
            await ctx.Step(WalAction.TruncateLog, s => s.Wal.Truncate());
        }
    }

    /// <summary>
    /// The recovery worker, launched fresh at each restart. It captures the
    /// durable decision and the owed client as immutable frame locals
    /// <em>before</em> its state-changing step, then completes recovery in one
    /// atomic action that restores service: it rolls an uncommitted redo back, or
    /// leaves the durable commit in place, and publishes the reply owed to the
    /// client whose request outlived the crash. Because a crash kills the request
    /// handler, this fresh recovery replaces the killed handler as the reporter.
    ///
    /// <para>There is no recovery-only shared phase and no separate reporter: the
    /// decision and the owed client live entirely as scalar locals, which a crash
    /// discards and a restart recomputes. Completing recovery and publishing the
    /// reply as one atomic step is what keeps a fresh handler from ever observing
    /// a half-rolled-back aborted transaction — the same safety the old design
    /// bought with a recovery-only phase, here bought with atomicity instead. A
    /// crash before this step simply retries on the next restart; with nothing
    /// outstanding the server is marked running immediately.</para>
    /// </summary>
    private static async ModelTask RecoveryIteration(ModelContext<WalProcessState> ctx)
    {
        await ctx.When("recovering", s => s.Server.Mode == ServerMode.Recovering);

        // Analyze durable state in a nested call frame. A crash discards both
        // the recovery iteration and this helper, so the next restart recomputes
        // the decision from current durable state.
        var decision = await ctx.Call("analyze-recovery", AnalyzeRecovery);

        // Capture the owed client identity too, only when a report is owed.
        var owed = decision == RecoveryDecision.None
            ? default(ClientId)
            : await ctx.Read("owed-client", s => s.Exchange.Pending.Client);

        switch (decision)
        {
            case RecoveryDecision.None:
                // Nothing outstanding: recovery is done and the server is up.
                await ctx.Step(WalAction.Recover, s => s.Server.MarkRunning());
                break;

            case RecoveryDecision.Commit:
                // The durable commit is rolled forward and reported.
                await ctx.Step(WalAction.AckCommit, s =>
                {
                    s.Exchange.Publish(owed, Outcome.Committed);
                    s.Server.MarkRunning();
                }, subject: owed);
                break;

            case RecoveryDecision.Abort:
                // The uncommitted redo is rolled back and the abort reported.
                await ctx.Step(WalAction.AckAbort, s =>
                {
                    s.Wal.DiscardRedo();
                    s.Exchange.Publish(owed, Outcome.Aborted);
                    s.Server.MarkRunning();
                }, subject: owed);
                break;
        }
    }

    private static async ModelTask<RecoveryDecision> AnalyzeRecovery(
        ModelContext<WalProcessState> ctx)
        => await ctx.Read("decision", s =>
            s.Exchange.Pending == null ? RecoveryDecision.None
            : s.Wal.LogCommit ? RecoveryDecision.Commit
            : RecoveryDecision.Abort);
}
