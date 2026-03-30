// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobQueue.Tests;

using JobQueue.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// WebApplicationFactory for testing the JobQueue API.
/// Uses an InMemory database for each instance.
/// </summary>
public class JobQueueServiceFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"JobQueueTestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<JobQueueDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add InMemory database with a unique name
            services.AddDbContext<JobQueueDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<JobQueueDbContext>();
            context.Database.EnsureCreated();
        });
    }

    public HttpClient CreateTestClient()
    {
        return CreateClient();
    }
}
