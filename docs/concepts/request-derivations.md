# Request Derivations

> **TL;DR**: Request derivations define how one operation's request can be automatically constructed from another operation's request and response. They enable generated test sequences by teaching the framework how to extract response data that subsequent requests need.

---

## The Problem: Operations That Need Response Data

Servers often generate data in responses that later requests need. A CreateTodo call returns a server-generated `TodoId`. A PUT request returns an `ETag` that subsequent PUT or DELETE calls need for optimistic concurrency. Authentication endpoints return tokens that subsequent requests must include.

When you write manual tests, this is straightforward. You call the first operation, save what you need from the response, and pass it to the next:

```csharp
var createResponse = await api.CreateTodo(new Todo { Title = "Buy milk" });
var todoId = createResponse.Data.TodoId;  // Server-generated ID

var getResponse = await api.GetTodo(todoId);  // Use the ID from response
```

But what about generated tests? When the framework executes a sequence of operations — CreateTodo, then GetTodo, then DeleteTodo — how does it know that GetTodo's request needs data from CreateTodo's response?

This is what derivations solve. They teach the framework how to extract response data for use in subsequent requests.

See the [TodoList-Extended sample](../../Samples/TodoList-Extended) for a complete working example of this pattern.

---

## A Concrete Example

Consider a TODO API where the server generates IDs:

```
POST /api/todos  { title: "Buy milk" }
→ 201 Created { todoId: "xyz-123", title: "Buy milk" }

GET /api/todos/xyz-123
→ 200 OK { todoId: "xyz-123", title: "Buy milk", completed: false }
```

GetTodo needs the `todoId` that CreateTodo returns. Here's how you express that relationship:

```csharp
// CreateTodo takes a CreateTodoRequest and returns a CreateTodoResponse
// GetTodo takes a string (the todoId)

spec.ConfigureDerivations("GetTodo",
    Derive.From<CreateTodoRequest, CreateTodoResponse, string>("CreateTodo")
        .When((createReq, createResp) => createResp.IsSuccess)
        .As((createReq, createResp) => createResp.TodoId));
```

Reading this: "GetTodo's request (a `string` todoId) can be derived from CreateTodo. The derivation takes CreateTodo's request (`CreateTodoRequest`) and response (`CreateTodoResponse`), and when the response indicates success, extracts the `TodoId` to use as GetTodo's request."

Now when the framework executes CreateTodo followed by GetTodo, it knows how to construct GetTodo's request from CreateTodo's response.

---

## How the Framework Uses Derivations

When the framework executes a sequence of operations, it tracks every request/response pair. When it needs to call an operation that has derivations configured, it looks for matching source data.

Here's what happens step by step:

1. Framework executes `CreateTodo` with some request
2. Framework stores the request/response pair: `{ "CreateTodo": (request, response) }`
3. Framework needs to execute `GetTodo` next
4. Framework checks: "Does GetTodo have derivations? Yes — it derives from CreateTodo"
5. Framework looks up the stored CreateTodo response
6. Framework runs the `.When()` filter — does it pass?
7. If yes, framework calls `.As()` to construct GetTodo's request
8. Framework executes GetTodo with the derived request

This happens automatically. You define derivations once, and the framework uses them during execution.

---

## When You Don't Need Derivations

An important subtlety: not all operation dependencies require derivations.

Consider a simple key-value store with CreateItem, GetItem, and DeleteItem. Each operation takes a `key` that the *client* chooses:

```csharp
// Client picks the key
await api.CreateItem("my-key", "my-value");
await api.GetItem("my-key");
await api.DeleteItem("my-key");
```

These operations are clearly related — you can't get an item that doesn't exist, and deleting only makes sense after creating. But notice: the framework can construct valid requests for all three operations *without* needing any response data. The key `"my-key"` is known from the start.

The framework's test generator explores all operation orderings automatically and we do not need request derivations as we don't need to look at a response of an operation to construct the request for a subsequent operation.

