using Microsoft.Accordant;
using Specmine.Accordant;

namespace TaskWorkflow.ProcessExperiment1.Model;

internal static class TaskWorkflowSpec
{
    public static Spec<TaskWorkflowState> Create()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Add(new CreateTaskOperation());
        spec.Add(new GetTaskOperation());
        spec.Add(new CompleteTaskOperation());
        spec.Add(new CancelTaskOperation());
        return spec;
    }

    private sealed class CreateTaskOperation : Operation<CreateTaskCall, ApiCallResponse, TaskWorkflowState>
    {
        public CreateTaskOperation()
            : base("CreateTask")
        {
        }

        public override ExpectedOutcomes Apply(CreateTaskCall request, TaskWorkflowState state)
        {
            Understanding.Assume(
                request.Body is not null,
                "create-task-body-present",
                "This feature model only covers adapter-generated CreateTask requests that include a body object.");

            var body = request.Body!;

            if (string.IsNullOrWhiteSpace(body.Title))
            {
                return Expect.That(r => ResponseChecks.ValidateBlankTitle(r))
                    .SameState();
            }

            var title = body.Title!;

            return Expect.That(r => ResponseChecks.ValidateCreatedTask(r, state, title))
                .ThenState(
                    (response, nextState) => nextState.AddOrUpdate(ResponseReaders.ReadTask(response.Body)),
                    mock: () => ResponseReaders.MockTaskResponse(
                        "00000000-0000-0000-0000-000000000001",
                        title,
                        TaskStatuses.Pending,
                        201));
        }
    }

    private sealed class GetTaskOperation : Operation<TaskByIdCall, ApiCallResponse, TaskWorkflowState>
    {
        public GetTaskOperation()
            : base("GetTask")
        {
        }

        public override ExpectedOutcomes Apply(TaskByIdCall request, TaskWorkflowState state)
        {
            var id = RequireTaskId(request, "get-task-id-present");

            if (!state.TryGetTask(id, out var existing))
            {
                return Expect.That(r => ResponseChecks.ValidateNotFound(r, id))
                    .SameState();
            }

            return Expect.That(r => ResponseChecks.ValidateTaskRepresentation(r, existing))
                .SameState();
        }
    }

    private sealed class CompleteTaskOperation : Operation<TaskByIdCall, ApiCallResponse, TaskWorkflowState>
    {
        public CompleteTaskOperation()
            : base("CompleteTask")
        {
        }

        public override ExpectedOutcomes Apply(TaskByIdCall request, TaskWorkflowState state) =>
            ApplyTransition(request, state, TaskStatuses.Completed);
    }

    private sealed class CancelTaskOperation : Operation<TaskByIdCall, ApiCallResponse, TaskWorkflowState>
    {
        public CancelTaskOperation()
            : base("CancelTask")
        {
        }

        public override ExpectedOutcomes Apply(TaskByIdCall request, TaskWorkflowState state) =>
            ApplyTransition(request, state, TaskStatuses.Canceled);
    }

    private static ExpectedOutcomes ApplyTransition(
        TaskByIdCall request,
        TaskWorkflowState state,
        string targetStatus)
    {
        var id = RequireTaskId(
            request,
            targetStatus == TaskStatuses.Completed ? "complete-task-id-present" : "cancel-task-id-present");

        if (!state.TryGetTask(id, out var existing))
        {
            return Expect.That<ApiCallResponse>(r => ResponseChecks.ValidateNotFound(r, id))
                .SameState();
        }

        if (string.Equals(existing.Status, targetStatus, StringComparison.Ordinal))
        {
            return Expect.That<ApiCallResponse>(r => ResponseChecks.ValidateTaskRepresentation(r, existing))
                .SameState();
        }

        if (!string.Equals(existing.Status, TaskStatuses.Pending, StringComparison.Ordinal))
        {
            return Expect.That<ApiCallResponse>(r => ResponseChecks.ValidateInvalidTransition(r))
                .SameState();
        }

        var transitioned = existing with { Status = targetStatus };

        return Expect.That<ApiCallResponse>(r => ResponseChecks.ValidateTaskRepresentation(r, transitioned))
            .ThenState<TaskWorkflowState>(
                (response, nextState) => nextState.AddOrUpdate(ResponseReaders.ReadTask(response.Body)),
                mock: () => ResponseReaders.MockTaskResponse(existing.Id, existing.Title, targetStatus, 200));
    }

    private static string RequireTaskId(TaskByIdCall request, string assumptionId)
    {
        Understanding.Assume(
            request.Path is not null && !string.IsNullOrWhiteSpace(request.Path.Id),
            assumptionId,
            "This feature model only covers adapter-generated task-by-id requests that include a nonblank path id.");

        return request.Path!.Id!;
    }
}
