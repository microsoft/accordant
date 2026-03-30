// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Tests;

using Booking.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// WebApplicationFactory for testing the Booking API.
/// Uses an InMemory database for each instance.
/// </summary>
public class BookingServiceFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"BookingTestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BookingDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add InMemory database with a unique name
            services.AddDbContext<BookingDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            context.Database.EnsureCreated();
        });
    }

    public HttpClient CreateTestClient()
    {
        return CreateClient();
    }
}
