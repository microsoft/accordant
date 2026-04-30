// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Api.Models
{
    /// <summary>
    /// Database entity representing a user.
    /// Includes server-managed timestamps.
    /// </summary>
    public class UserEntity
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Server-generated timestamp when the user was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Server-generated timestamp when the user was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; set; }

        // Navigation property for the user's todos
        public List<TodoEntity> Todos { get; set; } = new();
    }
}
