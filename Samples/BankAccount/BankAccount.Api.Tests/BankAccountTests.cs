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
public partial class BankState : State
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
        spec.Operation<string, ApiResult<decimal>>("CreateAccount", (accountId, state) =>
        {
            if (state.Accounts.ContainsKey(accountId))
            {
                // 409 Conflict - account already exists
                return Expect.That<ApiResult<decimal>>(r => r.IsConflict,
                           $"Should return 409 Conflict because account '{accountId}' already exists")
                       .SameState();
            }

            // 201 Created - new account with balance 0
            return Expect.That<ApiResult<decimal>>(
                       r => r.IsSuccess && r.Data == 0,
                       $"Should return 201 Created with balance 0")
                   .ThenState<BankState>(s => s.Accounts[accountId] = 0);
        });

        // ---------------------------------------------------------
        // GET BALANCE: GET /accounts/{accountId}
        // ---------------------------------------------------------
        spec.Operation<string, ApiResult<decimal>>("GetBalance", (accountId, state) =>
        {
            if (!state.Accounts.TryGetValue(accountId, out var balance))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<ApiResult<decimal>>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{accountId}' doesn't exist")
                       .SameState();
            }

            // 200 OK - return balance
            return Expect.That<ApiResult<decimal>>(
                       r => r.IsSuccess && r.Data == balance,
                       $"Should return 200 OK with balance {balance}")
                   .SameState();
        });

        // ---------------------------------------------------------
        // DEPOSIT: POST /accounts/{accountId}/deposit
        // ---------------------------------------------------------
        spec.Operation<(string AccountId, decimal Amount), ApiResult<decimal>>("Deposit", (request, state) =>
        {
            if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<ApiResult<decimal>>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{request.AccountId}' doesn't exist")
                       .SameState();
            }

            // 200 OK - deposit succeeds
            var newBalance = balance + request.Amount;
            return Expect.That<ApiResult<decimal>>(
                       r => r.IsSuccess && r.Data == newBalance,
                       $"Should return 200 OK with new balance {newBalance}")
                   .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
        });

        // ---------------------------------------------------------
        // WITHDRAW: POST /accounts/{accountId}/withdraw
        // ---------------------------------------------------------
        spec.Operation<(string AccountId, decimal Amount), ApiResult<decimal>>("Withdraw", (request, state) =>
        {
            if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<ApiResult<decimal>>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{request.AccountId}' doesn't exist")
                       .SameState();
            }

            if (balance < request.Amount)
            {
                // 400 Bad Request - insufficient funds
                return Expect.That<ApiResult<decimal>>(r => r.IsBadRequest,
                           $"Should return 400 Bad Request because balance {balance} < requested {request.Amount}")
                       .SameState();
            }

            // 200 OK - withdraw succeeds
            var newBalance = balance - request.Amount;
            return Expect.That<ApiResult<decimal>>(
                       r => r.IsSuccess && r.Data == newBalance,
                       $"Should return 200 OK with new balance {newBalance}")
                   .ThenState<BankState>(s => s.Accounts[request.AccountId] = newBalance);
        });

        // ---------------------------------------------------------
        // DELETE ACCOUNT: DELETE /accounts/{accountId}
        // ---------------------------------------------------------
        spec.Operation<string, ApiResult<decimal>>("DeleteAccount", (accountId, state) =>
        {
            if (!state.Accounts.ContainsKey(accountId))
            {
                // 404 Not Found - account doesn't exist
                return Expect.That<ApiResult<decimal>>(r => r.IsNotFound,
                           $"Should return 404 Not Found because account '{accountId}' doesn't exist")
                       .SameState();
            }

            // 204 No Content - account deleted
            return Expect.That<ApiResult<decimal>>(
                       r => r.StatusCode == 204,
                       $"Should return 204 No Content")
                   .ThenState<BankState>(s => s.Accounts.Remove(accountId));
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
        var spec = CreateSpec();

        // Bind spec operations to the HTTP client
        spec.ExecuteWith<BankApiClient>()
            .Bind<string, ApiResult<decimal>>("CreateAccount",
                (client, accountId) => client.CreateAccount(accountId).Result)
            .Bind<string, ApiResult<decimal>>("GetBalance",
                (client, accountId) => client.GetBalance(accountId).Result)
            .Bind<(string, decimal), ApiResult<decimal>>("Deposit",
                (client, req) => client.Deposit(req.Item1, req.Item2).Result)
            .Bind<(string, decimal), ApiResult<decimal>>("Withdraw",
                (client, req) => client.Withdraw(req.Item1, req.Item2).Result)
            .Bind<string, ApiResult<decimal>>("DeleteAccount",
                (client, accountId) => client.DeleteAccount(accountId).Result);

        // Define the inputs to explore
        var inputs = new InputSet
        {
            spec.GetOperation<string, ApiResult<decimal>>("CreateAccount").With("alice", "Create alice"),
            spec.GetOperation<string, ApiResult<decimal>>("CreateAccount").With("bob", "Create bob"),
            spec.GetOperation<string, ApiResult<decimal>>("GetBalance").With("alice", "Get alice balance"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Deposit").With(("alice", 100m), "Deposit 100 to alice"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Deposit").With(("alice", 50m), "Deposit 50 to alice"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Withdraw").With(("alice", 30m), "Withdraw 30 from alice"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Withdraw").With(("alice", 200m), "Withdraw 200 from alice"),
            spec.GetOperation<string, ApiResult<decimal>>("DeleteAccount").With("alice", "Delete alice"),
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
        TestContext.WriteLine($"Generated and ran {results.Count} test cases");
        Assert.That(results.All(r => r.Success), Is.True,
            $"Failed: {results.FirstOrDefault(r => !r.Success)?.LastFailureMessage}");
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
        var spec = CreateSpec();
        var initialState = new BankState();

        // Define inputs to explore - just alice with a few amounts
        var inputs = new InputSet
        {
            spec.GetOperation<string, ApiResult<decimal>>("CreateAccount").With("alice", "Create(alice)"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Deposit").With(("alice", 50m), "Deposit(alice, 50)"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Deposit").With(("alice", 100m), "Deposit(alice, 100)"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Withdraw").With(("alice", 30m), "Withdraw(alice, 30)"),
            spec.GetOperation<(string, decimal), ApiResult<decimal>>("Withdraw").With(("alice", 70m), "Withdraw(alice, 70)"),
            spec.GetOperation<string, ApiResult<decimal>>("DeleteAccount").With("alice", "Delete(alice)"),
        };

        // Generate DOT visualization
        var dot = spec.VisualizeStateSpace(
            initialState,
            inputs,
            generationOptions: new TestGenerationOptions { MaxDepth = 5 },
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
        var outputPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "state-graph.dot");
        File.WriteAllText(outputPath, dot);
        
        TestContext.WriteLine($"State graph written to: {outputPath}");
        TestContext.WriteLine();

        // Also generate test cases to count them
        var context = spec.CreateTestingContext();
        var testCases = TestCaseGenerator.GenerateSequentialTestCases(
            context,
            initialState,
            inputs,
            new TestGenerationOptions { MaxDepth = 5 });

        TestContext.WriteLine($"Generated {testCases.Count} test cases");
        TestContext.WriteLine();
        
        // Show all test cases
        foreach (var tc in testCases)
        {
            var sequence = string.Join(" → ", tc.OperationCalls.Select(op => op.Name));
            TestContext.WriteLine($"  {sequence}");
        }

        Assert.IsNotEmpty(dot);
    }
}
