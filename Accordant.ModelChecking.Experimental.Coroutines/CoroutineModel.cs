// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Accordant;

/// <summary>Identifies the kind of a coroutine checkpoint.</summary>
public enum ModelCheckpointKind
{
    Read,
    Choose,
    Step
}

/// <summary>
/// A completed, replayable coroutine checkpoint. Values are limited to immutable
/// scalar values so a tape cannot retain a mutable reference into model state.
/// </summary>
public sealed class ReplayEntry
{
    internal ReplayEntry(ModelCheckpointKind kind, string name, object value)
    {
        Kind = kind;
        Name = name;
        Value = value;
        Identity = kind.ToString().ToLowerInvariant() + ":" + name;
    }

    /// <summary>The checkpoint kind.</summary>
    public ModelCheckpointKind Kind { get; }

    /// <summary>The user-supplied stable checkpoint name.</summary>
    public string Name { get; }

    /// <summary>The immutable scalar value recorded for Read or Choose.</summary>
    public object Value { get; }

    /// <summary>The stable checkpoint identity.</summary>
    public string Identity { get; }
}

/// <summary>
/// Immutable-prefix replay history for a coroutine. A tape is copied when a
/// graph branch is created; entries are never mutated after recording.
/// </summary>
public sealed class ReplayTape
{
    private readonly List<ReplayEntry> entries;

    /// <summary>Creates an empty replay tape.</summary>
    public ReplayTape() : this(new List<ReplayEntry>())
    {
    }

    private ReplayTape(List<ReplayEntry> entries)
    {
        this.entries = entries;
    }

    /// <summary>The completed checkpoints in execution order.</summary>
    public IReadOnlyList<ReplayEntry> Entries => entries;

    internal ReplayTape Append(ModelCheckpointKind kind, string name, object value)
    {
        var identity = kind.ToString().ToLowerInvariant() + ":" + name;
        if (entries.Any(entry => entry.Identity == identity))
        {
            throw new ModelDefinitionException(
                $"Checkpoint '{identity}' was reached more than once. " +
                "This experimental front-end supports finite workflows with unique stable checkpoint names.");
        }

        var copy = new List<ReplayEntry>(entries) { new ReplayEntry(kind, name, value) };
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

/// <summary>Thrown when a coroutine cannot be compiled as a finite model workflow.</summary>
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
/// Read, Choose, or Step awaitables.
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
                "ModelTask only supports incomplete awaits of ModelContext.Read, Choose, or Step. " +
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

    private ModelAwaitable<TValue> AtCheckpoint<TValue>(
        ModelCheckpointKind kind,
        string stableName,
        Func<object> unknownValue,
        Action<TState> action)
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
            var expected = kind.ToString().ToLowerInvariant() + ":" + stableName;
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
        execution.SetPending(new PendingCheckpoint(kind, stableName, value, action));
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
            throw new ModelDefinitionException("Every Read, Choose, and Step requires a non-empty stable name.");
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
/// Compiles an experimental finite model coroutine to the ordinary state-graph
/// API. Read checkpoints are internal deterministic discovery; Choose and Step
/// become ordinary visible graph transitions.
/// </summary>
public static class CoroutineModel
{
    /// <summary>
    /// Explores one finite coroutine workflow. Hidden replay control is
    /// deliberately part of graph identity even though it is separate from
    /// <typeparamref name="TState"/>.
    /// </summary>
    public static StateGraphNode Explore<TState>(
        string workflowName,
        TState initialState,
        Func<ModelContext<TState>, ModelTask> process,
        int maxDepth = -1,
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
            steps.Add(new CoroutineStep<TState>(workflowName, process, initial.Tape, initial.Pending));
        }

        return StateGraph.ExploreStateGraph(steps, initialState, maxDepth: maxDepth, lazy: lazy);
    }
}

/// <summary>Metadata for a visible coroutine graph edge.</summary>
public sealed class CoroutineTransition
{
    internal CoroutineTransition(string workflowName, ModelCheckpointKind kind, string checkpointName, object value)
    {
        WorkflowName = workflowName;
        Kind = kind;
        CheckpointName = checkpointName;
        Value = value;
    }

    /// <summary>The stable workflow name.</summary>
    public string WorkflowName { get; }
    /// <summary>The selected checkpoint kind.</summary>
    public ModelCheckpointKind Kind { get; }
    /// <summary>The stable checkpoint name.</summary>
    public string CheckpointName { get; }
    /// <summary>The scalar Choose value, or null for Step.</summary>
    public object Value { get; }

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
    internal PendingCheckpoint(ModelCheckpointKind kind, string name, object value, object action)
    {
        Kind = kind;
        Name = name;
        Value = value;
        Action = action;
    }

    internal ModelCheckpointKind Kind { get; }
    internal string Name { get; }
    internal object Value { get; }
    internal object Action { get; }
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
        for (var reads = 0; ; reads++)
        {
            if (reads == 10000)
            {
                throw new ModelDefinitionException(
                    "Coroutine did not reach Choose, Step, or completion after 10,000 internal Read checkpoints. " +
                    "Only finite workflows are supported.");
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
                    "Coroutine suspended without a Read, Choose, or Step checkpoint.");
            }

            var pending = execution.Pending;
            if (pending.Kind != ModelCheckpointKind.Read)
            {
                return new CoroutineAdvance(tape, pending);
            }

            tape = tape.Append(pending.Kind, pending.Name, pending.Value);
        }
    }
}

internal sealed class CoroutineStep<TState> : BaseStepFunction
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
                    new CoroutineTransition(workflowName, pending.Kind, pending.Name, choice)));
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
                    new CoroutineTransition(workflowName, pending.Kind, pending.Name, null))
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
                new CoroutineStep<TState>(workflowName, process, advance.Tape, advance.Pending)
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
