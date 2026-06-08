// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BankAccount.Api.Tests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;
using BankAccount.Api.Contracts;

/// <summary>
/// State tracks accounts and their balances.
/// Compare this to the implementation: Controller + EF DbContext + Entity + InMemory DB!
/// This is just a dictionary.
/// </summary>
[State]
public partial class BankState
{
    /// <summary>
    /// Dictionary of account balances. Key = accountId, Value = balance.
    /// </summary>
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}

/// <summary>
/// Accordant tests for the BankAccount REST API.
/// 
/// This demonstrates:
/// - Testing a real ASP.NET Core Web API with InMemory database
/// - The spec is MUCH simpler than the implementation
/// - The spec doesn't know about HTTP, EF, database - just what should happen
/// </summary>
[TestFixture]
public class BankAccountTests
{
    // ============================================================
    // Spec Creation
    // ============================================================

    private static Spec<BankState> CreateSpec()
    {
        var spec = new Spec<BankState>();

        // ---------------------------------------------------------
        // CREATE ACCOUNT: PUT /accounts/{accountId}
        // ---------------------------------------------------------
        spec.Operation<CreateAccountRequest, CreateAccountResponse>("CreateAccount", (request, state) =>
        {
            if (state.Accounts.ContainsKey(request.AccountId))
            {
                // 409 Conflict - account already exists
                return Expect.That<CreateAccountResponse>(r => r.IsConflict,
                           $"Should return 409 Conflict because account '{request.AccountId}' already exists")
                       .SameState();
            }

            // 201 Created - new account with balance 0
            return Expect.That<CreateAccountResponse>(
                       r => r.IsSuccess && r.Balance == 0,
                       $"Should return 201 Created with balance 0")
                   .ThenState<BankState>(s => s.Accounts[request.AccountId] = 0);
        });

        // ---------------------------------------------------------
        // GET BALANCE: GET /accounts/{accountId}
        // ---------------------------------------------------------
        spec.Operation<GetBalanceRequest, GetBalanceResponse>("GetBalance", (request, state) =>
        {
            if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<GetBalanceResponse>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{request.AccountId}' doesn't exist")
                       .SameState();
            }

            // 200 OK - return balance
            return Expect.That<GetBalanceResponse>(
                       r => r.IsSuccess && r.Balance == balance,
                       $"Should return 200 OK with balance {balance}")
                   .SameState();
        });

