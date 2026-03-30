// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Tests
{
    using System;
    using System.Net.Http;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc.Testing;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using TodoList.Api.Data;

    /// <summary>
    /// Factory to create the ASP.NET test host with an InMemory database.
    /// Each test run gets a fresh database.
    /// </summary>
    public class TodoServiceFactory : WebApplicationFactory<TodoList.Api.Program>
    {
        private readonly string _databaseName = $"TodoTestDb_{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove the existing DbContext registration
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<TodoDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Add InMemory database with a unique name
                services.AddDbContext<TodoDbContext>(options =>
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

            // Ensure database is created
            using (var scope = Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
                dbContext.Database.EnsureCreated();
            }

            return client;
        }
    }
}
