// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// Entity-based state example: Users with nested Todos.
/// Demonstrates Dictionary-based collections for keyed entities.
/// </summary>
[State]
public partial class AppState : State
{
    /// <summary>
    /// Users keyed by user ID. Always initialize to avoid null reference in Apply.
    /// </summary>
    public Dictionary<string, UserState> Users { get; set; } = new();
}

/// <summary>
/// State class for a user. Must be a separate [State] class at namespace level.
/// </summary>
[State]
public partial class UserState : State
{
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Todos keyed by todo ID. Nested collection within user.
    /// </summary>
    public Dictionary<string, TodoState> Todos { get; set; } = new();
}

/// <summary>
/// State class for a todo item.
/// </summary>
[State]
public partial class TodoState : State
{
    public string Title { get; set; } = string.Empty;
    public bool Completed { get; set; } = false;
}
