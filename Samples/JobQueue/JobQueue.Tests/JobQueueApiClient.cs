// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobQueue.Tests;

using System.Net.Http.Json;
using JobQueue.Api.Contracts;

/// <summary>
/// HTTP client wrapper for the JobQueue API.
/// </summary>
public class JobQueueApiClient
{
    private readonly HttpClient _client;

    public JobQueueApiClient(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Create a new job.
    /// PUT /api/jobs/{jobId}
    /// </summary>
    public async Task<ApiResult<Job>> CreateJobAsync(string jobId)
    {
        var response = await _client.PutAsync($"/api/jobs/{jobId}", null);
        return await ToApiResult<Job>(response);
    }

    /// <summary>
    /// Get a job by ID.
    /// GET /api/jobs/{jobId}
    /// </summary>
    public async Task<ApiResult<Job>> GetJobAsync(string jobId)
    {
        var response = await _client.GetAsync($"/api/jobs/{jobId}");
        return await ToApiResult<Job>(response);
    }

    /// <summary>
    /// Delete a job.
    /// DELETE /api/jobs/{jobId}
    /// </summary>
    public async Task<int> DeleteJobAsync(string jobId)
    {
        var response = await _client.DeleteAsync($"/api/jobs/{jobId}");
        return (int)response.StatusCode;
    }

    private static async Task<ApiResult<T>> ToApiResult<T>(HttpResponseMessage response)
    {
        var result = new ApiResult<T>
        {
            StatusCode = (int)response.StatusCode
        };

        if (response.IsSuccessStatusCode)
        {
            result.Data = await response.Content.ReadFromJsonAsync<T>();
        }
        else
        {
            result.Error = await response.Content.ReadAsStringAsync();
        }

        return result;
    }
}
