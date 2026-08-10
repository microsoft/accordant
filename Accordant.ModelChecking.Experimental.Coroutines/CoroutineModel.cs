// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Accordant;

/// <summary>Identifies the kind of a coroutine checkpoint.</summary>
public enum ModelCheckpointKind
{
    Read,
    Choose,
    Step,
    Loop
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
        Value = value;
        Identity = CheckpointIdentity.Create(kind, name, loopSite);
    }

    /// <summary>The checkpoint kind.</summary>
    public ModelCheckpointKind Kind { get; }

    /// <summary>The user-supplied stable checkpoint name.</summary>
    public string Name { get; }

    /// <summary>The immutable scalar value recorded for a replay checkpoint.</summary>
    public object Value { get; }

    /// <summary>The stable checkpoint identity.</summary>
    public string Identity { get; }
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
                $"Loop '{name}' must be the first Accordant checkpoint in its workflow. " +
                "Place one-time ordinary code before Loop and all Read, Choose, and Step " +
                "checkpoints after the iteration boundary.");
        }

        var otherLoop = entries.FirstOrDefault(entry =>
            entry.Kind == ModelCheckpointKind.Loop &&
            entry.Identity != identity);
        if (otherLoop != null)
        {
            throw new ModelDefinitionException(
                $"Coroutine workflows currently support one Loop boundary. " +
                $"Loop '{name}' at '{loopSite}' conflicts with '{otherLoop.Name}'.");
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
            : string.Join("|", entries.Select(entry =>
                Encode(entry.Identity) +
                Encode(ScalarValues.Identity(entry.Value))));

    private static string Encode(string value)
        => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
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
                "Awaiting a nested or incomplete ModelTask is not supported by the experimental coroutine front-end.");
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
                "External asynchronous awaits are not replayable.");
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
    private int checkpointIndex;

    internal ModelContext(TState state, ReplayTape tape)
    {
        this.state = state;
        this.tape = tape;
    }

    /// <summary>Records a deterministic observation, which is internal to graph discovery.</summary>
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
    /// Records the canonical start of a replayable loop iteration. This is an
    /// internal rebase checkpoint, not a graph edge. The supplied value must
    /// include every live local that affects later iterations; replay returns
    /// the recorded value when the workflow restarts. This prototype permits
    /// one direct syntactic Loop boundary per workflow, and Loop must be the
    /// first Accordant checkpoint reached.
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
        string loopSite = null)
    {
        ValidateName(stableName);
        var execution = ModelRuntime.Current;
        if (execution == null)
        {
            throw new ModelDefinitionException(
                "ModelContext checkpoints may only be called while an async ModelTask is executing.");
        }

        if (checkpointIndex < tape.Entries.Count)
        {
            var entry = tape.Entries[checkpointIndex++];
            var expected = CheckpointIdentity.Create(kind, stableName, loopSite);
            if (entry.Identity != expected)
            {
                throw new ModelDefinitionException(
                    $"Replay expected checkpoint '{entry.Identity}' but the workflow reached '{expected}'. " +
                    "Checkpoint names and order must remain stable.");
            }

            return new ModelAwaitable<TValue>(true, (TValue)entry.Value);
        }

        if (checkpointIndex > tape.Entries.Count)
        {
            throw new ModelDefinitionException("Coroutine replay advanced past the end of its tape.");
        }

        var value = unknownValue();
        execution.SetPending(new PendingCheckpoint(kind, stableName, value, action, loopSite));
        return new ModelAwaitable<TValue>(false, default);
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

    private static void ValidateName(string stableName)
    {
        if (string.IsNullOrWhiteSpace(stableName))
        {
            throw new ModelDefinitionException(
                "Every Read, Choose, Step, and Loop requires a non-empty stable name.");
        }
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
public static class CoroutineModel
{
    /// <summary>
    /// Explores one coroutine workflow. Hidden replay control is deliberately
    /// part of graph identity even though it is separate from
    /// <typeparamref name="TState"/>. Exploration defaults to depth 16 so a
    /// missing Loop boundary cannot silently create an enormous replay tree.
    /// Pass -1 explicitly only when unbounded exploration is intentional.
    /// </summary>
    public static StateGraphNode Explore<TState>(
        string workflowName,
        TState initialState,
        Func<ModelContext<TState>, ModelTask> process,
        int maxDepth = 16,
        bool lazy = false)
        where TState : State
    {
        if (string.IsNullOrWhiteSpace(workflowName))
        {
            throw new ArgumentException("A coroutine workflow requires a stable name.", nameof(workflowName));
        }
        if (initialState == null) throw new ArgumentNullException(nameof(initialState));
        if (process == null) throw new ArgumentNullException(nameof(process));
        initialState.Freeze();
        var initial = CoroutineRunner.Advance(process, initialState, new ReplayTape());
        var steps = new List<IStepFunction>();
        if (initial.Pending != null)
        {
            steps.Add(new CoroutineStep<TState>(
                workflowName,
                process,
                initial.Tape,
                initial.Pending));
        }

        return StateGraph.ExploreStateGraph(
            steps,
            initialState,
            maxDepth: maxDepth,
            lazy: lazy);
    }
}

/// <summary>Metadata for a visible coroutine graph edge.</summary>
public sealed class CoroutineTransition
{
    internal CoroutineTransition(
        string workflowName,
        ModelCheckpointKind kind,
        string checkpointName,
        object value,
        IReadOnlyList<ReplayEntry> replayPrefix)
    {
        WorkflowName = workflowName;
        Kind = kind;
        CheckpointName = checkpointName;
        Value = value;
        ReplayPrefix = replayPrefix;
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
        if (Pending != null)
        {
            throw new ModelDefinitionException("A coroutine attempted to expose more than one pending checkpoint.");
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
        string loopSite)
    {
        Kind = kind;
        Name = name;
        Value = value;
        Action = action;
        LoopSite = loopSite;
    }

    internal ModelCheckpointKind Kind { get; }
    internal string Name { get; }
    internal object Value { get; }
    internal object Action { get; }
    internal string LoopSite { get; }
}

internal sealed class CoroutineAdvance
{
    internal CoroutineAdvance(ReplayTape tape, PendingCheckpoint pending)
    {
        Tape = tape;
        Pending = pending;
    }

    internal ReplayTape Tape { get; }
    internal PendingCheckpoint Pending { get; }
}

internal static class CoroutineRunner
{
    internal static CoroutineAdvance Advance<TState>(
        Func<ModelContext<TState>, ModelTask> process,
        TState state,
        ReplayTape tape)
        where TState : State
    {
        for (var internalCheckpoints = 0; ; internalCheckpoints++)
        {
            if (internalCheckpoints == 10000)
            {
                throw new ModelDefinitionException(
                    "Coroutine did not reach Choose, Step, or completion after 10,000 internal Read or Loop checkpoints. " +
                    "A synchronous infinite loop without a model checkpoint cannot be interrupted.");
            }

            ModelTask task;
            var before = state.StringRepresentation(forceRecompute: true);
            try
            {
                task = process(new ModelContext<TState>(state, tape));
            }
            catch (Exception exception)
            {
                throw new ModelDefinitionException("The coroutine factory could not be invoked.", exception);
            }
            var after = state.StringRepresentation(forceRecompute: true);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                throw new ModelDefinitionException(
                    "The coroutine mutated shared model state outside " +
                    "ModelContext.Step. Read and Choose selectors and ordinary " +
                    "coroutine code must not mutate captured state references.");
            }

            var execution = task.Execution;
            if (execution == null)
            {
                throw new ModelDefinitionException(
                    "The coroutine factory returned a default ModelTask. Declare it as an async ModelTask method.");
            }
            execution.ThrowIfFaulted();

            if (execution.Pending == null)
            {
                if (execution.Completed)
                {
                    return new CoroutineAdvance(tape, null);
                }
                throw new ModelDefinitionException(
                    "Coroutine suspended without a Read, Choose, Step, or Loop checkpoint.");
            }

            var pending = execution.Pending;
            if (pending.Kind == ModelCheckpointKind.Loop)
            {
                tape = tape.RebaseLoop(pending.Name, pending.LoopSite, pending.Value);
                continue;
            }

            if (pending.Kind != ModelCheckpointKind.Read)
            {
                return new CoroutineAdvance(tape, pending);
            }

            tape = tape.Append(pending.Kind, pending.Name, pending.Value);
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
    private readonly string id;

    internal CoroutineStep(
        string workflowName,
        Func<ModelContext<TState>, ModelTask> process,
        ReplayTape tape,
        PendingCheckpoint pending)
    {
        this.workflowName = workflowName;
        this.process = process;
        this.tape = tape;
        this.pending = pending;
        id = $"coroutine:{workflowName}:{pending.Kind.ToString().ToLowerInvariant()}:" +
            $"{pending.Name}@{tape.ContinuationIdentity}";
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
                        tape.Entries)));
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
                        tape.Entries))
            };
        }

        throw new ModelDefinitionException($"Unsupported visible checkpoint kind '{pending.Kind}'.");
    }

    private StepResult CreateResult(TState state, ReplayTape nextTape, CoroutineTransition transition)
    {
        var advance = CoroutineRunner.Advance(process, state, nextTape);

        var nextSteps = advance.Pending == null
            ? null
            : new IStepFunction[]
            {
                new CoroutineStep<TState>(
                    workflowName,
                    process,
                    advance.Tape,
                    advance.Pending)
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

internal static class CheckpointIdentity
{
    internal static string Create(ModelCheckpointKind kind, string name, string loopSite)
        => kind == ModelCheckpointKind.Loop
            ? $"loop:{name}@{loopSite}"
            : kind.ToString().ToLowerInvariant() + ":" + name;

    internal static string LoopSite(string callerMember, int callerLine, string callerFile)
        => Path.GetFileName(callerFile) + ":" + callerMember + ":" +
            callerLine.ToString(CultureInfo.InvariantCulture);
}

internal static class ScalarValues
{
    internal static object Validate<TValue>(TValue value, string checkpointName)
        => Validate((object)value, checkpointName);

    internal static object Validate(object value, string checkpointName)
    {
        if (value == null || value is string || value is bool || value is char ||
            value is byte || value is sbyte || value is short || value is ushort ||
            value is int || value is uint || value is long || value is ulong ||
            value is float || value is double || value is decimal || value is DateTime ||
            value is DateTimeOffset || value is TimeSpan || value is Guid || value is ModelUnit ||
            value.GetType().IsEnum)
        {
            return value;
        }

        throw new ModelDefinitionException(
            $"Checkpoint '{checkpointName}' returned unsupported value type '{value.GetType().FullName}'. " +
            "Replay values must be null, strings, primitives, enums, or supported immutable scalar value types.");
    }

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

    internal static string Display(object value) => value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static string Hex(byte[] bytes)
        => string.Concat(bytes.Select(value =>
            value.ToString("X2", CultureInfo.InvariantCulture)));
}
