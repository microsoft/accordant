// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Accordant;

/// <summary>Identifies the kind of a coroutine checkpoint.</summary>
public enum ModelCheckpointKind
{
    Read,
    Choose,
    Step,
    Loop,

    /// <summary>
    /// A guarded suspension. Like <see cref="Read"/> it is internal replay
    /// discovery and creates no graph edge, but the coroutine cannot advance
    /// past it until its predicate holds on the state the process is applied
    /// to. Once passed, the (optional) captured scalar is historical and is
    /// replayed verbatim; only a pending, not-yet-passed <c>When</c> is
    /// re-evaluated against live shared state.
    /// </summary>
    When
}

/// <summary>
/// A completed, replayable coroutine checkpoint. Values are limited to immutable
/// scalar values so a tape cannot retain a mutable reference into model state.
/// </summary>
public sealed class ReplayEntry
{
    internal ReplayEntry(ModelCheckpointKind kind, string name, object value, string loopSite = null)
    {
        Kind = kind;
        Name = name;
        Value = ScalarValues.ValidateRecorded(kind, name, value);
        LoopSite = loopSite;
        Identity = CheckpointIdentity.Create(kind, name, loopSite);
        Description = CheckpointIdentity.Describe(kind, name, loopSite);
    }

    /// <summary>The checkpoint kind.</summary>
    public ModelCheckpointKind Kind { get; }

    /// <summary>The user-supplied stable checkpoint name.</summary>
    public string Name { get; }

    /// <summary>The immutable scalar value recorded for a replay checkpoint.</summary>
    public object Value { get; }

    /// <summary>
    /// The collision-safe checkpoint identity. It is an opaque encoded string;
    /// use <see cref="Description"/> in messages.
    /// </summary>
    public string Identity { get; }

    /// <summary>A human-readable description of the checkpoint.</summary>
    public string Description { get; }

    internal string LoopSite { get; }

    /// <inheritdoc/>
    public override string ToString()
        => Description + "=" + ScalarValues.ValueIdentity(Value);
}

/// <summary>
/// Immutable-prefix replay history for a coroutine. A tape is copied when a
/// graph branch is created; entries are never mutated after recording. Ordinary
/// checkpoints may repeat; <see cref="RebaseLoop"/> intentionally replaces a
/// completed loop iteration with one canonical loop entry.
/// </summary>
public sealed class ReplayTape
{
    private readonly ReadOnlyCollection<ReplayEntry> entries;

    /// <summary>Creates an empty replay tape.</summary>
    public ReplayTape() : this(new List<ReplayEntry>())
    {
    }

    private ReplayTape(List<ReplayEntry> entries)
    {
        this.entries = new ReadOnlyCollection<ReplayEntry>(entries);
    }

    /// <summary>The completed checkpoints in execution order.</summary>
    public IReadOnlyList<ReplayEntry> Entries => entries;

    internal ReplayTape Append(ModelCheckpointKind kind, string name, object value)
    {
        if (kind == ModelCheckpointKind.Loop)
        {
            throw new ModelDefinitionException(
                $"Loop checkpoint '{name}' cannot be appended to a replay tape. " +
                "A loop boundary rebases the tape instead of extending it.");
        }

        var copy = new List<ReplayEntry>(entries) { new ReplayEntry(kind, name, value) };
        return new ReplayTape(copy);
    }

    internal ReplayTape RebaseLoop(string name, string loopSite, object persistentValue)
    {
        var identity = CheckpointIdentity.Create(ModelCheckpointKind.Loop, name, loopSite);
        if (!entries.Any(entry => entry.Kind == ModelCheckpointKind.Loop) &&
            entries.Count != 0)
        {
            throw new ModelDefinitionException(
                $"Loop '{name}' must be the first Accordant checkpoint in its workflow, but " +
                $"the replay prefix already contains {Describe()}. " +
                "Place one-time ordinary code before Loop and all Read, Choose, and Step " +
                "checkpoints after the iteration boundary.");
        }

        var otherLoop = entries.FirstOrDefault(entry =>
            entry.Kind == ModelCheckpointKind.Loop &&
            entry.Identity != identity);
        if (otherLoop != null)
        {
            throw new ModelDefinitionException(
                "Coroutine workflows currently support one Loop boundary. " +
                $"{CheckpointIdentity.Describe(ModelCheckpointKind.Loop, name, loopSite)} conflicts with " +
                $"{otherLoop.Description}. Model the inner iteration with the outer Loop's " +
                "persistent value instead of a second boundary.");
        }

        var existingLoop = entries
            .Select((entry, index) => new { entry, index })
            .FirstOrDefault(item => item.entry.Identity == identity);
        var prefixCount = existingLoop == null ? entries.Count : existingLoop.index;
        var copy = entries.Take(prefixCount).ToList();
        copy.Add(new ReplayEntry(ModelCheckpointKind.Loop, name, persistentValue, loopSite));
        return new ReplayTape(copy);
    }

    internal string ContinuationIdentity
        => entries.Count == 0
            ? "start"
            : Identifiers.Join(entries
                .SelectMany(entry => new[] { entry.Identity, ScalarValues.ValueIdentity(entry.Value) })
                .ToArray());

    internal string Describe()
        => entries.Count == 0
            ? "an empty replay prefix"
            : string.Join(" -> ", entries.Select(entry => entry.ToString()));
}

/// <summary>
/// A visible checkpoint action in a compiled coroutine graph. This typed view
/// avoids requiring consumers to interpret the stable step-function identity
/// when examining the full compiled graph.
/// </summary>
public interface ICoroutineCheckpointStep : IStepFunction
{
    /// <summary>The stable workflow name.</summary>
    string WorkflowName { get; }

    /// <summary>The kind of the checkpoint this action exposes.</summary>
    ModelCheckpointKind CheckpointKind { get; }

    /// <summary>The user-supplied stable checkpoint name.</summary>
    string CheckpointName { get; }

    /// <summary>
    /// The completed replay prefix before this checkpoint is taken.
    /// </summary>
    IReadOnlyList<ReplayEntry> ReplayPrefix { get; }
}

/// <summary>Thrown when a coroutine cannot be compiled as a replayable model workflow.</summary>
public sealed class ModelDefinitionException : InvalidOperationException
{
    /// <summary>Creates a definition error.</summary>
    public ModelDefinitionException(string message) : base(message)
    {
    }

