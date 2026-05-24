// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using Microsoft.Accordant;
using TodoList.FaultInjection.Api.Contracts;

/// <summary>
/// CREATE USER - base class automatically adds indefinite failure outcomes.
/// 
/// When response is null (indefinite failure), response?.Data?.Field returns null,
/// so timestamps become unknown automatically.
/// </summary>
public class CreateUserOperation : TodoApiOperation<User, User, AppState>
{
    public CreateUserOperation() : base("CreateUser") { }

    protected override ExpectedOutcomes ApplyInternal(User request, AppState state)
    {
        if (state.Users.ContainsKey(request.UserId))
        {
            return Expect.That((ApiResult<User> r) => r.IsConflict,
                "User exists - should return 409 Conflict")
                .SameState();
        }

        // Read timestamps from response (server-generated)
        // response?.Data?.Field handles null response (indefinite failure) gracefully
        return Expect.That((ApiResult<User> r) => r.IsSuccess && r.Data?.UserId == request.UserId,
            "Should return 200 OK with created user")
            .ThenState(
                (ApiResult<User> response, AppState next) =>
                {
                    next.Users[request.UserId] = new UserState
                    {
                        Name = request.Name,
                        CreatedAt = response?.Data?.CreatedAt,  // null if response is null
                        ModifiedAt = response?.Data?.ModifiedAt
                    };
                },
                mock: () => new ApiResult<User>
                {
                    Data = new User(request.UserId, request.Name, DateTime.UtcNow, DateTime.UtcNow),
                    StatusCode = 200
                });
    }
}

/// <summary>
/// GET USER - read operation that can disambiguate unknown timestamps.
/// Validates all known fields match.
/// </summary>
public class GetUserOperation : TodoApiOperation<string, User, AppState>
{
    public GetUserOperation() : base("GetUser") { }

    protected override ExpectedOutcomes ApplyInternal(string userId, AppState state)
    {
        if (state.Users.TryGetValue(userId, out var user))
        {
            // If timestamps are unknown, read them from response
            if (user.CreatedAt == null || user.ModifiedAt == null)
            {
                return Expect.That((ApiResult<User> r) =>
                        r.IsSuccess &&
                        r.Data?.UserId == userId &&
                        r.Data?.Name == user.Name,
                    "Should return user (learning timestamps)")
                    .ThenState(
                        (ApiResult<User> response, AppState next) =>
                        {
                            var u = next.Users[userId];
                            u.CreatedAt = response?.Data?.CreatedAt;
                            u.ModifiedAt = response?.Data?.ModifiedAt;
                        },
                        mock: () => new ApiResult<User>
                        {
                            Data = new User(userId, user.Name, DateTime.UtcNow, DateTime.UtcNow),
                            StatusCode = 200
                        });
            }

            // All fields known - validate everything matches
            return Expect.That((ApiResult<User> r) =>
                    r.IsSuccess &&
                    r.Data?.UserId == userId &&
                    r.Data?.Name == user.Name &&
                    r.Data?.CreatedAt == user.CreatedAt &&
                    r.Data?.ModifiedAt == user.ModifiedAt,
                "Should return user with matching fields")
                .SameState();
        }

        return Expect.That((ApiResult<User> r) => r.IsNotFound,
            "Should return 404 Not Found")
            .SameState();
    }
}

/// <summary>
/// DELETE USER operation.
/// </summary>
public class DeleteUserOperation : TodoApiOperation<string, int, AppState>
{
    public DeleteUserOperation() : base("DeleteUser") { }

    protected override ExpectedOutcomes ApplyInternal(string userId, AppState state)
    {
        if (!state.Users.ContainsKey(userId))
        {
            return Expect.That((ApiResult<int> r) => r.StatusCode == 404,
                "Should return 404 Not Found")
                .SameState();
        }

        return Expect.That((ApiResult<int> r) => r.StatusCode == 204,
            "Should return 204 No Content")
            .ThenState(next => next.Users.Remove(userId));
    }
}
