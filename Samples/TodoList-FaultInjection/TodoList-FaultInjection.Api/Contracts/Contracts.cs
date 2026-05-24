// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Api.Contracts;

/// <summary>
/// User contract - same type used for requests and responses.
/// Server ignores CreatedAt/ModifiedAt if provided by client; they are server-generated.
/// </summary>
public record User(
    string UserId, 
    string Name,
    DateTime CreatedAt = default,
    DateTime ModifiedAt = default)
{
    public override string ToString() => $"User({UserId}, {Name}, Created={CreatedAt:s}, Modified={ModifiedAt:s})";
}

/// <summary>
/// Todo contract - same type used for requests and responses.
/// Server ignores CreatedAt/ModifiedAt if provided by client; they are server-generated.
/// </summary>
public record Todo(
    string UserId, 
    string TodoId, 
    string Title, 
    bool Completed = false,
    DateTime CreatedAt = default,
    DateTime ModifiedAt = default)
{
    public override string ToString() => Completed 
        ? $"Todo({UserId}/{TodoId}, \"{Title}\", ✓, Created={CreatedAt:s}, Modified={ModifiedAt:s})"
        : $"Todo({UserId}/{TodoId}, \"{Title}\", Created={CreatedAt:s}, Modified={ModifiedAt:s})";
}

/// <summary>
/// Error information returned on failures.
/// </summary>
public record ErrorInfo(string Code, string Message)
{
    public override string ToString() => $"Error({Code}: {Message})";
}
