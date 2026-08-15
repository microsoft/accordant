// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// Explicit structured continuation frames for ProcessSystemModel.

namespace Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Accordant;

/// <summary>The kind of one explicit structured process continuation frame.</summary>
public enum ProcessContinuationFrameKind
{
    /// <summary>The registered process workflow.</summary>
    Root,

    /// <summary>A helper invoked through <c>ModelContext.Call</c>.</summary>
    Call,

    /// <summary>The current body iteration of <c>ModelContext.Forever</c>.</summary>
    ForeverIteration
}

/// <summary>An immutable scalar captured by a structured helper frame.</summary>
public sealed class ProcessRecordedLocal
{
    internal ProcessRecordedLocal(string name, object value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The compiler-generated captured-local name.</summary>
    public string Name { get; }

    /// <summary>The immutable scalar value retained by the frame.</summary>
    public object Value { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name}={ScalarValues.Display(Value)}";
}

/// <summary>A human-readable snapshot of one structured continuation frame.</summary>
public sealed class ProcessContinuationFrame
{
    internal ProcessContinuationFrame(
        ProcessContinuationFrameKind kind,
        string name,
        string location,
        IReadOnlyList<ProcessRecordedLocal> capturedLocals,
        IReadOnlyList<ProcessCheckpointRecord> checkpointHistory,
        bool hasVisibleProgress)
    {
        Kind = kind;
        Name = name;
        Location = location;
        CapturedLocals = capturedLocals;
        CheckpointHistory = checkpointHistory;
        HasVisibleProgress = hasVisibleProgress;
    }

    /// <summary>The frame kind.</summary>
    public ProcessContinuationFrameKind Kind { get; }

    /// <summary>The stable root, call, or iteration name.</summary>
    public string Name { get; }

    /// <summary>The helper method or structured call-site location.</summary>
    public string Location { get; }

    /// <summary>Immutable scalar locals captured when the frame was created.</summary>
    public IReadOnlyList<ProcessRecordedLocal> CapturedLocals { get; }

    /// <summary>Completed checkpoints local to this frame.</summary>
    public IReadOnlyList<ProcessCheckpointRecord> CheckpointHistory { get; }

    /// <summary>
    /// Whether a Forever iteration has already produced a visible edge.
    /// Always false for other frame kinds.
    /// </summary>
    public bool HasVisibleProgress { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var locals = CapturedLocals.Count == 0
            ? string.Empty
            : $" captures[{string.Join(", ", CapturedLocals)}]";
        var history = CheckpointHistory.Count == 0
            ? string.Empty
            : $" history[{string.Join(" -> ", CheckpointHistory)}]";
        return $"{Kind.ToString().ToLowerInvariant()}:{Name} at {Location}{locals}{history}";
    }
}

internal sealed class StructuredLocal
{
    internal StructuredLocal(string name, object value)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }
    internal object Value { get; }

    internal ProcessRecordedLocal Snapshot()
        => new ProcessRecordedLocal(Name, Value);
}

