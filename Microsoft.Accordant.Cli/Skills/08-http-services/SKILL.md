---
name: Accordant HTTP Services
description: How to test HTTP/REST APIs with Accordant - request/response patterns, ApiResult, HttpExecutable, and API client patterns
---

# Testing HTTP Services

Accordant provides two main approaches for testing HTTP services:
1. **Framework types** (`HttpRequest`, `HttpResponse`, `HttpExecutable`) - built-in HTTP abstractions
2. **ApiResult pattern** - lightweight wrapper for API client responses

## Choosing Your Approach

| Approach | When to use |
|----------|-------------|
| `HttpRequest`/`HttpResponse` + `HttpExecutable` | Full framework integration, automatic serialization |
| `ApiResult<T>` + custom API client | Simpler setup, easier to understand, more control over HTTP calls (recommended) |

## The ApiResult Pattern

A lightweight wrapper that captures both success data and error information:

```csharp
/// <summary>
/// Generic API result wrapper that captures success data or error information.
/// </summary>
public class ApiResult<T>
{
    public T? Data { get; set; }
    public int StatusCode { get; set; }
    public string? Error { get; set; }

    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotFound => StatusCode == 404;
    public bool IsConflict => StatusCode == 409;
    public bool IsBadRequest => StatusCode == 400;
}
```

### Why ApiResult?

- **Single return type** - always returns `ApiResult<T>`, never throws
- **Easy validation** - `r.IsSuccess`, `r.IsNotFound` etc. in `Apply`
- **Error access** - `r.Error` contains error message when failed
- **Idiomatic** - matches common API patterns

### Building an API Client with ApiResult

```csharp
public class MyApiClient
{
    private readonly HttpClient _client;

    public MyApiClient(HttpClient client) => _client = client;

    public async Task<ApiResult<User>> CreateUserAsync(string userId, CreateUserRequest request)
    {
        var response = await _client.PostAsJsonAsync($"/api/users/{userId}", request);
        return await ToApiResult<User>(response);
    }

    public async Task<ApiResult<User>> GetUserAsync(string userId)
    {
        var response = await _client.GetAsync($"/api/users/{userId}");
        return await ToApiResult<User>(response);
    }

    public async Task<int> DeleteUserAsync(string userId)
    {
        var response = await _client.DeleteAsync($"/api/users/{userId}");
        return (int)response.StatusCode;
    }

    private static async Task<ApiResult<T>> ToApiResult<T>(HttpResponseMessage response)
    {
        var result = new ApiResult<T> { StatusCode = (int)response.StatusCode };

        if (response.IsSuccessStatusCode)
            result.Data = await response.Content.ReadFromJsonAsync<T>();
        else
            result.Error = await response.Content.ReadAsStringAsync();

        return result;
    }
}
```

### Using ApiResult in Operations

```csharp
using FluentAssertions;

public class CreateUserOperation : Operation<CreateUserRequest, ApiResult<User>, AppState>
{
    public CreateUserOperation() : base("CreateUser") { }

    public override ExpectedOutcomes Apply(CreateUserRequest request, AppState state)
    {
        if (state.Users.ContainsKey(request.UserId))
        {
            // Expect 409 Conflict when user already exists
            return Expect.That(r => r.IsConflict,
                       $"Should return 409 because user '{request.UserId}' exists")
                   .SameState();
        }

        // Success case - use FluentAssertions with ValidationResult for detailed diff
        return Expect.That<ApiResult<User>>(response =>
               {
                   if (!response.IsSuccess || response.Data == null)
                       return ValidationResult.Invalid($"Expected success, got {response.StatusCode}");

                   var expected = new User
                   {
                       Id = request.UserId,
                       Name = request.Name,
                       Email = request.Email
                   };

                   try
                   {
                       response.Data.Should().BeEquivalentTo(expected, options => options
                           .Excluding(x => x.CreatedAt)   // Exclude server-controlled fields
                           .Excluding(x => x.UpdatedAt));
                       return ValidationResult.Valid();
                   }
                   catch (Exception ex)
                   {
                       return ValidationResult.Invalid(ex.Message);
                   }
               })
               .ThenState(next => next.Users[request.UserId] = new UserState
               {
                   Name = request.Name
               });
    }

    public override async Task<ApiResult<User>> ExecuteAsync(
        TestingContext context, CreateUserRequest request)
    {
        var client = context.Get<MyApiClient>();
        return await client.CreateUserAsync(request.UserId, request);
    }
}
```

