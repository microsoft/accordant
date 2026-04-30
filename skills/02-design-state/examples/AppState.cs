// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// Entity-based state example: Users with nested Todos.
/// Demonstrates Dictionary-based collections for keyed entities.
/// </summary>
public class AppState : JsonState
{
    /// <summary>
    /// Users keyed by user ID. Always initialize to avoid null reference in Apply.
    /// </summary>
    public Dictionary<string, UserState> Users { get; set; } = new();

    /// <summary>
    /// Nested state class for a user. Can be a plain class or JsonState subclass.
    /// </summary>
    public class UserState
    {
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Todos keyed by todo ID. Nested collection within user.
        /// </summary>
        public Dictionary<string, TodoState> Todos { get; set; } = new();
    }

    /// <summary>
    /// Nested state class for a todo item.
    /// </summary>
    public class TodoState
    {
        public string Title { get; set; } = string.Empty;
        public bool Completed { get; set; } = false;
    }
}
