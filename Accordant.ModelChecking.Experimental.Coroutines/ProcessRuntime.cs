// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// Structured process workflow primitives and frame execution.

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

/// <summary>Identifies the kind of a model-workflow checkpoint.</summary>
public enum ModelCheckpointKind
{
    Read,
    Choose,
    ChooseStep,
    Step,

    /// <summary>
    /// A guarded suspension. Like <see cref="Read"/> it is internal frame
    /// discovery and creates no graph edge, but the process cannot advance
    /// past it until its predicate holds on the state the process is applied
    /// to. Once passed, the (optional) captured scalar is historical and is
    /// returned from frame history verbatim; only a pending, not-yet-passed <c>When</c> is
    /// re-evaluated against live shared state.
    /// </summary>
    When,

    /// <summary>
    /// An internal structured call boundary. Calls create process frames and do
    /// not become graph edges.
    /// </summary>
    Call,

    /// <summary>
    /// An internal structured infinite-iteration boundary. Forever frames do
    /// not become graph edges.
    /// </summary>
    Forever
}

/// <summary>
/// A completed checkpoint in one structured process frame. Values are limited
/// to immutable scalars so continuation history cannot retain a mutable
/// reference into model state.
/// </summary>
public sealed class ProcessCheckpointRecord
{
    internal ProcessCheckpointRecord(
        ModelCheckpointKind kind,
        string name,
        object value,
        string location = null,
        string discriminator = null)
    {
        Kind = kind;
        Name = name;
        Value = ScalarValues.ValidateRecorded(kind, name, value);
        Location = location;
        Discriminator = discriminator;
        Identity = CheckpointIdentity.Create(kind, name, location, discriminator);
        Description = CheckpointIdentity.Describe(kind, name, location);
    }

    /// <summary>The checkpoint kind.</summary>
    public ModelCheckpointKind Kind { get; }

    /// <summary>The user-supplied stable checkpoint name.</summary>
    public string Name { get; }

    /// <summary>The immutable scalar value recorded for this checkpoint.</summary>
    public object Value { get; }

    /// <summary>
    /// The collision-safe checkpoint identity. It is an opaque encoded string;
    /// use <see cref="Description"/> in messages.
    /// </summary>
    public string Identity { get; }

    /// <summary>A human-readable description of the checkpoint.</summary>
    public string Description { get; }

    /// <summary>
    /// The source location of a structured frame boundary, or null for an
    /// ordinary checkpoint.
    /// </summary>
    public string Location { get; }

    internal string Discriminator { get; }

    /// <inheritdoc/>
    public override string ToString()
        => Description + "=" + ScalarValues.ValueIdentity(Value);
}

/// <summary>
/// Immutable checkpoint history for one structured process frame. History is
/// copied when a graph branch is created; records are never mutated after
/// recording.
/// </summary>
internal sealed class ProcessFrameHistory
{
    private readonly ReadOnlyCollection<ProcessCheckpointRecord> entries;

    internal ProcessFrameHistory() : this(new List<ProcessCheckpointRecord>())
    {
    }

    private ProcessFrameHistory(List<ProcessCheckpointRecord> entries)
    {
        this.entries = new ReadOnlyCollection<ProcessCheckpointRecord>(entries);
    }

    internal IReadOnlyList<ProcessCheckpointRecord> Records => entries;

    internal ProcessFrameHistory Append(
        ModelCheckpointKind kind,
        string name,
        object value,
        string location = null,
        string discriminator = null)
    {
        var copy = new List<ProcessCheckpointRecord>(entries)
        {
            new ProcessCheckpointRecord(kind, name, value, location, discriminator)
        };
        return new ProcessFrameHistory(copy);
    }

    internal string Identity
        => entries.Count == 0
            ? "start"
            : Identifiers.Join(entries
                .SelectMany(entry => new[] { entry.Identity, ScalarValues.ValueIdentity(entry.Value) })
                .ToArray());

    internal string Describe()
        => entries.Count == 0
            ? "an empty checkpoint history"
            : string.Join(" -> ", entries.Select(entry => entry.ToString()));
}

/// <summary>Thrown when a structured model workflow cannot be compiled safely.</summary>
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
/// The custom async return type for experimental structured model processes.
/// It is not a general-purpose task: incomplete awaits must be ModelContext
/// checkpoints, and checkpoint-bearing helpers must be invoked through Call.
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

    /// <summary>Whether the model workflow has completed.</summary>
    public bool IsCompleted => execution != null && execution.Completed;

    /// <summary>
    /// Directly awaiting a ModelTask is unsupported; use
    /// <c>ModelContext.Call</c>.
    /// </summary>
    public void GetResult()
    {
        throw new ModelDefinitionException(
            "Directly awaiting a ModelTask is not supported. Use ModelContext.Call to invoke " +
            "a checkpoint-bearing helper through an explicit process frame.");
    }

    /// <summary>Incomplete ModelTask continuations are not supported.</summary>
    public void OnCompleted(Action continuation)
    {
        throw new ModelDefinitionException(
            "Awaiting an incomplete ModelTask is not supported by the structured process runtime.");
    }
}

