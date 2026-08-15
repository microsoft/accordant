// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;

/// <summary>
/// The kind of scheduler edge, distinguishing an ordinary model-workflow checkpoint
/// from the failure-domain and launch control transitions the scheduler owns.
/// </summary>
public enum ProcessControlKind
{
    /// <summary>An ordinary model-workflow checkpoint (<see cref="ProcessTransition.Checkpoint"/> is set).</summary>
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
/// separate from any generated runtime id), the name of the failure domain that
/// owns that process (or null when the process is outside every domain), and
/// either the underlying <see cref="ProcessCheckpointTransition"/> for a workflow
/// checkpoint or the <see cref="ProcessControlKind"/> for a scheduler control
/// transition. A crash or restart edge names the failure domain it belongs to.
/// </summary>
public sealed class ProcessTransition
{
    internal ProcessTransition(
        string processRole,
        string domain,
        ProcessControlKind control,
        ProcessCheckpointTransition checkpoint)
    {
        ProcessRole = processRole;
        Domain = domain;
        Control = control;
        Checkpoint = checkpoint;
    }

    /// <summary>The stable role of the process that took the transition.</summary>
    public string ProcessRole { get; }

    /// <summary>
    /// The name of the failure domain that owns the acting process (or, for a
    /// crash/restart, the domain the control transition belongs to), or null
    /// when the process is outside every failure domain.
    /// </summary>
    public string Domain { get; }

    /// <summary>Whether the acting process belongs to a failure domain.</summary>
    public bool InFailureDomain => Domain != null;

    /// <summary>The control kind, or <see cref="ProcessControlKind.None"/> for a workflow checkpoint.</summary>
    public ProcessControlKind Control { get; }

    /// <summary>The model-workflow checkpoint metadata, or null for a control transition.</summary>
    public ProcessCheckpointTransition Checkpoint { get; }

    /// <summary>Whether this edge is a scheduler control transition rather than a workflow checkpoint.</summary>
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

    /// <summary>The frame-local checkpoint history before the transition, or null.</summary>
    public IReadOnlyList<ProcessCheckpointRecord> CheckpointHistory => Checkpoint?.CheckpointHistory;

    /// <inheritdoc/>
    public override string ToString()
        => IsControl
            ? $"{Control.ToString().ToLowerInvariant()}:{ProcessRole}"
            : $"{ProcessRole}:{Checkpoint}";
}

/// <summary>A snapshot of one live process in a scheduler configuration.</summary>
public sealed class ProcessInstance
{
    internal ProcessInstance(
        string role,
        string domain,
        string continuationId,
        IReadOnlyList<ProcessContinuationFrame> frames)
    {
        Role = role;
        Domain = domain;
        ContinuationId = continuationId;
        Frames = frames;
    }

    /// <summary>The stable process role.</summary>
    public string Role { get; }

    /// <summary>The name of the failure domain that owns the process, or null.</summary>
    public string Domain { get; }

    /// <summary>Whether the process belongs to a failure domain.</summary>
    public bool InFailureDomain => Domain != null;

    /// <summary>An opaque identity of the process's complete structured continuation.</summary>
    public string ContinuationId { get; }

    /// <summary>The structured root/call/iteration frames of this continuation.</summary>
    public IReadOnlyList<ProcessContinuationFrame> Frames { get; }