internal sealed class ModelWorkflow<TState>
    where TState : State
{
    private readonly Func<ModelContext<TState>, ModelExecution> invoke;
    private readonly bool enforceImmutableCaptures;

    private ModelWorkflow(
        string name,
        string location,
        Delegate source,
        Func<ModelContext<TState>, ModelExecution> invoke,
        bool structured,
        IReadOnlyList<StructuredLocal> explicitLocals = null)
    {
        Name = name;
        Location = location ?? MethodLocation(source);
        this.invoke = invoke;
        enforceImmutableCaptures = structured;
        Captures = CapturedInputMonitor.Create(source);
        var capturedLocals = structured
            ? CaptureStructuredLocals(source, Captures, name, location)
            : new List<StructuredLocal>();
        Locals = capturedLocals
            .Concat(explicitLocals ?? Array.Empty<StructuredLocal>())
            .ToList();
        Identity = Identifiers.Join(
            DelegateIdentity.Create(source) ?? string.Empty,
            source.GetType().AssemblyQualifiedName ?? source.GetType().FullName,
            Identifiers.Join(Locals
                .SelectMany(local => new[]
                {
                    local.Name,
                    ScalarValues.ValueIdentity(local.Value)
                })
                .ToArray()));
    }

    internal string Name { get; }
    internal string Location { get; }
    internal CapturedInputMonitor Captures { get; }
    internal IReadOnlyList<StructuredLocal> Locals { get; }
    internal string Identity { get; }

    internal ModelExecution Invoke(ModelContext<TState> context)
    {
        if (!enforceImmutableCaptures)
        {
            return invoke(context);
        }

        var before = Captures.Snapshot();
        var execution = invoke(context);
        Captures.EnsureUnchanged(before, $"structured frame '{Name}'");
        return execution;
    }

    internal static ModelWorkflow<TState> ForRoot(
        string name,
        Func<ModelContext<TState>, ModelTask> workflow)
    {
        if (workflow == null) throw new ArgumentNullException(nameof(workflow));
        return new ModelWorkflow<TState>(
            name,
            location: null,
            workflow,
            context => workflow(context).Execution,
            structured: false);
    }

    internal static ModelWorkflow<TState> ForCall(
        string name,
        string location,
        Func<ModelContext<TState>, ModelTask> workflow)
        => new ModelWorkflow<TState>(
            name,
            location,
            workflow,
            context => workflow(context).Execution,
            structured: true);

    internal static ModelWorkflow<TState> ForCall<TArgument>(
        string name,
        string location,
        TArgument argument,
        Func<ModelContext<TState>, TArgument, ModelTask> workflow)
        => new ModelWorkflow<TState>(
            name,
            location,
            workflow,
            context => workflow(context, argument).Execution,
            structured: true,
            explicitLocals: new[]
            {
                new StructuredLocal(
                    "argument",
                    ScalarValues.Validate(argument, name))
            });

    internal static ModelWorkflow<TState> ForCall<TValue>(
        string name,
        string location,
        Func<ModelContext<TState>, ModelTask<TValue>> workflow)
        => new ModelWorkflow<TState>(
            name,
            location,
            workflow,
            context => workflow(context).Execution,
            structured: true);

    internal static ModelWorkflow<TState> ForCall<TArgument, TValue>(
        string name,
        string location,
        TArgument argument,
        Func<ModelContext<TState>, TArgument, ModelTask<TValue>> workflow)
        => new ModelWorkflow<TState>(
            name,
            location,
            workflow,
            context => workflow(context, argument).Execution,
            structured: true,
            explicitLocals: new[]
            {
                new StructuredLocal(
                    "argument",
                    ScalarValues.Validate(argument, name))
            });

    internal static ModelWorkflow<TState> ForForever(
        string name,
        string location,
        Func<ModelContext<TState>, ModelTask> workflow)
        => new ModelWorkflow<TState>(
            name,
            location,
            workflow,
            context => workflow(context).Execution,
            structured: true);

    internal static ModelWorkflow<TState> ForForever<TArgument>(
        string name,
        string location,
        TArgument argument,
        Func<ModelContext<TState>, TArgument, ModelTask> workflow)
        => new ModelWorkflow<TState>(
            name,
            location,
            workflow,
            context => workflow(context, argument).Execution,
            structured: true,
            explicitLocals: new[]
            {
                new StructuredLocal(
                    "argument",
                    ScalarValues.Validate(argument, name))
            });

    private static IReadOnlyList<StructuredLocal> CaptureStructuredLocals(
        Delegate source,
        CapturedInputMonitor captures,
        string name,
        string location)
    {
        if (source.Target == null)
        {
            return new List<StructuredLocal>();
        }

        return captures.SnapshotStructuredLocals(
            $"Structured frame '{name}' at {location}");
    }

    private static string MethodLocation(Delegate source)
    {
        var method = source.Method;
        return (method.DeclaringType?.FullName ?? "<unknown>") + "." + method.Name;
    }
}

internal sealed class StructuredCall<TState>
    where TState : State
{
    internal StructuredCall(ModelWorkflow<TState> workflow)
    {
        Workflow = workflow;
    }

    internal ModelWorkflow<TState> Workflow { get; }
}

internal sealed class StructuredForever<TState>
    where TState : State
{
    internal StructuredForever(ModelWorkflow<TState> workflow)
    {
        Workflow = workflow;
    }

    internal ModelWorkflow<TState> Workflow { get; }
}