/// <summary>
/// A scalar-returning model workflow task. It may be used as the return type
/// of a helper invoked through <c>ModelContext.Call</c>.
/// It is not a general-purpose task and cannot be awaited directly.
/// </summary>
[AsyncMethodBuilder(typeof(ModelTaskMethodBuilder<>))]
public readonly struct ModelTask<TValue>
{
    private readonly ModelExecution execution;

    internal ModelTask(ModelExecution execution)
    {
        this.execution = execution;
    }

    internal ModelExecution Execution => execution;

    /// <summary>Gets an awaiter for compiler compatibility.</summary>
    public ModelTaskAwaiter<TValue> GetAwaiter() => new ModelTaskAwaiter<TValue>(execution);
}

/// <summary>Awaiter for <see cref="ModelTask{TValue}"/>.</summary>
public readonly struct ModelTaskAwaiter<TValue> : INotifyCompletion
{
    private readonly ModelExecution execution;

    internal ModelTaskAwaiter(ModelExecution execution)
    {
        this.execution = execution;
    }

    /// <summary>Whether the model workflow has completed.</summary>
    public bool IsCompleted => execution != null && execution.Completed;

    /// <summary>Direct ModelTask awaits are unsupported; use ModelContext.Call.</summary>
    public TValue GetResult()
    {
        throw new ModelDefinitionException(
            "Directly awaiting a ModelTask is not supported. Use ModelContext.Call to invoke " +
            "a checkpoint-bearing helper through an explicit process frame.");
    }

    /// <summary>Incomplete ModelTask continuations are not supported.</summary>
    public void OnCompleted(Action continuation)
    {
        throw new ModelDefinitionException(
            "Directly awaiting a ModelTask is not supported. Use ModelContext.Call.");
    }
}

/// <summary>Custom async method builder used by <see cref="ModelTask"/>.</summary>
public struct ModelTaskMethodBuilder
{
    private ModelExecution execution;

    /// <summary>Creates a builder.</summary>
    public static ModelTaskMethodBuilder Create()
        => new ModelTaskMethodBuilder { execution = new ModelExecution() };

    /// <summary>Gets the model workflow task.</summary>
    public ModelTask Task => new ModelTask(execution);

    /// <summary>Starts the synchronous segment of a model workflow.</summary>
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

    /// <summary>Marks the model workflow complete.</summary>
    public void SetResult() => execution.Complete(ModelUnitValue.Instance);

    /// <summary>Records a model workflow exception.</summary>
    public void SetException(Exception exception) => execution.Fault(exception);

    /// <summary>
    /// Suspends only at a model checkpoint. Other incomplete awaiters are
    /// rejected rather than allowing time, I/O, or scheduler state into deterministic
    /// frame re-execution.
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
                "ModelTask only supports incomplete awaits of ModelContext checkpoints. " +
                $"The workflow suspended on '{typeof(TAwaiter).FullName}', which is an external " +
                "asynchronous await and cannot be re-executed deterministically. " +
                "Use ModelContext.Call for nested " +
                "checkpoint-bearing helpers.");
        }
    }
}

/// <summary>Custom async method builder for <see cref="ModelTask{TValue}"/>.</summary>
public struct ModelTaskMethodBuilder<TValue>
{
    private ModelExecution execution;

    /// <summary>Creates a builder.</summary>
    public static ModelTaskMethodBuilder<TValue> Create()
        => new ModelTaskMethodBuilder<TValue> { execution = new ModelExecution() };

    /// <summary>Gets the model workflow task.</summary>
    public ModelTask<TValue> Task => new ModelTask<TValue>(execution);

    /// <summary>Starts the synchronous segment of a model workflow.</summary>
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

    /// <summary>Marks the model workflow complete with a scalar result.</summary>
    public void SetResult(TValue result) => execution.Complete(result);

    /// <summary>Records a model workflow exception.</summary>
    public void SetException(Exception exception) => execution.Fault(exception);

    /// <summary>Suspends only at a model checkpoint.</summary>
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
                "ModelTask only supports incomplete awaits of ModelContext checkpoints. " +
                $"The workflow suspended on '{typeof(TAwaiter).FullName}', which cannot be " +
                "re-executed deterministically. " +
                "Use ModelContext.Call for nested model helpers.");
        }
    }
}