    internal ModelDefinitionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>A no-value result used by <see cref="ModelContext{TState}.Step"/>.</summary>
public readonly struct ModelUnit
{
}

/// <summary>
/// The custom async return type for experimental model coroutines. It is not a
/// general-purpose task: an incomplete await must be one of this package's
/// Read, Choose, Step, or Loop awaitables.
/// </summary>
[AsyncMethodBuilder(typeof(ModelTaskMethodBuilder))]
public readonly struct ModelTask
{
    private readonly ModelExecution execution;

    internal ModelTask(ModelExecution execution)
    {
        this.execution = execution;
    }

    internal ModelExecution Execution => execution;

    /// <summary>Gets an awaiter for compiler compatibility.</summary>
    public ModelTaskAwaiter GetAwaiter() => new ModelTaskAwaiter(execution);
}

/// <summary>Awaiter for <see cref="ModelTask"/>.</summary>
public readonly struct ModelTaskAwaiter : INotifyCompletion
{
    private readonly ModelExecution execution;

    internal ModelTaskAwaiter(ModelExecution execution)
    {
        this.execution = execution;
    }

    /// <summary>Whether the coroutine has completed.</summary>
    public bool IsCompleted => execution != null && execution.Completed;

    /// <summary>Gets the result or throws a definition error for an incomplete task.</summary>
    public void GetResult()
    {
        if (execution == null || !execution.Completed)
        {
            throw new ModelDefinitionException(
                "Awaiting a nested or incomplete ModelTask is not supported by the experimental " +
                "coroutine front-end. Inline the helper into the workflow method so every " +
                "checkpoint belongs to one replayable state machine.");
        }

        execution.ThrowIfFaulted();
    }

    /// <summary>Incomplete ModelTask continuations are not supported.</summary>
    public void OnCompleted(Action continuation)
    {
        throw new ModelDefinitionException(
            "Awaiting an incomplete ModelTask is not supported by the experimental coroutine front-end.");
    }
}

/// <summary>Custom async method builder used by <see cref="ModelTask"/>.</summary>
public struct ModelTaskMethodBuilder
{
    private ModelExecution execution;

    /// <summary>Creates a builder.</summary>
    public static ModelTaskMethodBuilder Create()
        => new ModelTaskMethodBuilder { execution = new ModelExecution() };

    /// <summary>Gets the coroutine task.</summary>
    public ModelTask Task => new ModelTask(execution);

    /// <summary>Starts the synchronous segment of a model coroutine.</summary>
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        var previous = ModelRuntime.Current;
        ModelRuntime.Current = execution;
        try
        {
            stateMachine.MoveNext();
        }
        finally
        {
            ModelRuntime.Current = previous;
        }
    }

    /// <summary>Required by the async method builder pattern.</summary>
    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
    }

    /// <summary>Marks the coroutine complete.</summary>
    public void SetResult() => execution.Complete();

    /// <summary>Records a coroutine exception.</summary>
    public void SetException(Exception exception) => execution.Fault(exception);

    /// <summary>
    /// Suspends only at a model checkpoint. Other incomplete awaiters are
    /// rejected rather than allowing time, I/O, or scheduler state into replay.
    /// A foreign awaiter that reports <c>IsCompleted</c> is resumed inline by the
    /// compiler and never reaches this builder, so it cannot be rejected here;
    /// see the runtime-limitation section of the sample README.
    /// </summary>
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        ValidateModelAwaiter(awaiter);
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    /// <summary>Critical-notification counterpart to <see cref="AwaitOnCompleted"/>.</summary>
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        ValidateModelAwaiter(awaiter);
        awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
    }

    private static void ValidateModelAwaiter<TAwaiter>(TAwaiter awaiter)
    {
        if (!(awaiter is IModelAwaiter))
        {
            throw new ModelDefinitionException(
                "ModelTask only supports incomplete awaits of ModelContext.Read, Choose, Step, Loop, or LoopState. " +
                $"The workflow suspended on '{typeof(TAwaiter).FullName}', which is an external " +
                "asynchronous await and is not replayable.");
        }
    }
}

