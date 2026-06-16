// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Todo entity stored in the database.
/// Each todo belongs to a specific user.
/// </summary>
public class TodoEntity
{
    /// <summary>
    /// The user who owns this todo.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The todo ID (unique within a user's todos).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string TodoId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public bool Completed { get; set; } = false;
}
