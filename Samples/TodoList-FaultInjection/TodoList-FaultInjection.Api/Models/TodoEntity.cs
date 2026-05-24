// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Todo entity stored in the database.
/// Includes server-managed timestamps.
/// </summary>
public class TodoEntity
{
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string TodoId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public bool Completed { get; set; } = false;
    
    /// <summary>
    /// Server-generated timestamp when the todo was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Server-generated timestamp when the todo was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; }
}