/// <summary>
/// A context supplied to a model coroutine. It deliberately exposes no State
/// property; state can only be inspected by Read/Choose selectors or changed
/// by a scheduled Step action.
/// </summary>
public sealed class ModelContext<TState>
    where TState : State
{
    private readonly TState state;
    private readonly ReplayTape tape;
    private readonly CoroutineOptions options;
    private readonly bool probeValues;
    private int checkpointIndex;

    internal ModelContext(TState state, ReplayTape tape, CoroutineOptions options, bool probeValues)
    {
        this.state = state;
        this.tape = tape;
        this.options = options;
        this.probeValues = probeValues;
    }

    internal int ConsumedCheckpoints => checkpointIndex;

    /// <summary>
    /// Records a deterministic observation, which is internal to graph
    /// discovery. The selector must be a pure function of the frozen state: it
    /// is evaluated twice on the same state while determinism verification is
    /// enabled, and a differing result is reported as a definition error.
    /// </summary>
    public ModelAwaitable<TValue> Read<TValue>(
        string stableName,
        Func<TState, TValue> selector)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        return AtCheckpoint<TValue>(
            ModelCheckpointKind.Read,
            stableName,
            () => ScalarValues.Validate(selector(state), stableName),
            null);
    }

    /// <summary>Creates a visible finite branch from fixed choices.</summary>
    public ModelAwaitable<TValue> Choose<TValue>(
        string stableName,
        IEnumerable<TValue> choices)
    {
        if (choices == null) throw new ArgumentNullException(nameof(choices));
        return Choose(stableName, _ => choices);
    }

    /// <summary>Creates a visible finite branch from choices derived from frozen state.</summary>
    public ModelAwaitable<TValue> Choose<TValue>(
        string stableName,
        Func<TState, IEnumerable<TValue>> choices)
    {
        if (choices == null) throw new ArgumentNullException(nameof(choices));
        return AtCheckpoint<TValue>(
            ModelCheckpointKind.Choose,
            stableName,
            () => MaterializeChoices(stableName, choices(state)),
            null);
    }

    /// <summary>Schedules one visible state mutation. The action runs only when selected.</summary>
    public ModelAwaitable<ModelUnit> Step(string stableName, Action<TState> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return AtCheckpoint<ModelUnit>(
            ModelCheckpointKind.Step,
            stableName,
            () => ModelUnitValue.Instance,
            action);
    }

    /// <summary>
    /// Schedules one visible state mutation with a typed semantic action tag
    /// and optional immutable subject. The tag and subject are edge metadata;
    /// <paramref name="stableName"/> remains the checkpoint's replay identity.
    /// </summary>
    public ModelAwaitable<ModelUnit> Step<TAction>(
        string stableName,
        TAction semanticAction,
        Action<TState> action,
        object subject = null)
        where TAction : struct, Enum
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return AtCheckpoint<ModelUnit>(
            ModelCheckpointKind.Step,
            stableName,
            () => ModelUnitValue.Instance,
            action,
            semanticAction: semanticAction,
            subject: ScalarValues.Validate(subject, stableName));
    }

    /// <summary>
    /// Suspends the process until <paramref name="predicate"/> holds on the
    /// state the process is applied to. This is a guarded, internal checkpoint:
    /// it creates no graph edge, and a process whose pending <c>When</c> is not
    /// satisfied is simply not enabled, so an interleaved action by another
    /// process that makes the predicate true is what lets this process advance.
    /// Once the guard has been passed it is historical and never re-blocks on
    /// replay.
    /// </summary>
    public ModelAwaitable<ModelUnit> When(string stableName, Func<TState, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        RequireProcessScheduler(nameof(When));
        return AtCheckpoint<ModelUnit>(
            ModelCheckpointKind.When,
            stableName,
            () => ModelUnitValue.Instance,
            null,
            guard: () => predicate(state));
    }

    /// <summary>
    /// Suspends until <paramref name="ready"/> holds, then atomically captures
    /// the immutable scalar produced by <paramref name="capture"/> against the
    /// same enabling state. The captured value is intentionally historical:
    /// later replays return it verbatim, while a still-pending wait re-evaluates
    /// <paramref name="ready"/> against live shared state.
    /// </summary>
    public ModelAwaitable<TValue> WaitUntil<TValue>(
        string stableName,
        Func<TState, bool> ready,
        Func<TState, TValue> capture)
    {
        if (ready == null) throw new ArgumentNullException(nameof(ready));
        if (capture == null) throw new ArgumentNullException(nameof(capture));
        RequireProcessScheduler(nameof(WaitUntil));
        return AtCheckpoint<TValue>(
            ModelCheckpointKind.When,
            stableName,
            () => ScalarValues.Validate(capture(state), stableName),
            null,
            guard: () => ready(state));
    }

    private void RequireProcessScheduler(string checkpoint)
    {
        if (!options.AllowGuardedWaits)
        {
            throw new ModelDefinitionException(
                $"{checkpoint} is a live guarded wait and requires ProcessSystemModel. " +
                "A standalone coroutine has no independently active process that can " +
                "change shared state and unblock it.");
        }
    }

    /// <summary>
    /// Records the canonical start of a replayable loop iteration. This is an
    /// internal rebase checkpoint, not a graph edge. The supplied value must
    /// include every live local that affects later iterations; replay returns
    /// the recorded value when the workflow restarts. The runtime cannot detect
    /// an omitted live local. This prototype permits one direct syntactic Loop
    /// boundary per workflow, and Loop must be the first Accordant checkpoint
    /// reached. The boundary is identified by its compile-time caller
    /// information (file name, member, and line). Same-named files can collide,
    /// and two boundaries reached through one shared call site cannot be
    /// distinguished at all.
    /// </summary>
    public ModelAwaitable<TValue> LoopState<TValue>(
        string stableName,
        TValue persistentValue,
        [CallerMemberName] string callerMember = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerFilePath] string callerFile = "")
        => AtCheckpoint<TValue>(
            ModelCheckpointKind.Loop,
            stableName,
            () => ScalarValues.Validate(persistentValue, stableName),
            null,
            CheckpointIdentity.LoopSite(callerMember, callerLine, callerFile));

    /// <summary>
    /// Records the workflow's loop iteration boundary with no persistent local state.
    /// </summary>
    public ModelAwaitable<ModelUnit> Loop(
        string stableName,
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
        => LoopState(stableName, new ModelUnit(), callerMember, callerLine, callerFile);

    private ModelAwaitable<TValue> AtCheckpoint<TValue>(
        ModelCheckpointKind kind,
        string stableName,
        Func<object> unknownValue,
        Action<TState> action,
        string loopSite = null,
        Func<bool> guard = null,
        object semanticAction = null,
        object subject = null)
    {
        ValidateName(stableName);
        var execution = ModelRuntime.Current;
        if (execution == null)
        {
            throw new ModelDefinitionException(
                $"{CheckpointIdentity.Describe(kind, stableName, loopSite)} ran without an active " +
                "ModelTask on this thread. Checkpoints may only be called from the body of the " +
                "async ModelTask workflow itself, on the thread that started it.");
        }

        var reached = CheckpointIdentity.Create(kind, stableName, loopSite);
        if (checkpointIndex < tape.Entries.Count)
        {
            var entry = tape.Entries[checkpointIndex];
            if (entry.Identity != reached)
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' diverged from its replay tape at checkpoint " +
                    $"{checkpointIndex}: the tape recorded {entry.Description} but the workflow reached " +
                    $"{CheckpointIdentity.Describe(kind, stableName, loopSite)}. " +
                    $"Replay prefix: {tape.Describe()}. Checkpoint kinds, names, and order must be a " +
                    "deterministic function of TState, earlier checkpoint values, and Loop persistent values.");
            }

            checkpointIndex++;
            return new ModelAwaitable<TValue>(true, ReplayValue<TValue>(entry));
        }

        if (checkpointIndex > tape.Entries.Count)
        {
            throw new ModelDefinitionException(
                $"Workflow '{options.WorkflowName}' advanced past the end of its replay tape " +
                $"({checkpointIndex} > {tape.Entries.Count}).");
        }

        // A pending, not-yet-passed guarded wait blocks the process against the
        // live state. It records no value and produces no tape entry.
        if (guard != null && !guard())
        {
            execution.SetPending(new PendingCheckpoint(
                kind,
                stableName,
                value: null,
                action: null,
                loopSite: loopSite,
                blocked: true));
            return new ModelAwaitable<TValue>(false, default);
        }

        var value = unknownValue();
        if (probeValues && kind != ModelCheckpointKind.Loop)
        {
            EnsureValueIsAFunctionOfState(kind, stableName, loopSite, value, unknownValue());
        }

        execution.SetPending(new PendingCheckpoint(
            kind,
            stableName,
            value,
            action,
            loopSite,
            semanticAction: semanticAction,
            subject: subject));
        return new ModelAwaitable<TValue>(false, default);
    }

    private TValue ReplayValue<TValue>(ReplayEntry entry)
    {
        if (entry.Value == null)
        {
            if (typeof(TValue).IsValueType && Nullable.GetUnderlyingType(typeof(TValue)) == null)
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' recorded a null value for {entry.Description} " +
                    $"but now replays it as non-nullable '{typeof(TValue).FullName}'. " +
                    "A checkpoint's value type must be stable across replays.");
            }

            return default;
        }

        if (!(entry.Value is TValue typed))
        {
            throw new ModelDefinitionException(
                $"Workflow '{options.WorkflowName}' recorded {entry.Description} as " +
                $"'{entry.Value.GetType().FullName}' but now replays it as '{typeof(TValue).FullName}'. " +
                "A checkpoint's value type must be stable across replays.");
        }

        return typed;
    }

    private void EnsureValueIsAFunctionOfState(
        ModelCheckpointKind kind,
        string stableName,
        string loopSite,
        object first,
        object second)
    {
        var firstIdentity = ScalarValues.ValueIdentity(first);
        var secondIdentity = ScalarValues.ValueIdentity(second);
        if (string.Equals(firstIdentity, secondIdentity, StringComparison.Ordinal))
        {
            return;
        }

        throw new ModelDefinitionException(
            $"{CheckpointIdentity.Describe(kind, stableName, loopSite)} in workflow " +
            $"'{options.WorkflowName}' is not a function of the frozen model state: evaluating it " +
            $"twice on the same state produced {firstIdentity} and then {secondIdentity}. " +
            "Read and Choose selectors must not use clocks, randomness, I/O, or mutable captured " +
            "variables; move that data into TState or into a Loop persistent value.");
    }

    private static void ValidateName(string stableName)
    {
        if (string.IsNullOrWhiteSpace(stableName))
        {
            throw new ModelDefinitionException(
                "Every Read, Choose, Step, and Loop requires a non-empty stable name.");
        }
    }

    private static object MaterializeChoices<TValue>(string name, IEnumerable<TValue> choices)
    {
        if (choices == null)
        {
            throw new ModelDefinitionException($"Choose checkpoint '{name}' returned null choices.");
        }

        var values = choices.Select(value => ScalarValues.Validate(value, name)).ToArray();
        if (values.Length == 0)
        {
            throw new ModelDefinitionException($"Choose checkpoint '{name}' requires at least one finite choice.");
        }

        var duplicate = values
            .GroupBy(ScalarValues.Identity, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new ModelDefinitionException(
                $"Choose checkpoint '{name}' contains duplicate value '{duplicate.Key}'.");
        }

        return values;
    }
}

