// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Examples.Http;

using Microsoft.Accordant;
/// <summary>
/// Spec for the User API.
/// Registers all operations for testing.
/// </summary>
public class UserApiSpec : Spec<UserApiState>
{
    public CreateUserOperation CreateUser { get; } = new();
    public GetUserOperation GetUser { get; } = new();
    public DeleteUserOperation DeleteUser { get; } = new();

    public UserApiSpec()
    {
        RegisterOperationProperties();
    }
}
