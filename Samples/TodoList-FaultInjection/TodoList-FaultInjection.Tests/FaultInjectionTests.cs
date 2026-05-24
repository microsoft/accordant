// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;
using TodoList.FaultInjection.Api.Contracts;
using TodoList.FaultInjection.Api.FaultInjection;

/// <summary>
/// Tests demonstrating indefinite failure handling with Accordant.
/// 
/// Key concepts:
/// - Base class automatically adds indefinite failure outcomes
/// - Operations only define happy-path logic in ApplyInternal()
/// - MaybeExists lists track uncertain state from indefinite failures
/// - Subsequent operations (GetUser, GetTodo) disambiguate by observing actual state
/// - IndefiniteFailureSemantics.Suppress() can disable for baseline tests
/// </summary>
[TestFixture]
public class FaultInjectionTests
{
    // ============================================================
    // Spec Creation - Now using class-based operations
    // ============================================================

    private static Spec<AppState> CreateSpec()
    {
        var spec = new Spec<AppState>()
            .WithJsonPrinters();

        // Register operations - base class handles indefinite failure wrapping
        spec.Add(new CreateUserOperation());
        spec.Add(new GetUserOperation());
        spec.Add(new DeleteUserOperation());
        spec.Add(new CreateTodoOperation());
        spec.Add(new GetTodoOperation());

        // ==========================================================
        // Execution Bindings
        // ==========================================================
        spec.ExecuteWith<FaultInjectingApiClient>()
            .BindAsync<User, ApiResult<User>>("CreateUser",
                (client, req) => client.CreateUserAsync(req.UserId, req.Name))
            .BindAsync<string, ApiResult<User>>("GetUser",
                (client, userId) => client.GetUserAsync(userId))
            .BindAsync<string, ApiResult<int>>("DeleteUser",
                (client, userId) => client.DeleteUserAsync(userId))
            .BindAsync<Todo, ApiResult<Todo>>("CreateTodo",
                (client, req) => client.CreateTodoAsync(req.UserId, req.TodoId, req.Title))
            .BindAsync<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo",
                (client, req) => client.GetTodoAsync(req.UserId, req.TodoId));

