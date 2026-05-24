// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using TodoList.FaultInjection.Api.Contracts;

/// <summary>
/// API result that captures success, definite error, or indefinite failure.
/// 
/// An indefinite failure means we don't know what happened - the operation
/// might have succeeded (server processed it, we lost the response) or failed
/// (request never reached server, or server threw before processing).
/// </summary>
public class ApiResult<T>
{
    public T? Data { get; init; }
    public int StatusCode { get; init; }
    
    /// <summary>
    /// True if this result came from a network-level failure (socket timeout,
    /// connection reset, etc.) where we never got a proper HTTP response.
    /// </summary>
    public bool IsNetworkError { get; init; }
    
    /// <summary>
    /// Description of what went wrong (for failures).
    /// </summary>
    public string? FailureMessage { get; init; }

    /// <summary>
    /// True if we don't know whether the operation succeeded or failed.
    /// This is true when: status code >= 500 OR network error occurred.
    /// </summary>
    public bool IsIndefiniteFailure => IsServerError || IsNetworkError;

    public bool IsSuccess => !IsIndefiniteFailure && StatusCode >= 200 && StatusCode < 300;
    public bool IsNotFound => !IsIndefiniteFailure && StatusCode == 404;
    public bool IsConflict => !IsIndefiniteFailure && StatusCode == 409;
    
    /// <summary>
    /// True if this is a 5xx server error.
    /// </summary>
    public bool IsServerError => StatusCode >= 500 && StatusCode < 600;

    public override string ToString() => IsNetworkError
        ? $"[NetworkError: {FailureMessage}]"
        : IsServerError
            ? $"[{StatusCode} ServerError]"
            : IsSuccess
                ? $"[{StatusCode}] {Data}"
                : $"[{StatusCode}]";
}

/// <summary>
/// HTTP client that can inject faults to simulate network failures.
/// Also treats 500 errors as indefinite failures (for write operations).
/// </summary>
public class FaultInjectingApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ClientFaultConfig _config;
    private readonly Random _random;

    public FaultInjectingApiClient(HttpClient httpClient, ClientFaultConfig config)
    {
        _httpClient = httpClient;
        _config = config;
        _random = config.Seed.HasValue ? new Random(config.Seed.Value) : new Random();
    }

    // ============================================================
    // User Operations
    // ============================================================

    public async Task<ApiResult<User>> CreateUserAsync(string userId, string name)
    {
        return await ExecuteWithFaultInjection(
            async () =>
            {
                var request = new User(userId, name);
                var response = await _httpClient.PutAsJsonAsync($"/api/users/{userId}", request);
                return await ToResult<User>(response);
            },
            "CreateUser");
    }

    public async Task<ApiResult<User>> GetUserAsync(string userId)
    {
        return await ExecuteWithFaultInjection(
            async () =>
            {
                var response = await _httpClient.GetAsync($"/api/users/{userId}");
                return await ToResult<User>(response);
            },
            "GetUser");
    }

    public async Task<ApiResult<int>> DeleteUserAsync(string userId)
    {
        return await ExecuteWithFaultInjection(
            async () =>
            {
                var response = await _httpClient.DeleteAsync($"/api/users/{userId}");
                return new ApiResult<int> 
                { 
                    Data = (int)response.StatusCode, 
                    StatusCode = (int)response.StatusCode
                };
            },
            "DeleteUser");
    }

    /// <summary>
    /// Delete user without fault injection (for test cleanup).
    /// </summary>
    public async Task CleanupDeleteUserAsync(string userId)
    {
        await _httpClient.DeleteAsync($"/api/users/{userId}/try");
    }

    // ============================================================
    // Todo Operations
    // ============================================================

    public async Task<ApiResult<Todo>> CreateTodoAsync(string userId, string todoId, string title)
    {
        return await ExecuteWithFaultInjection(
            async () =>
            {
                var request = new Todo(userId, todoId, title);
                var response = await _httpClient.PutAsJsonAsync($"/api/users/{userId}/todos/{todoId}", request);
                return await ToResult<Todo>(response);
            },
            "CreateTodo");
    }

    public async Task<ApiResult<Todo>> GetTodoAsync(string userId, string todoId)
    {
        return await ExecuteWithFaultInjection(
            async () =>
            {
                var response = await _httpClient.GetAsync($"/api/users/{userId}/todos/{todoId}");
                return await ToResult<Todo>(response);
            },
            "GetTodo");
    }

    public async Task<ApiResult<Todo>> CompleteTodoAsync(string userId, string todoId)
    {
        return await ExecuteWithFaultInjection(
            async () =>
            {
                var response = await _httpClient.PostAsync($"/api/users/{userId}/todos/{todoId}/complete", null);
                return await ToResult<Todo>(response);
            },
            "CompleteTodo");
    }

    public async Task<ApiResult<int>> DeleteTodoAsync(string userId, string todoId)
    {
        return await ExecuteWithFaultInjection(
            async () =>
            {
                var response = await _httpClient.DeleteAsync($"/api/users/{userId}/todos/{todoId}");
                return new ApiResult<int> 
                { 
                    Data = (int)response.StatusCode, 
                    StatusCode = (int)response.StatusCode
                };
            },
            "DeleteTodo");
    }

    // ============================================================
    // Fault Injection Logic
    // ============================================================

    /// <summary>
    /// Unified method that handles:
    /// 1. Fault injection (throwing real SocketExceptions)
    /// 2. Catching network exceptions (both real and injected)
    /// 3. Converting to ApiResult with IsNetworkError = true
    /// </summary>
    private async Task<ApiResult<T>> ExecuteWithFaultInjection<T>(
        Func<Task<ApiResult<T>>> operation,
        string operationName)
    {
        try
        {
            if (_config.Enabled)
            {
                // Pre-request fault: throw SocketException (simulates network failure)
                if (_random.NextDouble() < _config.PreRequestFaultProbability)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }
            }

            // Execute the real operation
            var result = await operation();

            if (_config.Enabled)
            {
                // Post-response fault: throw after successful operation
                // (simulates losing the response after server processed request)
                if (result.IsSuccess && _random.NextDouble() < _config.PostResponseFaultProbability)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }
            }

            return result;
        }
        catch (Exception ex) when (IsNetworkRelatedException(ex))
        {
            // Convert network exceptions to ApiResult with IsNetworkError = true
            return new ApiResult<T>
            {
                IsNetworkError = true,
                FailureMessage = $"{operationName}: {ex.GetType().Name} - {ex.Message}",
                StatusCode = 0
            };
        }
    }

    /// <summary>
    /// Determines if an exception represents a network-level failure
    /// (socket timeout, connection reset, etc.)
    /// </summary>
    private static bool IsNetworkRelatedException(Exception ex)
    {
        return ex is TaskCanceledException ||  // Timeout
               ex is SocketException ||        // Socket-level errors
               (ex is HttpRequestException httpEx &&
                (httpEx.InnerException is WebException ||
                 httpEx.InnerException is SocketException));
    }

    private static async Task<ApiResult<T>> ToResult<T>(HttpResponseMessage response)
    {
        T? data = default;
        if (response.IsSuccessStatusCode)
        {
            data = await response.Content.ReadFromJsonAsync<T>();
        }

        return new ApiResult<T> 
        { 
            Data = data, 
            StatusCode = (int)response.StatusCode
            // IsIndefiniteFailure is computed: true if StatusCode >= 500 or IsNetworkError
        };
    }
}
