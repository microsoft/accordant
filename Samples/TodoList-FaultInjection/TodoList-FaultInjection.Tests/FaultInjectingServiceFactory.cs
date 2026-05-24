// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoList.FaultInjection.Api.Data;
using TodoList.FaultInjection.Api.FaultInjection;

/// <summary>
/// Factory to create the ASP.NET test host with configurable fault injection.
/// </summary>
public class FaultInjectingServiceFactory : WebApplicationFactory<TodoList.FaultInjection.Api.Program>
{
    private readonly string _databaseName = $"TodoTestDb_{Guid.NewGuid()}";
    private readonly ServerFaultConfig _serverFaultConfig;

    public FaultInjectingServiceFactory(ServerFaultConfig? serverFaultConfig = null)
    {
        _serverFaultConfig = serverFaultConfig ?? new ServerFaultConfig();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing registrations
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<FaultInjectingDbContext>));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            var faultConfigDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ServerFaultConfig));
            if (faultConfigDescriptor != null)
                services.Remove(faultConfigDescriptor);

            // Add our configured fault config
            services.AddSingleton(_serverFaultConfig);

            // Add InMemory database with unique name
            services.AddDbContext<FaultInjectingDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
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

        using (var scope = Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FaultInjectingDbContext>();
            dbContext.Database.EnsureCreated();
        }

        return client;
    }
}
