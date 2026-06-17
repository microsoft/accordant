// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Api.Data;

using Microsoft.EntityFrameworkCore;
using TodoList.Api.Models;

/// <summary>
/// Entity Framework DbContext for the Todo database.
/// </summary>
public class TodoDbContext : DbContext
{
    public TodoDbContext(DbContextOptions<TodoDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserEntity> Users { get; set; } = null!;
    public DbSet<TodoEntity> Todos { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);

            // Cascade delete: when user is deleted, their todos are deleted too
            entity.HasMany(e => e.Todos)
                  .WithOne()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TodoEntity>(entity =>
        {
            // Composite key: UserId + TodoId
            entity.HasKey(e => new { e.UserId, e.TodoId });
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.UserId);  // Index for querying by user
            entity.HasIndex(e => new { e.UserId, e.Completed });  // Index for filtering
        });
    }
}