/// <summary>Awaitable returned from a value-producing model checkpoint.</summary>
public readonly struct ModelAwaitable<TValue>
{
    private readonly bool completed;
    private readonly TValue value;

    internal ModelAwaitable(bool completed, TValue value)
    {
        this.completed = completed;
        this.value = value;
    }

    /// <summary>Gets the checkpoint awaiter.</summary>
    public ModelAwaiter<TValue> GetAwaiter() => new ModelAwaiter<TValue>(completed, value);
}

/// <summary>Awaiter for a model checkpoint.</summary>
public readonly struct ModelAwaiter<TValue> : ICriticalNotifyCompletion, IModelAwaiter
{
    private readonly bool completed;
    private readonly TValue value;

    internal ModelAwaiter(bool completed, TValue value)
    {
        this.completed = completed;
        this.value = value;
    }

    /// <summary>Whether replay supplied this checkpoint value.</summary>
    public bool IsCompleted => completed;

    /// <summary>Returns the replayed checkpoint value.</summary>
    public TValue GetResult() => value;

    /// <summary>Suspension is driven by coroutine graph construction.</summary>
    public void OnCompleted(Action continuation)
    {
    }

    /// <summary>Suspension is driven by coroutine graph construction.</summary>
    public void UnsafeOnCompleted(Action continuation)
    {
    }
}

/// <summary>
/// Compiles an experimental model coroutine to the ordinary state-graph API.
/// Read and Loop checkpoints are internal deterministic discovery; Choose and
/// Step become ordinary visible graph transitions.
/// </summary>
/// <remarks>
/// This is a prototype and is deliberately not promoted out of the
/// Experimental namespace: it is unpackaged and carries no compatibility
/// promise. See docs/concepts/model-checking-frontends.md for the decision,
/// the soundness evidence, and the migration boundary.
/// </remarks>
public static class CoroutineModel
{
    /// <summary>
    /// Explores one coroutine workflow. Hidden replay control is deliberately
    /// part of graph identity even though it is separate from
    /// <typeparamref name="TState"/>. Exploration defaults to depth 16 so a
    /// missing Loop boundary cannot silently create an enormous replay tree.
    /// Pass -1 explicitly only when unbounded exploration is intentional.
    /// </summary>
    /// <param name="workflowName">The stable workflow name used in identities and diagnostics.</param>
    /// <param name="initialState">The initial domain state; it is frozen before exploration.</param>
    /// <param name="process">The replayable async workflow factory.</param>
    /// <param name="maxDepth">The ordinary graph depth bound, or -1 for unbounded exploration.</param>
    /// <param name="lazy">Whether the ordinary graph is expanded lazily.</param>
    /// <param name="verifyDeterminism">
    /// When true, every replay segment is executed twice from the
    /// same frozen state and replay prefix, Read and Choose selectors are
    /// evaluated twice, and captured external inputs are compared before and
    /// after the body runs where runtime inspection supports that. This is an
    /// opt-in diagnostic audit, not a proof of determinism. It executes user
    /// code twice, may reject graph-irrelevant side effects, and roughly
    /// doubles exploration cost.
    /// </param>
    /// <param name="maxInternalCheckpoints">
    /// The number of consecutive internal Read or Loop checkpoints a single
    /// segment may take before exploration fails. It bounds replay-only
    /// livelock; it cannot interrupt a loop that reaches no checkpoint at all.
    /// </param>
    public static StateGraphNode Explore<TState>(
        string workflowName,
        TState initialState,
        Func<ModelContext<TState>, ModelTask> process,
        int maxDepth = 16,
        bool lazy = false,
        bool verifyDeterminism = false,
        int maxInternalCheckpoints = 10000)
        where TState : State
    {
        if (string.IsNullOrWhiteSpace(workflowName))
        {
            throw new ArgumentException("A coroutine workflow requires a stable name.", nameof(workflowName));
        }
        if (initialState == null) throw new ArgumentNullException(nameof(initialState));
        if (process == null) throw new ArgumentNullException(nameof(process));
        if (maxInternalCheckpoints <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInternalCheckpoints),
                maxInternalCheckpoints,
                "The internal checkpoint bound must be positive.");
        }

        initialState.Freeze();
        var options = new CoroutineOptions(
            workflowName,
            verifyDeterminism,
            maxInternalCheckpoints,
            CapturedInputMonitor.Create(process));
        var initial = CoroutineRunner.Advance(process, initialState, new ReplayTape(), options);
        var steps = new List<IStepFunction>();
        if (initial.Pending != null)
        {
            steps.Add(new CoroutineStep<TState>(
                workflowName,
                process,
                initial.Tape,
                initial.Pending,
                options));
        }

        return StateGraph.ExploreStateGraph(
            steps,
            initialState,
            maxDepth: maxDepth,
            lazy: lazy);
    }

    /// <summary>
    /// Reports the external variables captured by a workflow delegate and how
    /// closely each one can be monitored during exploration. This is a
    /// diagnostic report, not a guarantee: only compiler-generated closures are
    /// walked, and objects reported as
    /// <see cref="CoroutineCapturedInputMonitoring.ReferenceIdentityOnly"/> are
    /// compared by reference, so mutation of their contents is invisible.
    /// </summary>
    public static IReadOnlyList<CoroutineCapturedInput> DescribeCapturedInputs<TState>(
        Func<ModelContext<TState>, ModelTask> process)
        where TState : State
    {
        if (process == null) throw new ArgumentNullException(nameof(process));
        return CapturedInputMonitor.Create(process).Describe();
    }
}

