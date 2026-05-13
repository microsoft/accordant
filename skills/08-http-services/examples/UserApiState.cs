// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// State for the User API.
/// Tracks users by ID.
/// </summary>
[State]
public partial class UserApiState : State
{
    /// <summary>
    /// Users keyed by user ID.
    /// </summary>
    public Dictionary<string, UserState> Users { get; set; } = new();
}

[State]
public partial class UserState : State
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
