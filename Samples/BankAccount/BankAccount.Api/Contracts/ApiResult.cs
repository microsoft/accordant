// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Contracts;

/// <summary>
/// Simple wrapper for API responses that includes status code and optional data.
/// </summary>
public record ApiResult<T>(int StatusCode, T? Data = default)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotFound => StatusCode == 404;
    public bool IsConflict => StatusCode == 409;
    public bool IsBadRequest => StatusCode == 400;
}

/// <summary>
/// Request for deposit/withdraw operations.
/// </summary>
public record TransactionRequest(decimal Amount);
