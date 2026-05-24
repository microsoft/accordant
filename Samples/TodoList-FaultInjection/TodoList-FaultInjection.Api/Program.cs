// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TodoList.FaultInjection.Api.Controllers;
using TodoList.FaultInjection.Api.Data;
using TodoList.FaultInjection.Api.FaultInjection;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();

        // Fault injection config - defaults to no faults
        // Tests can override this via DI
        builder.Services.AddSingleton<ServerFaultConfig>();

        // Add fault-injecting DbContext
        builder.Services.AddDbContext<FaultInjectingDbContext>(options =>
            options.UseInMemoryDatabase("TodoDb"));

        builder.Services.AddSingleton<WriteLock>();

        var app = builder.Build();

        // Handle exceptions - return clean 500 without exception details
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\": \"Internal Server Error\"}");
            });
        });

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FaultInjectingDbContext>();
            dbContext.Database.EnsureCreated();
        }

        app.UseRouting();
        app.MapControllers();

        app.Run();
    }
}
