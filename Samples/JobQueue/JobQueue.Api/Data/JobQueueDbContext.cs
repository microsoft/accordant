// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobQueue.Api.Data;

using JobQueue.Api.Models;
using Microsoft.EntityFrameworkCore;

public class JobQueueDbContext : DbContext
{
    public JobQueueDbContext(DbContextOptions<JobQueueDbContext> options)
        : base(options)
    {
    }

    public DbSet<JobEntity> Jobs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.HasKey(e => e.JobId);
            entity.Property(e => e.JobId).IsRequired();
        });
    }
}
