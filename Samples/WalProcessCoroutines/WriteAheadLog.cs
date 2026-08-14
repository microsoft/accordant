// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// The volatile phase of the server process, all of it lost at a crash.
/// <see cref="Active"/> is the server holding the client's write in memory:
/// the outcome is in doubt, and a crash there dooms the transaction.
/// <see cref="RecoveredCommit"/> and <see cref="RecoveredAbort"/> are phases only
/// the recovery worker ever produces — a durable outcome it has determined and
/// owes to a still-waiting client — so a fresh handler's ordinary
/// <see cref="Committed"/> can never be mistaken for one recovery must report.
/// </summary>
public enum ServerPhase { Down, Recovering, Idle, Active, Committed, Aborted, RecoveredCommit, RecoveredAbort }

/// <summary>
/// The client is an external process outside the failure domain: it outlives
/// every crash and remembers what it asked for and what it was told.
/// </summary>
public enum ClientPhase { Idle, Waiting }

/// <summary>
/// The semantic action each process step performs, kept separate from the
/// process role and from the generated runtime step-function id.
/// </summary>
public enum WalAction
{
    Submit,
    AppendRedo,
    FlushCommit,
    InstallData,
    TruncateLog,
    AckCommit,
    AckAbort,
    Recover
}

/// <summary>The stable checkpoint names used by the process workflows.</summary>
public static class Checkpoints
{
    public const string ChooseTxn = "txn";
    public const string Submit = "submit";
    public const string RequestName = "request-name";
    public const string AppendRedo = "append-redo";
    public const string FlushCommit = "flush-commit";
    public const string InstallData = "install-data";
    public const string TruncateLog = "truncate-log";
    public const string AckCommit = "ack-commit";
    public const string AckAbort = "ack-abort";
    public const string Recover = "recover";
    public const string RecoveredDecision = "recovered-decision";
    public const string DirtyKey = "dirty-key";

}

/// <summary>The stable process roles of the system.</summary>
public static class Roles
{
    public const string Client = "client";
    public const string Handler = "request-handler";
    public const string PageWriter = "page-writer";
    public const string Recovery = "recovery";
}

/// <summary>
/// The implementation state: a single-node store with a write-ahead log.
/// Durable <see cref="Data"/>, <see cref="LogRedo"/>, <see cref="LogRecord"/>
/// and <see cref="LogCommit"/> survive a crash; the volatile <see cref="Server"/>
/// phase is lost; the client fields belong to an external process and outlive
/// every crash. Process continuations are <em>not</em> here — they live in the
/// scheduler configuration, so refinement's state semantics see only this
/// domain state.
/// </summary>
[State]
public partial class WalProcessState
{
    /// <summary>The durable data page of each key.</summary>
    public int[] Data { get; set; }

    /// <summary>Whether the redo record of the in-flight transaction is durable.</summary>
    public bool LogRedo { get; set; }

    /// <summary>The payload of the durable redo record: the whole write set, or <c>null</c>.</summary>
    public WriteSet LogRecord { get; set; }

    /// <summary>Whether the commit record is durable. This is the linearization point.</summary>
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

/// <summary>
/// The process-oriented write-ahead log: a set of independently active replay
/// coroutines whose atomic steps drive the same durable/volatile/client state
/// the hand-written WAL sample uses, plus an explicit failure domain whose crash
/// discards the server-domain process continuations.
/// </summary>
public static class WriteAheadLog
{
    // ---- durable recovery function ---------------------------------------

    /// <summary>The value recovery would install for <paramref name="key"/> from durable state.</summary>
    public static int Recovered(WalProcessState wal, int key)
        => wal.LogCommit ? wal.LogRecord[key] : wal.Data[key];

    /// <summary>The whole store recovery would install right now.</summary>
    public static int[] RecoveredSnapshot(WalProcessState wal)
    {
        var values = new int[wal.Data.Length];
        for (var key = 0; key < values.Length; key++)
        {
            values[key] = Recovered(wal, key);
        }

        return values;
    }

    /// <summary>Whether every durable data page already holds the logged write set.</summary>
    public static bool FullyInstalled(WalProcessState wal)
        => wal.LogRecord != null && wal.LogRecord.Matches(wal.Data);

