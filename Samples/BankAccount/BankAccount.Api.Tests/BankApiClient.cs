// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using BankAccount.Api.Contracts;

/// <summary>
/// HTTP client wrapper that converts API responses to typed response records.
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
    public async Task<CreateAccountResponse> CreateAccount(string accountId)
    {
        var response = await _client.PutAsync($"/accounts/{accountId}", null);
        var (statusCode, balance) = await ParseResponse(response);
        return new CreateAccountResponse(statusCode, balance);
    }

    /// <summary>
    /// Get account balance.
    /// GET /accounts/{id}
    /// </summary>
    public async Task<GetBalanceResponse> GetBalance(string accountId)
    {
        var response = await _client.GetAsync($"/accounts/{accountId}");
        var (statusCode, balance) = await ParseResponse(response);
        return new GetBalanceResponse(statusCode, balance);
    }

    /// <summary>
    /// Deposit funds.
    /// POST /accounts/{id}/deposit
    /// </summary>
    public async Task<DepositResponse> Deposit(string accountId, decimal amount)
    {
        var response = await _client.PostAsJsonAsync(
            $"/accounts/{accountId}/deposit",
            new TransactionRequest(amount));
        var (statusCode, balance) = await ParseResponse(response);
        return new DepositResponse(statusCode, balance);
    }

    /// <summary>
    /// Withdraw funds.
    /// POST /accounts/{id}/withdraw
    /// </summary>
    public async Task<WithdrawResponse> Withdraw(string accountId, decimal amount)
    {
        var response = await _client.PostAsJsonAsync(
            $"/accounts/{accountId}/withdraw",
            new TransactionRequest(amount));
        var (statusCode, balance) = await ParseResponse(response);
        return new WithdrawResponse(statusCode, balance);
    }

    /// <summary>
    /// Delete an account.
    /// DELETE /accounts/{id}
    /// </summary>
    public async Task<DeleteAccountResponse> DeleteAccount(string accountId)
    {
        var response = await _client.DeleteAsync($"/accounts/{accountId}");
        var (statusCode, balance) = await ParseResponse(response);
        return new DeleteAccountResponse(statusCode, balance);
    }

    private static async Task<(int StatusCode, decimal? Balance)> ParseResponse(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            return (statusCode, null);
        }

        // 204 No Content has no body
        if (statusCode == 204)
        {
            return (statusCode, null);
        }

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("balance", out var balanceElement))
        {
            return (statusCode, balanceElement.GetDecimal());
        }

        return (statusCode, null);
    }
}
