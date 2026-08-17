// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using System.Text.Json;

/// <summary>
/// A small, self-contained custom adapter used as a proof that the SDK's adapter/session
/// contracts do not require any real transport: the target here is nothing but an
/// in-process dictionary. It is deliberately simple - a couple of task lifecycle
/// operations - but exercises every part of the contract a real adapter would: adapter-
/// owned settings interpretation, session state held across calls, JSON Schema-described
/// operations, and explicit session disposal.
/// </summary>
internal sealed class InMemoryTaskAdapter : ITargetAdapter
{
    /// <inheritdoc/>
    public string AdapterType => "in-memory-task";

    /// <summary>
    /// Interprets this adapter's own settings shape - an optional string <c>idPrefix</c>
    /// used to prefix server-generated task IDs - and connects to a fresh in-memory
    /// session. No workspace or SDK type knows about <c>idPrefix</c>; it is entirely this
    /// adapter's business.
    /// </summary>
    public Task<ITargetSession> ConnectAsync(JsonElement settings, CancellationToken cancellationToken = default)
    {
        if (settings.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Settings must be a valid JSON value.", nameof(settings));
        }

        var idPrefix = "task";
        if (settings.ValueKind == JsonValueKind.Object &&
            settings.TryGetProperty("idPrefix", out var idPrefixElement))
        {
            idPrefix = idPrefixElement.ValueKind == JsonValueKind.String
                ? idPrefixElement.GetString()!
                : throw new ArgumentException("'idPrefix' must be a string.", nameof(settings));
        }

        return Task.FromResult<ITargetSession>(new InMemoryTaskSession(idPrefix));
    }
}

// Ordinary C# request/response records used only internally by InMemoryTaskSession - they
// never cross the ITargetSession boundary, which only ever exchanges JsonElement values.
internal sealed record CreateTaskRequest(string Title);

internal sealed record TaskIdRequest(string Id);

internal sealed record TaskResponse(string Id, string Title, string Status);

/// <summary>
/// The live session <see cref="InMemoryTaskAdapter"/> connects to: an in-process
/// dictionary of tasks, mutated in place by ordinary C# records, exposed as two JSON
/// Schema-described operations.
/// </summary>
internal sealed class InMemoryTaskSession : TargetSessionBase
{
    private readonly string _idPrefix;

    // Ordinary mutable C# state - a plain dictionary of records - is exactly what a real
    // in-process target (as opposed to an HTTP or OpenAPI-described one) looks like.
    private readonly Dictionary<string, TaskResponse> _tasks = new();
    private int _nextId = 1;

    /// <summary>Whether <see cref="DisposeAsync"/> has been called on this session.</summary>
    public bool Disposed { get; private set; }

    public InMemoryTaskSession(string idPrefix)
        : base(new[]
        {
            OperationDefinition.Create<CreateTaskRequest, TaskResponse>("CreateTask"),
            OperationDefinition.Create<TaskIdRequest, TaskResponse>("CompleteTask"),
        })
    {
        _idPrefix = idPrefix;
    }

    /// <inheritdoc/>
    protected override Task<JsonElement> ExecuteOperationAsync(
        OperationDefinition operation, JsonElement request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return operation.Name switch
        {
            "CreateTask" => Task.FromResult(CreateTask(request)),
            "CompleteTask" => Task.FromResult(CompleteTask(request)),
            _ => throw new InvalidOperationException($"No handler registered for operation '{operation.Name}'."),
        };
    }

    private JsonElement CreateTask(JsonElement request)
    {
        var typedRequest = JsonSerializer.Deserialize<CreateTaskRequest>(request)
            ?? throw new ArgumentException("CreateTask request must be a JSON object.", nameof(request));

        var id = $"{_idPrefix}-{_nextId++}";
        var task = new TaskResponse(id, typedRequest.Title, "open");
        _tasks[id] = task;

        return JsonSerializer.SerializeToElement(task);
    }

    private JsonElement CompleteTask(JsonElement request)
    {
        var typedRequest = JsonSerializer.Deserialize<TaskIdRequest>(request)
            ?? throw new ArgumentException("CompleteTask request must be a JSON object.", nameof(request));

        if (!_tasks.TryGetValue(typedRequest.Id, out var task))
        {
            throw new InvalidOperationException($"Task '{typedRequest.Id}' was not found.");
        }

        var completed = task with { Status = "completed" };
        _tasks[typedRequest.Id] = completed;

        return JsonSerializer.SerializeToElement(completed);
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
