// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobQueue.Api.Contracts;

/// <summary>
/// Job status - represents the lifecycle of a background job.
/// </summary>
public enum JobStatus
{
    /// <summary>Job created, processing has not completed yet.</summary>
    Pending,
    
    /// <summary>Job completed successfully, ResultPath is available.</summary>
    Completed,
    
    /// <summary>Job failed, no result available.</summary>
    Failed
}

/// <summary>
/// Represents a background job.
/// </summary>
/// <param name="JobId">The unique identifier for the job (client-provided)</param>
/// <param name="Status">Current status of the job</param>
/// <param name="ResultPath">Path to the result (only set when Completed)</param>
public record Job(
    string JobId,
    JobStatus Status,
    string? ResultPath = null);
