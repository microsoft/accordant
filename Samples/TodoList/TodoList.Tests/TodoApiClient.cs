// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Tests
{
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Threading.Tasks;
    using TodoList.Api.Contracts;

    /// <summary>
    /// API result that wraps either success data or an error status.
    /// This is a client-side abstraction - the server returns T or error codes.
    /// </summary>
    public class ApiResult<T>
    {
        public T? Data { get; init; }
        public int StatusCode { get; init; }

        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
        public bool IsNotFound => StatusCode == 404;
        public bool IsConflict => StatusCode == 409;

        public override string ToString() => IsSuccess 
            ? $"[{StatusCode}] {Data}"
            : $"[{StatusCode}]";
    }

    /// <summary>
    /// Simple HTTP client wrapper for calling the TodoList API.
    /// Returns ApiResult&lt;T&gt; for clean success/error handling.
    /// </summary>
    public class TodoApiClient
    {
        private readonly HttpClient _httpClient;

        public TodoApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // ============================================================
        // User Operations
        // ============================================================

        /// <summary>
        /// Creates a new user.
        /// </summary>
        public async Task<ApiResult<User>> CreateUserAsync(string userId, string name)
        {
            var request = new User(userId, name);
            var response = await _httpClient.PutAsJsonAsync($"/api/users/{userId}", request);
            return await ToResult<User>(response);
        }

        /// <summary>
        /// Gets a user by ID.
        /// </summary>
        public async Task<ApiResult<User>> GetUserAsync(string userId)
        {
            var response = await _httpClient.GetAsync($"/api/users/{userId}");
            return await ToResult<User>(response);
        }

        /// <summary>
        /// Deletes a user.
        /// </summary>
        public async Task<int> DeleteUserAsync(string userId)
        {
            var response = await _httpClient.DeleteAsync($"/api/users/{userId}");
            return (int)response.StatusCode;
        }

        // ============================================================
        // Todo Operations
        // ============================================================

        /// <summary>
        /// Creates a new todo for a user.
        /// </summary>
        public async Task<ApiResult<Todo>> CreateTodoAsync(string userId, string todoId, string title)
        {
            var request = new Todo(userId, todoId, title);
            var response = await _httpClient.PutAsJsonAsync($"/api/users/{userId}/todos/{todoId}", request);
            return await ToResult<Todo>(response);
        }

        /// <summary>
        /// Gets a todo by ID for a user.
        /// </summary>
        public async Task<ApiResult<Todo>> GetTodoAsync(string userId, string todoId)
        {
            var response = await _httpClient.GetAsync($"/api/users/{userId}/todos/{todoId}");
            return await ToResult<Todo>(response);
        }

        /// <summary>
        /// Marks a todo as completed.
        /// </summary>
        public async Task<ApiResult<Todo>> CompleteTodoAsync(string userId, string todoId)
        {
            var response = await _httpClient.PostAsync($"/api/users/{userId}/todos/{todoId}/complete", null);
            return await ToResult<Todo>(response);
        }

        /// <summary>
        /// Deletes a todo for a user.
        /// </summary>
        public async Task<int> DeleteTodoAsync(string userId, string todoId)
        {
            var response = await _httpClient.DeleteAsync($"/api/users/{userId}/todos/{todoId}");
            return (int)response.StatusCode;
        }

        // ============================================================
        // Helper
        // ============================================================

        private static async Task<ApiResult<T>> ToResult<T>(HttpResponseMessage response)
        {
            T? data = default;
            if (response.IsSuccessStatusCode)
            {
                data = await response.Content.ReadFromJsonAsync<T>();
            }
            return new ApiResult<T> { Data = data, StatusCode = (int)response.StatusCode };
        }
    }
}
