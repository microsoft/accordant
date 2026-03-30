// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Data;

public class AccountEntity
{
    public string AccountId { get; set; } = string.Empty;
    public decimal Balance { get; set; } = 0;
}