internal sealed class ProcessFrame<TState>
    where TState : State
{
    private ProcessFrame(
        ProcessContinuationFrameKind kind,
        string name,
        string location,
        ModelWorkflow<TState> workflow,
        ProcessFrameHistory history,
        bool hasVisibleProgress)
    {
        Kind = kind;
        Name = name;
        Location = location;
        Workflow = workflow;
        History = history;
        HasVisibleProgress = hasVisibleProgress;
    }

    internal ProcessContinuationFrameKind Kind { get; }
    internal string Name { get; }
    internal string Location { get; }
    internal ModelWorkflow<TState> Workflow { get; }
    internal ProcessFrameHistory History { get; }
    internal bool HasVisibleProgress { get; }

    internal string Identity
        => Identifiers.Join(
            Kind.ToString(),
            Name,
            Location,
            Workflow.Identity,
            History.Identity,
            HasVisibleProgress ? "productive" : "not-productive");

    internal static ProcessFrame<TState> Root(
        string role,
        ModelWorkflow<TState> workflow)
        => new ProcessFrame<TState>(
            ProcessContinuationFrameKind.Root,
            role,
            workflow.Location,
            workflow,
            new ProcessFrameHistory(),
            hasVisibleProgress: false);

    internal static ProcessFrame<TState> Call(ModelWorkflow<TState> workflow)
        => new ProcessFrame<TState>(
            ProcessContinuationFrameKind.Call,
            workflow.Name,
            workflow.Location,
            workflow,
            new ProcessFrameHistory(),
            hasVisibleProgress: false);

    internal static ProcessFrame<TState> Forever(ModelWorkflow<TState> workflow)
        => new ProcessFrame<TState>(
            ProcessContinuationFrameKind.ForeverIteration,
            workflow.Name,
            workflow.Location,
            workflow,
            new ProcessFrameHistory(),
            hasVisibleProgress: false);

    internal ProcessFrame<TState> WithHistory(ProcessFrameHistory history)
        => new ProcessFrame<TState>(
            Kind,
            Name,
            Location,
            Workflow,
            history,
            HasVisibleProgress);

    internal ProcessFrame<TState> WithVisibleProgress()
        => HasVisibleProgress || Kind != ProcessContinuationFrameKind.ForeverIteration
            ? this
            : new ProcessFrame<TState>(
                Kind,
                Name,
                Location,
                Workflow,
                History,
                hasVisibleProgress: true);

    internal ProcessFrame<TState> FreshForeverIteration()
        => new ProcessFrame<TState>(
            Kind,
            Name,
            Location,
            Workflow,
            new ProcessFrameHistory(),
            hasVisibleProgress: false);

    internal ProcessContinuationFrame Snapshot()
        => new ProcessContinuationFrame(
            Kind,
            Name,
            Location,
            new ReadOnlyCollection<ProcessRecordedLocal>(
                Workflow.Locals.Select(local => local.Snapshot()).ToList()),
            History.Records,
            HasVisibleProgress);
}

internal sealed class ProcessContinuation<TState>
    where TState : State
{
    private readonly ReadOnlyCollection<ProcessFrame<TState>> frames;

    private ProcessContinuation(List<ProcessFrame<TState>> frames)
    {
        this.frames = new ReadOnlyCollection<ProcessFrame<TState>>(frames);
    }

    internal IReadOnlyList<ProcessFrame<TState>> Frames => frames;
    internal ProcessFrame<TState> Top => frames[frames.Count - 1];

    internal string Identity
        => Identifiers.Join(frames.Select(frame => frame.Identity).ToArray());

    internal string DisplayIdentity
    {
        get
        {
            if (frames.Count != 1)
            {
                return Identity;
            }

            var records = frames[0].History.Records;
            return records.Count == 0
                ? "start"
                : Identity;
        }
    }

    internal IReadOnlyList<ProcessContinuationFrame> Snapshot()
        => frames.Select(frame => frame.Snapshot()).ToList();

    internal static ProcessContinuation<TState> Root(
        string role,
        ModelWorkflow<TState> workflow)
        => new ProcessContinuation<TState>(
            new List<ProcessFrame<TState>>
            {
                ProcessFrame<TState>.Root(role, workflow)
            });

    internal ProcessContinuation<TState> ReplaceTop(ProcessFrame<TState> frame)
    {
        var copy = frames.ToList();
        copy[copy.Count - 1] = frame;
        return new ProcessContinuation<TState>(copy);
    }

    internal ProcessContinuation<TState> Push(ProcessFrame<TState> frame)
    {
        var copy = frames.ToList();
        copy.Add(frame);
        return new ProcessContinuation<TState>(copy);
    }

    internal ProcessContinuation<TState> ReturnFromCall(object result)
    {
        var call = Top;
        var copy = frames.Take(frames.Count - 1).ToList();
        var parent = copy[copy.Count - 1];
        copy[copy.Count - 1] = parent.WithHistory(parent.History.Append(
            ModelCheckpointKind.Call,
            call.Name,
            result,
            call.Location,
            call.Workflow.Identity));
        return new ProcessContinuation<TState>(copy);
    }

    internal ProcessContinuation<TState> MarkVisibleProgress()
        => new ProcessContinuation<TState>(
            frames.Select(frame => frame.WithVisibleProgress()).ToList());
}

internal sealed class ProcessContinuationAdvance<TState>
    where TState : State
{
    internal ProcessContinuationAdvance(
        ProcessContinuation<TState> continuation,
        PendingCheckpoint pending,
        bool blocked = false,
        bool completed = false)
    {
        Continuation = continuation;
        Pending = pending;
        Blocked = blocked;
        Completed = completed;
    }

    internal ProcessContinuation<TState> Continuation { get; }
    internal PendingCheckpoint Pending { get; }
    internal bool Blocked { get; }
    internal bool Completed { get; }
}

