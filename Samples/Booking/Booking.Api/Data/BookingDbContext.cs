// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Api.Data;

using Booking.Api.Models;
using Microsoft.EntityFrameworkCore;

public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options)
    {
    }

    public DbSet<SlotEntity> Slots { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SlotEntity>(entity =>
        {
            entity.HasKey(e => e.SlotId);
            entity.Property(e => e.SlotId).IsRequired();
        });
    }
}
