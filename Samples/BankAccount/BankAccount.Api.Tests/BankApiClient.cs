// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using BankAccount.Api.Contracts;

/// <summary>
/// HTTP client wrapper that converts API responses to ApiResult&lt;T&gt;.
/// </summary>
public class BankApiClient
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BankApiClient(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Create a new account.
    /// PUT /accounts/{id}
    /// </summary>
    public async Task<ApiResult<decimal>> CreateAccount(string accountId)
    {
        var response = await _client.PutAsync($"/accounts/{accountId}", null);
        return await ToApiResult(response);
    }

    /// <summary>
    /// Get account balance.
    /// GET /accounts/{id}
    /// </summary>
    public async Task<ApiResult<decimal>> GetBalance(string accountId)
    {
        var response = await _client.GetAsync($"/accounts/{accountId}");
        return await ToApiResult(response);
    }

    /// <summary>
    /// Deposit funds.
    /// POST /accounts/{id}/deposit
    /// </summary>
    public async Task<ApiResult<decimal>> Deposit(string accountId, decimal amount)
    {
        var response = await _client.PostAsJsonAsync(
            $"/accounts/{accountId}/deposit",
            new TransactionRequest(amount));
        return await ToApiResult(response);
    }

    /// <summary>
    /// Withdraw funds.
    /// POST /accounts/{id}/withdraw
    /// </summary>
    public async Task<ApiResult<decimal>> Withdraw(string accountId, decimal amount)
    {
        var response = await _client.PostAsJsonAsync(
            $"/accounts/{accountId}/withdraw",
            new TransactionRequest(amount));
        return await ToApiResult(response);
    }

    /// <summary>
    /// Delete an account.
    /// DELETE /accounts/{id}
    /// </summary>
    public async Task<ApiResult<decimal>> DeleteAccount(string accountId)
    {
        var response = await _client.DeleteAsync($"/accounts/{accountId}");
        return await ToApiResult(response);
    }

    private static async Task<ApiResult<decimal>> ToApiResult(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            return new ApiResult<decimal>(statusCode);
        }

        // 204 No Content has no body
        if (statusCode == 204)
        {
            return new ApiResult<decimal>(statusCode);
        }

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("balance", out var balanceElement))
        {
            return new ApiResult<decimal>(statusCode, balanceElement.GetDecimal());
        }

        return new ApiResult<decimal>(statusCode);
    }
}
