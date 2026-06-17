// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Api.Data;

using Microsoft.EntityFrameworkCore;
using TodoListExtended.Api.Models;

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
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.ModifiedAt).IsRequired();

            // Cascade delete: when user is deleted, their todos are deleted too
            entity.HasMany(e => e.Todos)
                  .WithOne()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TodoEntity>(entity =>
        {
            // TodoId is the primary key (server-generated GUID)
            entity.HasKey(e => e.TodoId);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.ModifiedAt).IsRequired();
            entity.HasIndex(e => e.UserId);  // Index for querying by user
            entity.HasIndex(e => new { e.UserId, e.Completed });  // Index for filtering
        });
    }
}
