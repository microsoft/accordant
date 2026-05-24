// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using System.Collections.Generic;
using Microsoft.Accordant;
using TodoList.FaultInjection.Api.Contracts;

/// <summary>
/// CREATE TODO operation.
/// 
/// When response is null (indefinite failure), response?.Data?.Field returns null,
/// so timestamps become unknown automatically.
/// </summary>
public class CreateTodoOperation : TodoApiOperation<Todo, Todo, AppState>
{
    public CreateTodoOperation() : base("CreateTodo") { }

    protected override ExpectedOutcomes ApplyInternal(Todo request, AppState state)
    {
        if (!state.Users.ContainsKey(request.UserId))
        {
            return Expect.That((ApiResult<Todo> r) => r.IsNotFound,
                "User not found")
                .SameState();
        }

        var user = state.Users[request.UserId];
        if (user.Todos.ContainsKey(request.TodoId))
        {
            return Expect.That((ApiResult<Todo> r) => r.IsConflict,
                "Todo already exists")
                .SameState();
        }

        // Read timestamps from response (server-generated)
        // response?.Data?.Field handles null response (indefinite failure) gracefully
        return Expect.That((ApiResult<Todo> r) => r.IsSuccess && r.Data?.TodoId == request.TodoId,
            "Should create todo")
            .ThenState(
                (ApiResult<Todo> response, AppState next) =>
                {
                    next.Users[request.UserId].Todos[request.TodoId] = new TodoState
                    {
                        Title = request.Title,
                        CreatedAt = response?.Data?.CreatedAt,  // null if response is null
                        ModifiedAt = response?.Data?.ModifiedAt
                    };
                },
                mock: () => new ApiResult<Todo>
                {
                    Data = new Todo(request.UserId, request.TodoId, request.Title, false, DateTime.UtcNow, DateTime.UtcNow),
                    StatusCode = 200
                });
    }
}

/// <summary>
/// GET TODO operation - can disambiguate unknown timestamps.
/// Validates all known fields match.
/// </summary>
public class GetTodoOperation : TodoApiOperation<(string UserId, string TodoId), Todo, AppState>
{
    public GetTodoOperation() : base("GetTodo") { }

    protected override ExpectedOutcomes ApplyInternal((string UserId, string TodoId) request, AppState state)
    {
        var user = state.Users.GetValueOrDefault(request.UserId);
        var todo = user?.Todos.GetValueOrDefault(request.TodoId);

        if (todo != null)
        {
            // If timestamps are unknown, read them from response
            if (todo.CreatedAt == null || todo.ModifiedAt == null)
            {
                return Expect.That((ApiResult<Todo> r) =>
                        r.IsSuccess &&
                        r.Data?.UserId == request.UserId &&
                        r.Data?.TodoId == request.TodoId &&
                        r.Data?.Title == todo.Title &&
                        r.Data?.Completed == todo.Completed,
                    "Should return todo (learning timestamps)")
                    .ThenState(
                        (ApiResult<Todo> response, AppState next) =>
                        {
                            var t = next.Users[request.UserId].Todos[request.TodoId];
                            t.CreatedAt = response?.Data?.CreatedAt;
                            t.ModifiedAt = response?.Data?.ModifiedAt;
                        },
                        mock: () => new ApiResult<Todo>
                        {
                            Data = new Todo(request.UserId, request.TodoId, todo.Title, todo.Completed, DateTime.UtcNow, DateTime.UtcNow),
                            StatusCode = 200
                        });
            }

            // All fields known - validate everything matches
            return Expect.That((ApiResult<Todo> r) =>
                    r.IsSuccess &&
                    r.Data?.UserId == request.UserId &&
                    r.Data?.TodoId == request.TodoId &&
                    r.Data?.Title == todo.Title &&
                    r.Data?.Completed == todo.Completed &&
                    r.Data?.CreatedAt == todo.CreatedAt &&
                    r.Data?.ModifiedAt == todo.ModifiedAt,
                "Should return todo with matching fields")
                .SameState();
        }

        return Expect.That((ApiResult<Todo> r) => r.IsNotFound,
            "Todo not found")
            .SameState();
    }
}