    /// <summary>A human-readable structured continuation description.</summary>
    public string ContinuationDescription
        => string.Join(" -> ", Frames.Select(frame => frame.ToString()));

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
/// A failure domain registered on a <see cref="ProcessSystemModel{TState}"/>.
/// Processes and guarded launches registered <em>through</em> the domain object
/// belong to it, which is what expresses ownership structurally instead of a
/// boolean flag: a crash discards every continuation registered through this
/// domain and clears its volatile state, and a restart relaunches the domain's
/// persistent workers fresh. The domain carries a stable <see cref="Name"/> that
/// appears in the process/control edge metadata.
/// </summary>
public sealed class ProcessFailureDomain<TState>
    where TState : State
{
    private readonly ProcessSystemModel<TState> model;

    internal ProcessFailureDomain(
        ProcessSystemModel<TState> model,
        string name,
        Func<TState, bool> crashEnabled,
        Action<TState> onCrash,
        Func<TState, bool> restartEnabled,
        Action<TState> onRestart)
    {
        this.model = model;
        Name = name;
        CrashEnabled = crashEnabled ?? throw new ArgumentNullException(nameof(crashEnabled));
        OnCrash = onCrash ?? throw new ArgumentNullException(nameof(onCrash));
        RestartEnabled = restartEnabled ?? throw new ArgumentNullException(nameof(restartEnabled));
        OnRestart = onRestart ?? throw new ArgumentNullException(nameof(onRestart));
    }

    /// <summary>The stable name of this failure domain.</summary>
    public string Name { get; }

    internal Func<TState, bool> CrashEnabled { get; }
    internal Action<TState> OnCrash { get; }
    internal Func<TState, bool> RestartEnabled { get; }
    internal Action<TState> OnRestart { get; }

    /// <summary>
    /// Registers a persistent process that belongs to this failure domain: it is
    /// live from the start, discarded at a crash, and relaunched fresh at the
    /// next restart.
    /// </summary>
    public ProcessFailureDomain<TState> Process(
        string role,
        Func<ModelContext<TState>, ModelTask> workflow)
    {
        model.RegisterProcess(role, workflow, Name);
        return this;
    }

    /// <summary>
    /// Registers a guarded launch that belongs to this failure domain. When
    /// <paramref name="guard"/> holds and no instance of <paramref name="role"/>
    /// is live, the scheduler adds a fresh instance in one atomic transition; a
    /// crash discards it and does not relaunch it (a persistent domain worker
    /// replaces it instead).
    /// </summary>
    public ProcessFailureDomain<TState> On(
        string role,
        Func<TState, bool> guard,
        Func<ModelContext<TState>, ModelTask> workflow)
    {
        model.RegisterLaunch(role, guard, workflow, Name);
        return this;
    }

    /// <summary>
    /// Registers an always-enabled recurring atomic action in this failure
    /// domain. If its mutation is a semantic no-op, the resulting edge is a
    /// real graph self-loop rather than an artificial continuation state. The
    /// graph remains complete, but changing-edge fairness predicates naturally
    /// do not count a state-neutral self-loop as progress.
    /// </summary>
    public ProcessFailureDomain<TState> RepeatedAction<TAction>(
        string role,
        TAction semanticAction,
        Action<TState> action,
        object subject = null)
        where TAction : struct, Enum
        => RepeatedAction(role, _ => true, semanticAction, action, subject);

    /// <summary>
    /// Registers a guarded recurring atomic action in this failure domain. It
    /// is structurally unavailable between this domain's crash and restart.
    /// </summary>
    public ProcessFailureDomain<TState> RepeatedAction<TAction>(
        string role,
        Func<TState, bool> guard,
        TAction semanticAction,
        Action<TState> action,
        object subject = null)
        where TAction : struct, Enum
    {
        model.RegisterRepeatedAction(
            role,
            guard,
            semanticAction,
            action,
            subject,
            Name);
        return this;
    }
}

internal sealed class ProcessSpec<TState>
    where TState : State
{
    internal ProcessSpec(
        string role,
        ModelWorkflow<TState> workflow,
        string domain,
        ProcessRuntimeOptions options)
    {
        Role = role;
        Workflow = workflow;
        Domain = domain;
        Options = options;
    }

    internal string Role { get; }
    internal ModelWorkflow<TState> Workflow { get; }

    /// <summary>The failure domain that owns this process, or null.</summary>
    internal string Domain { get; }
    internal ProcessRuntimeOptions Options { get; }
}

internal sealed class LaunchSpec<TState>
    where TState : State
{
    internal LaunchSpec(
        string role,
        Func<TState, bool> guard,
        ModelWorkflow<TState> workflow,
        string domain,
        ProcessRuntimeOptions options)
    {
        Role = role;
        Guard = guard;
        Workflow = workflow;
        Domain = domain;
        Options = options;
    }

    internal string Role { get; }
    internal Func<TState, bool> Guard { get; }
    internal ModelWorkflow<TState> Workflow { get; }

    /// <summary>The failure domain that owns this launch, or null.</summary>
    internal string Domain { get; }
    internal ProcessRuntimeOptions Options { get; }
}

internal sealed class RepeatedActionSpec<TState>
    where TState : State
{
    internal RepeatedActionSpec(
        string role,
        Func<TState, bool> guard,
        string checkpointName,
        object semanticAction,
        Action<TState> action,
        object subject,
        string domain)
    {
        Role = role;
        Guard = guard;
        CheckpointName = checkpointName;
        SemanticAction = semanticAction;
        Action = action;
        Subject = subject;
        Domain = domain;
    }

    internal string Role { get; }
    internal Func<TState, bool> Guard { get; }
    internal object SemanticAction { get; }
    internal Action<TState> Action { get; }
    internal object Subject { get; }
    internal string Domain { get; }
    internal string CheckpointName { get; }
}

/// <summary>
/// The composition root of an experimental process system. It registers a set
/// of independently active structured model processes, optional guarded process
/// launches, and an optional failure domain, and compiles the whole system to
/// an ordinary Accordant state graph.
///
/// <para>Ownership is expressed <em>structurally</em>: a process registered with
/// <see cref="Process(string, Func{ModelContext{TState}, ModelTask})"/> is
/// outside every failure domain and survives every crash, while a process
/// registered through a <see cref="ProcessFailureDomain{TState}"/> (returned by
/// <see cref="FailureDomain"/>) belongs to that domain and is discarded at a
/// crash. There is no boolean domain flag.</para>
///
/// <para>Each process has an explicit structured continuation in scheduler-node
/// configuration, never in the domain <typeparamref name="TState"/>,
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
    private readonly List<RepeatedActionSpec<TState>> repeatedActions =
        new List<RepeatedActionSpec<TState>>();
    private readonly bool verifyDeterminism;
    private readonly int maxInternalCheckpoints;
    private ProcessFailureDomain<TState> failureDomain;

