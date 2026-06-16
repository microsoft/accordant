// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobQueue.Api.Models;

using System.ComponentModel.DataAnnotations;
using JobQueue.Api.Contracts;

/// <summary>
/// Entity Framework entity for a job.
/// </summary>
public class JobEntity
{
    [Key]
    public string JobId { get; set; } = string.Empty;

    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>
    /// Path to the result - only set when Status is Completed.
    /// </summary>
    public string? ResultPath { get; set; }
}
