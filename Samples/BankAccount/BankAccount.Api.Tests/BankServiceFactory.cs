// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Tests;

using BankAccount.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Factory to create the ASP.NET test host with an InMemory database.
/// Each test run gets a fresh database.
/// </summary>
public class BankServiceFactory : WebApplicationFactory<BankAccount.Api.Program>
{
    private readonly string _databaseName = $"BankTestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BankDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add InMemory database with a unique name
            services.AddDbContext<BankDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });

        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        builder.UseEnvironment("Testing");
    }

    public HttpClient CreateTestClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        // Ensure database is created and empty
        ResetDatabase();

        return client;
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        dbContext.Database.EnsureDeleted();
        dbContext.Database.EnsureCreated();
    }
}