/// <summary>
/// A context supplied to a structured model workflow. It deliberately exposes no State
/// property; state can only be inspected by Read/Choose selectors or changed
/// by a scheduled Step action.
/// </summary>
public sealed class ModelContext<TState>
    where TState : State
{
    private readonly TState state;
    private readonly ProcessFrameHistory history;
    private readonly ProcessRuntimeOptions options;
    private readonly bool probeValues;
    private readonly bool evaluateNewCheckpoints;
    private int checkpointIndex;

    internal ModelContext(
        TState state,
        ProcessFrameHistory history,
        ProcessRuntimeOptions options,
        bool probeValues,
        bool evaluateNewCheckpoints = true)
    {
        this.state = state;
        this.history = history;
        this.options = options;
        this.probeValues = probeValues;
        this.evaluateNewCheckpoints = evaluateNewCheckpoints;
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

    /// <summary>
    /// Creates one visible edge per finite scalar alternative. Each edge both
    /// records and returns its selected value and applies the corresponding
    /// state mutation atomically; no state-neutral Choose configuration is
    /// introduced.
    /// </summary>
    public ModelAwaitable<TValue> ChooseStep<TValue, TAction>(
        string stableName,
        TAction semanticAction,
        IEnumerable<TValue> choices,
        Action<TState, TValue> then,
        Func<TValue, object> subject = null)
        where TAction : struct, Enum
    {
        if (choices == null) throw new ArgumentNullException(nameof(choices));
        return ChooseStep(stableName, semanticAction, _ => choices, then, subject);
    }

    /// <summary>
    /// Creates one atomic visible edge per finite scalar alternative derived
    /// from the state against which the process is currently scheduled.
    /// </summary>
    public ModelAwaitable<TValue> ChooseStep<TValue, TAction>(
        string stableName,
        TAction semanticAction,
        Func<TState, IEnumerable<TValue>> choices,
        Action<TState, TValue> then,
        Func<TValue, object> subject = null)
        where TAction : struct, Enum
    {
        if (choices == null) throw new ArgumentNullException(nameof(choices));
        if (then == null) throw new ArgumentNullException(nameof(then));
        return AtCheckpoint<TValue>(
            ModelCheckpointKind.ChooseStep,
            stableName,
            () => MaterializeChoices(stableName, choices(state)),
            new Action<TState, object>((next, value) => then(next, (TValue)value)),
            semanticAction: semanticAction,
            actionIdentity: DelegateIdentity.Create(then),
            choiceSubject: subject == null
                ? null
                : new Func<object, object>(value =>
                    ScalarValues.Validate(subject((TValue)value), stableName)));
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
    /// <paramref name="stableName"/> remains the checkpoint's stable identity.
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
    /// Schedules one visible state mutation whose typed semantic action tag also
    /// generates the checkpoint's stable identity, so a naturally
    /// sequential step needs no duplicated string. The generated name is a
    /// collision-safe function of the enum type and value (for example
    /// <c>WalAction.FlushCommit</c>); fairness and refinement read the typed
    /// <see cref="ProcessTransition.SemanticAction"/>, never this generated name.
    /// </summary>
    public ModelAwaitable<ModelUnit> Step<TAction>(
        TAction semanticAction,
        Action<TState> action,
        object subject = null)
        where TAction : struct, Enum
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        var stableName = EnumCheckpointName.Of(semanticAction);
        return AtCheckpoint<ModelUnit>(
            ModelCheckpointKind.Step,
            stableName,
            () => ModelUnitValue.Instance,
            action,
            semanticAction: semanticAction,
            subject: ScalarValues.Validate(subject, stableName));
    }

    /// <summary>
    /// A guarded atomic step: the sole primitive for claiming a shared resource.
    /// The <paramref name="when"/> guard is re-evaluated against the live state
    /// every time the process is considered; while it is false the process is
    /// blocked and contributes no edge or checkpoint-history entry, and when it holds the
    /// guard and the <paramref name="then"/> mutation are one indivisible visible
    /// transition. Because the guard leaves no passed-guard entry in frame history
    /// until the step is actually taken, a historical guard can never fire stale
    /// after another process interleaves and invalidates it.
    ///
    /// <para>This is emphatically <em>not</em> a defensive guard to insert
    /// between naturally sequential local steps of one workflow: ordinary
    /// sequential code uses plain <see cref="Step{TAction}(TAction, Action{TState}, object)"/>
    /// and lets the model expose any invalidated assumption as a real bug. Use
    /// <c>StepWhen</c> only where independent processes atomically contend for a
    /// shared slot (for example admitting one transaction into a capacity-one
    /// request table). Like <see cref="When"/> it requires a
    /// <see cref="ProcessSystemModel{TState}"/>, which supplies the scheduler
    /// that can interleave another independently active process.</para>
    /// </summary>
    public ModelAwaitable<ModelUnit> StepWhen<TAction>(
        TAction semanticAction,
        Func<TState, bool> when,
        Action<TState> then,
        object subject = null)
        where TAction : struct, Enum
    {
        if (when == null) throw new ArgumentNullException(nameof(when));
        if (then == null) throw new ArgumentNullException(nameof(then));
        var stableName = EnumCheckpointName.Of(semanticAction);
        return AtCheckpoint<ModelUnit>(
            ModelCheckpointKind.Step,
            stableName,
            () => ModelUnitValue.Instance,
            then,
            guard: () => when(state),
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
    /// later frame re-execution.
    /// </summary>
    public ModelAwaitable<ModelUnit> When(string stableName, Func<TState, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
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
    /// later frame re-execution returns it verbatim, while a still-pending wait re-evaluates
    /// <paramref name="ready"/> against live shared state.
    /// </summary>
    public ModelAwaitable<TValue> WaitUntil<TValue>(
        string stableName,
        Func<TState, bool> ready,
        Func<TState, TValue> capture)
    {
        if (ready == null) throw new ArgumentNullException(nameof(ready));
        if (capture == null) throw new ArgumentNullException(nameof(capture));
        return AtCheckpoint<TValue>(
            ModelCheckpointKind.When,
            stableName,
            () => ScalarValues.Validate(capture(state), stableName),
            null,
            guard: () => ready(state));
    }

    /// <summary>
    /// Invokes a checkpoint-bearing helper through an explicit process frame.
    /// Call and return are internal administrative progress; only checkpoints
    /// reached by the helper become graph edges.
    /// </summary>
    public ModelAwaitable<ModelUnit> Call(
        string stableName,
        Func<ModelContext<TState>, ModelTask> helper,
        [CallerMemberName] string callerMember = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerFilePath] string callerFile = "")
    {
        if (helper == null) throw new ArgumentNullException(nameof(helper));
        var location = CheckpointIdentity.Location(callerMember, callerLine, callerFile);
        var workflow = ModelWorkflow<TState>.ForCall(stableName, location, helper);
        return AtCheckpoint<ModelUnit>(
            ModelCheckpointKind.Call,
            stableName,
            unknownValue: null,
            action: null,
            location: location,
            structured: new StructuredCall<TState>(workflow),
            discriminator: workflow.Identity);
    }

    /// <summary>
    /// Invokes a helper with one explicitly recorded immutable scalar argument.
    /// This overload avoids retaining an opaque compiler closure for a local
    /// value that must survive helper interleavings.
    /// </summary>
    public ModelAwaitable<ModelUnit> Call<TArgument>(
        string stableName,
        TArgument argument,
        Func<ModelContext<TState>, TArgument, ModelTask> helper,
        [CallerMemberName] string callerMember = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerFilePath] string callerFile = "")
    {
        if (helper == null) throw new ArgumentNullException(nameof(helper));
        var location = CheckpointIdentity.Location(callerMember, callerLine, callerFile);
        var workflow = ModelWorkflow<TState>.ForCall(
            stableName,
            location,
            argument,
            helper);
        return AtCheckpoint<ModelUnit>(
            ModelCheckpointKind.Call,
            stableName,
            unknownValue: null,
            action: null,
            location: location,
            structured: new StructuredCall<TState>(workflow),
            discriminator: workflow.Identity);
    }

    /// <summary>
    /// Invokes a scalar-returning checkpoint-bearing helper through an explicit
    /// process frame.
    /// </summary>
    public ModelAwaitable<TValue> Call<TValue>(
        string stableName,
        Func<ModelContext<TState>, ModelTask<TValue>> helper,
        [CallerMemberName] string callerMember = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerFilePath] string callerFile = "")
    {
        if (helper == null) throw new ArgumentNullException(nameof(helper));
        var location = CheckpointIdentity.Location(callerMember, callerLine, callerFile);
        var workflow = ModelWorkflow<TState>.ForCall(stableName, location, helper);
        return AtCheckpoint<TValue>(
            ModelCheckpointKind.Call,
            stableName,
            unknownValue: null,
            action: null,
            location: location,
            structured: new StructuredCall<TState>(workflow),
            discriminator: workflow.Identity);
    }

    /// <summary>
    /// Invokes a scalar-returning helper with one explicitly recorded immutable
    /// scalar argument.
    /// </summary>
    public ModelAwaitable<TValue> Call<TArgument, TValue>(
        string stableName,
        TArgument argument,
        Func<ModelContext<TState>, TArgument, ModelTask<TValue>> helper,
        [CallerMemberName] string callerMember = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerFilePath] string callerFile = "")
    {
        if (helper == null) throw new ArgumentNullException(nameof(helper));
        var location = CheckpointIdentity.Location(callerMember, callerLine, callerFile);
        var workflow = ModelWorkflow<TState>.ForCall(
            stableName,
            location,
            argument,
            helper);
        return AtCheckpoint<TValue>(
            ModelCheckpointKind.Call,
            stableName,
            unknownValue: null,
            action: null,
            location: location,
            structured: new StructuredCall<TState>(workflow),
            discriminator: workflow.Identity);
    }

    /// <summary>
    /// Defines an infinite structured process body. Each completed iteration is
    /// discarded and replaced directly by a fresh body frame, so
    /// iteration-local checkpoint history cannot accumulate. An iteration must reach a
    /// visible checkpoint before returning.
    /// </summary>
    public ModelTask Forever(
        string stableName,
        Func<ModelContext<TState>, ModelTask> body,
        [CallerMemberName] string callerMember = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerFilePath] string callerFile = "")
    {
        ValidateName(stableName);
        if (body == null) throw new ArgumentNullException(nameof(body));
        var location = CheckpointIdentity.Location(callerMember, callerLine, callerFile);
        var workflow = ModelWorkflow<TState>.ForForever(stableName, location, body);
        var execution = new ModelExecution();
        execution.SetPending(new PendingCheckpoint(
            ModelCheckpointKind.Forever,
            stableName,
            value: null,
            action: null,
            location: location,
            structured: new StructuredForever<TState>(workflow),
            discriminator: workflow.Identity));
        return new ModelTask(execution);
    }

    /// <summary>
    /// Defines an infinite structured body with one explicitly recorded
    /// immutable scalar argument.
    /// </summary>
    public ModelTask Forever<TArgument>(
        string stableName,
        TArgument argument,
        Func<ModelContext<TState>, TArgument, ModelTask> body,
        [CallerMemberName] string callerMember = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerFilePath] string callerFile = "")
    {
        ValidateName(stableName);
        if (body == null) throw new ArgumentNullException(nameof(body));
        var location = CheckpointIdentity.Location(callerMember, callerLine, callerFile);
        var workflow = ModelWorkflow<TState>.ForForever(
            stableName,
            location,
            argument,
            body);
        var execution = new ModelExecution();
        execution.SetPending(new PendingCheckpoint(
            ModelCheckpointKind.Forever,
            stableName,
            value: null,
            action: null,
            location: location,
            structured: new StructuredForever<TState>(workflow),
            discriminator: workflow.Identity));
        return new ModelTask(execution);
    }

    private ModelAwaitable<TValue> AtCheckpoint<TValue>(
        ModelCheckpointKind kind,
        string stableName,
        Func<object> unknownValue,
        object action,
        string location = null,
        Func<bool> guard = null,
        object semanticAction = null,
        object subject = null,
        Func<object, object> choiceSubject = null,
        object structured = null,
        string discriminator = null,
        string actionIdentity = null)
    {
        ValidateName(stableName);
        var execution = ModelRuntime.Current;
        if (execution == null)
        {
            throw new ModelDefinitionException(
                $"{CheckpointIdentity.Describe(kind, stableName, location)} ran without an active " +
                "ModelTask on this thread. Checkpoints may only be called from the body of the " +
                "async ModelTask workflow itself, on the thread that started it.");
        }

        var reached = CheckpointIdentity.Create(kind, stableName, location, discriminator);
        if (checkpointIndex < history.Records.Count)
        {
            var entry = history.Records[checkpointIndex];
            if (entry.Identity != reached)
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' diverged from its frame history at checkpoint " +
                    $"{checkpointIndex}: the history recorded {entry.Description} but the workflow reached " +
                    $"{CheckpointIdentity.Describe(kind, stableName, location)}. " +
                    $"Checkpoint history: {history.Describe()}. Checkpoint kinds, names, and order must be a " +
                    "deterministic function of TState, earlier checkpoint values, and structured-frame values.");
            }

            checkpointIndex++;
            return new ModelAwaitable<TValue>(true, RecordedValue<TValue>(entry));
        }

        if (checkpointIndex > history.Records.Count)
        {
            throw new ModelDefinitionException(
                $"Workflow '{options.WorkflowName}' advanced past the end of its frame history " +
                $"({checkpointIndex} > {history.Records.Count}).");
        }

        if (structured != null)
        {
            execution.SetPending(new PendingCheckpoint(
                kind,
                stableName,
                value: null,
                action: null,
                location: location,
                semanticAction: semanticAction,
                subject: subject,
                structured: structured,
                discriminator: discriminator));
            return new ModelAwaitable<TValue>(false, default);
        }

        // Administrative normalization after a visible edge may re-execute calls,
        // returns, and completed iterations from frame history, but it stops before every new
        // non-structured checkpoint. The process therefore re-creates guards,
        // selectors, choices, and actions from the live shared state only when
        // it is actually scheduled again.
        if (!evaluateNewCheckpoints)
        {
            execution.SetPending(new PendingCheckpoint(
                kind,
                stableName,
                value: null,
                action: action,
                location: location,
                deferred: true,
                semanticAction: semanticAction,
                subject: subject,
                choiceSubject: choiceSubject,
                discriminator: discriminator,
                actionIdentity: actionIdentity));
            return new ModelAwaitable<TValue>(false, default);
        }

        // A pending, not-yet-passed guarded wait blocks the process against the
        // live state. It records no value and produces no history entry.
        if (guard != null && !guard())
        {
            execution.SetPending(new PendingCheckpoint(
                kind,
                stableName,
                value: null,
                action: null,
                location: location,
                blocked: true));
            return new ModelAwaitable<TValue>(false, default);
        }

        var value = unknownValue();
        if (probeValues)
        {
            EnsureValueIsAFunctionOfState(kind, stableName, location, value, unknownValue());
        }

        execution.SetPending(new PendingCheckpoint(
            kind,
            stableName,
            value,
            action,
            location,
            semanticAction: semanticAction,
            subject: subject,
            choiceSubject: choiceSubject,
            discriminator: discriminator,
            actionIdentity: actionIdentity));
        return new ModelAwaitable<TValue>(false, default);
    }

    private TValue RecordedValue<TValue>(ProcessCheckpointRecord entry)
    {
        if (entry.Value == null)
        {
            if (typeof(TValue).IsValueType && Nullable.GetUnderlyingType(typeof(TValue)) == null)
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' recorded a null value for {entry.Description} " +
                    $"but now requests it as non-nullable '{typeof(TValue).FullName}'. " +
                    "A checkpoint's value type must remain stable.");
            }

            return default;
        }

        if (!(entry.Value is TValue typed))
        {
            throw new ModelDefinitionException(
                $"Workflow '{options.WorkflowName}' recorded {entry.Description} as " +
                $"'{entry.Value.GetType().FullName}' but now requests it as '{typeof(TValue).FullName}'. " +
                "A checkpoint's value type must remain stable.");
        }

        return typed;
    }

    private void EnsureValueIsAFunctionOfState(
        ModelCheckpointKind kind,
        string stableName,
        string location,
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
            $"{CheckpointIdentity.Describe(kind, stableName, location)} in workflow " +
            $"'{options.WorkflowName}' is not a function of the frozen model state: evaluating it " +
            $"twice on the same state produced {firstIdentity} and then {secondIdentity}. " +
            "Read and Choose selectors must not use clocks, randomness, I/O, or mutable captured " +
            "variables; move that data into TState or an explicit scalar frame argument.");
    }

    private static void ValidateName(string stableName)
    {
        if (string.IsNullOrWhiteSpace(stableName))
        {
            throw new ModelDefinitionException(
                "Every model checkpoint and structured frame requires a non-empty stable name.");
        }
    }

    private static object MaterializeChoices<TValue>(string name, IEnumerable<TValue> choices)
    {
        if (choices == null)
        {
            throw new ModelDefinitionException($"Choice checkpoint '{name}' returned null choices.");
        }

        var values = choices.Select(value => ScalarValues.Validate(value, name)).ToArray();
        if (values.Length == 0)
        {
            throw new ModelDefinitionException($"Choice checkpoint '{name}' requires at least one finite choice.");
        }

        var duplicate = values
            .GroupBy(ScalarValues.Identity, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new ModelDefinitionException(
                $"Choice checkpoint '{name}' contains duplicate value '{duplicate.Key}'.");
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

    /// <summary>Whether frame history supplied this checkpoint value.</summary>
    public bool IsCompleted => completed;

    /// <summary>Returns the checkpoint value supplied by frame history.</summary>
    public TValue GetResult() => value;

    /// <summary>Suspension is driven by structured process graph construction.</summary>
    public void OnCompleted(Action continuation)
    {
    }

    /// <summary>Suspension is driven by structured process graph construction.</summary>
    public void UnsafeOnCompleted(Action continuation)
    {
    }
}

/// <summary>Metadata for a visible structured-process checkpoint edge.</summary>
public sealed class ProcessCheckpointTransition
{
    internal ProcessCheckpointTransition(
        string workflowName,
        ModelCheckpointKind kind,
        string checkpointName,
        object value,
        IReadOnlyList<ProcessCheckpointRecord> checkpointHistory,
        object semanticAction = null,
        object subject = null)
    {
        WorkflowName = workflowName;
        Kind = kind;
        CheckpointName = checkpointName;
        Value = value;
        CheckpointHistory = checkpointHistory;
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
    /// The completed frame-local checkpoint history before this edge was taken. This is the
    /// typed source for locals selected by earlier Choose checkpoints.
    /// </summary>
    public IReadOnlyList<ProcessCheckpointRecord> CheckpointHistory { get; }
    /// <summary>The typed semantic action tag supplied by a Step, or null.</summary>
    public object SemanticAction { get; }
    /// <summary>The optional immutable action subject supplied by a Step.</summary>
    public object Subject { get; }

    /// <inheritdoc/>
    public override string ToString()
        => Kind == ModelCheckpointKind.Choose
            ? $"choose:{CheckpointName}={ScalarValues.Display(Value)}"
            : Kind == ModelCheckpointKind.ChooseStep
                ? $"choose-step:{CheckpointName}={ScalarValues.Display(Value)}"
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
    internal object Result { get; private set; }
    private Exception exception;

    internal void SetPending(PendingCheckpoint pending)
    {
        if (Completed)
        {
            throw new ModelDefinitionException(
                "A process workflow exposed a checkpoint after it completed.");
        }
        if (Pending != null)
        {
            throw new ModelDefinitionException(
                $"A process workflow attempted to expose more than one pending checkpoint: " +
                $"{CheckpointIdentity.Describe(Pending.Kind, Pending.Name, Pending.Location)} is already " +
                $"pending and {CheckpointIdentity.Describe(pending.Kind, pending.Name, pending.Location)} " +
                "was reached without awaiting the first one.");
        }
        Pending = pending;
    }

    internal void Complete(object result)
    {
        Result = result;
        Completed = true;
    }
    internal void Fault(Exception value) => exception = value;

    internal void ThrowIfFaulted()
    {
        if (exception != null)
        {
            if (exception is ModelDefinitionException definitionException)
            {
                throw definitionException;
            }
            throw new ModelDefinitionException(
                $"The process workflow threw while its frame was being re-executed: {exception.Message}",
                exception);
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
        string location,
        bool blocked = false,
        bool deferred = false,
        object semanticAction = null,
        object subject = null,
        Func<object, object> choiceSubject = null,
        object structured = null,
        string discriminator = null,
        string actionIdentity = null)
    {
        Kind = kind;
        Name = name;
        Value = value;
        Action = action;
        ActionIdentity = actionIdentity ?? DelegateIdentity.Create(action as Delegate);
        Location = location;
        Blocked = blocked;
        Deferred = deferred;
        SemanticAction = semanticAction;
        Subject = subject;
        ChoiceSubject = choiceSubject;
        Structured = structured;
        Discriminator = discriminator;
    }

    internal ModelCheckpointKind Kind { get; }
    internal string Name { get; }
    internal object Value { get; }
    internal object Action { get; }
    internal string ActionIdentity { get; }
    internal string Location { get; }
    internal object SemanticAction { get; }
    internal object Subject { get; }
    internal Func<object, object> ChoiceSubject { get; }
    internal object Structured { get; }
    internal string Discriminator { get; }

    /// <summary>
    /// Whether this is a guarded wait whose predicate does not yet hold, so the
    /// process is blocked and contributes no transition.
    /// </summary>
    internal bool Blocked { get; }

    /// <summary>
    /// Whether administrative normalization stopped before evaluating this
    /// checkpoint against shared state.
    /// </summary>
    internal bool Deferred { get; }

    internal object SubjectFor(object choice)
        => ChoiceSubject == null ? Subject : ChoiceSubject(choice);
}

internal sealed class ProcessRuntimeOptions
{
    internal ProcessRuntimeOptions(
        string workflowName,
        bool verifyDeterminism,
        int maxInternalCheckpoints,
        CapturedInputMonitor captures)
    {
        WorkflowName = workflowName;
        VerifyDeterminism = verifyDeterminism;
        MaxInternalCheckpoints = maxInternalCheckpoints;
        Captures = captures;
    }

    internal string WorkflowName { get; }

    internal bool VerifyDeterminism { get; }

    internal int MaxInternalCheckpoints { get; }

    internal CapturedInputMonitor Captures { get; }

    internal ProcessRuntimeOptions ForFrame(string workflowName, CapturedInputMonitor captures)
        => new ProcessRuntimeOptions(
            workflowName,
            VerifyDeterminism,
            MaxInternalCheckpoints,
            captures);
}

internal sealed class ProcessFrameAdvance
{
    internal ProcessFrameAdvance(
        ProcessFrameHistory history,
        PendingCheckpoint pending,
        IReadOnlyList<string> trace,
        bool blocked = false,
        object result = null)
    {
        History = history;
        Pending = pending;
        Trace = trace;
        Blocked = blocked;
        Result = result;
    }

    internal ProcessFrameHistory History { get; }
    internal PendingCheckpoint Pending { get; }
    internal IReadOnlyList<string> Trace { get; }
    internal object Result { get; }

    /// <summary>
    /// Whether the segment ended blocked on a guarded wait rather than at a
    /// visible checkpoint or completion. A blocked advance has a null
    /// <see cref="Pending"/> and is not completion: the process is simply not
    /// currently enabled.
    /// </summary>
    internal bool Blocked { get; }
}

internal static class ProcessFrameRunner
{
    internal static ProcessFrameAdvance Advance<TState>(
        ModelWorkflow<TState> process,
        TState state,
        ProcessFrameHistory history,
        ProcessRuntimeOptions options,
        bool evaluateNewCheckpoints = true)
        where TState : State
    {
        if (!options.VerifyDeterminism)
        {
            return Run(
                process,
                state,
                history,
                options,
                probeValues: false,
                evaluateNewCheckpoints);
        }

        var capturedBefore = options.Captures.Snapshot();
        var primary = Run(
            process,
            state,
            history,
            options,
            probeValues: true,
            evaluateNewCheckpoints);
        var audit = RunAudit(
            process,
            state,
            history,
            options,
            evaluateNewCheckpoints);
        EnsureSameTrace(options.WorkflowName, primary, audit);
        options.Captures.EnsureUnchanged(capturedBefore, options.WorkflowName);
        return primary;
    }

    private static ProcessFrameAdvance RunAudit<TState>(
        ModelWorkflow<TState> process,
        TState state,
        ProcessFrameHistory history,
        ProcessRuntimeOptions options,
        bool evaluateNewCheckpoints)
        where TState : State
    {
        try
        {
            return Run(
                process,
                state,
                history,
                options,
                probeValues: false,
                evaluateNewCheckpoints);
        }
        catch (ModelDefinitionException exception)
        {
            throw new ModelDefinitionException(
                $"Workflow '{options.WorkflowName}' failed the frame-determinism audit: re-running the " +
                "same frame segment from the same frozen state and checkpoint history did not behave the same way. " +
                exception.Message,
                exception);
        }
    }

    private static void EnsureSameTrace(
        string workflowName,
        ProcessFrameAdvance primary,
        ProcessFrameAdvance audit)
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
            primary.History.Identity,
            audit.History.Identity,
            StringComparison.Ordinal))
        {
            throw NondeterministicSegment(
                workflowName,
                shared,
                primary.History.Describe(),
                audit.History.Describe());
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
            $"Workflow '{workflowName}' is not deterministic under frame re-execution. Running the same segment " +
            $"twice from the same frozen state and checkpoint history reached {first} on the first run and " +
            $"{second} on the second run at position {index}. Model workflow code may only depend " +
            "on TState, earlier checkpoint values, and recorded structured-frame values; clocks, randomness, " +
            "mutable captured variables, and awaits of foreign already-completed tasks cannot be " +
            "re-executed deterministically.");

    private static ProcessFrameAdvance Run<TState>(
        ModelWorkflow<TState> process,
        TState state,
        ProcessFrameHistory history,
        ProcessRuntimeOptions options,
        bool probeValues,
        bool evaluateNewCheckpoints)
        where TState : State
    {
        var trace = new List<string>();
        for (var internalCheckpoints = 0; ; internalCheckpoints++)
        {
            if (internalCheckpoints == options.MaxInternalCheckpoints)
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' did not reach a visible checkpoint or completion after " +
                    $"{options.MaxInternalCheckpoints} internal Read/When checkpoints. A synchronous " +
                    "loop that only takes internal checkpoints cannot make visible progress; a " +
                    "synchronous loop that takes no checkpoint at all cannot be interrupted by this " +
                    $"bound at all. Last checkpoint history: {history.Describe()}.");
            }

            var context = new ModelContext<TState>(
                state,
                history,
                options,
                probeValues,
                evaluateNewCheckpoints);
            ModelExecution execution;
            var before = state.StringRepresentation(forceRecompute: true);
            try
            {
                execution = process.Invoke(context);
            }
            catch (ModelDefinitionException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ModelDefinitionException(
                    $"The process workflow '{options.WorkflowName}' could not be invoked.",
                    exception);
            }
            var after = state.StringRepresentation(forceRecompute: true);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' mutated shared model state outside " +
                    $"ModelContext.Step: the state changed from '{before}' to '{after}' while the " +
                    "process body ran. Read and Choose selectors and ordinary " +
                    "model workflow code must not mutate captured state references.");
            }

            if (execution == null)
            {
                throw new ModelDefinitionException(
                    $"The process workflow '{options.WorkflowName}' returned a default " +
                    "ModelTask. Declare it as an async ModelTask method.");
            }
            try
            {
                execution.ThrowIfFaulted();
            }
            catch (ModelDefinitionException exception)
            {
                throw new ModelDefinitionException(
                    $"Workflow frame '{options.WorkflowName}' failed while being re-executed. " +
                    exception.Message,
                    exception);
            }

            if (execution.Pending == null)
            {
                if (execution.Completed)
                {
                    if (context.ConsumedCheckpoints != history.Records.Count)
                    {
                        throw new ModelDefinitionException(
                            $"Workflow '{options.WorkflowName}' completed after consuming only " +
                            $"{context.ConsumedCheckpoints} of {history.Records.Count} recorded checkpoints. " +
                            $"Checkpoint history: {history.Describe()}. Frame re-execution must reach every " +
                            "checkpoint it recorded before it may finish.");
                    }

                    trace.Add($"completion after {context.ConsumedCheckpoints} historical checkpoints");
                    return new ProcessFrameAdvance(
                        history,
                        null,
                        trace,
                        result: execution.Result);
                }

                throw new ModelDefinitionException(
                    $"Workflow '{options.WorkflowName}' suspended without a process checkpoint " +
                    "Only this package's checkpoints may suspend a ModelTask.");
            }

            var pending = execution.Pending;
            trace.Add(
                CheckpointIdentity.Describe(pending.Kind, pending.Name, pending.Location) +
                "=" + ScalarValues.ValueIdentity(pending.Value) +
                (pending.Discriminator == null
                    ? string.Empty
                    : $" frame={pending.Discriminator}") +
                (pending.ActionIdentity == null ? string.Empty : $" action={pending.ActionIdentity}") +
                $" after {context.ConsumedCheckpoints} historical checkpoints");

            if (pending.Deferred)
            {
                return new ProcessFrameAdvance(history, pending, trace);
            }

            // A guarded checkpoint whose predicate does not yet hold blocks the
            // process: no visible checkpoint, no completion, no history entry. This
            // covers both a passive When and a StepWhen resource claim.
            if (pending.Blocked)
            {
                return new ProcessFrameAdvance(history, null, trace, blocked: true);
            }

            // A passed guarded wait behaves exactly like a Read: it captures its
            // (optional) scalar into frame history and the segment continues.
            if (pending.Kind == ModelCheckpointKind.Read ||
                pending.Kind == ModelCheckpointKind.When)
            {
                history = history.Append(pending.Kind, pending.Name, pending.Value);
                continue;
            }

            return new ProcessFrameAdvance(history, pending, trace);
        }
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
    internal static string Create(
        ModelCheckpointKind kind,
        string name,
        string location,
        string discriminator = null)
        => Identifiers.Join(
            kind.ToString(),
            name ?? string.Empty,
            HasStructuredLocation(kind) ? location ?? string.Empty : string.Empty,
            discriminator ?? string.Empty);

    internal static string Describe(ModelCheckpointKind kind, string name, string location)
        => HasStructuredLocation(kind)
            ? $"{kind} '{name}' at {location}"
            : $"{kind} checkpoint '{name}'";

    private static bool HasStructuredLocation(ModelCheckpointKind kind)
        => kind == ModelCheckpointKind.Call ||
            kind == ModelCheckpointKind.Forever;

    internal static string Location(string callerMember, int callerLine, string callerFile)
    {
        return Path.GetFileName(callerFile ?? string.Empty) + ":" +
            (callerMember ?? string.Empty) + ":" +
            callerLine.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Derives a stable checkpoint name from a typed semantic action enum. The name
/// combines the enum type and value (for example <c>WalAction.FlushCommit</c>)
/// so two different actions can never collapse to the same continuation identity. It
/// is a diagnostic name only; refinement and fairness read the typed
/// <see cref="ProcessTransition.SemanticAction"/>, never this string.
/// </summary>
internal static class EnumCheckpointName
{
    internal static string Of<TAction>(TAction action)
        where TAction : struct, Enum
        => typeof(TAction).Name + "." + action.ToString();
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

    internal IReadOnlyList<StructuredLocal> SnapshotStructuredLocals(string frameDescription)
    {
        if (!analyzable)
        {
            throw new ModelDefinitionException(
                $"{frameDescription} uses an instance delegate whose captured inputs cannot be " +
                "serialized into a process frame. Use a static helper or a compiler-generated " +
                "closure that captures only supported immutable scalar values; prefer the " +
                "explicit scalar argument overloads of Call or Forever.");
        }

        var locals = new List<StructuredLocal>();
        foreach (var field in fields)
        {
            var value = field.Read();
            if (value != null && !ScalarValues.IsImmutableScalar(value))
            {
                throw new ModelDefinitionException(
                    $"{frameDescription} captures '{field.Name}' as unsupported mutable type " +
                    $"'{value.GetType().FullName}'. Structured process frames may capture only " +
                    "the existing immutable scalar whitelist; use an explicit scalar argument " +
                    "or pass model data through checkpoints.");
            }

            locals.Add(new StructuredLocal(field.Name, value));
        }

        return locals;
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
                $"'{fields[index].Name}' while its process body ran: the value changed from " +
                $"{before[index]} to {now[index]}. Model workflow code must not write to captured " +
                "variables; model that data in TState or pass an explicit scalar frame argument. " +
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
            "Process checkpoint values must be null, strings, primitives, enums, or supported immutable scalar value types.");
    }

    /// <summary>
    /// Re-validates a value at the moment it is recorded in frame history so no
    /// internal path can retain a mutable reference.
    /// </summary>
    internal static object ValidateRecorded(ModelCheckpointKind kind, string name, object value)
    {
        if (value == null || IsImmutableScalar(value))
        {
            return value;
        }

        throw new ModelDefinitionException(
            $"{CheckpointIdentity.Describe(kind, name, null)} cannot record a value of type " +
            $"'{value.GetType().FullName}' in process frame history. Recorded values must be null, strings, " +
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
