// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BankAccount.Api.Data;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services
        builder.Services.AddControllers();

        // Add InMemory database
        builder.Services.AddDbContext<BankDbContext>(options =>
            options.UseInMemoryDatabase("BankDb"));

        var app = builder.Build();

        // Ensure database is created
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BankDbContext>();
            dbContext.Database.EnsureCreated();
        }

        // Configure pipeline
        app.UseRouting();
        app.MapControllers();

        app.Run();
    }
}
