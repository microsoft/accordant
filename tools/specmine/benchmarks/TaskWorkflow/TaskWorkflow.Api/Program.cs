// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TaskWorkflow.Api;

using Microsoft.AspNetCore.Http;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<TaskStore>();

        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")))
            .WithName("Health");

        app.MapGet("/openapi.json", () =>
                Results.File(
                    Path.Combine(AppContext.BaseDirectory, "openapi.json"),
                    "application/json"))
            .WithName("GetOpenApiDocument");

        app.MapPost("/tasks", (CreateTaskRequest request, TaskStore store) =>
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                {
                    return Results.BadRequest(
                        new ErrorResponse("validation_error", "Title must not be blank."));
                }

                var task = store.Create(request.Title);
                return Results.Created($"/tasks/{task.Id}", task);
            })
            .WithName("CreateTask");

        app.MapGet("/tasks/{id}", (string id, TaskStore store) =>
            {
                return store.TryGet(id, out var task)
                    ? Results.Ok(task)
                    : Results.NotFound(
                        new ErrorResponse("not_found", $"Task '{id}' was not found."));
            })
            .WithName("GetTask");

        app.MapPost("/tasks/{id}/complete", (string id, TaskStore store) =>
                ToTransitionResult(id, store.Transition(id, "completed")))
            .WithName("CompleteTask");

        app.MapPost("/tasks/{id}/cancel", (string id, TaskStore store) =>
                ToTransitionResult(id, store.Transition(id, "canceled")))
            .WithName("CancelTask");

        app.MapPost("/__test/reset", (TaskStore store) =>
            {
                store.Reset();
                return Results.NoContent();
            })
            .WithName("Reset");

        app.Run();
    }

    private static IResult ToTransitionResult(string id, TransitionResult result) =>
        result.Outcome switch
        {
            TransitionOutcome.Success => Results.Ok(result.Task),
            TransitionOutcome.NotFound => Results.NotFound(
                new ErrorResponse("not_found", $"Task '{id}' was not found.")),
            TransitionOutcome.Conflict => Results.Conflict(
                new ErrorResponse("invalid_transition", "The requested task transition conflicts with its current status.")),
            _ => throw new InvalidOperationException($"Unknown transition outcome '{result.Outcome}'.")
        };
}