        // ---------------------------------------------------------
        // DEPOSIT: POST /accounts/{accountId}/deposit
        // ---------------------------------------------------------
        spec.Operation<DepositRequest, DepositResponse>("Deposit", (request, state) =>
        {
            if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<DepositResponse>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{request.AccountId}' doesn't exist")
                       .SameState();
            }

            // 200 OK - deposit succeeds
            var newBalance = balance + request.Amount;
            return Expect.That<DepositResponse>(
                       r => r.IsSuccess && r.Balance == newBalance,
                       $"Should return 200 OK with new balance {newBalance}")
                   .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
        });

        // ---------------------------------------------------------
        // WITHDRAW: POST /accounts/{accountId}/withdraw
        // ---------------------------------------------------------
        spec.Operation<WithdrawRequest, WithdrawResponse>("Withdraw", (request, state) =>
        {
            if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<WithdrawResponse>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{request.AccountId}' doesn't exist")
                       .SameState();
            }

            if (balance < request.Amount)
            {
                // 400 Bad Request - insufficient funds
                return Expect.That<WithdrawResponse>(r => r.IsBadRequest,
                           $"Should return 400 Bad Request because balance {balance} < requested {request.Amount}")
                       .SameState();
            }

            // 200 OK - withdraw succeeds
            var newBalance = balance - request.Amount;
            return Expect.That<WithdrawResponse>(
                       r => r.IsSuccess && r.Balance == newBalance,
                       $"Should return 200 OK with new balance {newBalance}")
                   .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
        });

        // ---------------------------------------------------------
        // DELETE ACCOUNT: DELETE /accounts/{accountId}
        // ---------------------------------------------------------
        spec.Operation<DeleteAccountRequest, DeleteAccountResponse>("DeleteAccount", (request, state) =>
        {
            if (!state.Accounts.ContainsKey(request.AccountId))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<DeleteAccountResponse>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{request.AccountId}' doesn't exist")
                       .SameState();
            }

            // 204 No Content - account deleted
            return Expect.That<DeleteAccountResponse>(
                       r => r.StatusCode == 204,
                       $"Should return 204 No Content")
                   .ThenState<BankState>(s => s.Accounts.Remove(request.AccountId));
        });

        return spec;
    }

    // ============================================================
    // Test: Auto-generated sequential tests
    // ============================================================

    private BankServiceFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new BankServiceFactory();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task AutoGeneratedSequentialTests()
    {
        TestContext.WriteLine("=== Auto-Generated Sequential Tests ===");
        TestContext.WriteLine("Exploring state space and generating test sequences...");

        var spec = CreateSpec();

        // Bind spec operations to the HTTP client
        spec.ExecuteWith<BankApiClient>()
            .Bind<CreateAccountRequest, CreateAccountResponse>("CreateAccount",
                (client, req) => client.CreateAccount(req.AccountId).Result)
            .Bind<GetBalanceRequest, GetBalanceResponse>("GetBalance",
                (client, req) => client.GetBalance(req.AccountId).Result)
            .Bind<DepositRequest, DepositResponse>("Deposit",
                (client, req) => client.Deposit(req.AccountId, req.Amount).Result)
            .Bind<WithdrawRequest, WithdrawResponse>("Withdraw",
                (client, req) => client.Withdraw(req.AccountId, req.Amount).Result)
            .Bind<DeleteAccountRequest, DeleteAccountResponse>("DeleteAccount",
                (client, req) => client.DeleteAccount(req.AccountId).Result);

        // Define the inputs to explore
        var inputs = new InputSet
        {
            spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount").With(new CreateAccountRequest("alice"), "Create alice"),
            spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount").With(new CreateAccountRequest("bob"), "Create bob"),
            spec.GetOperation<GetBalanceRequest, GetBalanceResponse>("GetBalance").With(new GetBalanceRequest("alice"), "Get alice balance"),
            spec.GetOperation<DepositRequest, DepositResponse>("Deposit").With(new DepositRequest("alice", 100m), "Deposit 100 to alice"),
            spec.GetOperation<DepositRequest, DepositResponse>("Deposit").With(new DepositRequest("alice", 50m), "Deposit 50 to alice"),
            spec.GetOperation<WithdrawRequest, WithdrawResponse>("Withdraw").With(new WithdrawRequest("alice", 30m), "Withdraw 30 from alice"),
            spec.GetOperation<WithdrawRequest, WithdrawResponse>("Withdraw").With(new WithdrawRequest("alice", 200m), "Withdraw 200 from alice"),
            spec.GetOperation<DeleteAccountRequest, DeleteAccountResponse>("DeleteAccount").With(new DeleteAccountRequest("alice"), "Delete alice"),
        };

        // Generate test sequences
        var initialState = new BankState();
        var testCases = spec.GenerateTests(initialState, inputs, new TestGenerationOptions
        {
            MaxDepth = 4,
            StateConstraint = s =>
            {
                var state = (BankState)s;
                // Limit state space: max 2 accounts, max balance 300
                return state.Accounts.Count <= 2 &&
                       state.Accounts.Values.All(b => b <= 300);
            }
        });

        // Run tests
        var context = spec.CreateTestingContext();
        
        // Create a single client - simulates a real client talking to a persistent service
        var httpClient = _factory.CreateClient();
        var client = new BankApiClient(httpClient);
        context.Register(client);
        
        // Known accounts used in tests - we'll clean these up before each test
        var knownAccounts = new[] { "alice", "bob" };
        
        var results = await spec.RunTests(context, initialState, testCases, new TestExecutionOptions
        {
            BeforeEachAsync = async _ =>
            {
                // Clean up known accounts before each test (ignore 404 if they don't exist)
                foreach (var accountId in knownAccounts)
                {
                    await client.DeleteAccount(accountId);
                }
            }
        });

        // Report results
        var logPath = results.FirstOrDefault()?.LogFilePath;
        TestContext.WriteLine($"Generated and ran {results.Count} test cases");
        
        // Show a few example sequences (longest first)
        var sampleCases = testCases.OrderByDescending(tc => tc.OperationCalls.Count).Take(10);
        foreach (var tc in sampleCases)
        {
            var sequence = string.Join(" → ", tc.OperationCalls.Select(op => op.Name));
            TestContext.WriteLine($"  {sequence}");
        }
        if (testCases.Count > 10)
            TestContext.WriteLine($"  ... and {testCases.Count - 10} more");

        if (logPath != null)
        {
            TestContext.WriteLine($"Detailed test logs: {logPath}");
            TestContext.WriteLine($"  Open test-runner.txt in that folder to see step-by-step execution details.");
        }
        Assert.That(results.All(r => r.Success), Is.True,
            $"Failed: {results.FirstOrDefault(r => !r.Success)?.LastFailureMessage}");
    }

    // ============================================================
    // Hand-written tests using the spec as oracle
    // ============================================================
    // 
    // You don't have to rely only on auto-generated tests.
    // Write targeted scenarios — the spec validates every response.
    // A local Check() helper keeps it concise.
    // ============================================================

    [Test]
    public async Task HandWrittenScenarios()
    {
        TestContext.WriteLine("=== Hand-Written Scenarios (spec as oracle) ===");
        var spec = CreateSpec();
        var httpClient = _factory.CreateClient();
        var client = new BankApiClient(httpClient);

        // Clean up any leftover state from other tests
        foreach (var id in new[] { "alice", "bob", "carol", "ghost" })
            await client.DeleteAccount(id);

        var createOp = spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount");
        var depositOp = spec.GetOperation<DepositRequest, DepositResponse>("Deposit");
        var withdrawOp = spec.GetOperation<WithdrawRequest, WithdrawResponse>("Withdraw");
        var getBalanceOp = spec.GetOperation<GetBalanceRequest, GetBalanceResponse>("GetBalance");
        var deleteOp = spec.GetOperation<DeleteAccountRequest, DeleteAccountResponse>("DeleteAccount");

        // Helper: validate response against spec, advance state profile
        var stateProfile = new StateProfile(new BankState());
        void Check<TReq, TResp>(Operation<TReq, TResp, BankState> op, TReq request, object response)
        {
            var (isValid, message, next) = spec.Allows(op, request, response, stateProfile);
            Assert.That(isValid, Is.True, message);
            stateProfile = next;
        }

        // Reset both model and system state between scenarios
        async Task Reset()
        {
            await client.DeleteAccount("alice");
            stateProfile = new StateProfile(new BankState());
        }

        // --- Scenario 1: Full lifecycle (create → deposit → withdraw → check → delete → verify gone) ---
        await Reset();
        Check(createOp, new CreateAccountRequest("alice"), await client.CreateAccount("alice"));
        Check(depositOp, new DepositRequest("alice", 200m), await client.Deposit("alice", 200m));
        Check(withdrawOp, new WithdrawRequest("alice", 75m), await client.Withdraw("alice", 75m));
        Check(getBalanceOp, new GetBalanceRequest("alice"), await client.GetBalance("alice"));
        Check(deleteOp, new DeleteAccountRequest("alice"), await client.DeleteAccount("alice"));
        Check(getBalanceOp, new GetBalanceRequest("alice"), await client.GetBalance("alice")); // 404

        // --- Scenario 2: Insufficient funds rejected ---
        await Reset();
        Check(createOp, new CreateAccountRequest("alice"), await client.CreateAccount("alice"));
        Check(depositOp, new DepositRequest("alice", 50m), await client.Deposit("alice", 50m));
        Check(withdrawOp, new WithdrawRequest("alice", 100m), await client.Withdraw("alice", 100m)); // 400

        // --- Scenario 3: Duplicate create returns 409 ---
        await Reset();
        Check(createOp, new CreateAccountRequest("alice"), await client.CreateAccount("alice"));
        Check(createOp, new CreateAccountRequest("alice"), await client.CreateAccount("alice")); // 409

        // --- Scenario 4: All operations on nonexistent account return 404 ---
        await Reset();
        Check(getBalanceOp, new GetBalanceRequest("alice"), await client.GetBalance("alice"));
        Check(depositOp, new DepositRequest("alice", 100m), await client.Deposit("alice", 100m));
        Check(withdrawOp, new WithdrawRequest("alice", 50m), await client.Withdraw("alice", 50m));
        Check(deleteOp, new DeleteAccountRequest("alice"), await client.DeleteAccount("alice"));

        TestContext.WriteLine("All 4 hand-written scenarios passed (spec validated every response).");
    }

    // ============================================================
    // Visualization: Generate state graph DOT file
    // ============================================================

    /// <summary>
    /// Generates a DOT file visualizing the state space.
    /// Run with: dotnet test --filter "VisualizeStateSpace"
    /// Convert to PNG with: dot -Tpng state-graph.dot -o state-graph.png
    /// </summary>
    [Test]
    public void VisualizeStateSpace()
    {
        TestContext.WriteLine("=== State Space Visualization ===");
        var spec = CreateSpec();
        var initialState = new BankState();

        // Define inputs to explore - just alice with a few amounts
        var inputs = new InputSet
        {
            spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount").With(new CreateAccountRequest("alice"), "Create(alice)"),
            spec.GetOperation<DepositRequest, DepositResponse>("Deposit").With(new DepositRequest("alice", 50m), "Deposit(alice, 50)"),
            spec.GetOperation<DepositRequest, DepositResponse>("Deposit").With(new DepositRequest("alice", 100m), "Deposit(alice, 100)"),
            spec.GetOperation<WithdrawRequest, WithdrawResponse>("Withdraw").With(new WithdrawRequest("alice", 30m), "Withdraw(alice, 30)"),
            spec.GetOperation<WithdrawRequest, WithdrawResponse>("Withdraw").With(new WithdrawRequest("alice", 70m), "Withdraw(alice, 70)"),
            spec.GetOperation<DeleteAccountRequest, DeleteAccountResponse>("DeleteAccount").With(new DeleteAccountRequest("alice"), "Delete(alice)"),
        };

        // Generate DOT visualization
        var dot = spec.VisualizeStateSpace(
            initialState,
            inputs,
            generationOptions: new TestGenerationOptions 
            { 
                MaxDepth = 5,
            },
            visualizationOptions: new VisualizationOptions
            {
                NodeLabelLambda = node =>
                {
                    var state = (BankState)node.State;
                    if (state.Accounts.Count == 0)
                        return "Empty";
                    return string.Join(", ", state.Accounts.Select(kv => $"{kv.Key}: {kv.Value}"));
                }
            });

        // Write to file
        var dotPath = Path.GetFullPath("bank-account-state-graph.dot");
        File.WriteAllText(dotPath, dot);
        
        TestContext.WriteLine($"State graph written to: {dotPath}");
        TestContext.WriteLine("Convert to PNG: dot -Tpng bank-account-state-graph.dot -o bank-account-state-graph.png");
        TestContext.WriteLine();

        Assert.IsNotEmpty(dot);
    }
}
