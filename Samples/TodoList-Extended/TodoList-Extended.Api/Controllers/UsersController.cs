// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoListExtended.Api.Contracts;
using TodoListExtended.Api.Data;
using TodoListExtended.Api.Models;

/// <summary>
/// REST API controller for managing users.
/// 
/// Key difference from simple TodoList:
/// - Server generates CreatedAt/ModifiedAt timestamps
/// 
/// Endpoints:
/// - PUT    /api/users/{userId}  - Create user (returns 409 if exists)
/// - GET    /api/users/{userId}  - Get user by ID
/// - DELETE /api/users/{userId}  - Delete user (cascade deletes todos)
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly TodoDbContext _context;
    private readonly object _writeLock;

    public UsersController(TodoDbContext context, WriteLock writeLock)
    {
        _context = context;
        _writeLock = writeLock.Lock;
    }

    /// <summary>
    /// Create a new user.
    /// Server generates CreatedAt and ModifiedAt timestamps (ignores any client-provided values).
    /// Returns 409 Conflict if user already exists.
    /// </summary>
    [HttpPut("{userId}")]
    public ActionResult<User> CreateUser(string userId, [FromBody] User request)
    {
        lock (_writeLock)
        {
            var existing = _context.Users.FirstOrDefault(u => u.UserId == userId);
            if (existing != null)
            {
                return Conflict(new ErrorInfo("USER_EXISTS", $"User '{userId}' already exists"));
            }

            var now = DateTime.UtcNow;
            var user = new UserEntity
            {
                UserId = userId,
                Name = request.Name,
                CreatedAt = now,
                ModifiedAt = now
            };

            _context.Users.Add(user);
            _context.SaveChanges();

            return Ok(new User(user.UserId, user.Name, user.CreatedAt, user.ModifiedAt));
        }
    }

    /// <summary>
    /// Get a user by ID.
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<ActionResult<User>> GetUser(string userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
        {
            return NotFound(new ErrorInfo("USER_NOT_FOUND", $"User '{userId}' not found"));
        }

        return Ok(new User(user.UserId, user.Name, user.CreatedAt, user.ModifiedAt));
    }

    /// <summary>
    /// Delete a user (and all their todos).
    /// </summary>
    [HttpDelete("{userId}")]
    public ActionResult DeleteUser(string userId)
    {
        lock (_writeLock)
        {
            var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
            {
                return NotFound(new ErrorInfo("USER_NOT_FOUND", $"User '{userId}' not found"));
            }

            // Explicitly delete todos (InMemory provider doesn't cascade)
            var todos = _context.Todos.Where(t => t.UserId == userId).ToList();
            _context.Todos.RemoveRange(todos);

            _context.Users.Remove(user);
            _context.SaveChanges();
            return NoContent();
        }
    }
}
