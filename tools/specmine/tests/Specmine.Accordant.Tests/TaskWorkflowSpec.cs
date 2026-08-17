// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant.Tests;

using Microsoft.Accordant;

// A small, self-contained Accordant spec used only by this test project: two operations,
// with a server-generated ID (CreateTask's response) captured into state and later
// consumed by CompleteTask - the exact response-dependent-state shape the tests exercise.

public sealed record CreateTaskRequest(string Title);

public sealed record CreateTaskResponse(string TaskId, string Title, string Status);

public sealed record CompleteTaskRequest(string TaskId);

public sealed record CompleteTaskResponse(string TaskId, string Status);

[State]
public partial class TaskWorkflowState
{
    public Dictionary<string, TaskRecordState> Tasks { get; set; } = new();
}

[State]
public partial class TaskRecordState
{
    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = "Open";
}

/// <summary>
/// Builds the small spec every test in this project replays traces against.
/// </summary>
public static class TaskWorkflowSpec
{
    public static Spec<TaskWorkflowState> Create()
    {
        var spec = new Spec<TaskWorkflowState>();

        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (request, state) =>
        {
            return Expect.That<CreateTaskResponse>(
                    r => r.Title == request.Title && r.Status == "Open" && !string.IsNullOrEmpty(r.TaskId),
                    "Creating a task should return an open task with a server-generated ID and the requested title")
                .ThenState<TaskWorkflowState>(
                    (CreateTaskResponse response, TaskWorkflowState nextState) =>
                        nextState.Tasks[response.TaskId] = new TaskRecordState
                        {
                            Title = request.Title,
                            Status = "Open",
                        },
                    mock: () => new CreateTaskResponse(Guid.NewGuid().ToString(), request.Title, "Open"));
        });

        spec.Operation<CompleteTaskRequest, CompleteTaskResponse>("CompleteTask", (request, state) =>
        {
            if (!state.Tasks.TryGetValue(request.TaskId, out var task))
            {
                return Expect.That<CompleteTaskResponse>(
                        r => r.TaskId == request.TaskId && r.Status == "NotFound",
                        $"Task '{request.TaskId}' does not exist -> expect NotFound")
                    .SameState();
            }

            return Expect.That<CompleteTaskResponse>(
                    r => r.TaskId == request.TaskId && r.Status == "Completed",
                    $"Task '{request.TaskId}' exists (status '{task.Status}') -> expect it to be marked Completed")
                .ThenState<TaskWorkflowState>(
                    nextState => nextState.Tasks[request.TaskId].Status = "Completed");
        });

        return spec;
    }

    public static TaskWorkflowState InitialState() => new();
}