internal static class ProcessContinuationRunner
{
    internal static ProcessContinuationAdvance<TState> Advance<TState>(
        ProcessContinuation<TState> continuation,
        TState state,
        ProcessRuntimeOptions options)
        where TState : State
        => Run(continuation, state, options, evaluateNewCheckpoints: true);

    internal static ProcessContinuationAdvance<TState> Normalize<TState>(
        ProcessContinuation<TState> continuation,
        TState state,
        ProcessRuntimeOptions options)
        where TState : State
        => Run(continuation, state, options, evaluateNewCheckpoints: false);

    internal static ProcessContinuationAdvance<TState> Commit<TState>(
        ProcessContinuation<TState> continuation,
        PendingCheckpoint pending,
        object recordedValue,
        TState resultingState,
        ProcessRuntimeOptions options)
        where TState : State
    {
        var top = continuation.Top;
        var committed = continuation.ReplaceTop(top.WithHistory(top.History.Append(
            pending.Kind,
            pending.Name,
            recordedValue,
            pending.Location,
            pending.Discriminator)));
        committed = committed.MarkVisibleProgress();
        return Normalize(committed, resultingState, options);
    }

    private static ProcessContinuationAdvance<TState> Run<TState>(
        ProcessContinuation<TState> continuation,
        TState state,
        ProcessRuntimeOptions options,
        bool evaluateNewCheckpoints)
        where TState : State
    {
        var current = continuation;

        for (var progress = 0; progress < options.MaxInternalCheckpoints; progress++)
        {
            var frame = current.Top;
            var frameOptions = options.ForFrame(
                DescribePath(current),
                frame.Workflow.Captures);
            var advance = ProcessFrameRunner.Advance(
                frame.Workflow,
                state,
                frame.History,
                frameOptions,
                evaluateNewCheckpoints);
            current = current.ReplaceTop(frame.WithHistory(advance.History));

            if (advance.Blocked)
            {
                return new ProcessContinuationAdvance<TState>(
                    current,
                    pending: null,
                    blocked: true);
            }

            if (advance.Pending != null)
            {
                var pending = advance.Pending;
                if (pending.Deferred)
                {
                    return new ProcessContinuationAdvance<TState>(current, pending);
                }

                if (pending.Kind == ModelCheckpointKind.Call)
                {
                    var call = pending.Structured as StructuredCall<TState>;
                    if (call == null)
                    {
                        throw InvalidStructuredCheckpoint(current, pending);
                    }

                    current = current.Push(ProcessFrame<TState>.Call(call.Workflow));
                    continue;
                }

                if (pending.Kind == ModelCheckpointKind.Forever)
                {
                    var forever = pending.Structured as StructuredForever<TState>;
                    if (forever == null)
                    {
                        throw InvalidStructuredCheckpoint(current, pending);
                    }

                    current = current.Push(ProcessFrame<TState>.Forever(forever.Workflow));
                    continue;
                }

                return new ProcessContinuationAdvance<TState>(current, pending);
            }

            if (frame.Kind == ProcessContinuationFrameKind.Root)
            {
                return new ProcessContinuationAdvance<TState>(
                    current,
                    pending: null,
                    completed: true);
            }

            if (frame.Kind == ProcessContinuationFrameKind.Call)
            {
                var result = ScalarValues.Validate(advance.Result, frame.Name);
                current = current.ReturnFromCall(result);
                continue;
            }

            if (!current.Top.HasVisibleProgress)
            {
                throw new ModelDefinitionException(
                    $"Forever iteration '{frame.Name}' at {frame.Location} completed without a " +
                    "visible Choose, ChooseStep, or Step edge. A structured iteration must offer " +
                    "observable scheduler progress before returning.");
            }

            current = current.ReplaceTop(current.Top.FreshForeverIteration());
        }

        throw new ModelDefinitionException(
            $"Structured workflow '{DescribePath(current)}' did not reach a new non-structured " +
            $"checkpoint, visible edge, block, or completion after " +
            $"{options.MaxInternalCheckpoints} call/return/iteration operations.");
    }

    private static ModelDefinitionException InvalidStructuredCheckpoint<TState>(
        ProcessContinuation<TState> continuation,
        PendingCheckpoint pending)
        where TState : State
        => new ModelDefinitionException(
            $"{CheckpointIdentity.Describe(pending.Kind, pending.Name, pending.Location)} in " +
            $"'{DescribePath(continuation)}' did not carry a valid structured frame descriptor.");

    private static string DescribePath<TState>(ProcessContinuation<TState> continuation)
        where TState : State
        => string.Join(
            " / ",
            continuation.Frames.Select(frame =>
                $"{frame.Kind.ToString().ToLowerInvariant()} '{frame.Name}'"));
}
