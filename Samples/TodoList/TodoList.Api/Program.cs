// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Api.Controllers;
using TodoList.Api.Data;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services
        builder.Services.AddControllers();

        // Add InMemory database
        builder.Services.AddDbContext<TodoDbContext>(options =>
            options.UseInMemoryDatabase("TodoDb"));

        // Add write lock for serializability
        builder.Services.AddSingleton<WriteLock>();

        var app = builder.Build();

        // Ensure database is created
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            dbContext.Database.EnsureCreated();
        }

        // Configure pipeline
        app.UseRouting();
        app.MapControllers();

        app.Run();
    }
}
