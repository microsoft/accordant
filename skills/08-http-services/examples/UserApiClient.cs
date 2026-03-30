// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Examples.Http;

using System.Net.Http.Json;

/// <summary>
/// HTTP client wrapper using the ApiResult pattern.
/// Each method returns ApiResult&lt;T&gt; instead of throwing exceptions.
/// </summary>
public class UserApiClient
{
    private readonly HttpClient _client;

    public UserApiClient(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Create a new user.
    /// POST /api/users
    /// </summary>
    public async Task<ApiResult<User>> CreateUserAsync(CreateUserRequest request)
    {
        var response = await _client.PostAsJsonAsync("/api/users", request);
        return await ToApiResult<User>(response);
    }

    /// <summary>
    /// Get a user by ID.
    /// GET /api/users/{userId}
    /// </summary>
    public async Task<ApiResult<User>> GetUserAsync(string userId)
    {
        var response = await _client.GetAsync($"/api/users/{userId}");
        return await ToApiResult<User>(response);
    }

    /// <summary>
    /// Update a user.
    /// PUT /api/users/{userId}
    /// </summary>
    public async Task<ApiResult<User>> UpdateUserAsync(string userId, UpdateUserRequest request)
    {
        var response = await _client.PutAsJsonAsync($"/api/users/{userId}", request);
        return await ToApiResult<User>(response);
    }

    /// <summary>
    /// Delete a user.
    /// DELETE /api/users/{userId}
    /// </summary>
    public async Task<int> DeleteUserAsync(string userId)
    {
        var response = await _client.DeleteAsync($"/api/users/{userId}");
        return (int)response.StatusCode;
    }

    /// <summary>
    /// Helper to convert HttpResponseMessage to ApiResult.
    /// </summary>
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

// ----- Request/Response contracts -----

/// <summary>
/// Request to create a user.
/// </summary>
public class CreateUserRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Request to update a user.
/// </summary>
public class UpdateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// User response from the API.
/// </summary>
public class User
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
