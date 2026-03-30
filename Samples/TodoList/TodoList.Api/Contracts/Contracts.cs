// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Api.Contracts
{
    /// <summary>
    /// User - same type used for both requests and responses.
    /// </summary>
    public record User(string UserId, string Name)
    {
        public override string ToString() => $"User({UserId}, {Name})";
    }

    /// <summary>
    /// Todo - same type used for both requests and responses.
    /// </summary>
    public record Todo(string UserId, string TodoId, string Title, bool Completed = false)
    {
        public override string ToString() => Completed 
            ? $"Todo({UserId}/{TodoId}, \"{Title}\", ✓)"
            : $"Todo({UserId}/{TodoId}, \"{Title}\")";
    }

    /// <summary>
    /// Error information returned on failures.
    /// </summary>
    public record ErrorInfo(string Code, string Message)
    {
        public override string ToString() => $"Error({Code}: {Message})";
    }
}
