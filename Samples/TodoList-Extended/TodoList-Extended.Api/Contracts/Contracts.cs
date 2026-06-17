// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Api.Contracts;

/// <summary>
/// User - same type used for both requests and responses.
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
/// Todo - same type used for both requests and responses.
/// Server ignores TodoId/CreatedAt/ModifiedAt if provided by client; they are server-generated.
/// For create requests, TodoId can be empty/null - server will generate it.
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
