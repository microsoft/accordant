// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoList.FaultInjection.Api.Contracts;
using TodoList.FaultInjection.Api.Data;
using TodoList.FaultInjection.Api.Models;

/// <summary>
/// REST API controller for managing todos.
/// Includes fault injection points that can simulate database failures.
/// </summary>
[ApiController]
[Route("api/users/{userId}/todos")]
public class TodosController : ControllerBase
{
    private readonly FaultInjectingDbContext _context;
    private readonly object _writeLock;

    public TodosController(FaultInjectingDbContext context, WriteLock writeLock)
    {
        _context = context;
        _writeLock = writeLock.Lock;
    }

    /// <summary>
    /// Create a new todo for a user.
    /// </summary>
    [HttpPut("{todoId}")]
    public ActionResult<Todo> CreateTodo(string userId, string todoId, [FromBody] Todo request)
    {
        lock (_writeLock)
        {
            var userExists = _context.Users.Any(u => u.UserId == userId);
            if (!userExists)
            {
                return NotFound(new ErrorInfo("USER_NOT_FOUND", $"User '{userId}' not found"));
            }

            var existing = _context.Todos.FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);
            if (existing != null)
            {
                return Conflict(new ErrorInfo("TODO_EXISTS", $"Todo '{todoId}' already exists"));
            }

            var now = DateTime.UtcNow;
            var todo = new TodoEntity
            {
                UserId = userId,
                TodoId = todoId,
                Title = request.Title,
                Completed = false,
                CreatedAt = now,
                ModifiedAt = now
            };

            _context.Todos.Add(todo);
            
            _context.MaybeInjectPreSaveFault("CreateTodo");
            _context.SaveChanges();
            _context.MaybeInjectPostSaveFault("CreateTodo");

            return Ok(new Todo(todo.UserId, todo.TodoId, todo.Title, todo.Completed, todo.CreatedAt, todo.ModifiedAt));
        }
    }

    /// <summary>
    /// Get a todo by ID.
    /// </summary>
    [HttpGet("{todoId}")]
    public async Task<ActionResult<Todo>> GetTodo(string userId, string todoId)
    {
        _context.MaybeInjectReadFault("GetTodo");
        
        var todo = await _context.Todos.FirstOrDefaultAsync(t => t.UserId == userId && t.TodoId == todoId);
        if (todo == null)
        {
            return NotFound(new ErrorInfo("TODO_NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'"));
        }

        return Ok(new Todo(todo.UserId, todo.TodoId, todo.Title, todo.Completed, todo.CreatedAt, todo.ModifiedAt));
    }

    /// <summary>
    /// Mark a todo as completed.
    /// </summary>
    [HttpPost("{todoId}/complete")]
    public ActionResult<Todo> CompleteTodo(string userId, string todoId)
    {
        lock (_writeLock)
        {
            var todo = _context.Todos.FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);
            if (todo == null)
            {
                return NotFound(new ErrorInfo("TODO_NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'"));
            }

            todo.Completed = true;
            todo.ModifiedAt = DateTime.UtcNow;
            
            _context.MaybeInjectPreSaveFault("CompleteTodo");
            _context.SaveChanges();
            _context.MaybeInjectPostSaveFault("CompleteTodo");

            return Ok(new Todo(todo.UserId, todo.TodoId, todo.Title, todo.Completed, todo.CreatedAt, todo.ModifiedAt));
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
            var todo = _context.Todos.FirstOrDefault(t => t.UserId == userId && t.TodoId == todoId);
            if (todo == null)
            {
                return NotFound(new ErrorInfo("TODO_NOT_FOUND", $"Todo '{todoId}' not found for user '{userId}'"));
            }

            _context.Todos.Remove(todo);
            
            _context.MaybeInjectPreSaveFault("DeleteTodo");
            _context.SaveChanges();
            _context.MaybeInjectPostSaveFault("DeleteTodo");

            return NoContent();
        }
    }
}