    private static int FirstDirtyKey(WalProcessState wal)
    {
        if (wal.LogRecord == null)
        {
            return -1;
        }

        for (var key = 0; key < wal.Data.Length; key++)
        {
            if (wal.Data[key] != wal.LogRecord[key])
            {
                return key;
            }
        }

        return -1;
    }

    private static bool HasCommittedDirtyKey(WalProcessState wal)
        => wal.Server == ServerPhase.Committed && wal.LogCommit && FirstDirtyKey(wal) >= 0;

    private static bool CanTruncate(WalProcessState wal)
        => wal.Server == ServerPhase.Committed &&
            wal.LogCommit &&
            FullyInstalled(wal) &&
            wal.Client == ClientPhase.Idle;

    // ---- the initial state -----------------------------------------------

    /// <summary>The initial state: empty log, nothing in flight, server idle.</summary>
    public static WalProcessState InitialState(WalConfig config)
        => new WalProcessState
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

    // ---- the composition root --------------------------------------------

    /// <summary>
    /// Registers the process system structurally: an external client outside
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
        // it clears the volatile server phase and discards every continuation
        // registered through this domain object.
        var server = model.FailureDomain(
            "server",
            crashEnabled: s => s.Server != ServerPhase.Down,
            onCrash: s => s.Server = ServerPhase.Down,
            restartEnabled: s => s.Server == ServerPhase.Down,
            onRestart: s => s.Server = ServerPhase.Recovering);

        // The client is an independently active process outside the failure
        // domain: it survives every crash with its continuation intact.
        model.Process(Roles.Client, ctx => Client(ctx, config));

        // Persistent server-domain workers: discarded at a crash, relaunched
        // fresh at the next restart.
        server.Process(Roles.PageWriter, PageWriter);
        server.Process(Roles.Recovery, Recovery);

        // A guarded request-handler launch in the server domain: at most one
        // handler per in-flight transaction, and never a duplicate while the
        // launch condition remains true. A crash discards it; recovery — not an
        // automatic relaunch — replaces it.
        server.On(
            Roles.Handler,
            guard: s => s.Server == ServerPhase.Active && s.Request != null && !s.LogRedo,
            workflow: ctx => Handler(ctx, config));

        return model;
    }

    /// <summary>Explores the compiled process-system graph.</summary>
    public static StateGraphNode Explore(WalConfig config = null, int maxDepth = -1, bool lazy = false)
        => Build(config).Explore(maxDepth, lazy);

    // ---- the processes ---------------------------------------------------

    /// <summary>
    /// The external client: choose a finite transaction, wait for the server to
    /// be ready, submit it atomically, wait for the reported outcome, and loop.
    /// </summary>
    private static async ModelTask Client(ModelContext<WalProcessState> ctx, WalConfig config)
    {
        while (true)
        {
            await ctx.Loop("client-loop");

            var name = await ctx.Choose(Checkpoints.ChooseTxn, config.TransactionNames);

            await ctx.When(
                "server-ready",
                s => s.Server == ServerPhase.Idle && s.Client == ClientPhase.Idle);

            await ctx.Step(Checkpoints.Submit, WalAction.Submit, s =>
            {
                s.Client = ClientPhase.Waiting;
                s.Request = config.Find(name).Copy();
                s.Reported = Outcome.None;
                s.Server = ServerPhase.Active;
            }, subject: name);

            // Wait for the reported outcome, then observe/reset and loop.
            await ctx.When("outcome-reported", s => s.Client == ClientPhase.Idle);
        }
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
        // Capture the request identity once, from an intentional local snapshot,
        // and resolve the immutable configured write set. WriteSet is nested
        // mutable state, so the handler carries the scalar name and re-resolves
        // it through config rather than retaining a mutable closure.
        var requestName = await ctx.Read(Checkpoints.RequestName, s => s.Request.Name);
        var request = config.Find(requestName);

        await ctx.Step(Checkpoints.AppendRedo, WalAction.AppendRedo, s =>
        {
            s.LogRedo = true;
            s.LogRecord = request.Copy();
        });

        await ctx.Step(Checkpoints.FlushCommit, WalAction.FlushCommit, s =>
        {
            s.LogCommit = true;
            s.Server = ServerPhase.Committed;
        });

        await ctx.Step(Checkpoints.AckCommit, WalAction.AckCommit, s =>
        {
            s.Client = ClientPhase.Idle;
            s.Request = null;
            s.Reported = Outcome.Committed;
        });
    }

