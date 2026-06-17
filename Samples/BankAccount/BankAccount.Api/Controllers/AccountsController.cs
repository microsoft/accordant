// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Controllers;

using BankAccount.Api.Contracts;
using BankAccount.Api.Data;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Bank accounts REST API controller.
/// 
/// NOTE: This sample uses EF Core InMemory provider which does NOT support
/// serializable transactions. We use a SemaphoreSlim to serialize operations
/// for correctness in the single-machine test scenario.
/// 
/// In production, use a real database (SQL Server, PostgreSQL, etc.) with
/// proper transaction isolation (e.g., SERIALIZABLE or optimistic concurrency).
/// </summary>
[ApiController]
[Route("accounts")]
public class AccountsController : ControllerBase
{
    private readonly BankDbContext _db;

    // InMemory DB doesn't support transactions - use lock for test correctness
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public AccountsController(BankDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Create a new account with zero balance.
    /// PUT /accounts/{id}
    /// </summary>
    [HttpPut("{accountId}")]
    public async Task<IActionResult> CreateAccount(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var existing = await _db.Accounts.FindAsync(accountId);
            if (existing != null)
            {
                return Conflict(new { error = "Account already exists" });
            }

            var account = new AccountEntity
            {
                AccountId = accountId,
                Balance = 0
            };

            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();

            return Created($"/accounts/{accountId}", new { accountId, balance = 0m });
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Get account balance.
    /// GET /accounts/{id}
    /// </summary>
    [HttpGet("{accountId}")]
    public async Task<IActionResult> GetBalance(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var account = await _db.Accounts.FindAsync(accountId);
            if (account == null)
            {
                return NotFound(new { error = "Account not found" });
            }

            return Ok(new { accountId, balance = account.Balance });
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deposit funds into an account.
    /// POST /accounts/{id}/deposit
    /// </summary>
    [HttpPost("{accountId}/deposit")]
    public async Task<IActionResult> Deposit(string accountId, [FromBody] TransactionRequest request)
    {
        await _lock.WaitAsync();
        try
        {
            var account = await _db.Accounts.FindAsync(accountId);
            if (account == null)
            {
                return NotFound(new { error = "Account not found" });
            }

            account.Balance += request.Amount;
            await _db.SaveChangesAsync();

            return Ok(new { accountId, balance = account.Balance });
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Withdraw funds from an account.
    /// POST /accounts/{id}/withdraw
    /// </summary>
    [HttpPost("{accountId}/withdraw")]
    public async Task<IActionResult> Withdraw(string accountId, [FromBody] TransactionRequest request)
    {
        await _lock.WaitAsync();
        try
        {
            var account = await _db.Accounts.FindAsync(accountId);
            if (account == null)
            {
                return NotFound(new { error = "Account not found" });
            }

            if (account.Balance < request.Amount)
            {
                return BadRequest(new { error = "Insufficient funds", balance = account.Balance, requested = request.Amount });
            }

            account.Balance -= request.Amount;
            await _db.SaveChangesAsync();

            return Ok(new { accountId, balance = account.Balance });
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Delete an account.
    /// DELETE /accounts/{id}
    /// </summary>
    [HttpDelete("{accountId}")]
    public async Task<IActionResult> DeleteAccount(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var account = await _db.Accounts.FindAsync(accountId);
            if (account == null)
            {
                return NotFound(new { error = "Account not found" });
            }

            _db.Accounts.Remove(account);
            await _db.SaveChangesAsync();

            return NoContent();
        }
        finally
        {
            _lock.Release();
        }
    }
}