## Framework HTTP Types

Accordant provides built-in types in `Accordant.Http` namespace.

### HttpRequest Classes

Create request classes by inheriting from `HttpRequest` or `HttpRequest<TPayload>`:

```csharp
using Microsoft.Accordant;

// GET request (no payload)
public class GetAccountRequest : HttpRequest
{
    public override string Verb => HttpVerb.Get;
    public override string Url => $"/accounts/{AccountName}";
    
    public string AccountName { get; set; }
}

// PUT request with payload
public class PutAccountRequest : HttpRequest<Account>
{
    public override string Verb => HttpVerb.Put;
    public override string Url => $"/accounts";
    
    public string AccountName { get; set; }
    // Payload property comes from base class
}

// POST request with payload
public class CreateTodoRequest : HttpRequest<TodoData>
{
    public override string Verb => HttpVerb.Post;
    public override string Url => $"/users/{UserId}/todos";
    
    public string UserId { get; set; }
}

// DELETE request
public class DeleteAccountRequest : HttpRequest
{
    public override string Verb => HttpVerb.Delete;
    public override string Url => $"/accounts/{AccountName}";
    
    public string AccountName { get; set; }
}
```

### HttpResponse Classes

Responses are `HttpResponse` or `HttpResponse<TPayload>`:

```csharp
// Response with just status code
HttpResponse response = new HttpResponse(HttpStatusCode.OK);

// Response with payload
HttpResponse<Account> response = new HttpResponse<Account>(200)
{
    Payload = new Account { Name = "test" }
};
```

### HttpExecutable

Use `HttpExecutable` to send requests and parse responses:

```csharp
public override async Task<HttpResponse> ExecuteAsync(
    TestingContext context, GetAccountRequest request)
{
    // Returns HttpResponse with status code
    return await HttpExecutable.Default.ExecuteAsync(
        context,
        request,
        shouldParsePayload: statusCode => false);
}

public override async Task<HttpResponse<Account>> ExecuteAsync(
    TestingContext context, GetAccountRequest request)
{
    // Returns HttpResponse<Account> with parsed payload on success
    return await HttpExecutable.Default.ExecuteAsync<Account>(
        context,
        request,
        shouldParsePayload: statusCode => statusCode == 200);
}
```

### Using HttpResponse with Predicates

Validate responses using simple predicates (FluentAssertions style):

```csharp
public override ExpectedOutcomes Apply(GetAccountRequest request, AppState state)
{
    if (!state.Accounts.ContainsKey(request.AccountName))
    {
        return Expect.That(r => r.StatusCode == 404, "Should return 404 Not Found")
               .SameState();
    }

    var account = state.Accounts[request.AccountName];
    return Expect.That(
               r => r.StatusCode == 200 &&
                    r.Payload != null &&
                    r.Payload.Name == account.Name,
               $"Should return account '{account.Name}'")
           .SameState();
}
```

## Request Object Construction

Request objects should be straightforward for AI/humans to construct:

### Simple Request (just IDs)
```csharp
public class GetUserRequest
{
    public string UserId { get; set; }
}

// Usage in InputSet:
spec.GetUser.With(new GetUserRequest { UserId = "alice" }, "Get Alice")
```

### Request with Payload
```csharp
public class CreateUserRequest
{
    public string UserId { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

// Usage in InputSet:
spec.CreateUser.With(new CreateUserRequest 
{ 
    UserId = "alice", 
    Name = "Alice", 
    Email = "alice@example.com" 
}, "Create Alice")
```

### Tuple Requests (for simple cases)
For operations with few parameters, use tuples:

