// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Data;

using Microsoft.EntityFrameworkCore;

public class BankDbContext : DbContext
{
    public BankDbContext(DbContextOptions<BankDbContext> options)
        : base(options)
    {
    }

    public DbSet<AccountEntity> Accounts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountEntity>(entity =>
        {
            entity.HasKey(e => e.AccountId);
            entity.Property(e => e.Balance).HasPrecision(18, 2);
        });
    }
}
