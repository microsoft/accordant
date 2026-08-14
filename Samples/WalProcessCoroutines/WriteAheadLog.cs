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
/// </summary>
public enum ServerPhase { Down, Recovering, Idle, Active, Committed, Aborted }

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
    Reconnect,
    AckCommit,
    AckAbort,
    Recover
}

/// <summary>The stable checkpoint names used by the process workflows.</summary>
public static class Checkpoints
{
    public const string ChooseTxn = "txn";
    public const string Submit = "submit";
    public const string AppendRedo = "append-redo";
    public const string FlushCommit = "flush-commit";
    public const string InstallData = "install-data";
    public const string TruncateLog = "truncate-log";
    public const string Reconnect = "reconnect";
    public const string AckCommit = "ack-commit";
    public const string AckAbort = "ack-abort";
    public const string Recover = "recover";
    public const string DirtyKey = "dirty-key";

}

/// <summary>The stable process roles of the system.</summary>
public static class Roles
{
    public const string Client = "client";
    public const string Handler = "request-handler";
    public const string PageWriter = "page-writer";
    public const string Reporter = "reporter";
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
    /// Registers the process system:
    /// an external client, a guarded request-handler launch, a persistent
    /// page-writer, a persistent reporter and a persistent recovery worker in
    /// the server failure domain, and the crash/restart behavior.
    /// </summary>
    public static ProcessSystemModel<WalProcessState> Build(WalConfig config = null)
    {
        config ??= WalConfig.Default;

        var model = new ProcessSystemModel<WalProcessState>(InitialState(config))
            // The client is an independently active process outside the failure
            // domain: it survives every crash with its continuation intact.
            .AddProcess(Roles.Client, ctx => Client(ctx, config), serverDomain: false)

            // Persistent server-domain workers: discarded at a crash, relaunched
            // fresh at the next restart.
            .AddProcess(Roles.PageWriter, PageWriter, serverDomain: true)
            .AddProcess(Roles.Reporter, Reporter, serverDomain: true)
            .AddProcess(Roles.Recovery, Recovery, serverDomain: true)

            // A guarded request-handler launch in the server domain: at most one
            // handler per in-flight transaction, and never a duplicate while the
            // launch condition remains true.
            .On(
                Roles.Handler,
                guard: s => s.Server == ServerPhase.Active && s.Request != null && !s.LogRedo,
                workflow: Handler,
                serverDomain: true)

            .WithFailureDomain(new FailureDomain<WalProcessState>(
                // A crash may interleave between any two atomic checkpoints while
                // the server is up (including during recovery).
                crashEnabled: s => s.Server != ServerPhase.Down,
                onCrash: s => s.Server = ServerPhase.Down,
                restartEnabled: s => s.Server == ServerPhase.Down,
                onRestart: s => s.Server = ServerPhase.Recovering));

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
    /// The request handler, launched atomically once per in-flight transaction:
    /// append the whole redo record, then flush the commit record (the
    /// linearization point). There is no implementation abort; a precommit crash
    /// discards this continuation and recovery produces the abstract abort.
    /// </summary>
    private static async ModelTask Handler(ModelContext<WalProcessState> ctx)
    {
        await ctx.When(
            "appendable",
            s => s.Server == ServerPhase.Active && s.Request != null && !s.LogRedo);
        await ctx.Step(Checkpoints.AppendRedo, WalAction.AppendRedo, s =>
        {
            s.LogRedo = true;
            s.LogRecord = s.Request.Copy();
        });

        await ctx.When(
            "flushable",
            s => s.Server == ServerPhase.Active && s.LogRedo && !s.LogCommit);
        await ctx.Step(Checkpoints.FlushCommit, WalAction.FlushCommit, s =>
        {
            s.LogCommit = true;
            s.Server = ServerPhase.Committed;
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
    /// The reporter: wait until the waiting client's transaction is decided,
    /// then deliver the outcome. A committed transaction is reported directly; an
    /// aborted one is reconnected (server goes to <see cref="ServerPhase.Aborted"/>)
    /// and then reported.
    /// </summary>
    private static async ModelTask Reporter(ModelContext<WalProcessState> ctx)
    {
        while (true)
        {
            await ctx.Loop("reporter-loop");

            await ctx.When("reportable", s =>
                s.Client == ClientPhase.Waiting &&
                (s.Server == ServerPhase.Committed ||
                    s.Server == ServerPhase.Aborted ||
                    (s.Server == ServerPhase.Idle && !s.LogCommit)));

            var phase = await ctx.Read("server-phase", s => s.Server);
            if (phase == ServerPhase.Committed)
            {
                await ctx.Step(Checkpoints.AckCommit, WalAction.AckCommit, s =>
                {
                    s.Client = ClientPhase.Idle;
                    s.Request = null;
                    s.Reported = Outcome.Committed;
                });
            }
            else if (phase == ServerPhase.Aborted)
            {
                await ctx.Step(Checkpoints.AckAbort, WalAction.AckAbort, s =>
                {
                    s.Client = ClientPhase.Idle;
                    s.Request = null;
                    s.Reported = Outcome.Aborted;
                    s.Server = ServerPhase.Idle;
                });
            }
            else
            {
                // Server idle after recovery with no commit record: reconnect,
                // then the next iteration reports the abort.
                await ctx.Step(
                    Checkpoints.Reconnect,
                    WalAction.Reconnect,
                    s => s.Server = ServerPhase.Aborted);
            }
        }
    }

    /// <summary>
    /// The recovery worker, launched fresh at each restart. It reads durable
    /// state only: a durable commit record is rolled forward (the write-back is
    /// left to the page writer), and its absence discards the redo record and
    /// returns the server to idle.
    /// </summary>
    private static async ModelTask Recovery(ModelContext<WalProcessState> ctx)
    {
        while (true)
        {
            await ctx.Loop("recovery-loop");

            await ctx.When("recovering", s => s.Server == ServerPhase.Recovering);

            await ctx.Step(Checkpoints.Recover, WalAction.Recover, s =>
            {
                if (s.LogCommit)
                {
                    s.Server = ServerPhase.Committed;
                }
                else
                {
                    s.LogRedo = false;
                    s.LogRecord = null;
                    s.Server = ServerPhase.Idle;
                }
            });
        }
    }
}
