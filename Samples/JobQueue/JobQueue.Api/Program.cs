// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using JobQueue.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();

// Configure InMemory database
builder.Services.AddDbContext<JobQueueDbContext>(options =>
    options.UseInMemoryDatabase("JobQueueDb"));

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<JobQueueDbContext>();
    context.Database.EnsureCreated();
}

app.MapControllers();

app.Run();

// Make Program accessible for WebApplicationFactory
public partial class Program { }
