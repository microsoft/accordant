// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobQueue.Api.Controllers;

using JobQueue.Api.Contracts;
using JobQueue.Api.Data;
using JobQueue.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly JobQueueDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    
    // Track active background tasks (internal tracking only)
    private static readonly ConcurrentDictionary<string, Task> _backgroundTasks = new();

    /// <summary>
    /// Delay in milliseconds before a job completes. Set to 0 for immediate completion (testing).
    /// </summary>
    public static int ProcessingDelayMs { get; set; } = 100;

    /// <summary>
    /// Probability (0-100) that a job fails instead of completing.
    /// </summary>
    public static int FailureProbability { get; set; } = 0;

    public JobsController(JobQueueDbContext context, IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Create a new job.
    /// PUT /api/jobs/{jobId}
    /// Returns immediately with Pending status, processing happens in background.
    /// </summary>
    [HttpPut("{jobId}")]
    public async Task<IActionResult> CreateJob(string jobId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var existing = await _context.Jobs.FindAsync(jobId);
            if (existing != null)
            {
                return Conflict(new { error = $"Job '{jobId}' already exists" });
            }

            var entity = new JobEntity
            {
                JobId = jobId,
                Status = JobStatus.Pending,
                ResultPath = null
            };

            _context.Jobs.Add(entity);
            await _context.SaveChangesAsync();

            // Start background processing using a scope factory (safer than IServiceProvider)
            var task = Task.Run(() => ProcessJobInternalAsync(jobId, _scopeFactory));
            _backgroundTasks[jobId] = task;

            return Ok(new Job(entity.JobId, entity.Status, entity.ResultPath));
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Background job processing - simulates work then completes or fails.
    /// Uses a new service scope to get a fresh DbContext.
    /// </summary>
    private static async Task ProcessJobInternalAsync(string jobId, IServiceScopeFactory scopeFactory)
    {
        try
        {
            // Simulate processing time
            if (ProcessingDelayMs > 0)
            {
                await Task.Delay(ProcessingDelayMs);
            }

            await _writeLock.WaitAsync();
            try
            {
                // Create a new scope to get a fresh DbContext
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<JobQueueDbContext>();

                var entity = await context.Jobs.FindAsync(jobId);
                if (entity == null || entity.Status != JobStatus.Pending)
                {
                    return; // Job was deleted or already processed
                }

                // Determine success or failure
                var random = new Random();
                if (random.Next(100) < FailureProbability)
                {
                    entity.Status = JobStatus.Failed;
                    entity.ResultPath = null;
                }
                else
                {
                    entity.Status = JobStatus.Completed;
                    // ResultPath includes a random component - simulates server-generated value
                    var resultId = Guid.NewGuid().ToString("N")[..8];
                    entity.ResultPath = $"/api/jobs/{jobId}/result/{resultId}";
                }

                await context.SaveChangesAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }
        finally
        {
            _backgroundTasks.TryRemove(jobId, out _);
        }
    }

    /// <summary>
    /// Get a job by ID.
    /// GET /api/jobs/{jobId}
    /// Use this to poll for job completion.
    /// </summary>
    [HttpGet("{jobId}")]
    public async Task<IActionResult> GetJob(string jobId)
    {
        var entity = await _context.Jobs.FindAsync(jobId);
        if (entity == null)
        {
            return NotFound(new { error = $"Job '{jobId}' not found" });
        }

        return Ok(new Job(entity.JobId, entity.Status, entity.ResultPath));
    }

    /// <summary>
    /// Delete a job.
    /// DELETE /api/jobs/{jobId}
    /// </summary>
    [HttpDelete("{jobId}")]
    public async Task<IActionResult> DeleteJob(string jobId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var entity = await _context.Jobs.FindAsync(jobId);
            if (entity == null)
            {
                return NotFound(new { error = $"Job '{jobId}' not found" });
            }

            _context.Jobs.Remove(entity);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Get the result of a completed job.
    /// GET /api/jobs/{jobId}/result
    /// Only available when job status is Completed.
    /// </summary>
    [HttpGet("{jobId}/result")]
    public async Task<IActionResult> GetJobResult(string jobId)
    {
        var entity = await _context.Jobs.FindAsync(jobId);
        if (entity == null)
        {
            return NotFound(new { error = $"Job '{jobId}' not found" });
        }

        if (entity.Status != JobStatus.Completed)
        {
            return BadRequest(new { error = $"Job '{jobId}' is not completed (status: {entity.Status})" });
        }

        // Return a simple result - in reality this could be a file download, etc.
        return Ok(new { jobId, result = $"Result data for job {jobId}" });
    }
}