/// <summary>How closely a captured external input can be monitored during replay.</summary>
public enum CoroutineCapturedInputMonitoring
{
    /// <summary>The captured value is an immutable scalar and is compared by value.</summary>
    ImmutableScalar,

    /// <summary>The captured value is a model state and is compared by string representation.</summary>
    ModelState,

    /// <summary>
    /// Only the reference is compared; mutation of the object's contents cannot be detected.
    /// </summary>
    ReferenceIdentityOnly,

    /// <summary>
    /// The workflow delegate does not target a compiler-generated closure, so its
    /// captured inputs are not enumerated at all.
    /// </summary>
    NotAnalyzable
}

/// <summary>One external variable captured by a coroutine workflow delegate.</summary>
public sealed class CoroutineCapturedInput
{
    internal CoroutineCapturedInput(
        string name,
        string declaredType,
        CoroutineCapturedInputMonitoring monitoring)
    {
        Name = name;
        DeclaredType = declaredType;
        Monitoring = monitoring;
    }

    /// <summary>The captured variable name, including its closure path.</summary>
    public string Name { get; }

    /// <summary>The declared type of the captured variable.</summary>
    public string DeclaredType { get; }

    /// <summary>How closely the value can be monitored.</summary>
    public CoroutineCapturedInputMonitoring Monitoring { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} : {DeclaredType} ({Monitoring})";
}

/// <summary>Metadata for a visible coroutine graph edge.</summary>
public sealed class CoroutineTransition
{
    internal CoroutineTransition(
        string workflowName,
        ModelCheckpointKind kind,
        string checkpointName,
        object value,
        IReadOnlyList<ReplayEntry> replayPrefix,
        object semanticAction = null,
        object subject = null)
    {
        WorkflowName = workflowName;
        Kind = kind;
        CheckpointName = checkpointName;
        Value = value;
        ReplayPrefix = replayPrefix;
        SemanticAction = semanticAction;
        Subject = subject;
    }

    /// <summary>The stable workflow name.</summary>
    public string WorkflowName { get; }
    /// <summary>The selected checkpoint kind.</summary>
    public ModelCheckpointKind Kind { get; }
    /// <summary>The stable checkpoint name.</summary>
    public string CheckpointName { get; }
    /// <summary>The scalar Choose value, or null for Step.</summary>
    public object Value { get; }
    /// <summary>
    /// The completed replay prefix before this edge was taken. This is the
    /// typed source for locals selected by earlier Choose checkpoints.
    /// </summary>
    public IReadOnlyList<ReplayEntry> ReplayPrefix { get; }
    /// <summary>The typed semantic action tag supplied by a Step, or null.</summary>
    public object SemanticAction { get; }
    /// <summary>The optional immutable action subject supplied by a Step.</summary>
    public object Subject { get; }

    /// <inheritdoc/>
    public override string ToString()
        => Kind == ModelCheckpointKind.Choose
            ? $"choose:{CheckpointName}={ScalarValues.Display(Value)}"
            : $"step:{CheckpointName}";
}

internal interface IModelAwaiter
{
}

internal static class ModelRuntime
{
    [ThreadStatic]
    internal static ModelExecution Current;
}

internal sealed class ModelExecution
{
    internal PendingCheckpoint Pending { get; private set; }
    internal bool Completed { get; private set; }
    private Exception exception;

    internal void SetPending(PendingCheckpoint pending)
    {
        if (Completed)
        {
            throw new ModelDefinitionException(
                "A coroutine exposed a checkpoint after it completed.");
        }
        if (Pending != null)
        {
            throw new ModelDefinitionException(
                $"A coroutine attempted to expose more than one pending checkpoint: " +
                $"{CheckpointIdentity.Describe(Pending.Kind, Pending.Name, Pending.LoopSite)} is already " +
                $"pending and {CheckpointIdentity.Describe(pending.Kind, pending.Name, pending.LoopSite)} " +
                "was reached without awaiting the first one.");
        }
        Pending = pending;
    }

    internal void Complete() => Completed = true;
    internal void Fault(Exception value) => exception = value;

    internal void ThrowIfFaulted()
    {
        if (exception != null)
        {
            if (exception is ModelDefinitionException definitionException)
            {
                throw definitionException;
            }
            throw new ModelDefinitionException("The coroutine threw while being replayed.", exception);
        }
    }
}

internal sealed class PendingCheckpoint
{
    internal PendingCheckpoint(
        ModelCheckpointKind kind,
        string name,
        object value,
        object action,
        string loopSite,
        bool blocked = false,
        object semanticAction = null,
        object subject = null)
    {
        Kind = kind;
        Name = name;
        Value = value;
        Action = action;
        ActionIdentity = DelegateIdentity.Create(action as Delegate);
        LoopSite = loopSite;
        Blocked = blocked;
        SemanticAction = semanticAction;
        Subject = subject;
    }

    internal ModelCheckpointKind Kind { get; }
    internal string Name { get; }
    internal object Value { get; }
    internal object Action { get; }
    internal string ActionIdentity { get; }
    internal string LoopSite { get; }
    internal object SemanticAction { get; }
    internal object Subject { get; }

    /// <summary>
    /// Whether this is a guarded wait whose predicate does not yet hold, so the
    /// process is blocked and contributes no transition.
    /// </summary>
    internal bool Blocked { get; }
}

internal sealed class CoroutineOptions
{
    internal CoroutineOptions(
        string workflowName,
        bool verifyDeterminism,
        int maxInternalCheckpoints,
        CapturedInputMonitor captures,
        bool allowGuardedWaits = false)
    {
        WorkflowName = workflowName;
        VerifyDeterminism = verifyDeterminism;
        MaxInternalCheckpoints = maxInternalCheckpoints;
        Captures = captures;
        AllowGuardedWaits = allowGuardedWaits;
    }

    internal string WorkflowName { get; }

    internal bool VerifyDeterminism { get; }

    internal int MaxInternalCheckpoints { get; }

    internal CapturedInputMonitor Captures { get; }

    internal bool AllowGuardedWaits { get; }
}

internal sealed class CoroutineAdvance
{
    internal CoroutineAdvance(
        ReplayTape tape,
        PendingCheckpoint pending,
        IReadOnlyList<string> trace,
        bool blocked = false)
    {
        Tape = tape;
        Pending = pending;
        Trace = trace;
        Blocked = blocked;
    }

    internal ReplayTape Tape { get; }
    internal PendingCheckpoint Pending { get; }
    internal IReadOnlyList<string> Trace { get; }

    /// <summary>
    /// Whether the segment ended blocked on a guarded wait rather than at a
    /// visible checkpoint or completion. A blocked advance has a null
    /// <see cref="Pending"/> and is not completion: the process is simply not
    /// currently enabled.
    /// </summary>
    internal bool Blocked { get; }
}

