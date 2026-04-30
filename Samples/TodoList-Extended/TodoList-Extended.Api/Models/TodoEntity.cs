// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Api.Models
{
    /// <summary>
    /// Database entity representing a todo item.
    /// TodoId is server-generated (GUID).
    /// Includes server-managed timestamps.
    /// </summary>
    public class TodoEntity
    {
        /// <summary>
        /// Server-generated unique identifier for the todo.
        /// </summary>
        public string TodoId { get; set; } = string.Empty;
        
        /// <summary>
        /// The user who owns this todo.
        /// </summary>
        public string UserId { get; set; } = string.Empty;
        
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
}
