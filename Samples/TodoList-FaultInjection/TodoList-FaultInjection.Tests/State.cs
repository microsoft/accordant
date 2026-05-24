// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// State tracks users and todos.
/// 
/// Indefinite failures are handled via branching - the base operation class
/// automatically creates branches for "state changed" vs "state unchanged".
/// 
/// Server-generated timestamps (CreatedAt, ModifiedAt) use nullable DateTime:
/// - null = unknown (after indefinite failure where we lost the response)
/// - value = known (from successful response)
/// </summary>
[State]
public partial class AppState
{
    public Dictionary<string, UserState> Users { get; set; } = new();
}

[State]
public partial class UserState
{
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Server-generated timestamp. Null if unknown (indefinite failure).
    /// </summary>
    public DateTime? CreatedAt { get; set; }
    
    /// <summary>
    /// Server-generated timestamp. Null if unknown (indefinite failure).
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
    
    public Dictionary<string, TodoState> Todos { get; set; } = new();
}

[State]
public partial class TodoState
{
    public string Title { get; set; } = string.Empty;
    public bool Completed { get; set; } = false;
    
    /// <summary>
    /// Server-generated timestamp. Null if unknown (indefinite failure).
    /// </summary>
    public DateTime? CreatedAt { get; set; }
    
    /// <summary>
    /// Server-generated timestamp. Null if unknown (indefinite failure).
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}
