// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Api.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using TodoListExtended.Api.Contracts;
    using TodoListExtended.Api.Data;
    using TodoListExtended.Api.Models;

    /// <summary>
    /// REST API controller for managing todos.
    /// 
    /// Key differences from simple TodoList:
    /// - Server generates TodoId (GUID) - client doesn't provide it
    /// - Server generates CreatedAt/ModifiedAt timestamps
    /// 
    /// Endpoints:
    /// - POST   /api/users/{userId}/todos              - Create todo (server generates TodoId)
    /// - GET    /api/users/{userId}/todos/{todoId}     - Get todo
    /// - POST   /api/users/{userId}/todos/{todoId}/complete - Mark complete
    /// - DELETE /api/users/{userId}/todos/{todoId}     - Delete todo
    /// </summary>
    [ApiController]
    [Route("api/users/{userId}/todos")]
    public class TodosController : ControllerBase
    {
        private readonly TodoDbContext _context;
        private readonly object _writeLock;

        public TodosController(TodoDbContext context, WriteLock writeLock)
        {
            _context = context;
            _writeLock = writeLock.Lock;
        }

        /// <summary>
        /// Create a new todo. Server generates TodoId and timestamps (ignores any client-provided values).
        /// Returns 404 if user doesn't exist.
        /// </summary>
        [HttpPost]
        public ActionResult<Todo> CreateTodo(string userId, [FromBody] Todo request)
        {
            lock (_writeLock)
            {
                var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
                if (user == null)
                {
                    return NotFound(new ErrorInfo("USER_NOT_FOUND", $"User '{userId}' not found"));
                }

                var now = DateTime.UtcNow;
                var todo = new TodoEntity
                {
                    TodoId = Guid.NewGuid().ToString(),  // Server generates the ID
                    UserId = userId,
                    Title = request.Title,
                    Completed = false,
                    CreatedAt = now,
                    ModifiedAt = now
                };

                _context.Todos.Add(todo);
                _context.SaveChanges();

                return Ok(new Todo(
                    todo.UserId, 
                    todo.TodoId, 
                    todo.Title, 
                    todo.Completed, 
                    todo.CreatedAt, 
                    todo.ModifiedAt));
            }
        }

        /// <summary>
        /// Get a todo by ID.
        /// </summary>
        [HttpGet("{todoId}")]
        public async Task<ActionResult<Todo>> GetTodo(string userId, string todoId)
        {
            var todo = await _context.Todos
                .FirstOrDefaultAsync(t => t.UserId == userId && t.TodoId == todoId);

            if (todo == null)
            {
                return NotFound(new ErrorInfo("TODO_NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'"));
            }

            return Ok(new Todo(
                todo.UserId, 
                todo.TodoId, 
                todo.Title, 
                todo.Completed, 
                todo.CreatedAt, 
                todo.ModifiedAt));
        }

        /// <summary>
        /// Mark a todo as completed. Updates ModifiedAt timestamp.
        /// </summary>
        [HttpPost("{todoId}/complete")]
        public ActionResult<Todo> CompleteTodo(string userId, string todoId)
        {
            lock (_writeLock)
            {
                var todo = _context.Todos
                    .FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);

                if (todo == null)
                {
                    return NotFound(new ErrorInfo("TODO_NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'"));
                }

                todo.Completed = true;
                todo.ModifiedAt = DateTime.UtcNow;
                _context.SaveChanges();

                return Ok(new Todo(
                    todo.UserId, 
                    todo.TodoId, 
                    todo.Title, 
                    todo.Completed, 
                    todo.CreatedAt, 
                    todo.ModifiedAt));
            }
        }

        /// <summary>
        /// Delete a todo.
        /// </summary>
        [HttpDelete("{todoId}")]
        public ActionResult DeleteTodo(string userId, string todoId)
        {
            lock (_writeLock)
            {
                var todo = _context.Todos
                    .FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);

                if (todo == null)
                {
                    return NotFound(new ErrorInfo("TODO_NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'"));
                }

                _context.Todos.Remove(todo);
                _context.SaveChanges();
                return NoContent();
            }
        }
    }
}
