// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;

/// <summary>
/// The kind of scheduler edge, distinguishing an ordinary coroutine checkpoint
/// from the failure-domain and launch control transitions the scheduler owns.
/// </summary>
public enum ProcessControlKind
{
    /// <summary>An ordinary coroutine checkpoint (<see cref="ProcessTransition.Checkpoint"/> is set).</summary>
    None,

    /// <summary>A guarded launch that added a fresh process to the live set.</summary>
    Launch,

    /// <summary>
    /// A failure-domain crash: it cleared volatile domain state and discarded
    /// every server-domain process continuation.
    /// </summary>
    Crash,

    /// <summary>A restart that relaunched the persistent server-domain workers.</summary>
    Restart,

    /// <summary>A process reached completion and left the live set.</summary>
    Completion
}

/// <summary>
/// The typed metadata attached to every edge of a compiled process-system
/// graph. It carries the identity of the process that acted (its stable role,
/// separate from any generated runtime id), whether that process belongs to the
/// server failure domain, and either the underlying
/// <see cref="CoroutineTransition"/> for a coroutine checkpoint or the
/// <see cref="ProcessControlKind"/> for a scheduler control transition.
/// </summary>
public sealed class ProcessTransition
{
    internal ProcessTransition(
        string processRole,
        bool serverDomain,
        ProcessControlKind control,
        CoroutineTransition checkpoint)
    {
        ProcessRole = processRole;
        ServerDomain = serverDomain;
        Control = control;
        Checkpoint = checkpoint;
    }

    /// <summary>The stable role of the process that took the transition.</summary>
    public string ProcessRole { get; }

    /// <summary>Whether the acting process belongs to the server failure domain.</summary>
    public bool ServerDomain { get; }

    /// <summary>The control kind, or <see cref="ProcessControlKind.None"/> for a coroutine checkpoint.</summary>
    public ProcessControlKind Control { get; }

    /// <summary>The coroutine checkpoint metadata, or null for a control transition.</summary>
    public CoroutineTransition Checkpoint { get; }

    /// <summary>Whether this edge is a scheduler control transition rather than a coroutine checkpoint.</summary>
    public bool IsControl => Control != ProcessControlKind.None;

    /// <summary>The checkpoint kind, or null for a control transition.</summary>
    public ModelCheckpointKind? CheckpointKind => Checkpoint?.Kind;

    /// <summary>The stable checkpoint name, or null for a control transition.</summary>
    public string CheckpointName => Checkpoint?.CheckpointName;

    /// <summary>The selected Choose value, or null.</summary>
    public object Value => Checkpoint?.Value;

    /// <summary>The typed semantic action tag supplied by a Step, or null.</summary>
    public object SemanticAction => Checkpoint?.SemanticAction;

    /// <summary>The optional immutable action subject supplied by a Step.</summary>
    public object Subject => Checkpoint?.Subject;

    /// <summary>The replay prefix of the underlying checkpoint, or null.</summary>
    public IReadOnlyList<ReplayEntry> ReplayPrefix => Checkpoint?.ReplayPrefix;

    /// <inheritdoc/>
    public override string ToString()
        => IsControl
            ? $"{Control.ToString().ToLowerInvariant()}:{ProcessRole}"
            : $"{ProcessRole}:{Checkpoint}";
}

/// <summary>A snapshot of one live process in a scheduler configuration.</summary>
public sealed class ProcessInstance
{
    internal ProcessInstance(string role, bool serverDomain, string continuationId)
    {
        Role = role;
        ServerDomain = serverDomain;
        ContinuationId = continuationId;
    }

    /// <summary>The stable process role.</summary>
    public string Role { get; }

    /// <summary>Whether the process belongs to the server failure domain.</summary>
    public bool ServerDomain { get; }

    /// <summary>An opaque identity of the process's serialized continuation (replay tape).</summary>
    public string ContinuationId { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Role}#{ContinuationId}";
}

/// <summary>
/// The live processes of a scheduler node. Because process control is part of
/// graph-node configuration and never part of the domain <see cref="IState"/>,
/// tests and diagnostics read continuations here rather than from the state.
/// </summary>
public interface IProcessSchedulerStep : IStepFunction
{
    /// <summary>The processes live in this configuration.</summary>
    IReadOnlyList<ProcessInstance> LiveProcesses { get; }
}