internal static class CoroutineRunner
{
    internal static CoroutineAdvance Advance<TState>(
        Func<ModelContext<TState>, ModelTask> process,
        TState state,
        ReplayTape tape,
        CoroutineOptions options)
        where TState : State
    {
        if (!options.VerifyDeterminism)
        {
            return Run(process, state, tape, options, probeValues: false);
        }

        var capturedBefore = options.Captures.Snapshot();
        var primary = Run(process, state, tape, options, probeValues: true);
        var audit = RunAudit(process, state, tape, options);
        EnsureSameTrace(options.WorkflowName, primary, audit);
        options.Captures.EnsureUnchanged(capturedBefore, options.WorkflowName);
        return primary;
    }

    private static CoroutineAdvance RunAudit<TState>(
        Func<ModelContext<TState>, ModelTask> process,
        TState state,
        ReplayTape tape,
        CoroutineOptions options)
        where TState : State
    {
        try
        {
            return Run(process, state, tape, options, probeValues: false);
        }
        catch (ModelDefinitionException exception)
        {
            throw new ModelDefinitionException(
                $"Workflow '{options.WorkflowName}' failed the replay-determinism audit: re-running the " +
                "same segment from the same frozen state and replay prefix did not behave the same way. " +
                exception.Message,
                exception);
        }
    }

    private static void EnsureSameTrace(
        string workflowName,
        CoroutineAdvance primary,
        CoroutineAdvance audit)
    {
        var shared = Math.Min(primary.Trace.Count, audit.Trace.Count);
        for (var index = 0; index < shared; index++)
        {
            if (!string.Equals(primary.Trace[index], audit.Trace[index], StringComparison.Ordinal))
            {
                throw NondeterministicSegment(
                    workflowName,
                    index,
                    primary.Trace[index],
                    audit.Trace[index]);
            }
        }

        if (primary.Trace.Count != audit.Trace.Count)
        {
            throw NondeterministicSegment(
                workflowName,
                shared,
                Describe(primary.Trace, shared),
                Describe(audit.Trace, shared));
        }

        if (!string.Equals(
            primary.Tape.ContinuationIdentity,
            audit.Tape.ContinuationIdentity,
            StringComparison.Ordinal))
        {
            throw NondeterministicSegment(
                workflowName,
                shared,
                primary.Tape.Describe(),
                audit.Tape.Describe());
        }
    }

    private static string Describe(IReadOnlyList<string> trace, int index)
        => index < trace.Count ? trace[index] : "nothing further";

    private static ModelDefinitionException NondeterministicSegment(
        string workflowName,
        int index,
        string first,
        string second)
        => new ModelDefinitionException(
            $"Workflow '{workflowName}' is not deterministic under replay. Running the same segment " +
            $"twice from the same frozen state and replay prefix reached {first} on the first run and " +
            $"{second} on the second run at position {index}. Replayed coroutine code may only depend " +
            "on TState, earlier checkpoint values, and Loop persistent values; clocks, randomness, " +
            "mutable captured variables, and awaits of foreign already-completed tasks are not replayable.");

    private static CoroutineAdvance Run<TState>(
        Func<ModelContext<TState>, ModelTask> process,
        TState state,
        ReplayTape tape,
        CoroutineOptions options,
        bool probeValues)
        where TState : State
    {
        var trace = new List<string>();
        for (var internalCheckpoints = 0; ; internalCheckpoints++)
        {
            if (internalCheckpoints == options.MaxInternalCheckpoints)
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' did not reach Choose, Step, or completion after " +
                    $"{options.MaxInternalCheckpoints} internal Read or Loop checkpoints. A synchronous " +
                    "loop that only takes internal checkpoints cannot make visible progress; a " +
                    "synchronous loop that takes no checkpoint at all cannot be interrupted by this " +
                    $"bound at all. Last replay prefix: {tape.Describe()}.");
            }

            var context = new ModelContext<TState>(state, tape, options, probeValues);
            ModelTask task;
            var before = state.StringRepresentation(forceRecompute: true);
            try
            {
                task = process(context);
            }
            catch (ModelDefinitionException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ModelDefinitionException(
                    $"The coroutine factory for workflow '{options.WorkflowName}' could not be invoked.",
                    exception);
            }
            var after = state.StringRepresentation(forceRecompute: true);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' mutated shared model state outside " +
                    $"ModelContext.Step: the state changed from '{before}' to '{after}' while the " +
                    "replayable body ran. Read and Choose selectors and ordinary " +
                    "coroutine code must not mutate captured state references.");
            }

            var execution = task.Execution;
            if (execution == null)
            {
                throw new ModelDefinitionException(
                    $"The coroutine factory for workflow '{options.WorkflowName}' returned a default " +
                    "ModelTask. Declare it as an async ModelTask method.");
            }
            execution.ThrowIfFaulted();

            if (execution.Pending == null)
            {
                if (execution.Completed)
                {
                    if (context.ConsumedCheckpoints != tape.Entries.Count)
                    {
                        throw new ModelDefinitionException(
                            $"Workflow '{options.WorkflowName}' completed after replaying only " +
                            $"{context.ConsumedCheckpoints} of {tape.Entries.Count} recorded checkpoints. " +
                            $"Replay prefix: {tape.Describe()}. A replayed run must reach every " +
                            "checkpoint it recorded before it may finish.");
                    }

                    trace.Add($"completion after {context.ConsumedCheckpoints} replayed checkpoints");
                    return new CoroutineAdvance(tape, null, trace);
                }

                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' suspended without a Read, Choose, Step, or Loop " +
                    "checkpoint. Only this package's checkpoints may suspend a ModelTask.");
            }

            var pending = execution.Pending;
            trace.Add(
                CheckpointIdentity.Describe(pending.Kind, pending.Name, pending.LoopSite) +
                "=" + ScalarValues.ValueIdentity(pending.Value) +
                (pending.ActionIdentity == null ? string.Empty : $" action={pending.ActionIdentity}") +
                $" after {context.ConsumedCheckpoints} replayed checkpoints");

            if (pending.Kind == ModelCheckpointKind.Loop)
            {
                tape = tape.RebaseLoop(pending.Name, pending.LoopSite, pending.Value);
                continue;
            }

            // A guarded wait whose predicate does not yet hold blocks the
            // process: no visible checkpoint, no completion, no tape entry.
            if (pending.Kind == ModelCheckpointKind.When && pending.Blocked)
            {
                return new CoroutineAdvance(tape, null, trace, blocked: true);
            }

            // A passed guarded wait behaves exactly like a Read: it captures its
            // (optional) scalar into the tape and the segment continues.
            if (pending.Kind == ModelCheckpointKind.Read ||
                pending.Kind == ModelCheckpointKind.When)
            {
                tape = tape.Append(pending.Kind, pending.Name, pending.Value);
                continue;
            }

            return new CoroutineAdvance(tape, pending, trace);
        }
    }
}

