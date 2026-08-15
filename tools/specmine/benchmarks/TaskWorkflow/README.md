# Task Workflow benchmark

This benchmark is a small in-memory ASP.NET Core system under test for experimental
spec-mining tools. It intentionally does not reference Accordant. Its committed
`TaskWorkflow.Api/openapi.json` describes callable request and response shapes while
leaving lifecycle behavior for a miner to discover.

## Run

From the repository root:

```powershell
dotnet test tools\specmine\benchmarks\TaskWorkflow\TaskWorkflow.Tests\TaskWorkflow.Tests.csproj
```

To run the API directly:

```powershell
dotnet run --project tools\specmine\benchmarks\TaskWorkflow\TaskWorkflow.Api\TaskWorkflow.Api.csproj
```

The service exposes `GET /health`, serves the committed contract at
`GET /openapi.json`, and provides `POST /__test/reset` for isolation between
benchmark runs.

## Hidden ground truth

The following rules are maintainer ground truth and are deliberately absent from
the OpenAPI descriptions:

- `CreateTask` accepts a nonblank title, assigns a server-generated ID, and starts
  the task in `pending`.
- `GetTask` returns the stable task representation or a typed `404`.
- `CompleteTask` transitions `pending` to `completed`. Repeating it after completion
  is idempotent; applying it to a canceled task returns a typed `409`.
- `CancelTask` transitions `pending` to `canceled`. Repeating it after cancellation
  is idempotent; applying it to a completed task returns a typed `409`.
- Both terminal operations return a typed `404` for an unknown ID.
- Reset atomically clears the in-memory task collection.

The operation names and their request/response shapes are designed to map directly
to Accordant named operations. In particular, `CreateTask` returns the generated ID
that a response-dependent state model can capture for later operations.