/// <summary>
/// Describes a failure domain: when a crash may occur, how it clears volatile
/// domain state, and when and how a restart re-establishes the server. A crash
/// additionally discards every server-domain process continuation, and a
/// restart relaunches the persistent server-domain workers; those are handled
/// by the scheduler and need not be spelled out here.
/// </summary>
public sealed class FailureDomain<TState>
    where TState : State
{
    /// <summary>Creates a failure domain.</summary>
    public FailureDomain(
        Func<TState, bool> crashEnabled,
        Action<TState> onCrash,
        Func<TState, bool> restartEnabled,
        Action<TState> onRestart)
    {
        CrashEnabled = crashEnabled ?? throw new ArgumentNullException(nameof(crashEnabled));
        OnCrash = onCrash ?? throw new ArgumentNullException(nameof(onCrash));
        RestartEnabled = restartEnabled ?? throw new ArgumentNullException(nameof(restartEnabled));
        OnRestart = onRestart ?? throw new ArgumentNullException(nameof(onRestart));
    }

    internal Func<TState, bool> CrashEnabled { get; }
    internal Action<TState> OnCrash { get; }
    internal Func<TState, bool> RestartEnabled { get; }
    internal Action<TState> OnRestart { get; }
}

internal sealed class ProcessSpec<TState>
    where TState : State
{
    internal ProcessSpec(
        string role,
        Func<ModelContext<TState>, ModelTask> workflow,
        bool serverDomain,
        CoroutineOptions options)
    {
        Role = role;
        Workflow = workflow;
        ServerDomain = serverDomain;
        Options = options;
    }

    internal string Role { get; }
    internal Func<ModelContext<TState>, ModelTask> Workflow { get; }
    internal bool ServerDomain { get; }
    internal CoroutineOptions Options { get; }
}

internal sealed class LaunchSpec<TState>
    where TState : State
{
    internal LaunchSpec(
        string role,
        Func<TState, bool> guard,
        Func<ModelContext<TState>, ModelTask> workflow,
        bool serverDomain,
        CoroutineOptions options)
    {
        Role = role;
        Guard = guard;
        Workflow = workflow;
        ServerDomain = serverDomain;
        Options = options;
    }

    internal string Role { get; }
    internal Func<TState, bool> Guard { get; }
    internal Func<ModelContext<TState>, ModelTask> Workflow { get; }
    internal bool ServerDomain { get; }
    internal CoroutineOptions Options { get; }
}

