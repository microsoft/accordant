// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Accordant;
using FluentAssertions;

/// <summary>
/// CREATE USER operation using ApiResult pattern with FluentAssertions.
/// Demonstrates: success/conflict handling, response validation with ValidationResult.
/// </summary>
public class CreateUserOperation : Operation<CreateUserRequest, ApiResult<User>, UserApiState>
{
    public CreateUserOperation() : base("CreateUser") { }

    public override ExpectedOutcomes Apply(CreateUserRequest request, UserApiState state)
    {
        // Check for conflict (user already exists)
        if (state.Users.ContainsKey(request.UserId))
        {
            return Expect.That(r => r.IsConflict,
                       $"Should return 409 Conflict - user '{request.UserId}' already exists")
                   .SameState();
        }

        // Validation: email required
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Expect.That(r => r.IsBadRequest,
                       "Should return 400 Bad Request - email is required")
                   .SameState();
        }

        // Success case - using FluentAssertions for detailed validation
        return Expect.That<ApiResult<User>>(response =>
               {
                   if (!response.IsSuccess || response.Data == null)
                       return ValidationResult.Invalid($"Expected success, got {response.StatusCode}");

                   var expected = new User
                   {
                       Id = request.UserId,
                       Name = request.Name,
                       Email = request.Email
                   };

                   try
                   {
                       // FluentAssertions gives detailed diff on failure
                       response.Data.Should().BeEquivalentTo(expected);
                       return ValidationResult.Valid();
                   }
                   catch (Exception ex)
                   {
                       return ValidationResult.Invalid(ex.Message);
                   }
               })
               .ThenState(next => next.Users[request.UserId] = new UserApiState.UserState
               {
                   Name = request.Name,
                   Email = request.Email
               });
    }

    public override async Task<ApiResult<User>> ExecuteAsync(
        TestingContext context, CreateUserRequest request)
    {
        var client = context.Get<UserApiClient>();
        return await client.CreateUserAsync(request);
    }
}

/// <summary>
/// GET USER operation using ApiResult pattern with FluentAssertions.
/// Demonstrates: success/not found handling, FluentAssertions validation.
/// </summary>
public class GetUserOperation : Operation<string, ApiResult<User>, UserApiState>
{
    public GetUserOperation() : base("GetUser") { }

    /// <summary>
    /// Derivation: GetUser request (userId) can be derived from CreateUser request.
    /// </summary>
    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        Derive.From<CreateUserRequest, ApiResult<User>, string>("CreateUser")
              .When((req, resp) => resp.IsSuccess)  // Only derive when create succeeded
              .As((req, resp) => req.UserId)
    };

    public override ExpectedOutcomes Apply(string userId, UserApiState state)
    {
        if (!state.Users.TryGetValue(userId, out var user))
        {
            return Expect.That(r => r.IsNotFound,
                       $"Should return 404 Not Found - user '{userId}' doesn't exist")
                   .SameState();
        }

        // Success case - using FluentAssertions for detailed validation
        return Expect.That<ApiResult<User>>(response =>
               {
                   if (!response.IsSuccess || response.Data == null)
                       return ValidationResult.Invalid($"Expected success, got {response.StatusCode}");

                   var expected = new User
                   {
                       Id = userId,
                       Name = user.Name,
                       Email = user.Email
                   };

                   try
                   {
                       response.Data.Should().BeEquivalentTo(expected);
                       return ValidationResult.Valid();
                   }
                   catch (Exception ex)
                   {
                       return ValidationResult.Invalid(ex.Message);
                   }
               })
               .SameState();
    }

    public override async Task<ApiResult<User>> ExecuteAsync(
        TestingContext context, string userId)
    {
        var client = context.Get<UserApiClient>();
        return await client.GetUserAsync(userId);
    }
}

/// <summary>
/// DELETE USER operation.
/// Demonstrates: void-like operation returning status code.
/// </summary>
public class DeleteUserOperation : Operation<string, int, UserApiState>
{
    public DeleteUserOperation() : base("DeleteUser") { }

    public override ExpectedOutcomes Apply(string userId, UserApiState state)
    {
        if (!state.Users.ContainsKey(userId))
        {
            return Expect.That(r => r == 404,
                       $"Should return 404 Not Found - user '{userId}' doesn't exist")
                   .SameState();
        }

        return Expect.That(r => r == 200 || r == 204,
                   $"Should return 200/204 OK after deleting user '{userId}'")
               .ThenState(next => next.Users.Remove(userId));
    }

    public override async Task<int> ExecuteAsync(
        TestingContext context, string userId)
    {
        var client = context.Get<UserApiClient>();
        return await client.DeleteUserAsync(userId);
    }
}