    /// <summary>Creates a process system rooted at <paramref name="initialState"/>.</summary>
    public ProcessSystemModel(
        TState initialState,
        bool verifyDeterminism = false,
        int maxInternalCheckpoints = 10000)
    {
        this.initialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        if (maxInternalCheckpoints <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInternalCheckpoints),
                "The internal checkpoint limit must be positive.");
        }

        this.verifyDeterminism = verifyDeterminism;
        this.maxInternalCheckpoints = maxInternalCheckpoints;
    }

    /// <summary>
    /// Registers an independently active process that is <em>outside</em> every
    /// failure domain: it is live from the start and survives every crash with
    /// its continuation intact (for example an external client). To place a
    /// process inside a failure domain, register it through the
    /// <see cref="ProcessFailureDomain{TState}"/> returned by
    /// <see cref="FailureDomain"/> instead.
    /// </summary>
    public ProcessSystemModel<TState> Process(
        string role,
        Func<ModelContext<TState>, ModelTask> workflow)
    {
        RegisterProcess(role, workflow, domain: null);
        return this;
    }

    /// <summary>
    /// Registers a guarded launch outside every failure domain. When
    /// <paramref name="guard"/> holds and no instance of <paramref name="role"/>
    /// is currently live, the scheduler adds a fresh instance in one atomic
    /// transition. The no-duplicate rule uses control state the scheduler owns,
    /// so a launch cannot fire unboundedly while its guard remains true. To place
    /// a launch inside a failure domain, use
    /// <see cref="ProcessFailureDomain{TState}.On"/>.
    /// </summary>
    public ProcessSystemModel<TState> On(
        string role,
        Func<TState, bool> guard,
        Func<ModelContext<TState>, ModelTask> workflow)
    {
        RegisterLaunch(role, guard, workflow, domain: null);
        return this;
    }

    /// <summary>
    /// Registers an always-enabled recurring atomic action outside every
    /// failure domain. A semantic no-op intentionally produces a graph
    /// self-loop and no continuation configuration. The graph remains complete,
    /// but changing-edge fairness predicates do not count that loop as progress.
    /// </summary>
    public ProcessSystemModel<TState> RepeatedAction<TAction>(
        string role,
        TAction semanticAction,
        Action<TState> action,
        object subject = null)
        where TAction : struct, Enum
        => RepeatedAction(role, _ => true, semanticAction, action, subject);

    /// <summary>
    /// Registers a guarded recurring atomic action outside every failure
    /// domain.
    /// </summary>
    public ProcessSystemModel<TState> RepeatedAction<TAction>(
        string role,
        Func<TState, bool> guard,
        TAction semanticAction,
        Action<TState> action,
        object subject = null)
        where TAction : struct, Enum
    {
        RegisterRepeatedAction(
            role,
            guard,
            semanticAction,
            action,
            subject,
            domain: null);
        return this;
    }

    /// <summary>
    /// Registers a failure domain with a stable <paramref name="name"/> and its
    /// crash/restart behavior, and returns it so processes and launches can be
    /// registered through it. Registering through the returned object is what
    /// expresses domain ownership structurally. Exactly one failure domain is
    /// supported.
    /// </summary>
    public ProcessFailureDomain<TState> FailureDomain(
        string name,
        Func<TState, bool> crashEnabled,
        Action<TState> onCrash,
        Func<TState, bool> restartEnabled,
        Action<TState> onRestart)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A failure domain requires a stable name.", nameof(name));
        }

        if (failureDomain != null)
        {
            throw new ModelDefinitionException(
                "This prototype supports a single failure domain. " +
                $"'{failureDomain.Name}' is already registered.");
        }

        failureDomain = new ProcessFailureDomain<TState>(
            this, name, crashEnabled, onCrash, restartEnabled, onRestart);
        return failureDomain;
    }

    internal void RegisterProcess(
        string role,
        Func<ModelContext<TState>, ModelTask> workflow,
        string domain)
    {
        RequireRole(role);
        if (workflow == null) throw new ArgumentNullException(nameof(workflow));
        var modelWorkflow = ModelWorkflow<TState>.ForRoot(role, workflow);
        processes.Add(new ProcessSpec<TState>(
            role,
            modelWorkflow,
            domain,
            OptionsFor(role, modelWorkflow)));
    }

    internal void RegisterLaunch(
        string role,
        Func<TState, bool> guard,
        Func<ModelContext<TState>, ModelTask> workflow,
        string domain)
    {
        RequireRole(role);
        if (guard == null) throw new ArgumentNullException(nameof(guard));
        if (workflow == null) throw new ArgumentNullException(nameof(workflow));
        var modelWorkflow = ModelWorkflow<TState>.ForRoot(role, workflow);
        launches.Add(new LaunchSpec<TState>(
            role,
            guard,
            modelWorkflow,
            domain,
            OptionsFor(role, modelWorkflow)));
    }

    internal void RegisterRepeatedAction<TAction>(
        string role,
        Func<TState, bool> guard,
        TAction semanticAction,
        Action<TState> action,
        object subject,
        string domain)
        where TAction : struct, Enum
    {
        RequireRole(role);
        if (guard == null) throw new ArgumentNullException(nameof(guard));
        if (action == null) throw new ArgumentNullException(nameof(action));
        repeatedActions.Add(new RepeatedActionSpec<TState>(
            role,
            guard,
            EnumCheckpointName.Of(semanticAction),
            semanticAction,
            action,
            ScalarValues.Validate(subject, role),
            domain));
    }

    /// <summary>Compiles the system to an ordinary state graph.</summary>
    public StateGraphNode Explore(int maxDepth = -1, bool lazy = false)
    {
        var uniqueRoles = processes.Select(p => p.Role)
            .Concat(launches.Select(l => l.Role))
            .Concat(repeatedActions.Select(action => action.Role))
            .GroupBy(role => role, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (uniqueRoles != null)
        {
            throw new ModelDefinitionException(
                $"Process role '{uniqueRoles.Key}' is registered more than once. " +
                "Each process identity must be unique and stable.");
        }

        initialState.Freeze();
        var configuration = new ProcessConfiguration<TState>(
            processes,
            launches,
            repeatedActions,
            failureDomain);
        var live = processes
            .Select(spec => FreshProcess(spec.Role, spec.Workflow, spec.Options, initialState))
            .OrderBy(p => p.Role, StringComparer.Ordinal)
            .ToList();

        var scheduler = new ProcessSchedulerStep<TState>(
            configuration,
            live,
            failureDomainRunning: failureDomain != null);
        return StateGraph.ExploreStateGraph(
            new IStepFunction[] { scheduler },
            initialState,
            maxDepth: maxDepth,
            lazy: lazy);
    }

    private LiveProcess<TState> FreshProcess(
        string role,
        ModelWorkflow<TState> workflow,
        ProcessRuntimeOptions options,
        TState state)
    {
        var start = ProcessContinuation<TState>.Root(role, workflow);
        var normalized = ProcessContinuationRunner.Normalize(start, state, options);
        return new LiveProcess<TState>(
            role,
            normalized.Completed ? start : normalized.Continuation);
    }

    private ProcessRuntimeOptions OptionsFor(string role, ModelWorkflow<TState> workflow)
        => new ProcessRuntimeOptions(
            role,
            verifyDeterminism,
            maxInternalCheckpoints,
            workflow.Captures);

    private static void RequireRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("A process requires a stable role.", nameof(role));
        }
    }
}

