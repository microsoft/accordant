// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Api.Data;

using Microsoft.EntityFrameworkCore;
using TodoList.FaultInjection.Api.FaultInjection;
using TodoList.FaultInjection.Api.Models;

/// <summary>
/// DbContext that can inject faults to simulate database failures.
/// 
/// Key insight: Faults after SaveChanges creates TRUE indefinite failures.
/// The data was persisted, but the client sees a 500 error.
/// Subsequent operations will reveal that the write actually happened.
/// </summary>
public class FaultInjectingDbContext : DbContext
{
    private readonly ServerFaultConfig _faultConfig;
    private readonly Random _random;

    public FaultInjectingDbContext(
        DbContextOptions<FaultInjectingDbContext> options,
        ServerFaultConfig faultConfig)
        : base(options)
    {
        _faultConfig = faultConfig;
        _random = faultConfig.Seed.HasValue 
            ? new Random(faultConfig.Seed.Value) 
            : new Random();
    }

    public DbSet<UserEntity> Users { get; set; } = null!;
    public DbSet<TodoEntity> Todos { get; set; } = null!;

    /// <summary>
    /// Inject a fault during a read operation.
    /// Call this before returning data from GET endpoints.
    /// </summary>
    public void MaybeInjectReadFault(string operation)
    {
        if (_faultConfig.Enabled && _random.NextDouble() < _faultConfig.ReadFaultProbability)
        {
            throw new SimulatedServerFaultException(
                $"Simulated read fault during {operation}: Database connection lost");
        }
    }

    /// <summary>
    /// Inject a fault before SaveChanges.
    /// If this throws, no data was persisted.
    /// </summary>
    public void MaybeInjectPreSaveFault(string operation)
    {
        if (_faultConfig.Enabled && _random.NextDouble() < _faultConfig.PreSaveFaultProbability)
        {
            throw new SimulatedServerFaultException(
                $"Simulated pre-save fault during {operation}: Database timeout before write");
        }
    }

    /// <summary>
    /// Inject a fault after SaveChanges.
    /// If this throws, data WAS persisted but client sees an error.
    /// This creates a TRUE indefinite failure.
    /// </summary>
    public void MaybeInjectPostSaveFault(string operation)
    {
        if (_faultConfig.Enabled && _random.NextDouble() < _faultConfig.PostSaveFaultProbability)
        {
            throw new SimulatedServerFaultException(
                $"Simulated post-save fault during {operation}: Connection lost after commit");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            
            entity.HasMany(e => e.Todos)
                  .WithOne()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TodoEntity>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.TodoId });
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.Completed });
        });
    }
}