internal sealed class CoroutineStep<TState> : BaseStepFunction, ICoroutineCheckpointStep
    where TState : State
{
    private readonly string workflowName;
    private readonly Func<ModelContext<TState>, ModelTask> process;
    private readonly ReplayTape tape;
    private readonly PendingCheckpoint pending;
    private readonly CoroutineOptions options;
    private readonly string id;

    internal CoroutineStep(
        string workflowName,
        Func<ModelContext<TState>, ModelTask> process,
        ReplayTape tape,
        PendingCheckpoint pending,
        CoroutineOptions options)
    {
        this.workflowName = workflowName;
        this.process = process;
        this.tape = tape;
        this.pending = pending;
        this.options = options;
        id = $"coroutine:{workflowName}:{pending.Kind.ToString().ToLowerInvariant()}:{pending.Name}#" +
            Identifiers.Join(
                workflowName,
                pending.Kind.ToString(),
                pending.Name,
                pending.LoopSite ?? string.Empty,
                ScalarValues.ValueIdentity(pending.Value),
                pending.ActionIdentity ?? string.Empty,
                tape.ContinuationIdentity);
    }

    public override string StepFunctionId => id;

    public string WorkflowName => workflowName;

    public ModelCheckpointKind CheckpointKind => pending.Kind;

    public string CheckpointName => pending.Name;

    public IReadOnlyList<ReplayEntry> ReplayPrefix => tape.Entries;

    protected override IList<StepResult> ApplyInternal(IState source)
    {
        var state = (TState)source;
        if (pending.Kind == ModelCheckpointKind.Choose)
        {
            var results = new List<StepResult>();
            foreach (var choice in (object[])pending.Value)
            {
                var nextTape = tape.Append(pending.Kind, pending.Name, choice);
                results.Add(CreateResult(
                    state,
                    nextTape,
                    new CoroutineTransition(
                        workflowName,
                        pending.Kind,
                        pending.Name,
                        choice,
                        tape.Entries,
                        pending.SemanticAction,
                        pending.Subject)));
            }
            return results;
        }

        if (pending.Kind == ModelCheckpointKind.Step)
        {
            var next = (TState)state.Clone();
            ((Action<TState>)pending.Action)(next);
            next.Freeze();
            var nextTape = tape.Append(pending.Kind, pending.Name, ModelUnitValue.Instance);
            return new[]
            {
                CreateResult(
                    next,
                    nextTape,
                    new CoroutineTransition(
                        workflowName,
                        pending.Kind,
                        pending.Name,
                        null,
                        tape.Entries,
                        pending.SemanticAction,
                        pending.Subject))
            };
        }

        throw new ModelDefinitionException($"Unsupported visible checkpoint kind '{pending.Kind}'.");
    }

    private StepResult CreateResult(TState state, ReplayTape nextTape, CoroutineTransition transition)
    {
        var advance = CoroutineRunner.Advance(process, state, nextTape, options);

        var nextSteps = advance.Pending == null
            ? null
            : new IStepFunction[]
            {
                new CoroutineStep<TState>(
                    workflowName,
                    process,
                    advance.Tape,
                    advance.Pending,
                    options)
            };

        return new StepResult
        {
            State = state,
            StepFunctions = nextSteps,
            EdgeMetadata = transition
        };
    }
}

internal static class ModelUnitValue
{
    internal static readonly object Instance = new ModelUnit();
}

internal static class DelegateIdentity
{
    internal static string Create(Delegate value)
    {
        if (value == null)
        {
            return null;
        }

        var method = value.Method;
        return Identifiers.Join(
            method.Module.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture),
            method.MetadataToken.ToString(CultureInfo.InvariantCulture),
            method.DeclaringType?.FullName ?? string.Empty,
            method.Name);
    }
}

internal static class CheckpointIdentity
{
    internal static string Create(ModelCheckpointKind kind, string name, string loopSite)
        => Identifiers.Join(
            kind.ToString(),
            name ?? string.Empty,
            kind == ModelCheckpointKind.Loop ? loopSite ?? string.Empty : string.Empty);

    internal static string Describe(ModelCheckpointKind kind, string name, string loopSite)
        => kind == ModelCheckpointKind.Loop
            ? $"Loop '{name}' at {loopSite}"
            : $"{kind} checkpoint '{name}'";

    internal static string LoopSite(string callerMember, int callerLine, string callerFile)
    {
        return Path.GetFileName(callerFile ?? string.Empty) + ":" +
            (callerMember ?? string.Empty) + ":" +
            callerLine.ToString(CultureInfo.InvariantCulture);
    }
}

internal static class Identifiers
{
    /// <summary>
    /// Joins components with a length prefix so no component value, however it
    /// is punctuated, can be confused with a different decomposition.
    /// </summary>
    internal static string Join(params string[] parts)
        => string.Join("|", parts.Select(Encode));

    internal static string Encode(string value)
    {
        value = value ?? string.Empty;
        return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    }
}

internal sealed class CapturedInputMonitor
{
    private static readonly string[] NoValues = new string[0];

    private readonly IReadOnlyList<CapturedInputField> fields;
    private readonly bool analyzable;

    private CapturedInputMonitor(IReadOnlyList<CapturedInputField> fields, bool analyzable)
    {
        this.fields = fields;
        this.analyzable = analyzable;
    }

    internal static CapturedInputMonitor Create(Delegate process)
    {
        var target = process?.Target;
        if (target == null)
        {
            return new CapturedInputMonitor(new List<CapturedInputField>(), analyzable: false);
        }

        if (!IsCompilerGenerated(target.GetType()))
        {
            return new CapturedInputMonitor(new List<CapturedInputField>(), analyzable: false);
        }

        var collected = new List<CapturedInputField>();
        Collect(target, string.Empty, collected, new HashSet<object>(), depth: 0);
        return new CapturedInputMonitor(collected, analyzable: true);
    }

    internal IReadOnlyList<CoroutineCapturedInput> Describe()
    {
        if (!analyzable)
        {
            return new[]
            {
                new CoroutineCapturedInput(
                    "<delegate target>",
                    "<not a compiler-generated closure>",
                    CoroutineCapturedInputMonitoring.NotAnalyzable)
            };
        }

        return fields
            .Select(field => new CoroutineCapturedInput(
                field.Name,
                field.DeclaredType,
                Classify(field.Read())))
            .ToList();
    }

    internal string[] Snapshot()
    {
        if (fields.Count == 0)
        {
            return NoValues;
        }

        var values = new string[fields.Count];
        for (var index = 0; index < fields.Count; index++)
        {
            values[index] = DescribeValue(fields[index].Read());
        }

        return values;
    }