    /// <summary>
    /// The page writer: wait for a committed dirty key, install one page, and
    /// loop. Once every page is installed and the client no longer depends on
    /// the log, the same worker truncates it in one guarded atomic action.
    /// </summary>
    private static async ModelTask PageWriter(ModelContext<WalProcessState> ctx)
    {
        while (true)
        {
            await ctx.Loop("page-writer-loop");

            await ctx.When(
                "installable-or-truncatable",
                s => HasCommittedDirtyKey(s) || CanTruncate(s));

            var key = await ctx.Read(Checkpoints.DirtyKey, FirstDirtyKey);
            if (key >= 0)
            {
                await ctx.Step(
                    Checkpoints.InstallData,
                    WalAction.InstallData,
                    s => s.Data[key] = s.LogRecord[key],
                    subject: key);
            }
            else
            {
                await ctx.Step(Checkpoints.TruncateLog, WalAction.TruncateLog, s =>
                {
                    s.LogRedo = false;
                    s.LogCommit = false;
                    s.LogRecord = null;
                    s.Server = ServerPhase.Idle;
                });
            }
        }
    }

    /// <summary>
    /// The recovery worker, launched fresh at each restart. It genuinely waits
    /// for the server to be recovering, then reads durable state only: a durable
    /// commit record is rolled forward (the write-back is left to the page
    /// writer), and its absence discards the redo record. Because a crash kills
    /// the request handler, this fresh recovery is also the one that reports the
    /// outcome to a client whose request outlived that crash — but only if the
    /// killed handler never reported it.
    ///
    /// <para>The report owed to a still-waiting client is captured atomically at
    /// recover time as a recovery-only phase (<see cref="ServerPhase.RecoveredCommit"/>
    /// or <see cref="ServerPhase.RecoveredAbort"/>). No other process ever
    /// produces those phases, so recovery can never mistake a fresh handler's
    /// ordinary commit for one it must report — the handler and recovery stay
    /// strictly alternative reporters. An already-idle client (reported before
    /// the crash, or no transaction at all) leaves the server in an ordinary
    /// phase, and recovery simply loops back to wait for the next restart.</para>
    /// </summary>
    private static async ModelTask Recovery(ModelContext<WalProcessState> ctx)
    {
        while (true)
        {
            await ctx.Loop("recovery-loop");

            await ctx.When("recovering", s => s.Server == ServerPhase.Recovering);

            await ctx.Step(Checkpoints.Recover, WalAction.Recover, s =>
            {
                var waiting = s.Client == ClientPhase.Waiting;
                if (s.LogCommit)
                {
                    // Roll the durable commit forward. A report is owed only to a
                    // client still waiting for its killed handler.
                    s.Server = waiting ? ServerPhase.RecoveredCommit : ServerPhase.Committed;
                }
                else
                {
                    s.LogRedo = false;
                    s.LogRecord = null;
                    s.Server = waiting ? ServerPhase.RecoveredAbort : ServerPhase.Idle;
                }
            });

            // Report the durable decision to the waiting client, replacing the
            // handler the crash destroyed. Only the recovery-only phases trigger
            // a report; anything else means there is nothing for recovery to say.
            var decision = await ctx.Read(Checkpoints.RecoveredDecision, s => s.Server);

            if (decision == ServerPhase.RecoveredCommit)
            {
                await ctx.Step(Checkpoints.AckCommit, WalAction.AckCommit, s =>
                {
                    s.Client = ClientPhase.Idle;
                    s.Request = null;
                    s.Reported = Outcome.Committed;
                    s.Server = ServerPhase.Committed;
                });
            }
            else if (decision == ServerPhase.RecoveredAbort)
            {
                await ctx.Step(Checkpoints.AckAbort, WalAction.AckAbort, s =>
                {
                    s.Client = ClientPhase.Idle;
                    s.Request = null;
                    s.Reported = Outcome.Aborted;
                    s.Server = ServerPhase.Idle;
                });
            }
        }
    }
}