/// <summary>One live process: its stable role and its serialized continuation.</summary>
internal sealed class LiveProcess<TState>
    where TState : State
{
    internal LiveProcess(string role, ProcessContinuation<TState> continuation)
    {
        Role = role;
        Continuation = continuation;
    }

    internal string Role { get; }
    internal ProcessContinuation<TState> Continuation { get; }
}

/// <summary>The immutable registration a scheduler shares across all of its nodes.</summary>
internal sealed class ProcessConfiguration<TState>
    where TState : State
{
    private readonly Dictionary<string, ProcessSpec<TState>> processByRole;
    private readonly Dictionary<string, LaunchSpec<TState>> launchByRole;
    private readonly Dictionary<string, RepeatedActionSpec<TState>> actionByRole;

    internal ProcessConfiguration(
        IReadOnlyList<ProcessSpec<TState>> processes,
        IReadOnlyList<LaunchSpec<TState>> launches,
        IReadOnlyList<RepeatedActionSpec<TState>> repeatedActions,
        ProcessFailureDomain<TState> failureDomain)
    {
        Processes = processes;
        Launches = launches;
        RepeatedActions = repeatedActions;
        FailureDomain = failureDomain;
        processByRole = processes.ToDictionary(p => p.Role, StringComparer.Ordinal);
        launchByRole = launches.ToDictionary(l => l.Role, StringComparer.Ordinal);
        actionByRole = repeatedActions.ToDictionary(a => a.Role, StringComparer.Ordinal);
    }

    internal IReadOnlyList<ProcessSpec<TState>> Processes { get; }
    internal IReadOnlyList<LaunchSpec<TState>> Launches { get; }
    internal IReadOnlyList<RepeatedActionSpec<TState>> RepeatedActions { get; }
    internal ProcessFailureDomain<TState> FailureDomain { get; }

    internal ProcessRuntimeOptions OptionsFor(string role)
        => processByRole.TryGetValue(role, out var process)
            ? process.Options
            : launchByRole[role].Options;

    /// <summary>The name of the failure domain that owns <paramref name="role"/>, or null.</summary>
    internal string DomainOfRole(string role)
        => processByRole.TryGetValue(role, out var process)
            ? process.Domain
            : launchByRole.TryGetValue(role, out var launch)
                ? launch.Domain
                : actionByRole[role].Domain;
}