```csharp
public class GetTodoOperation : Operation<(string UserId, string TodoId), ApiResult<Todo>, AppState>
{
    public override ExpectedOutcomes Apply((string UserId, string TodoId) request, AppState state)
    {
        var (userId, todoId) = request;
        // ...
    }
}

// Usage in InputSet:
spec.GetTodo.With(("alice", "todo-1"), "Get Alice's todo")
```

## Response Validation Patterns

### Simple Predicate
```csharp
return Expect.That(r => r.IsSuccess, "should return 200 OK")
       .ThenState(next => /* update state */);
```

### Using FluentAssertions with ValidationResult

For complex response validation, use FluentAssertions with `ValidationResult`:

```csharp
using FluentAssertions;

return Expect.That<ApiResult<User>>(response =>
{
    if (!response.IsSuccess || response.Data == null)
        return ValidationResult.Invalid($"Expected success, got {response.StatusCode}");

    var expected = new User
    {
        Id = request.UserId,
        Name = request.Name,
        Email = request.Email
    };

    try
    {
        response.Data.Should().BeEquivalentTo(expected, options => options
            .Excluding(x => x.CreatedAt)   // Server-controlled field
            .Excluding(x => x.UpdatedAt)); // Server-controlled field

        return ValidationResult.Valid();
    }
    catch (Exception ex)
    {
        return ValidationResult.Invalid(ex.Message);
    }
})
.ThenState(next => next.Users[request.UserId] = new UserState { Name = request.Name });
```

When validation fails, FluentAssertions gives detailed output:
```
Expected member Name to be "Alice", but "Bob" differs near "Bob".
Expected member Email to be "alice@example.com", but "bob@example.com" differs near "bob".
```

### Multiple Conditions (Simple)
```csharp
return Expect.That(
           r => r.IsSuccess &&
                r.Data != null &&
                r.Data.Id == request.Id,
           $"Should return user {request.Id}")
       .ThenState(/* ... */);
```

### Error Responses
```csharp
if (state.Users.ContainsKey(request.UserId))
{
    return Expect.That(r => r.IsConflict, "Should return 409 Conflict")
           .SameState();
}

if (!IsValidEmail(request.Email))
{
    return Expect.That(r => r.IsBadRequest, "Should return 400 Bad Request")
           .SameState();
}
```

## Test Setup for HTTP Services

```csharp
[Test]
public async Task HttpServiceTests()
{
    // 1. Create test server (ASP.NET Core TestServer pattern)
    using var factory = new WebApplicationFactory<Program>();
    var httpClient = factory.CreateClient();

    // 2. Create spec and context
    var spec = new MyApiSpec();
    var context = spec.CreateTestingContext();
    
    // 3a. Register ApiResult-style client
    context.Register(new MyApiClient(httpClient));
    
    // 3b. OR register HttpClient directly for HttpExecutable
    context.Register(httpClient);

    // 4. Generate and run tests
    var inputs = new InputSet { /* ... */ };
    var testCases = spec.GenerateTests(new AppState(), inputs);
    
    var results = await spec.RunTests(
        context,
        new AppState(),
        testCases,
        new TestExecutionOptions
        {
            BeforeEachAsync = async info =>
            {
                // Reset database/state before each test
                await ResetDatabaseAsync();
                // Re-register fresh client if needed
                info.Context.Register(new MyApiClient(httpClient));
            }
        });

    Assert.IsTrue(results.All(r => r.Success));
}
```

## Best Practices

1. **Use ApiResult for most cases** - simpler, clearer, sufficient for most APIs
2. **Use FluentAssertions + ValidationResult** - `response.Data.Should().BeEquivalentTo(expected)` gives detailed diffs on failure
3. **Keep request classes simple** - use clear property names matching API parameters
4. **Include all status code checks** in `Apply` - 200, 400, 404, 409, etc.
5. **Use IsSuccess/IsNotFound helpers** instead of raw status code comparisons
6. **Exclude server-controlled fields** - use `.Excluding(x => x.CreatedAt)` for timestamps, IDs generated by server
7. **Reset state in BeforeEach** - HTTP tests need clean state each time