        return spec;
    }

    // ============================================================
    // Test: No faults - baseline behavior
    // ============================================================

    [Test]
    public async Task NoFaults_BaselineBehavior()
    {
        var serverConfig = new ServerFaultConfig { Enabled = false };
        var clientConfig = new ClientFaultConfig { Enabled = false };

        using var factory = new FaultInjectingServiceFactory(serverConfig);
        var httpClient = factory.CreateTestClient();
        var client = new FaultInjectingApiClient(httpClient, clientConfig);

        var spec = CreateSpec();
        var initialState = new AppState();

        var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
        var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");
        var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");

        var inputs = new InputSet
        {
            createUser.With(new User("alice", "Alice Smith"), "Create Alice"),
            createUser.With(new User("bob", "Bob Jones"), "Create Bob"),
            getUser.With("alice", "Get Alice"),
            getUser.With("unknown", "Get unknown user"),
            createTodo.With(new Todo("alice", "todo-1", "Buy milk"), "Create todo"),
        };

        // Suppress indefinite failure semantics entirely - no faults expected
        IndefiniteFailureSemantics.Enabled = false;

        var testCases = spec.GenerateTests(
            initialState,
            inputs,
            new TestGenerationOptions { MaxDepth = 3 });

        var context = spec.CreateTestingContext();
        context.Register(client);

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async _ =>
                {
                    await client.CleanupDeleteUserAsync("alice");
                    await client.CleanupDeleteUserAsync("bob");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();
        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
        TestContext.WriteLine($"Ran {results.Count} test cases - all passed!");
    }

    // ============================================================
    // Test: With server-side faults (DB layer - 500 errors)
    // ============================================================

    [Test]
    public async Task ServerFaults_DBLayerIndefiniteFailures()
    {
        var serverConfig = new ServerFaultConfig
        {
            Enabled = true,
            PreSaveFaultProbability = 0.05,   // 5% chance before save
            PostSaveFaultProbability = 0.05,  // 5% chance after save (true indefinite!)
            ReadFaultProbability = 0.02,
            Seed = 42
        };
        var clientConfig = new ClientFaultConfig { Enabled = false };

        using var factory = new FaultInjectingServiceFactory(serverConfig);
        var httpClient = factory.CreateTestClient();
        var client = new FaultInjectingApiClient(httpClient, clientConfig);

        var spec = CreateSpec();
        var initialState = new AppState();

        var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
        var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");

        var inputs = new InputSet
        {
            createUser.With(new User("alice", "Alice"), "Create Alice"),
            createUser.With(new User("bob", "Bob"), "Create Bob"),
            getUser.With("alice", "Get Alice"),
            getUser.With("bob", "Get Bob"),
        };

        // Suppress indefinite failures during exploration (deterministic state space)
        // Enable during execution (to handle faults)
        var testCases = IndefiniteFailureSemantics.Suppress(() =>
            spec.GenerateTests(
                initialState,
                inputs,
                new TestGenerationOptions { MaxDepth = 4 }));

        var context = spec.CreateTestingContext();
        context.Register(client);

        // Enable indefinite failure semantics during execution
        IndefiniteFailureSemantics.Enabled = true;

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async _ =>
                {
                    await client.CleanupDeleteUserAsync("alice");
                    await client.CleanupDeleteUserAsync("bob");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();

        TestContext.WriteLine($"Ran {results.Count} test cases");
        TestContext.WriteLine($"  Passed: {results.Count - failures.Count}");
        TestContext.WriteLine($"  Failed: {failures.Count}");

        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    }

    // ============================================================
    // Test: With client-side faults (network layer)
    // ============================================================

    [Test]
    public async Task ClientFaults_NetworkLayerIndefiniteFailures()
    {
        var serverConfig = new ServerFaultConfig { Enabled = false };
        var clientConfig = new ClientFaultConfig
        {
            Enabled = true,
            PreRequestFaultProbability = 0.05,
            PostResponseFaultProbability = 0.05,
            Seed = 123
        };

        using var factory = new FaultInjectingServiceFactory(serverConfig);
        var httpClient = factory.CreateTestClient();
        var client = new FaultInjectingApiClient(httpClient, clientConfig);

        var spec = CreateSpec();
        var initialState = new AppState();

        var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
        var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");

        var inputs = new InputSet
        {
            createUser.With(new User("alice", "Alice"), "Create Alice"),
            getUser.With("alice", "Get Alice"),
            getUser.With("unknown", "Get unknown"),
        };

        // Suppress during exploration, enable during execution
        var testCases = IndefiniteFailureSemantics.Suppress(() =>
            spec.GenerateTests(
                initialState,
                inputs,
                new TestGenerationOptions { MaxDepth = 4 }));

        var context = spec.CreateTestingContext();
        context.Register(client);

        IndefiniteFailureSemantics.Enabled = true;

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async _ =>
                {
                    await client.CleanupDeleteUserAsync("alice");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();

        TestContext.WriteLine($"Ran {results.Count} test cases");
        TestContext.WriteLine($"  Passed: {results.Count - failures.Count}");
        TestContext.WriteLine($"  Failed: {failures.Count}");

        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    }

    // ============================================================
    // Test: Both server and client faults
    // ============================================================

    [Test]
    public async Task BothFaults_FullChaosMode()
    {
        var serverConfig = new ServerFaultConfig
        {
            Enabled = true,
            PreSaveFaultProbability = 0.03,
            PostSaveFaultProbability = 0.03,
            ReadFaultProbability = 0.02,
            Seed = 42
        };
        var clientConfig = new ClientFaultConfig
        {
            Enabled = true,
            PreRequestFaultProbability = 0.03,
            PostResponseFaultProbability = 0.03,
            Seed = 123
        };

        using var factory = new FaultInjectingServiceFactory(serverConfig);
        var httpClient = factory.CreateTestClient();
        var client = new FaultInjectingApiClient(httpClient, clientConfig);

        var spec = CreateSpec();
        var initialState = new AppState();

        var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
        var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");

        var inputs = new InputSet
        {
            createUser.With(new User("alice", "Alice"), "Create Alice"),
            createUser.With(new User("bob", "Bob"), "Create Bob"),
            getUser.With("alice", "Get Alice"),
            getUser.With("bob", "Get Bob"),
        };

        // Suppress during exploration, enable during execution
        var testCases = IndefiniteFailureSemantics.Suppress(() =>
            spec.GenerateTests(
                initialState,
                inputs,
                new TestGenerationOptions { MaxDepth = 4 }));

        var context = spec.CreateTestingContext();
        context.Register(client);

        IndefiniteFailureSemantics.Enabled = true;

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async _ =>
                {
                    await client.CleanupDeleteUserAsync("alice");
                    await client.CleanupDeleteUserAsync("bob");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();

        TestContext.WriteLine($"Ran {results.Count} test cases under chaos conditions");
        TestContext.WriteLine($"  Passed: {results.Count - failures.Count}");
        TestContext.WriteLine($"  Failed: {failures.Count}");

        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    }
}
