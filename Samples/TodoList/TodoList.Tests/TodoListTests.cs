// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Accordant;
        using NUnit.Framework;
    using TodoList.Api.Contracts;

    // ============================================================
    // State Definition
    // ============================================================

    /// <summary>
    /// State tracks users and their todos.
    /// Compare this to the implementation: 2 Controllers + 2 Entities + EF DbContext + SQLite + WriteLock!
    /// </summary>
    [State]
    public partial class AppState
    {
        /// <summary>
        /// Dictionary of users. Key = userId, Value = user with their todos.
        /// </summary>
        public Dictionary<string, UserState> Users { get; set; } = new();
    }

    [State]
    public partial class UserState
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, TodoState> Todos { get; set; } = new();
    }

    [State]
    public partial class TodoState
    {
        public string Title { get; set; } = string.Empty;
        public bool Completed { get; set; } = false;
    }

    /// <summary>
    /// Accordant tests for the TodoList REST API.
    /// 
    /// This demonstrates:
    /// - Testing a real ASP.NET Core Web API with SQLite database
    /// - Two resources: Users and Todos (users must exist before creating todos)
    /// - Using WebApplicationFactory for in-process testing
    /// - The spec is MUCH simpler than the implementation
    /// </summary>
    [TestFixture]
    public class TodoListTests
    {
        // ============================================================
        // API Result - captures success data OR error
        // ============================================================
        // Note: ApiResult<T> is defined in TodoList.Api.Contracts

        // ============================================================
        // Spec Creation
        // ============================================================

        private static Spec<AppState> CreateSpec()
        {
            var spec = new Spec<AppState>()
                .WithJsonPrinters();  // Enable nice JSON formatting for request/response logs

            // ==========================================================
            // USER OPERATIONS
            // ==========================================================

            // ---------------------------------------------------------
            // CREATE USER: PUT /api/users/{userId}
            // ---------------------------------------------------------
            spec.Operation<User, ApiResult<User>>("CreateUser", (request, state) =>
            {
                if (state.Users.ContainsKey(request.UserId))
                {
                    return Expect.That<ApiResult<User>>(r => r.IsConflict,
                               $"Should return 409 Conflict because user '{request.UserId}' already exists")
                           .SameState();
                }

                return Expect.That<ApiResult<User>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.UserId == request.UserId &&
                                r.Data.Name == request.Name,
                           $"Should return 200 OK with created user '{request.UserId}'")
                       .ThenState<AppState>(nextState => nextState.Users[request.UserId] = new UserState
                       {
                           Name = request.Name,
                           Todos = new()
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
                                r.Data.Name == user.Name,
                           $"Should return 200 OK with user '{userId}'")
                       .SameState();
            });

            // ---------------------------------------------------------
            // DELETE USER: DELETE /api/users/{userId}
            // Cascades to delete all user's todos
            // ---------------------------------------------------------
            spec.Operation<string, int>("DeleteUser", (userId, state) =>
            {
                if (!state.Users.ContainsKey(userId))
                {
                    return Expect.That<int>(s => s == 404, $"Should return 404 Not Found because user '{userId}' doesn't exist").SameState();
                }

                var newState = (AppState)state.Clone();
                newState.Users.Remove(userId);

                return Expect.That<int>(s => s == 204, $"Should return 204 No Content after deleting user '{userId}' and their todos")
                       .ThenState<AppState>(nextState => nextState.Users.Remove(userId));
            });

            // ==========================================================
            // TODO OPERATIONS
            // ==========================================================

            // ---------------------------------------------------------
            // CREATE TODO: PUT /api/users/{userId}/todos/{todoId}
            // Requires: user must exist
            // ---------------------------------------------------------
            spec.Operation<Todo, ApiResult<Todo>>("CreateTodo", (request, state) =>
            {
                if (!state.Users.TryGetValue(request.UserId, out var user))
                {
                    return Expect.That<ApiResult<Todo>>(r => r.IsNotFound,
                               $"Should return 404 Not Found because user '{request.UserId}' doesn't exist")
                           .SameState();
                }

                if (user.Todos.ContainsKey(request.TodoId))
                {
                    return Expect.That<ApiResult<Todo>>(r => r.IsConflict,
                               $"Should return 409 Conflict because todo '{request.TodoId}' already exists")
                           .SameState();
                }

                return Expect.That<ApiResult<Todo>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.UserId == request.UserId &&
                                r.Data.TodoId == request.TodoId &&
                                r.Data.Title == request.Title &&
                                r.Data.Completed == false,
                           $"Should return 200 OK with created todo '{request.TodoId}'")
                       .ThenState<AppState>(nextState => nextState.Users[request.UserId].Todos[request.TodoId] = new TodoState
                       {
                           Title = request.Title,
                           Completed = false
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
                               $"Should return 404 Not Found because todo '{request.TodoId}' doesn't exist for user '{request.UserId}'")
                           .SameState();
                }

                return Expect.That<ApiResult<Todo>>(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.UserId == request.UserId &&
                                r.Data.TodoId == request.TodoId &&
                                r.Data.Title == todo.Title &&
                                r.Data.Completed == todo.Completed,
                           $"Should return 200 OK with todo '{request.TodoId}'")
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
                               $"Should return 404 Not Found because todo '{request.TodoId}' doesn't exist for user '{request.UserId}'")
                           .SameState();
                }

                return Expect.That<ApiResult<Todo>>(
                           r => r.IsSuccess && r.Data != null && r.Data.Completed == true,
                           $"Should return 200 OK with todo '{request.TodoId}' marked as completed")
                       .ThenState<AppState>(nextState => nextState.Users[request.UserId].Todos[request.TodoId] = new TodoState
                       {
                           Title = todo.Title,
                           Completed = true
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
                               $"Should return 404 Not Found because todo '{request.TodoId}' doesn't exist for user '{request.UserId}'").SameState();
                }

                return Expect.That<int>(s => s == 204, 
                           $"Should return 204 No Content after deleting todo '{request.TodoId}'")
                       .ThenState<AppState>(nextState => nextState.Users[request.UserId].Todos.Remove(request.TodoId));
            });

            // ==========================================================
            // Bind to HTTP API - client returns ApiResult<T> directly
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
                    (client, req) => client.CreateTodoAsync(req.UserId, req.TodoId, req.Title))
                .BindAsync<(string UserId, string TodoId), ApiResult<Todo>>("GetTodo", 
                    (client, req) => client.GetTodoAsync(req.UserId, req.TodoId))
                .BindAsync<(string UserId, string TodoId), ApiResult<Todo>>("CompleteTodo", 
                    (client, req) => client.CompleteTodoAsync(req.UserId, req.TodoId))
                .BindAsync<(string UserId, string TodoId), int>("DeleteTodo", 
                    (client, req) => client.DeleteTodoAsync(req.UserId, req.TodoId));

            return spec;
        }

        // ============================================================
        // Helper to extract user IDs for cleanup
        // ============================================================

        private static IEnumerable<string> GetUserIds(InputSet inputs)
        {
            var ids = new HashSet<string>();
            foreach (var input in inputs)
            {
                switch (input.Request)
                {
                    case User u: ids.Add(u.UserId); break;
                    case string s: ids.Add(s); break;
                    case Todo t: ids.Add(t.UserId); break;
                    case (string userId, string _): ids.Add(userId); break;
                }
            }
            return ids;
        }

        // ============================================================
        // Test 1: Users and Todos together
        // ============================================================

        [Test]
        public async Task SequentialTests_UsersAndTodos()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var getUser = spec.GetOperation<string, ApiResult<User>>("GetUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            var getTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("GetTodo");

            var inputs = new InputSet()
            {
                // User operations
                createUser.With(new User("alice", "Alice Smith"), "Create Alice"),
                getUser.With("alice", "Get Alice"),
                getUser.With("unknown", "Get unknown user"),
                
                // Todo operations (require user to exist)
                createTodo.With(new Todo("alice", "todo-1", "Buy milk"), "Create todo"),
                getTodo.With(("alice", "todo-1"), "Get todo"),
                createTodo.With(new Todo("unknown", "todo-1", "Task"), "Create todo for unknown user"),
            };

            var userIds = GetUserIds(inputs).ToList();

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
                    BeforeEachAsync = async info =>
                    {
                        var c = info.Context.Get<TodoApiClient>();
                        foreach (var id in userIds) await c.DeleteUserAsync(id);
                    }
                });

            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
            TestContext.WriteLine($"Ran {results.Count} test cases - all passed!");
        }

        // ============================================================
        // Test 2: Cascade delete
        // ============================================================

        [Test]
        public async Task SequentialTests_CascadeDelete()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var deleteUser = spec.GetOperation<string, int>("DeleteUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            var getTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("GetTodo");

            var inputs = new InputSet()
            {
                createUser.With(new User("alice", "Alice"), "Create Alice"),
                createTodo.With(new Todo("alice", "todo-1", "Task"), "Create todo"),
                deleteUser.With("alice", "Delete Alice (cascade)"),
                getTodo.With(("alice", "todo-1"), "Get todo (should be gone)"),
            };

            var userIds = GetUserIds(inputs).ToList();

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
                    BeforeEachAsync = async info =>
                    {
                        var c = info.Context.Get<TodoApiClient>();
                        foreach (var id in userIds) await c.DeleteUserAsync(id);
                    }
                });

            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
            TestContext.WriteLine($"Ran {results.Count} cascade delete test cases - all passed!");
        }

        // ============================================================
        // Test 3: Multiple users
        // ============================================================

        [Test]
        public async Task SequentialTests_MultipleUsers()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            var getTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("GetTodo");

            // Two users, each with their own todos (same todoId allowed for different users)
            var inputs = new InputSet()
            {
                createUser.With(new User("alice", "Alice"), "Create Alice"),
                createUser.With(new User("bob", "Bob"), "Create Bob"),
                createTodo.With(new Todo("alice", "todo-1", "Alice's task"), "Alice: Create todo-1"),
                createTodo.With(new Todo("bob", "todo-1", "Bob's task"), "Bob: Create todo-1"),
                getTodo.With(("alice", "todo-1"), "Get Alice's todo"),
                getTodo.With(("bob", "todo-1"), "Get Bob's todo"),
            };

            var userIds = GetUserIds(inputs).ToList();

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
                    BeforeEachAsync = async info =>
                    {
                        var c = info.Context.Get<TodoApiClient>();
                        foreach (var id in userIds) await c.DeleteUserAsync(id);
                    }
                });

            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
            TestContext.WriteLine($"Ran {results.Count} multi-user test cases - all passed!");
        }

        // ============================================================
        // Test 4: Concurrent tests
        // ============================================================

        [Test]
        public async Task ConcurrentTests()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            var completeTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("CompleteTodo");
            var deleteTodo = spec.GetOperation<(string, string), int>("DeleteTodo");

            var inputs = new InputSet()
            {
                createUser.With(new User("alice", "Alice"), "Create Alice"),
                createTodo.With(new Todo("alice", "todo-1", "Task"), "Create todo"),
                completeTodo.With(("alice", "todo-1"), "Complete todo"),
                deleteTodo.With(("alice", "todo-1"), "Delete todo"),
            };

            var userIds = GetUserIds(inputs).ToList();

            var testCases = spec.GenerateConcurrentTests(
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
                    BeforeEachAsync = async info =>
                    {
                        var c = info.Context.Get<TodoApiClient>();
                        foreach (var id in userIds) await c.DeleteUserAsync(id);
                    }
                });

            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
            TestContext.WriteLine($"Ran {results.Count} concurrent test cases - all passed!");
        }

        // ============================================================
        // Test 5: Visualize state space
        // ============================================================

        [Test]
        public void VisualizeStateSpace()
        {
            var spec = CreateSpec();
            var initialState = new AppState();

            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var deleteUser = spec.GetOperation<string, int>("DeleteUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            var getTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("GetTodo");

            var inputs = new InputSet()
            {
                createUser.With(new User("alice", "Alice"), "Create user"),
                createTodo.With(new Todo("alice", "todo-1", "Task"), "Create todo"),
                getTodo.With(("alice", "todo-1"), "Get todo"),
                deleteUser.With("alice", "Delete user"),
            };

            var dot = spec.VisualizeStateSpace(
                initialState,
                inputs,
                generationOptions: new TestGenerationOptions { MaxDepth = 4 });

            TestContext.WriteLine("State space visualization (DOT format):");
            TestContext.WriteLine("Copy to https://dreampuf.github.io/GraphvizOnline/");
            TestContext.WriteLine();
            TestContext.WriteLine(dot);

            Assert.IsNotEmpty(dot);
        }

        // ============================================================
        // Test 6: Manual test - A day in Alice's life (using Allows)
        // ============================================================

        /// <summary>
        /// Demonstrates using Allows() to manually write a test scenario.
        /// Uses operation.ExecuteAsync to call the API through the spec bindings.
        /// </summary>
        [Test]
        public async Task ManualTest_ADayInAlicesLife()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            // Get operations
            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var createTodo = spec.GetOperation<Todo, ApiResult<Todo>>("CreateTodo");
            var getTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("GetTodo");
            var completeTodo = spec.GetOperation<(string, string), ApiResult<Todo>>("CompleteTodo");
            var deleteUser = spec.GetOperation<string, int>("DeleteUser");

            // Create context and state profile for manual testing
            var context = spec.CreateTestingContext();
            context.Register(client);
            var stateProfile = new StateProfile(initialState);

            // Cleanup in case Alice exists from a previous run
            await deleteUser.ExecuteAsync(context, "alice");

            // Helper: execute operation and validate response against spec
            async Task<TResp> Allows<TReq, TResp>(
                Operation<TReq, TResp, AppState> op, TReq request)
            {
                var response = await op.ExecuteAsync(context, request);
                var (isValid, message, nextProfile) = spec.Allows(op, request, response, stateProfile);
                Assert.IsTrue(isValid, message);
                stateProfile = nextProfile;
                return response;
            }

            // Alice signs up
            await Allows(createUser, new User("alice", "Alice"));

            // Alice creates her first todo
            await Allows(createTodo, new Todo("alice", "groceries", "Buy groceries"));

            // Alice creates another todo
            await Allows(createTodo, new Todo("alice", "exercise", "Go for a run"));

            // Alice checks her groceries todo
            await Allows(getTodo, ("alice", "groceries"));

            // Alice completes the groceries
            await Allows(completeTodo, ("alice", "groceries"));

            // Verify the todo is now completed
            await Allows(getTodo, ("alice", "groceries"));
        }

        // ============================================================
        // Test 7: Manual test - Concurrent race condition
        // ============================================================

        /// <summary>
        /// Demonstrates using AllowsConcurrent() to validate concurrent operations.
        /// Two users try to create the same username at the same time.
        /// Uses operation.ExecuteAsync to call the API through the spec bindings.
        /// </summary>
        [Test]
        public async Task ManualTest_ConcurrentUserCreation()
        {
            using var factory = new TodoServiceFactory();
            var spec = CreateSpec();
            var initialState = new AppState();
            var client = new TodoApiClient(factory.CreateTestClient());

            // Get operations
            var createUser = spec.GetOperation<User, ApiResult<User>>("CreateUser");
            var deleteUser = spec.GetOperation<string, int>("DeleteUser");

            // Create context and state profile for manual testing
            var context = spec.CreateTestingContext();
            context.Register(client);
            var stateProfile = new StateProfile(initialState);

            // Cleanup in case bob exists from a previous run
            await deleteUser.ExecuteAsync(context, "bob");

            // Fire both requests concurrently - two users try to claim "bob" at the same time
            // Use Task.Run to ensure true concurrency (avoid synchronous portions blocking)
            var task1 = Task.Run(() => createUser.ExecuteAsync(context, new User("bob", "Bob One")));
            var task2 = Task.Run(() => createUser.ExecuteAsync(context, new User("bob", "Bob Two")));
            await Task.WhenAll(task1, task2);

            var bobOneResponse = await task1;
            var bobTwoResponse = await task2;

            // Validate with AllowsConcurrent - the spec should accept these responses
            // because they can be explained by some logical ordering
            var (isValid, message, _) = spec.AllowsConcurrent(
                stateProfile,
                new List<(IOperation, object, object)>
                {
                    (createUser, new User("bob", "Bob One"), bobOneResponse),
                    (createUser, new User("bob", "Bob Two"), bobTwoResponse),
                });

            Assert.IsTrue(isValid, $"Responses should be valid. {message}");

            // Exactly one should win (200), one should lose (409)
            var codes = new[] { bobOneResponse.StatusCode, bobTwoResponse.StatusCode }.OrderBy(x => x).ToArray();
            Assert.AreEqual(new[] { 200, 409 }, codes, "One should succeed, one should conflict");
        }
    }
}