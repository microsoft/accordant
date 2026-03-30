// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Api.Controllers
{
    using System.Linq;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using TodoList.Api.Contracts;
    using TodoList.Api.Data;
    using TodoList.Api.Models;

    /// <summary>
    /// REST API controller for managing users.
    /// 
    /// Endpoints:
    /// - PUT  /api/users/{userId}      - Create user (returns 409 if exists)
    /// - GET  /api/users/{userId}      - Get user by ID
    /// - DELETE /api/users/{userId}    - Delete user (cascade deletes todos)
    /// - DELETE /api/users/{userId}/try - Delete user if exists (for test cleanup)
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

                var user = new UserEntity
                {
                    UserId = userId,
                    Name = request.Name
                };

                _context.Users.Add(user);
                _context.SaveChanges();

                return Ok(new User(user.UserId, user.Name));
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

            return Ok(new User(user.UserId, user.Name));
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
}