**The key distinction: can valid requests be constructed without response data?** In this key-value store, yes — the client controls the key. No derivation needed.

But if CreateItem returned a *server-generated* key:

```csharp
var response = await api.CreateItem("my-value");  // No key provided
var key = response.GeneratedKey;  // Server created this
await api.GetItem(key);  // Need the generated key
```

Now the framework *cannot* construct a valid GetItem request without first seeing CreateItem's response. That's when you need a derivation.

---

## How to Specify Derivations

There are two ways to configure derivations, depending on how your operations are defined.

**For inline operations** — those defined with `spec.Operation<TReq, TResp>(...)` — use `ConfigureDerivations` after defining the operation:

```csharp
spec.Operation<string, GetItemResponse>("GetItem", (key, state) => { ... });

spec.ConfigureDerivations("GetItem",
    Derive.From<CreateItemRequest, CreateItemResponse, string>("CreateItem")
        .When((req, resp) => resp.IsSuccess)
        .As((req, resp) => resp.GeneratedKey));
```

**For class-based operations** — those extending `Operation<TReq, TResp, TState>` — override the `DerivedFrom` property:

```csharp
public class GetItemOperation : Operation<string, GetItemResponse, StoreState>
{
    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        Derive.From<CreateItemRequest, CreateItemResponse, string>("CreateItem")
              .As((req, resp) => resp.GeneratedKey)
    };

    // ...
}
```

The derivation logic is identical. Only the configuration syntax differs.

---

## The Derive.From API

`Derive.From<TSourceReq, TSourceResp, TDerivedReq>("SourceOperation")` starts a derivation builder. The type parameters are:

- `TSourceReq`: the source operation's request type (what was sent to CreateItem)
- `TSourceResp`: the source operation's response type (what CreateItem returned)
- `TDerivedReq`: the type of request you're deriving (what GetItem needs)

The string parameter is the source operation's name — the operation whose response you're deriving from.

From there, you chain `.When()` to filter and `.As()` or `.AsVariants()` to produce the derived request.

---

## Filtering with .When()

Not every source response produces a valid derived request. If CreateItem failed, there's no `GeneratedKey` to extract. The `.When()` filter handles this:

```csharp
.When((req, resp) => resp.IsSuccess && resp.GeneratedKey != null)
```

When the predicate returns `false`, the derivation is skipped entirely. No request is produced. This prevents the framework from trying to call GetItem with garbage data after a failed CreateItem.

You can omit `.When()` if every response is usable, but most real APIs have failure modes that make filtering essential.

---

## Producing the Request with .As()

`.As()` provides the factory function that constructs the derived request:

```csharp
.As((req, resp) => resp.GeneratedKey)
```

The factory receives:
- `req`: the source operation's request (what was sent to CreateItem)
- `resp`: the source operation's response (what CreateItem returned)

It returns the derived request — in this case, the key string that GetItem expects.

The factory runs only when the `.When()` filter passes (or if no filter was specified).

---

## Templates

Sometimes a derivation needs data that comes from neither the source request nor the source response — external test data you want to inject.

Consider a TODO API where each todo has a category. After creating a todo, you want to test updating it:

```csharp
// CreateTodo response has the todoId
// UpdateTodo request needs: todoId (from response) + new category (from... where?)
```

The todoId comes from the response, but the category is test data. You could hardcode it in the derivation, but then you can't easily vary it across test runs. Templates solve this.

Configure templates in `TestGenerationOptions.RequestTemplates`:

```csharp
var options = new TestGenerationOptions
{
    RequestTemplates = new Dictionary<string, Func<object>>
    {
        ["UpdateTodo"] = () => new UpdateTodoTemplate 
        { 
            Category = "Work",
            Priority = "High"
        }
    }
};

spec.RunTests(options);
```

Then use the three-argument form of `.As()`:

```csharp
Derive.From<CreateTodoRequest, CreateTodoResponse, UpdateTodoRequest>("CreateTodo")
    .As((createReq, createResp, template) => 
    {
        var t = (UpdateTodoTemplate)template;
        return new UpdateTodoRequest
        {
            TodoId = createResp.TodoId,    // From response
            Category = t.Category,          // From template
            Priority = t.Priority           // From template
        };
    })
```

The framework calls your template factory, passes the result to your derivation function, and you combine response data with template data to build the request.

You can also make the template factory return different values each time (using a random generator or cycling through a list) to vary test data across runs.

---

## Producing Multiple Variants with .AsVariants()

Sometimes one source response can produce multiple interesting derived requests that each use the response data in different ways.

Consider an order management API. After creating an order, you want to test different state transitions — approve it, reject it, or cancel it. Each action needs the order ID from the response, but represents a different operation:

```csharp
Derive.From<CreateOrderRequest, CreateOrderResponse, OrderActionRequest>("CreateOrder")
    .AsVariants((req, resp) => new Dictionary<string, OrderActionRequest>
    {
        ["Approve"] = new OrderActionRequest
        {
            OrderId = resp.OrderId,  // Need the ID from response
            Action = OrderAction.Approve
        },
        ["Reject"] = new OrderActionRequest
        {
            OrderId = resp.OrderId,
            Action = OrderAction.Reject,
            Reason = "Failed validation"
        },
        ["Cancel"] = new OrderActionRequest
        {
            OrderId = resp.OrderId,
            Action = OrderAction.Cancel
        }
    })
```

Each dictionary entry is a labeled variant. During test execution, the framework explores all of them, giving you coverage of different state transition paths from the same starting point.

The labels (`"Approve"`, `"Reject"`, `"Cancel"`) appear in test output, making it easy to identify which path is being tested.

---

## Derivations and Polling

Derivations also pair naturally with polling. When an operation triggers async background work, you typically poll a separate operation until completion.

In some polling scenarios, you could technically construct the poll request without any response data — for example, if the client chose the jobId when calling CreateJob, that same jobId could be used for GetJob without looking at the response. In other cases you need to look at the response to construct the polling request, say if the jobId comes from the respnse.

But to keep things simple and uniform, you write a derivation anyway. The derivation gives the framework a function to construct the poll request, and you implement that function however makes sense:

**From the request** — when the client chose the identifier:

```csharp
// CreateJob takes a jobId chosen by the client
// GetJob polls using the same jobId

Derive.From<string, CreateJobResponse, string>("CreateJob")
    .As((jobId, resp) => jobId)  // Pass through the original jobId
```

**From the response** — when the server generated the identifier:

```csharp
// CreateJob takes job parameters, server generates a jobId
// GetJob polls using the server-generated jobId

Derive.From<CreateJobRequest, CreateJobResponse, string>("CreateJob")
    .When((req, resp) => resp.IsSuccess)
    .As((req, resp) => resp.JobId)  // Extract jobId from response
```

The derivation is just a function that takes the source request and response and returns the derived request. You decide what to use inside that function.

See [Step Functions and Async Operations](step-functions-and-async.md) for more on polling.

---

## When to Use Derivations

Use derivations when:

- **Server-generated values**: The server returns an identifier (ID, ETag, token) that subsequent operations need
- **Polling operations**: Even when the identifier comes from the original request, you need a derivation to tell the framework how to construct the poll request

You don't need derivations when:

- **Requests are constructible independently**: If the framework can build valid requests without data from prior operations (client-chosen IDs, fixed test data), derivations aren't needed

The key question: **Does the framework need to be told how to construct this request from prior operation data?** If yes, write a derivation. If the request can be constructed without any prior operation data, you do need to write a derivation function. And derivation functions are only needed if you're using the framework's test execution capabilities and are not needed if you're constructing the test cases yourself and only using the spec for conformance testing using its `Allows` or `AllowsConcurrent` methods.