/// <summary>
/// The composition root of an experimental process system. It registers a set
/// of independently active replay-coroutine processes, optional guarded process
/// launches, and an optional server failure domain, and compiles the whole
/// system to an ordinary Accordant state graph.
///
/// <para>Each process is a replay coroutine whose serialized continuation lives
/// in scheduler-node configuration, never in the domain <typeparamref name="TState"/>,
/// so refinement's state semantics see only domain state. The scheduler advances
/// every live process against the <em>current</em> shared state at each step, so
/// a process is a function of the state it is applied to and guarded waits are
/// re-evaluated live after an interleaving.</para>
/// </summary>
public sealed class ProcessSystemModel<TState>
    where TState : State
{
    private readonly TState initialState;
    private readonly List<ProcessSpec<TState>> processes = new List<ProcessSpec<TState>>();
    private readonly List<LaunchSpec<TState>> launches = new List<LaunchSpec<TState>>();
    private readonly bool verifyDeterminism;
    private readonly int maxInternalCheckpoints;
    private FailureDomain<TState> failureDomain;

    /// <summary>Creates a process system rooted at <paramref name="initialState"/>.</summary>
    public ProcessSystemModel(
        TState initialState,
        bool verifyDeterminism = false,
        int maxInternalCheckpoints = 10000)
    {
        this.initialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        this.verifyDeterminism = verifyDeterminism;
        this.maxInternalCheckpoints = maxInternalCheckpoints;
    }

    /// <summary>
    /// Registers an independently active process. It is live from the start.
    /// A server-domain process is discarded at a crash and relaunched fresh at
    /// the next restart; a non-server-domain process (for example an external
    /// client) survives every crash.
    /// </summary>
    public ProcessSystemModel<TState> AddProcess(
        string role,
        Func<ModelContext<TState>, ModelTask> workflow,
        bool serverDomain = false)
    {
        RequireRole(role);
        if (workflow == null) throw new ArgumentNullException(nameof(workflow));
        processes.Add(new ProcessSpec<TState>(role, workflow, serverDomain, OptionsFor(role, workflow)));
        return this;
    }

    /// <summary>
    /// Registers a guarded launch. When <paramref name="guard"/> holds and no
    /// instance of <paramref name="role"/> is currently live, the scheduler adds
    /// a fresh instance in one atomic transition. The no-duplicate rule uses
    /// control state the scheduler owns, so a launch cannot fire unboundedly
    /// while its guard remains true.
    /// </summary>
    public ProcessSystemModel<TState> On(
        string role,
        Func<TState, bool> guard,
        Func<ModelContext<TState>, ModelTask> workflow,
        bool serverDomain = true)
    {
        RequireRole(role);
        if (guard == null) throw new ArgumentNullException(nameof(guard));
        if (workflow == null) throw new ArgumentNullException(nameof(workflow));
        launches.Add(new LaunchSpec<TState>(role, guard, workflow, serverDomain, OptionsFor(role, workflow)));
        return this;
    }

    /// <summary>Registers the server failure domain.</summary>
    public ProcessSystemModel<TState> WithFailureDomain(FailureDomain<TState> domain)
    {
        failureDomain = domain ?? throw new ArgumentNullException(nameof(domain));
        return this;
    }

    /// <summary>Compiles the system to an ordinary state graph.</summary>
    public StateGraphNode Explore(int maxDepth = -1, bool lazy = false)
    {
        var uniqueRoles = processes.Select(p => p.Role)
            .Concat(launches.Select(l => l.Role))
            .GroupBy(role => role, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (uniqueRoles != null)
        {
            throw new ModelDefinitionException(
                $"Process role '{uniqueRoles.Key}' is registered more than once. " +
                "Each process identity must be unique and stable.");
        }

        initialState.Freeze();
        var configuration = new ProcessConfiguration<TState>(processes, launches, failureDomain);
        var live = processes
            .Select(spec => new LiveProcess(spec.Role, new ReplayTape()))
            .OrderBy(p => p.Role, StringComparer.Ordinal)
            .ToList();

        var scheduler = new ProcessSchedulerStep<TState>(configuration, live);
        return StateGraph.ExploreStateGraph(
            new IStepFunction[] { scheduler },
            initialState,
            maxDepth: maxDepth,
            lazy: lazy);
    }

    private CoroutineOptions OptionsFor(string role, Func<ModelContext<TState>, ModelTask> workflow)
        => new CoroutineOptions(
            role,
            verifyDeterminism,
            maxInternalCheckpoints,
            CapturedInputMonitor.Create(workflow),
            allowGuardedWaits: true);

    private static void RequireRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("A process requires a stable role.", nameof(role));
        }
    }
}

/// <summary>One live process: its stable role and its serialized continuation.</summary>
internal sealed class LiveProcess
{
    internal LiveProcess(string role, ReplayTape tape)
    {
        Role = role;
        Tape = tape;
    }

    internal string Role { get; }
    internal ReplayTape Tape { get; }
}

