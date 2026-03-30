// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Api.Models
{
    /// <summary>
    /// Database entity representing a user.
    /// </summary>
    public class UserEntity
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        // Navigation property for the user's todos
        public List<TodoEntity> Todos { get; set; } = new();
    }
}
