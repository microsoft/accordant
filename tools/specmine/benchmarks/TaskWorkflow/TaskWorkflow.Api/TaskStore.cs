// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TaskWorkflow.Api;

using System.Collections.Concurrent;

public sealed class TaskStore
{
    private const string Pending = "pending";
    private readonly ConcurrentDictionary<string, TaskResponse> _tasks = new();

    public TaskResponse Create(string title)
    {
        while (true)
        {
            var id = Guid.NewGuid().ToString("D");
            var task = new TaskResponse(id, title, Pending);

            if (_tasks.TryAdd(id, task))
            {
                return task;
            }
        }
    }

    public bool TryGet(string id, out TaskResponse? task) =>
        _tasks.TryGetValue(id, out task);

    public TransitionResult Transition(string id, string targetStatus)
    {
        while (true)
        {
            if (!_tasks.TryGetValue(id, out var current))
            {
                return TransitionResult.NotFound();
            }

            if (current.Status == targetStatus)
            {
                return TransitionResult.Success(current);
            }

            if (current.Status != Pending)
            {
                return TransitionResult.Conflict();
            }

            var updated = current with { Status = targetStatus };
            if (_tasks.TryUpdate(id, updated, current))
            {
                return TransitionResult.Success(updated);
            }
        }
    }

    public void Reset() => _tasks.Clear();
}

public sealed record TransitionResult(TaskResponse? Task, TransitionOutcome Outcome)
{
    public static TransitionResult Success(TaskResponse task) =>
        new(task, TransitionOutcome.Success);

    public static TransitionResult NotFound() =>
        new(null, TransitionOutcome.NotFound);

    public static TransitionResult Conflict() =>
        new(null, TransitionOutcome.Conflict);
}

public enum TransitionOutcome
{
    Success,
    NotFound,
    Conflict
}