/// <summary>The immutable registration a scheduler shares across all of its nodes.</summary>
internal sealed class ProcessConfiguration<TState>
    where TState : State
{
    private readonly Dictionary<string, ProcessSpec<TState>> processByRole;
    private readonly Dictionary<string, LaunchSpec<TState>> launchByRole;

    internal ProcessConfiguration(
        IReadOnlyList<ProcessSpec<TState>> processes,
        IReadOnlyList<LaunchSpec<TState>> launches,
        FailureDomain<TState> failureDomain)
    {
        Processes = processes;
        Launches = launches;
        FailureDomain = failureDomain;
        processByRole = processes.ToDictionary(p => p.Role, StringComparer.Ordinal);
        launchByRole = launches.ToDictionary(l => l.Role, StringComparer.Ordinal);
    }

    internal IReadOnlyList<ProcessSpec<TState>> Processes { get; }
    internal IReadOnlyList<LaunchSpec<TState>> Launches { get; }
    internal FailureDomain<TState> FailureDomain { get; }

    internal Func<ModelContext<TState>, ModelTask> WorkflowFor(string role)
        => processByRole.TryGetValue(role, out var process)
            ? process.Workflow
            : launchByRole[role].Workflow;

    internal CoroutineOptions OptionsFor(string role)
        => processByRole.TryGetValue(role, out var process)
            ? process.Options
            : launchByRole[role].Options;

    internal bool ServerDomainRole(string role)
        => processByRole.TryGetValue(role, out var process)
            ? process.ServerDomain
            : launchByRole[role].ServerDomain;
}

