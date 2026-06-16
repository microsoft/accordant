// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Api.Controllers;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoList.Api.Contracts;
using TodoList.Api.Data;
using TodoList.Api.Models;

[ApiController]
[Route("api/users/{userId}/todos")]
public class TodosController : ControllerBase
{
    private readonly TodoDbContext _dbContext;
    private readonly object _writeLock;

    public TodosController(TodoDbContext dbContext, WriteLock writeLock)
    {
        _dbContext = dbContext;
        _writeLock = writeLock.Lock;
    }

    /// <summary>
    /// Creates a new todo item for a user.
    /// PUT /api/users/{userId}/todos/{todoId}
    /// </summary>
    [HttpPut("{todoId}")]
    public ActionResult<Todo> CreateTodo(
        [FromRoute] string userId,
        [FromRoute] string todoId,
        [FromBody] Todo request)
    {
        // Use write lock for serializability
        lock (_writeLock)
        {
            // Check if user exists
            var userExists = _dbContext.Users.Any(u => u.UserId == userId);
            if (!userExists)
            {
                return NotFound(new ErrorInfo("USER_NOT_FOUND", $"User '{userId}' does not exist."));
            }

            // Check if todo already exists for this user
            var existing = _dbContext.Todos
                .FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);

            if (existing != null)
            {
                return Conflict(new ErrorInfo("ALREADY_EXISTS", $"Todo '{todoId}' already exists for user '{userId}'."));
            }

            var entity = new TodoEntity
            {
                UserId = userId,
                TodoId = todoId,
                Title = request.Title,
                Completed = false
            };

            _dbContext.Todos.Add(entity);
            _dbContext.SaveChanges();

            return Ok(ToContract(entity));
        }
    }

    /// <summary>
    /// Gets a todo by ID for a user.
    /// GET /api/users/{userId}/todos/{todoId}
    /// </summary>
    [HttpGet("{todoId}")]
    public async Task<ActionResult<Todo>> GetTodo(
        [FromRoute] string userId,
        [FromRoute] string todoId)
    {
        var entity = await _dbContext.Todos
            .FirstOrDefaultAsync(t => t.UserId == userId && t.TodoId == todoId);

        if (entity == null)
        {
            return NotFound(new ErrorInfo("NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'."));
        }

        return Ok(ToContract(entity));
    }

    /// <summary>
    /// Marks a todo as completed.
    /// POST /api/users/{userId}/todos/{todoId}/complete
    /// </summary>
    [HttpPost("{todoId}/complete")]
    public ActionResult<Todo> CompleteTodo(
        [FromRoute] string userId,
        [FromRoute] string todoId)
    {
        lock (_writeLock)
        {
            var entity = _dbContext.Todos
                .FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);

            if (entity == null)
            {
                return NotFound(new ErrorInfo("NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'."));
            }

            entity.Completed = true;
            _dbContext.SaveChanges();

            return Ok(ToContract(entity));
        }
    }

    /// <summary>
    /// Deletes a todo.
    /// DELETE /api/users/{userId}/todos/{todoId}
    /// </summary>
    [HttpDelete("{todoId}")]
    public ActionResult DeleteTodo(
        [FromRoute] string userId,
        [FromRoute] string todoId)
    {
        lock (_writeLock)
        {
            var entity = _dbContext.Todos
                .FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);

            if (entity == null)
            {
                return NotFound(new ErrorInfo("NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'."));
            }

            _dbContext.Todos.Remove(entity);
            _dbContext.SaveChanges();

            return NoContent();
        }
    }

    private static Todo ToContract(TodoEntity entity)
    {
        return new Todo(entity.UserId, entity.TodoId, entity.Title, entity.Completed);
    }
}

/// <summary>
/// Singleton service to hold the write lock.
/// This ensures serializable execution of write operations.
/// </summary>
public class WriteLock
{
    public object Lock { get; } = new object();
}
