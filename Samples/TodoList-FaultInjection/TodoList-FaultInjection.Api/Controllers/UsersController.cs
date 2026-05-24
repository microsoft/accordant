// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoList.FaultInjection.Api.Contracts;
using TodoList.FaultInjection.Api.Data;
using TodoList.FaultInjection.Api.Models;

/// <summary>
/// REST API controller for managing users.
/// Includes fault injection points that can simulate database failures.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly FaultInjectingDbContext _context;
    private readonly object _writeLock;

    public UsersController(FaultInjectingDbContext context, WriteLock writeLock)
    {
        _context = context;
        _writeLock = writeLock.Lock;
    }

    /// <summary>
    /// Create a new user. Fault injection points:
    /// - PreSave: throws before SaveChanges (user NOT created)
    /// - PostSave: throws after SaveChanges (user WAS created, but client sees 500)
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
            
            // FAULT POINT: Before save - if this throws, user was NOT created
            _context.MaybeInjectPreSaveFault("CreateUser");
            
            _context.SaveChanges();
            
            // FAULT POINT: After save - if this throws, user WAS created!
            // This creates a TRUE indefinite failure.
            _context.MaybeInjectPostSaveFault("CreateUser");

            return Ok(new User(user.UserId, user.Name, user.CreatedAt, user.ModifiedAt));
        }
    }

    /// <summary>
    /// Get a user by ID.
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<ActionResult<User>> GetUser(string userId)
    {
        // FAULT POINT: Read fault - no state change, just an error
        _context.MaybeInjectReadFault("GetUser");
        
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

            var todos = _context.Todos.Where(t => t.UserId == userId).ToList();
            _context.Todos.RemoveRange(todos);
            _context.Users.Remove(user);
            
            _context.MaybeInjectPreSaveFault("DeleteUser");
            _context.SaveChanges();
            _context.MaybeInjectPostSaveFault("DeleteUser");

            return NoContent();
        }
    }

    /// <summary>
    /// Delete a user if exists (for test cleanup). No faults injected.
    /// </summary>
    [HttpDelete("{userId}/try")]
    public ActionResult TryDeleteUser(string userId)
    {
        lock (_writeLock)
        {
            var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
            if (user != null)
            {
                var todos = _context.Todos.Where(t => t.UserId == userId).ToList();
                _context.Todos.RemoveRange(todos);
                _context.Users.Remove(user);
                _context.SaveChanges();
            }
            return NoContent();
        }
    }
}

/// <summary>
/// Simple lock for serializing writes (ensures linearizable behavior).
/// </summary>
public class WriteLock
{
    public object Lock { get; } = new object();
}