/// <summary>
/// The single active step function of a compiled process system. It owns the
/// whole set of live process continuations, which is what lets a crash discard
/// every server-domain continuation atomically instead of leaking disabled
/// steps into the graph.
/// </summary>
internal sealed class ProcessSchedulerStep<TState> : BaseStepFunction, IProcessSchedulerStep
    where TState : State
{
    private readonly ProcessConfiguration<TState> configuration;
    private readonly IReadOnlyList<LiveProcess> live;
    private readonly string id;

    internal ProcessSchedulerStep(
        ProcessConfiguration<TState> configuration,
        IReadOnlyList<LiveProcess> live)
    {
        this.configuration = configuration;
        this.live = live;
        id = "process-system#" + Identifiers.Join(
            live.SelectMany(p => new[] { p.Role, p.Tape.ContinuationIdentity }).ToArray());
    }

    public override string StepFunctionId => id;

    public IReadOnlyList<ProcessInstance> LiveProcesses
        => live
            .Select(p => new ProcessInstance(
                p.Role,
                configuration.ServerDomainRole(p.Role),
                p.Tape.ContinuationIdentity))
            .ToList();

    protected override IList<StepResult> ApplyInternal(IState source)
    {
        var state = (TState)source;
        var results = new List<StepResult>();

        // 1. Every live process advances against the current shared state.
        foreach (var process in live)
        {
            AdvanceProcess(state, process, results);
        }

        // 2. Guarded launches add a fresh process when enabled and not already live.
        foreach (var launch in configuration.Launches)
        {
            if (launch.Guard(state) && !live.Any(p => p.Role == launch.Role))
            {
                var next = live
                    .Concat(new[] { new LiveProcess(launch.Role, new ReplayTape()) })
                    .ToList();
                results.Add(Control(state, next, ProcessControlKind.Launch, launch.Role, launch.ServerDomain));
            }
        }

        // 3. The failure domain: crash discards every server-domain continuation.
        var domain = configuration.FailureDomain;
        if (domain != null && domain.CrashEnabled(state))
        {
            var crashed = (TState)state.Clone();
            domain.OnCrash(crashed);
            crashed.Freeze();
            var survivors = live
                .Where(p => !configuration.ServerDomainRole(p.Role))
                .ToList();
            results.Add(Control(crashed, survivors, ProcessControlKind.Crash, null, serverDomain: true));
        }

        // 4. Restart relaunches the persistent server-domain workers fresh.
        if (domain != null && domain.RestartEnabled(state))
        {
            var restarted = (TState)state.Clone();
            domain.OnRestart(restarted);
            restarted.Freeze();
            var relaunched = live.ToList();
            foreach (var spec in configuration.Processes)
            {
                if (spec.ServerDomain && relaunched.All(p => p.Role != spec.Role))
                {
                    relaunched.Add(new LiveProcess(spec.Role, new ReplayTape()));
                }
            }

            results.Add(Control(restarted, relaunched, ProcessControlKind.Restart, null, serverDomain: true));
        }

        return results;
    }

    private void AdvanceProcess(TState state, LiveProcess process, List<StepResult> results)
    {
        var workflow = configuration.WorkflowFor(process.Role);
        var options = configuration.OptionsFor(process.Role);
        var advance = CoroutineRunner.Advance(workflow, state, process.Tape, options);

        if (advance.Blocked)
        {
            return;
        }

        if (advance.Pending == null)
        {
            // The process completed. Drop it in one state-neutral control edge.
            results.Add(Control(
                state,
                WithoutRole(process.Role),
                ProcessControlKind.Completion,
                process.Role,
                configuration.ServerDomainRole(process.Role)));
            return;
        }

        var pending = advance.Pending;
        var checkpointPrefix = advance.Tape.Entries;

        if (pending.Kind == ModelCheckpointKind.Choose)
        {
            foreach (var choice in (object[])pending.Value)
            {
                var nextTape = advance.Tape.Append(ModelCheckpointKind.Choose, pending.Name, choice);
                var committed = Commit(process.Role, workflow, options, nextTape, state);
                results.Add(Coroutine(
                    state,
                    Replace(process.Role, committed),
                    process.Role,
                    new CoroutineTransition(
                        process.Role,
                        pending.Kind,
                        pending.Name,
                        choice,
                        checkpointPrefix,
                        pending.SemanticAction,
                        pending.Subject)));
            }

            return;
        }

        if (pending.Kind == ModelCheckpointKind.Step)
        {
            var next = (TState)state.Clone();
            ((Action<TState>)pending.Action)(next);
            next.Freeze();
            var nextTape = advance.Tape.Append(ModelCheckpointKind.Step, pending.Name, ModelUnitValue.Instance);
            var committed = Commit(process.Role, workflow, options, nextTape, next);
            results.Add(Coroutine(
                next,
                Replace(process.Role, committed),
                process.Role,
                new CoroutineTransition(
                    process.Role,
                    pending.Kind,
                    pending.Name,
                    null,
                    checkpointPrefix,
                    pending.SemanticAction,
                    pending.Subject)));
            return;
        }

        throw new ModelDefinitionException(
            $"Process '{process.Role}' reached an unsupported visible checkpoint kind '{pending.Kind}'.");
    }

    /// <summary>
    /// Decides whether a process still exists after taking a visible checkpoint.
    /// Advancing the committed tape against the resulting state reveals whether
    /// the process continues (or blocks) or falls off the end of its workflow;
    /// in the latter case it is folded away with the same edge.
    /// </summary>
    private LiveProcess Commit(
        string role,
        Func<ModelContext<TState>, ModelTask> workflow,
        CoroutineOptions options,
        ReplayTape tape,
        TState resultingState)
    {
        var peek = CoroutineRunner.Advance(workflow, resultingState, tape, options);
        return peek.Pending == null && !peek.Blocked
            ? null
            : new LiveProcess(role, tape);
    }

    private List<LiveProcess> Replace(string role, LiveProcess replacement)
    {
        var next = live.Where(p => p.Role != role).ToList();
        if (replacement != null)
        {
            next.Add(replacement);
        }

        return next;
    }

    private List<LiveProcess> WithoutRole(string role)
        => live.Where(p => p.Role != role).ToList();

    private StepResult Coroutine(
        TState state,
        List<LiveProcess> nextLive,
        string role,
        CoroutineTransition checkpoint)
        => Emit(
            state,
            nextLive,
            new ProcessTransition(
                role,
                configuration.ServerDomainRole(role),
                ProcessControlKind.None,
                checkpoint));

    private StepResult Control(
        TState state,
        List<LiveProcess> nextLive,
        ProcessControlKind control,
        string role,
        bool serverDomain)
        => Emit(
            state,
            nextLive,
            new ProcessTransition(role, serverDomain, control, checkpoint: null));

    private StepResult Emit(TState state, List<LiveProcess> nextLive, ProcessTransition transition)
    {
        var ordered = nextLive
            .OrderBy(p => p.Role, StringComparer.Ordinal)
            .ToList();
        return new StepResult
        {
            State = state,
            StepFunctions = new IStepFunction[]
            {
                new ProcessSchedulerStep<TState>(configuration, ordered)
            },
            EdgeMetadata = transition
        };
    }
}