/// <summary>
/// The single active step function of a compiled process system. It owns the
/// whole set of live process continuations, which is what lets a crash discard
/// every failure-domain continuation atomically instead of leaking disabled
/// steps into the graph.
/// </summary>
internal sealed class ProcessSchedulerStep<TState> :
    BaseStepFunction,
    IProcessSchedulerStep,
    IProcessSchedulerDiagnostics
    where TState : State
{
    private readonly ProcessConfiguration<TState> configuration;
    private readonly IReadOnlyList<LiveProcess<TState>> live;
    private readonly bool failureDomainRunning;
    private readonly string id;

    internal ProcessSchedulerStep(
        ProcessConfiguration<TState> configuration,
        IReadOnlyList<LiveProcess<TState>> live,
        bool failureDomainRunning)
    {
        this.configuration = configuration;
        this.live = live;
        this.failureDomainRunning = failureDomainRunning;
        id = "process-system#" + Identifiers.Join(
            new[]
            {
                configuration.FailureDomain == null
                    ? "no-failure-domain"
                    : configuration.FailureDomain.Name + ":" +
                        (failureDomainRunning ? "running" : "crashed")
            }
            .Concat(live.SelectMany(p => new[] { p.Role, p.Continuation.Identity }))
            .ToArray());
    }

    public override string StepFunctionId => id;

    public IReadOnlyList<ProcessInstance> LiveProcesses
        => live
            .Select(p => new ProcessInstance(
                p.Role,
                configuration.DomainOfRole(p.Role),
                p.Continuation.DisplayIdentity,
                p.Continuation.Snapshot()))
            .ToList();

    public IReadOnlyList<string> ProcessRoles
        => configuration.Processes.Select(process => process.Role)
            .Concat(configuration.Launches.Select(launch => launch.Role))
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<string> RepeatedActionRoles
        => configuration.RepeatedActions
            .Select(action => action.Role)
            .OrderBy(role => role, StringComparer.Ordinal)
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

        // 2. Recurring stateless actions offer one atomic edge and retain no
        // continuation configuration.
        foreach (var action in configuration.RepeatedActions)
        {
            if (!DomainAvailable(action.Domain) || !action.Guard(state))
            {
                continue;
            }

            var next = (TState)state.Clone();
            action.Action(next);
            next.Freeze();
            results.Add(Checkpoint(
                next,
                live.ToList(),
                action.Role,
                new ProcessCheckpointTransition(
                    action.Role,
                    ModelCheckpointKind.Step,
                    action.CheckpointName,
                    value: null,
                    checkpointHistory: Array.Empty<ProcessCheckpointRecord>(),
                    semanticAction: action.SemanticAction,
                    subject: action.Subject)));
        }

        // 3. Guarded launches add a fresh process when enabled and not already live.
        foreach (var launch in configuration.Launches)
        {
            if (DomainAvailable(launch.Domain) &&
                launch.Guard(state) &&
                !live.Any(p => p.Role == launch.Role))
            {
                var next = live
                    .Concat(new[]
                    {
                        FreshProcess(
                            launch.Role,
                            launch.Workflow,
                            launch.Options,
                            state)
                    })
                    .ToList();
                results.Add(Control(state, next, ProcessControlKind.Launch, launch.Role, launch.Domain));
            }
        }

        // 4. The failure domain: crash discards every continuation it owns and
        // structurally disables its stateless actions and launches.
        var domain = configuration.FailureDomain;
        if (domain != null && failureDomainRunning && domain.CrashEnabled(state))
        {
            var crashed = (TState)state.Clone();
            domain.OnCrash(crashed);
            crashed.Freeze();
            var survivors = live
                .Where(p => configuration.DomainOfRole(p.Role) != domain.Name)
                .ToList();
            results.Add(Control(
                crashed,
                survivors,
                ProcessControlKind.Crash,
                role: null,
                domain: domain.Name,
                nextFailureDomainRunning: false));
        }

        // 5. Restart relaunches the domain's persistent workers fresh.
        if (domain != null && !failureDomainRunning && domain.RestartEnabled(state))
        {
            var restarted = (TState)state.Clone();
            domain.OnRestart(restarted);
            restarted.Freeze();
            var relaunched = live.ToList();
            foreach (var spec in configuration.Processes)
            {
                if (spec.Domain == domain.Name && relaunched.All(p => p.Role != spec.Role))
                {
                    relaunched.Add(FreshProcess(
                        spec.Role,
                        spec.Workflow,
                        spec.Options,
                        restarted));
                }
            }

            results.Add(Control(
                restarted,
                relaunched,
                ProcessControlKind.Restart,
                role: null,
                domain: domain.Name,
                nextFailureDomainRunning: true));
        }

        return results;
    }

    private void AdvanceProcess(
        TState state,
        LiveProcess<TState> process,
        List<StepResult> results)
    {
        var options = configuration.OptionsFor(process.Role);
        var advance = ProcessContinuationRunner.Advance(
            process.Continuation,
            state,
            options);

        if (advance.Blocked)
        {
            return;
        }

        if (advance.Completed)
        {
            // The process completed. Drop it in one state-neutral control edge.
            results.Add(Control(
                state,
                WithoutRole(process.Role),
                ProcessControlKind.Completion,
                process.Role,
                configuration.DomainOfRole(process.Role)));
            return;
        }

        var pending = advance.Pending;
        if (pending == null || pending.Deferred)
        {
            throw new ModelDefinitionException(
                $"Process '{process.Role}' did not normalize to a visible checkpoint.");
        }

        var checkpointHistory = advance.Continuation.Top.History.Records;

        if (pending.Kind == ModelCheckpointKind.Choose)
        {
            foreach (var choice in (object[])pending.Value)
            {
                var committed = Commit(
                    process.Role,
                    advance.Continuation,
                    options,
                    pending,
                    choice,
                    state);
                results.Add(Checkpoint(
                    state,
                    Replace(process.Role, committed),
                    process.Role,
                    new ProcessCheckpointTransition(
                        process.Role,
                        pending.Kind,
                        pending.Name,
                        choice,
                        checkpointHistory,
                        pending.SemanticAction,
                        pending.Subject)));
            }

            return;
        }

        if (pending.Kind == ModelCheckpointKind.ChooseStep)
        {
            foreach (var choice in (object[])pending.Value)
            {
                var next = (TState)state.Clone();
                ((Action<TState, object>)pending.Action)(next, choice);
                next.Freeze();
                var committed = Commit(
                    process.Role,
                    advance.Continuation,
                    options,
                    pending,
                    choice,
                    next);
                results.Add(Checkpoint(
                    next,
                    Replace(process.Role, committed),
                    process.Role,
                    new ProcessCheckpointTransition(
                        process.Role,
                        pending.Kind,
                        pending.Name,
                        choice,
                        checkpointHistory,
                        pending.SemanticAction,
                        pending.SubjectFor(choice))));
            }

            return;
        }

        if (pending.Kind == ModelCheckpointKind.Step)
        {
            var next = (TState)state.Clone();
            ((Action<TState>)pending.Action)(next);
            next.Freeze();
            var committed = Commit(
                process.Role,
                advance.Continuation,
                options,
                pending,
                ModelUnitValue.Instance,
                next);
            results.Add(Checkpoint(
                next,
                Replace(process.Role, committed),
                process.Role,
                new ProcessCheckpointTransition(
                    process.Role,
                    pending.Kind,
                    pending.Name,
                    null,
                    checkpointHistory,
                    pending.SemanticAction,
                    pending.Subject)));
            return;
        }

        throw new ModelDefinitionException(
            $"Process '{process.Role}' reached an unsupported visible checkpoint kind '{pending.Kind}'.");
    }

    private LiveProcess<TState> Commit(
        string role,
        ProcessContinuation<TState> continuation,
        ProcessRuntimeOptions options,
        PendingCheckpoint pending,
        object recordedValue,
        TState resultingState)
    {
        var normalized = ProcessContinuationRunner.Commit(
            continuation,
            pending,
            recordedValue,
            resultingState,
            options);
        return normalized.Completed
            ? null
            : new LiveProcess<TState>(role, normalized.Continuation);
    }

    private LiveProcess<TState> FreshProcess(
        string role,
        ModelWorkflow<TState> workflow,
        ProcessRuntimeOptions options,
        TState state)
    {
        var start = ProcessContinuation<TState>.Root(role, workflow);
        var normalized = ProcessContinuationRunner.Normalize(start, state, options);
        return new LiveProcess<TState>(
            role,
            normalized.Completed ? start : normalized.Continuation);
    }

    private bool DomainAvailable(string domain)
        => domain == null || failureDomainRunning;

    private List<LiveProcess<TState>> Replace(
        string role,
        LiveProcess<TState> replacement)
    {
        var next = live.Where(p => p.Role != role).ToList();
        if (replacement != null)
        {
            next.Add(replacement);
        }

        return next;
    }

    private List<LiveProcess<TState>> WithoutRole(string role)
        => live.Where(p => p.Role != role).ToList();

    private StepResult Checkpoint(
        TState state,
        List<LiveProcess<TState>> nextLive,
        string role,
        ProcessCheckpointTransition checkpoint)
        => Emit(
            state,
            nextLive,
            new ProcessTransition(
                role,
                configuration.DomainOfRole(role),
                ProcessControlKind.None,
                checkpoint));

    private StepResult Control(
        TState state,
        List<LiveProcess<TState>> nextLive,
        ProcessControlKind control,
        string role,
        string domain,
        bool? nextFailureDomainRunning = null)
        => Emit(
            state,
            nextLive,
            new ProcessTransition(role, domain, control, checkpoint: null),
            nextFailureDomainRunning);

    private StepResult Emit(
        TState state,
        List<LiveProcess<TState>> nextLive,
        ProcessTransition transition,
        bool? nextFailureDomainRunning = null)
    {
        var ordered = nextLive
            .OrderBy(p => p.Role, StringComparer.Ordinal)
            .ToList();
        return new StepResult
        {
            State = state,
            StepFunctions = new IStepFunction[]
            {
                new ProcessSchedulerStep<TState>(
                    configuration,
                    ordered,
                    nextFailureDomainRunning ?? failureDomainRunning)
            },
            EdgeMetadata = transition
        };
    }
}