    internal void EnsureUnchanged(string[] before, string workflowName)
    {
        if (before.Length == 0)
        {
            return;
        }

        var now = Snapshot();
        for (var index = 0; index < before.Length; index++)
        {
            if (string.Equals(before[index], now[index], StringComparison.Ordinal))
            {
                continue;
            }

            throw new ModelDefinitionException(
                $"Workflow '{workflowName}' mutated the captured external variable " +
                $"'{fields[index].Name}' while its replayable body ran: the value changed from " +
                $"{before[index]} to {now[index]}. Replayed coroutine code must not write to captured " +
                "variables; model that data in TState or carry it in a Loop persistent value. " +
                "Captured objects that are only compared by reference are not covered by this check.");
        }
    }

    private static void Collect(
        object owner,
        string prefix,
        List<CapturedInputField> collected,
        HashSet<object> visited,
        int depth)
    {
        if (owner == null || depth > 4 || !visited.Add(owner))
        {
            return;
        }

        foreach (var field in owner.GetType().GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var name = prefix + FriendlyName(field.Name);
            var value = field.GetValue(owner);
            if (value != null && !(value is Delegate) && IsCompilerGenerated(value.GetType()))
            {
                Collect(value, name + ".", collected, visited, depth + 1);
                continue;
            }

            collected.Add(new CapturedInputField(name, field.FieldType.FullName, owner, field));
        }
    }

    private static bool IsCompilerGenerated(Type type)
        => type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), inherit: false).Length != 0;

    private static string FriendlyName(string fieldName)
    {
        if (fieldName.Length > 1 && fieldName[0] == '<')
        {
            var end = fieldName.IndexOf('>');
            if (end > 1)
            {
                return fieldName.Substring(1, end - 1);
            }
        }

        return fieldName;
    }

    private static CoroutineCapturedInputMonitoring Classify(object value)
    {
        if (value == null || ScalarValues.IsImmutableScalar(value))
        {
            return CoroutineCapturedInputMonitoring.ImmutableScalar;
        }

        return value is State
            ? CoroutineCapturedInputMonitoring.ModelState
            : CoroutineCapturedInputMonitoring.ReferenceIdentityOnly;
    }

    private static string DescribeValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (ScalarValues.IsImmutableScalar(value))
        {
            return ScalarValues.Identity(value);
        }

        if (value is State state)
        {
            return "state:" + state.StringRepresentation(forceRecompute: true);
        }

        return "reference:" + RuntimeHelpers.GetHashCode(value).ToString(CultureInfo.InvariantCulture);
    }

    private sealed class CapturedInputField
    {
        private readonly object owner;
        private readonly FieldInfo field;

        internal CapturedInputField(string name, string declaredType, object owner, FieldInfo field)
        {
            Name = name;
            DeclaredType = declaredType;
            this.owner = owner;
            this.field = field;
        }

        internal string Name { get; }

        internal string DeclaredType { get; }

        internal object Read() => field.GetValue(owner);
    }
}

internal static class ScalarValues
{
    internal static object Validate<TValue>(TValue value, string checkpointName)
        => Validate((object)value, checkpointName);

    internal static object Validate(object value, string checkpointName)
    {
        if (value == null || IsImmutableScalar(value))
        {
            return value;
        }

        throw new ModelDefinitionException(
            $"Checkpoint '{checkpointName}' returned unsupported value type '{value.GetType().FullName}'. " +
            "Replay values must be null, strings, primitives, enums, or supported immutable scalar value types.");
    }

    /// <summary>
    /// Re-validates a value at the moment it is recorded on a tape so no
    /// internal path can retain a mutable reference in replay history.
    /// </summary>
    internal static object ValidateRecorded(ModelCheckpointKind kind, string name, object value)
    {
        if (value == null || IsImmutableScalar(value))
        {
            return value;
        }

        throw new ModelDefinitionException(
            $"{CheckpointIdentity.Describe(kind, name, null)} cannot record a value of type " +
            $"'{value.GetType().FullName}' on a replay tape. Recorded values must be null, strings, " +
            "primitives, enums, or supported immutable scalar value types.");
    }

    internal static bool IsImmutableScalar(object value)
        => value is string || value is bool || value is char ||
            value is byte || value is sbyte || value is short || value is ushort ||
            value is int || value is uint || value is long || value is ulong ||
            value is float || value is double || value is decimal || value is DateTime ||
            value is DateTimeOffset || value is TimeSpan || value is Guid || value is ModelUnit ||
            (value != null && value.GetType().IsEnum);

    /// <summary>
    /// The identity of a recorded value or of a materialized Choose choice set.
    /// </summary>
    internal static string ValueIdentity(object value)
        => value is object[] choices
            ? "choices" + Identifiers.Join(
                choices.Select(Identity).OrderBy(identity => identity, StringComparer.Ordinal).ToArray())
            : Identity(value);

    internal static string Identity(object value)
    {
        if (value == null) return "null";
        var type = value.GetType();
        if (value is string text) return "string:\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (value is char character) return "char:\"" + character.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (value is bool boolean) return "bool:" + (boolean ? "true" : "false");
        if (value is ModelUnit) return "unit";
        if (value is DateTime dateTime)
        {
            return "datetime:" +
                dateTime.Ticks.ToString(CultureInfo.InvariantCulture) + ":" +
                ((int)dateTime.Kind).ToString(CultureInfo.InvariantCulture);
        }
        if (value is DateTimeOffset dateTimeOffset)
        {
            return "datetimeoffset:" +
                dateTimeOffset.Ticks.ToString(CultureInfo.InvariantCulture) + ":" +
                dateTimeOffset.Offset.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        if (value is TimeSpan timeSpan)
        {
            return "timespan:" +
                timeSpan.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        if (value is Guid guid)
        {
            return "guid:" + guid.ToString("N");
        }
        if (value is float single)
        {
            return "single:" + Hex(BitConverter.GetBytes(single));
        }
        if (value is double @double)
        {
            return "double:" + Hex(BitConverter.GetBytes(@double));
        }
        if (value is decimal @decimal)
        {
            return "decimal:" + string.Join(
                ":",
                decimal.GetBits(@decimal).Select(part =>
                    part.ToString("X8", CultureInfo.InvariantCulture)));
        }
        if (type.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(type);
            return "enum:" + type.AssemblyQualifiedName + ":" +
                underlying.FullName + ":" +
                Convert.ToString(
                    Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture);
        }
        if (value is IFormattable formattable)
        {
            return type.FullName + ":" + formattable.ToString(null, CultureInfo.InvariantCulture);
        }
        return type.FullName + ":" + value;
    }

    internal static string Display(object value)
        => value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static string Hex(byte[] bytes)
        => string.Concat(bytes.Select(value =>
            value.ToString("X2", CultureInfo.InvariantCulture)));
}
