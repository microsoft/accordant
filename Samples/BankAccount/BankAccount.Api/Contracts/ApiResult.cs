// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Contracts;

/// <summary>
/// Base response type for bank API operations.
/// Contains the HTTP status code and optional account balance.
/// </summary>
public record BankResponse(int StatusCode, decimal? Balance = default)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotFound => StatusCode == 404;
    public bool IsConflict => StatusCode == 409;
    public bool IsBadRequest => StatusCode == 400;
}

/// <summary>Response from creating an account.</summary>
public record CreateAccountResponse(int StatusCode, decimal? Balance = default) : BankResponse(StatusCode, Balance);

/// <summary>Response from getting an account balance.</summary>
public record GetBalanceResponse(int StatusCode, decimal? Balance = default) : BankResponse(StatusCode, Balance);

/// <summary>Response from depositing funds.</summary>
public record DepositResponse(int StatusCode, decimal? Balance = default) : BankResponse(StatusCode, Balance);

/// <summary>Response from withdrawing funds.</summary>
public record WithdrawResponse(int StatusCode, decimal? Balance = default) : BankResponse(StatusCode, Balance);

/// <summary>Response from deleting an account.</summary>
public record DeleteAccountResponse(int StatusCode, decimal? Balance = default) : BankResponse(StatusCode, Balance);

/// <summary>
/// Request for creating an account (used by the spec).
/// </summary>
public record CreateAccountRequest(string AccountId);

/// <summary>
/// Request for getting an account balance (used by the spec).
/// </summary>
public record GetBalanceRequest(string AccountId);

/// <summary>
/// Request for deposit operations (used by the spec).
/// </summary>
public record DepositRequest(string AccountId, decimal Amount);

/// <summary>
/// Request for withdraw operations (used by the spec).
/// </summary>
public record WithdrawRequest(string AccountId, decimal Amount);

/// <summary>
/// Request for deleting an account (used by the spec).
/// </summary>
public record DeleteAccountRequest(string AccountId);

/// <summary>
/// HTTP request body for deposit/withdraw operations.
/// </summary>
public record TransactionRequest(decimal Amount);
