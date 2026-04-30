// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Accordant;
        using NUnit.Framework;
    using TodoListExtended.Api.Contracts;

    // ============================================================
    // State Definition
    // ============================================================

    /// <summary>
    /// State tracks users and their todos, including timestamps.
    /// </summary>
    [State]
    public partial class AppState
    {
        public Dictionary<string, UserState> Users { get; set; } = new();
    }

    [State]
    public partial class UserState
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        
        /// <summary>
        /// Todos keyed by server-generated TodoId.
        /// </summary>
        public Dictionary<string, TodoState> Todos { get; set; } = new();
    }

    [State]
    public partial class TodoState
    {
        public string Title { get; set; } = string.Empty;
        public bool Completed { get; set; } = false;
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
    }

    /// <summary>
    /// Accordant tests for the TodoList-Extended REST API.
    /// 
    /// This sample demonstrates TWO key advanced patterns:
    /// 
    /// 1. Response-Dependent State (Users):
    ///    - Server generates CreatedAt/ModifiedAt timestamps
    ///    - Use ThenState with lambda + mock to capture server values
    /// 
    /// 2. Server-Generated IDs + Request Derivation (Todos):
    ///    - Server generates TodoId (client doesn't know it ahead of time)
    ///    - Use ConfigureDerivations to generate GetTodo/CompleteTodo/DeleteTodo requests
    ///      from CreateTodo responses
    /// </summary>
    [TestFixture]
    public class TodoListExtendedTests
    {
        // ============================================================
        // Spec Creation
        // ============================================================

        private static Spec<AppState> CreateSpec()
        {
            var spec = new Spec<AppState>()
                .WithJsonPrinters();

            // ==========================================================
            // USER OPERATIONS (Response-Dependent State: timestamps)
            // ==========================================================

            // ---------------------------------------------------------
            // CREATE USER: PUT /api/users/{userId}
            // Server generates CreatedAt and ModifiedAt
            // ---------------------------------------------------------
            spec.Operation<User, ApiResult<User>>("CreateUser", (request, state) =>
            {
                if (state.Users.ContainsKey(request.UserId))
                {
                    return Expect.That<ApiResult<User>>(r => r.IsConflict,
                               $"Should return 409 Conflict because user '{request.UserId}' already exists")
                           .SameState();
                }

                // Response-dependent state: timestamps come from server
                return Expect.That<ApiResult<User>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.UserId == request.UserId &&
                                r.Data.Name == request.Name &&
                                r.Data.CreatedAt <= DateTime.UtcNow &&
                                r.Data.ModifiedAt == r.Data.CreatedAt,
                           $"Should return 200 OK with created user '{request.UserId}' and timestamps")
                       .ThenState<AppState>(
                           // State transition: uses actual response values during test execution
                           (ApiResult<User> response, AppState nextState) =>
                           {
                               nextState.Users[request.UserId] = new UserState
                               {
                                   Name = request.Name,
                                   CreatedAt = response.Data!.CreatedAt,
                                   ModifiedAt = response.Data!.ModifiedAt,
                                   Todos = new()
                               };
                           },
                           // Mock response: used during simulation (must satisfy the expectation above)
                           mock: () =>
                           {
                               var now = DateTime.UtcNow;
                               return new ApiResult<User>
                               {
                                   Data = new User(request.UserId, request.Name, now, now),
                                   StatusCode = 200
                               };
                           });
            });

            // ---------------------------------------------------------
            // GET USER: GET /api/users/{userId}
            // ---------------------------------------------------------
            spec.Operation<string, ApiResult<User>>("GetUser", (userId, state) =>
            {
                if (!state.Users.TryGetValue(userId, out var user))
                {
                    return Expect.That<ApiResult<User>>(r => r.IsNotFound,
                               $"Should return 404 Not Found because user '{userId}' doesn't exist")
                           .SameState();
                }

                return Expect.That<ApiResult<User>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.UserId == userId &&
                                r.Data.Name == user.Name &&
                                r.Data.CreatedAt == user.CreatedAt &&
                                r.Data.ModifiedAt == user.ModifiedAt,
                           $"Should return 200 OK with user '{userId}'")
                       .SameState();
            });

            // ---------------------------------------------------------
            // DELETE USER: DELETE /api/users/{userId}
            // ---------------------------------------------------------
            spec.Operation<string, int>("DeleteUser", (userId, state) =>
            {
                if (!state.Users.ContainsKey(userId))
                {
                    return Expect.That<int>(s => s == 404,
                               $"Should return 404 Not Found because user '{userId}' doesn't exist")
                           .SameState();
                }

                var newState = (AppState)state.Clone();
                newState.Users.Remove(userId);

                return Expect.That<int>(s => s == 204,
                           $"Should return 204 No Content after deleting user '{userId}'")
                       .ThenState<AppState>(nextState => nextState.Users.Remove(userId));
            });

            // ==========================================================
            // TODO OPERATIONS (Server-Generated IDs + Request Derivation)
            // ==========================================================

            // ---------------------------------------------------------
            // CREATE TODO: POST /api/users/{userId}/todos
            // Server generates TodoId and timestamps
            // ---------------------------------------------------------
            spec.Operation<Todo, ApiResult<Todo>>("CreateTodo", (request, state) =>
            {
                if (!state.Users.TryGetValue(request.UserId, out var user))
                {
                    return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                               $"Should return 404 Not Found because user '{request.UserId}' doesn't exist")
                           .SameState();
                }

                // Response-dependent state: TodoId and timestamps come from server
                return Expect.That<ApiResult<Todo>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.UserId == request.UserId &&
                                r.Data.Title == request.Title &&
                                r.Data.Completed == false &&
                                !string.IsNullOrEmpty(r.Data.TodoId) &&
                                r.Data.CreatedAt <= DateTime.UtcNow,
                           $"Should return 200 OK with created todo and server-generated TodoId")
                       .ThenState<AppState>(
                           (ApiResult<Todo> response, AppState nextState) =>
                           {
                               nextState.Users[request.UserId].Todos[response.Data!.TodoId] = new TodoState
                               {
                                   Title = request.Title,
                                   Completed = false,
                                   CreatedAt = response.Data!.CreatedAt,
                                   ModifiedAt = response.Data!.ModifiedAt
                               };
                           },
                           mock: () =>
                           {
                               var now = DateTime.UtcNow;
                               return new ApiResult<Todo>
                               {
                                   Data = new Todo(
                                       request.UserId,
                                       Guid.NewGuid().ToString(),
                                       request.Title,
                                       false,
                                       now,
                                       now),
                                   StatusCode = 200
                               };
                           });
            });

            // ---------------------------------------------------------
            // GET TODO: GET /api/users/{userId}/todos/{todoId}
            // ---------------------------------------------------------
            spec.Operation<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo", (request, state) =>
            {
                var todo = state.Users.GetValueOrDefault(request.UserId)
                                ?.Todos.GetValueOrDefault(request.TodoId);

                if (todo == null)
                {
                    return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                               $"Should return 404 Not Found")
                           .SameState();
                }

                return Expect.That<ApiResult<Todo>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.UserId == request.UserId &&
                                r.Data.TodoId == request.TodoId &&
                                r.Data.Title == todo.Title &&
                                r.Data.Completed == todo.Completed &&
                                r.Data.CreatedAt == todo.CreatedAt &&
                                r.Data.ModifiedAt == todo.ModifiedAt,
                           $"Should return 200 OK with todo")
                       .SameState();
            });

            // ---------------------------------------------------------
            // COMPLETE TODO: POST /api/users/{userId}/todos/{todoId}/complete
            // ---------------------------------------------------------
            spec.Operation<(string UserId, string TodoId), ApiResult<Todo>>("CompleteTodo", (request, state) =>
            {
                var todo = state.Users.GetValueOrDefault(request.UserId)
                                ?.Todos.GetValueOrDefault(request.TodoId);

                if (todo == null)
                {
                    return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                               $"Should return 404 Not Found")
                           .SameState();
                }

                // Response-dependent state: ModifiedAt is updated by server
                return Expect.That<ApiResult<Todo>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.Completed == true &&
                                r.Data.ModifiedAt >= todo.ModifiedAt,
                           $"Should return 200 OK with completed todo")
                       .ThenState<AppState>(
                           (ApiResult<Todo> response, AppState nextState) =>
                           {
                               var todoState = nextState.Users[request.UserId].Todos[request.TodoId];
                               todoState.Completed = true;
                               todoState.ModifiedAt = response.Data!.ModifiedAt;
                           },
                           mock: () => new ApiResult<Todo>
                           {
                               Data = new Todo(
                                   request.UserId,
                                   request.TodoId,
                                   todo.Title,
                                   true,
                                   todo.CreatedAt,
                                   DateTime.UtcNow),
                               StatusCode = 200
                           });
            });

            // ---------------------------------------------------------
            // DELETE TODO: DELETE /api/users/{userId}/todos/{todoId}
            // ---------------------------------------------------------
            spec.Operation<(string UserId, string TodoId), int>("DeleteTodo", (request, state) =>
            {
                var todos = state.Users.GetValueOrDefault(request.UserId)?.Todos;

                if (todos == null || !todos.ContainsKey(request.TodoId))
                {
                    return Expect.That<int>(s => s == 404,
                               $"Should return 404 Not Found")
                           .SameState();
                }

                var newState = (AppState)state.Clone();
                newState.Users[request.UserId].Todos.Remove(request.TodoId);

                return Expect.That<int>(s => s == 204,
                           $"Should return 204 No Content")
                       .ThenState<AppState>(nextState => nextState.Users[request.UserId].Todos.Remove(request.TodoId));
            });

            // ==========================================================
            // Configure Derivations for Todo Operations
            // These operations derive their (UserId, TodoId) from CreateTodo's response
            // ==========================================================
            spec.ConfigureDerivations("GetTodo",
                Derive.From<Todo, ApiResult<Todo>, (string UserId, string TodoId)>("CreateTodo")
                    .When((req, resp) => resp?.Data != null && resp.IsSuccess)
                    .As((req, resp) => (resp!.Data!.UserId, resp.Data.TodoId)));

            spec.ConfigureDerivations("CompleteTodo",
                Derive.From<Todo, ApiResult<Todo>, (string UserId, string TodoId)>("CreateTodo")
                    .When((req, resp) => resp?.Data != null && resp.IsSuccess)
                    .As((req, resp) => (resp!.Data!.UserId, resp.Data.TodoId)));

            spec.ConfigureDerivations("DeleteTodo",
                Derive.From<Todo, ApiResult<Todo>, (string UserId, string TodoId)>("CreateTodo")
                    .When((req, resp) => resp?.Data != null && resp.IsSuccess)
                    .As((req, resp) => (resp!.Data!.UserId, resp.Data.TodoId)));

            // ==========================================================
            // Bind to HTTP API
            // ==========================================================
            spec.ExecuteWith<TodoApiClient>()
                // User operations
                .BindAsync<User, ApiResult<User>>("CreateUser",
                    (client, req) => client.CreateUserAsync(req.UserId, req.Name))
                .BindAsync<string, ApiResult<User>>("GetUser",
                    (client, userId) => client.GetUserAsync(userId))
                .BindAsync<string, int>("DeleteUser",
                    (client, userId) => client.DeleteUserAsync(userId))
                // Todo operations
                .BindAsync<Todo, ApiResult<Todo>>("CreateTodo",
                    (client, req) => client.CreateTodoAsync(req.UserId, req.Title))
                .BindAsync<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo",
                    (client, req) => client.GetTodoAsync(req.UserId, req.TodoId))
                .BindAsync<(string UserId, string TodoId), ApiResult<Todo>>("CompleteTodo",
                    (client, req) => client.CompleteTodoAsync(req.UserId, req.TodoId))
                .BindAsync<(string UserId, string TodoId), int>("DeleteTodo",
                    (client, req) => client.DeleteTodoAsync(req.UserId, req.TodoId));

            return spec;
        }

        // ============================================================
        // Test 1: Users Only - Response-Dependent State (Timestamps)
        // ============================================================

        /// <summary>
        /// Tests user operations with server-generated timestamps.
        /// This is the first pattern to learn: response-dependent state.
        /// </summary>
        [Test]
        public async Task SequentialTests_UsersWithTimestamps()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");
            var deleteUser = spec.GetOperation<string, int>("DeleteUser");

            var inputs = new InputSet()
            {
                createUser.With(new User("alice", "Alice Smith"), "Create Alice"),
                createUser.With(new User("bob", "Bob Jones"), "Create Bob"),
                getUser.With("alice", "Get Alice"),
                getUser.With("unknown", "Get unknown user"),
                deleteUser.With("alice", "Delete Alice"),
            };

            var testCases = spec.GenerateTests(
                initialState,
                inputs,
                new TestGenerationOptions { MaxDepth = 4 });

            var context = spec.CreateTestingContext();
            context.Register(client);

            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions
                {
                    BeforeEachAsync = async ctx =>
                    {
                        var c = ctx.Context.Get<TodoApiClient>();
                        await c.DeleteUserAsync("alice");
                        await c.DeleteUserAsync("bob");
                    }
                });

            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
            TestContext.WriteLine($"Ran {results.Count} user tests with timestamps - all passed!");
        }

        // ============================================================
        // Test 2: Todos - Server-Generated IDs (Request Derivation)
        // ============================================================

        /// <summary>
        /// Tests todo operations with server-generated TodoId.
        /// This demonstrates DerivedFrom: GetTodo/CompleteTodo/DeleteTodo
        /// derive their TodoId from CreateTodo's response.
        /// </summary>
        [Test]
        public async Task SequentialTests_TodosWithServerGeneratedIds()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            // Note: GetTodo, CompleteTodo, DeleteTodo are NOT in InputSet directly
            // They will be derived from CreateTodo!

            var inputs = new InputSet()
            {
                // User must exist first
                createUser.With(new User("alice", "Alice"), "Create Alice"),
                
                // Create todos - the TodoId will be server-generated
                createTodo.With(new Todo("alice", "", "Buy groceries"), "Create todo 1"),
                createTodo.With(new Todo("alice", "", "Go running"), "Create todo 2"),
                
                // This will fail (user doesn't exist)
                createTodo.With(new Todo("unknown", "", "Task"), "Create todo for unknown user"),
            };

            var testCases = spec.GenerateTests(
                initialState,
                inputs,
                new TestGenerationOptions
                {
                    MaxDepth = 4,
                    // Enable derivations - GetTodo/CompleteTodo/DeleteTodo will be derived from CreateTodo
                    DerivationSelectors = new List<DerivationSelector>
                    {
                        DerivationSelector.For("GetTodo").From("CreateTodo"),
                        DerivationSelector.For("CompleteTodo").From("CreateTodo"),
                        DerivationSelector.For("DeleteTodo").From("CreateTodo"),
                    }
                });

            var context = spec.CreateTestingContext();
            context.Register(client);

            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions
                {
                    BeforeEachAsync = async info =>
                    {
                        var c = info.Context.Get<TodoApiClient>();
                        await c.DeleteUserAsync("alice");
                    }
                });

            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
            TestContext.WriteLine($"Ran {results.Count} todo tests with server-generated IDs - all passed!");
        }

        // ============================================================
        // Test 3: Full Integration - Users + Todos
        // ============================================================

        /// <summary>
        /// Full integration test combining both patterns:
        /// - Users with timestamps (response-dependent state)
        /// - Todos with server-generated IDs (request derivation)
        /// </summary>
        [Test]
        public async Task SequentialTests_FullIntegration()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");
            var deleteUser = spec.GetOperation<string, int>("DeleteUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");

            var inputs = new InputSet()
            {
                createUser.With(new User("alice", "Alice"), "Create Alice"),
                createUser.With(new User("bob", "Bob"), "Create Bob"),
                getUser.With("alice", "Get Alice"),
                createTodo.With(new Todo("alice", "", "Task A"), "Alice: Create todo"),
                createTodo.With(new Todo("bob", "", "Task B"), "Bob: Create todo"),
                deleteUser.With("alice", "Delete Alice (cascade)"),
            };

            var testCases = spec.GenerateTests(
                initialState,
                inputs,
                new TestGenerationOptions
                {
                    MaxDepth = 3,
                    DerivationSelectors = new List<DerivationSelector>
                    {
                        DerivationSelector.For("GetTodo").From("CreateTodo"),
                        DerivationSelector.For("CompleteTodo").From("CreateTodo"),
                    }
                });

            var context = spec.CreateTestingContext();
            context.Register(client);

            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions
                {
                    BeforeEachAsync = async info =>
                    {
                        var c = info.Context.Get<TodoApiClient>();
                        await c.DeleteUserAsync("alice");
                        await c.DeleteUserAsync("bob");
                    }
                });

            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
            TestContext.WriteLine($"Ran {results.Count} full integration tests - all passed!");
        }

        // ============================================================
        // Test 4: Manual Test - Shows the flow explicitly
        // ============================================================

        /// <summary>
        /// Manual test showing the complete flow:
        /// 1. Create user → get server timestamps
        /// 2. Create todo → get server-generated TodoId
        /// 3. Use that TodoId to get/complete the todo
        /// </summary>
        [Test]
        public async Task ManualTest_ServerGeneratedValues()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            var getTodo = spec.GetOperation<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo");
            var completeTodo = spec.GetOperation<(string UserId, string TodoId), ApiResult<Todo>>("CompleteTodo");
            var deleteUser = spec.GetOperation<string, int>("DeleteUser");

            // Create context and state profile for manual testing
            var context = spec.CreateTestingContext();
            context.Register(client);
            var stateProfile = new StateProfile(initialState);

            // Cleanup
            await deleteUser.ExecuteAsync(context, "alice");

            // Helper to validate
            async Task<TResp> Allows<TReq, TResp>(
                Operation<TReq, TResp, AppState> op, TReq request)
            {
                var response = await op.ExecuteAsync(context, request);
                var (isValid, message, nextProfile) = spec.Allows(op, request, response, stateProfile);
                Assert.IsTrue(isValid, message);
                stateProfile = nextProfile;
                return response;
            }

            // 1. Create user - server generates timestamps
            var userResult = await Allows(createUser, new User("alice", "Alice"));
            TestContext.WriteLine($"Created user with CreatedAt: {userResult.Data!.CreatedAt}");

            // 2. Create todo - server generates TodoId!
            var todoResult = await Allows(createTodo, new Todo("alice", "", "Buy milk"));
            var serverGeneratedTodoId = todoResult.Data!.TodoId;
            TestContext.WriteLine($"Created todo with server-generated ID: {serverGeneratedTodoId}");

            // 3. Use the server-generated TodoId to get the todo (tuple request)
            var getResult = await Allows(getTodo, ("alice", serverGeneratedTodoId));
            Assert.AreEqual("Buy milk", getResult.Data!.Title);

            // 4. Complete the todo using the server-generated ID (tuple request)
            var completeResult = await Allows(completeTodo, ("alice", serverGeneratedTodoId));
            Assert.IsTrue(completeResult.Data!.Completed);
            Assert.That(completeResult.Data!.ModifiedAt, Is.GreaterThanOrEqualTo(todoResult.Data!.CreatedAt));

            TestContext.WriteLine("Manual test passed - successfully used server-generated values!");
        }
    }
}
