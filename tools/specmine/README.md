# Specmine

`specmine` is the working name for experimental specification-mining tooling.
The initial benchmark services provide small systems with known semantics that
an investigator can explore through externally visible operations.

## Benchmarks

| Benchmark | Behavioral focus |
| --- | --- |
| `TaskWorkflow` | Server-generated IDs, terminal states, and idempotent retries |
| `PaymentProcessing` | Idempotency keys, changed retries, and payment lifecycle |
| `InventoryReservation` | Finite capacity, failed reservations, release, and concurrency |

Each benchmark contains:

- an in-memory ASP.NET Core API;
- a public OpenAPI document that describes operation shapes without disclosing
  behavioral rules;
- health and benchmark reset endpoints;
- ground-truth tests and maintainer documentation.

## Black-box investigations

Benchmark source and ground-truth tests are available to maintainers but should
not be exposed to the investigating agent. A black-box run should:

1. Build and start the selected API.
2. Create a separate investigation directory.
3. Copy only the benchmark's OpenAPI document into that directory.
4. Give the investigator the service URL and investigation directory.
5. Keep this source tree and the ground-truth tests outside the investigator's
   accessible filesystem.

Running an agent elsewhere in this repository and merely asking it not to read
the benchmark is useful for early development, but it is not an enforced
black-box evaluation.

`POST /__test/reset` is benchmark infrastructure rather than a domain
operation. It is intentionally externally invocable so an investigation can
record when reset was attempted and can experimentally check its effects.

## Tracing library (`src/Specmine`)

`Specmine` is a small library for recording one bounded investigation as a
single-file trace. It intentionally does not depend on Accordant.

A `TraceRecorder` is scoped to one experiment:

```csharp
var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory, async recorder =>
{
    var created = await recorder.ExecuteAsync("CreateTask", new CreateTaskRequest("write benchmark"),
        request => client.CreateTaskAsync(request));

    // Response-dependent code works naturally: the server-generated ID from
    // `created` flows straight into the next call.
    await recorder.ExecuteAsync("CompleteTask", new TaskIdRequest(created.Id),
        request => client.CompleteTaskAsync(request));
});
```

`ExecuteAsync` snapshots the request, invokes the execution delegate against the
system under test, and records either the response or - if the delegate throws -
an execution error (exception type and message) before rethrowing. `RunAsync`
persists a `Completed` trace if the body finishes, or an `Interrupted` trace (with
the calls recorded so far) if it throws, and always rethrows the original failure.

`TraceStore.SaveAsync` writes the trace as one indented JSON file named after its
trace ID, atomically (via a temporary file plus a non-overwriting move) into a
caller-provided traces directory; a persisted trace file is never overwritten.
`TraceStore.LoadAsync` reloads a trace file back into a `RecordedTrace`.

## Investigation workspace (`src/Specmine`)

A `Workspace` is a minimal, on-disk investigation workspace: a small, fixed set of
artifacts rooted at one directory.

```
<root>/
  workspace.json   - schema version and the target adapter declaration
  target/          - reserved for adapter-owned target artifacts
  traces/          - recorded traces (see TraceRecorder / TraceStore above)
  frontier.md      - free-form notes on open questions and next steps
  journal.md       - free-form running log of what was tried and observed
```

```csharp
var settings = JsonSerializer.SerializeToElement(new { baseUrl = "https://localhost:5001" });
var workspace = await Workspace.InitializeAsync(workspaceRoot, new TargetAdapterDeclaration("openapi", settings));

// Later, in a fresh process:
var reloaded = await Workspace.LoadAsync(workspaceRoot);

// Point the recorder at the workspace's traces directory.
await TraceRecorder.RunAsync(reloaded.TracesDirectory, async recorder => { /* ... */ });
```

`workspace.json` declares a schema version and a single `TargetAdapter` with an
`AdapterType` string and an opaque `Settings` JSON value. **Adapter settings are
entirely adapter-owned**: the workspace schema does not define a base URL,
credential model, capability taxonomy, reset model, or operation catalog - an
OpenAPI adapter, a command adapter, or any future custom adapter defines its own
`Settings` shape without changing the workspace schema. `Settings` is snapshotted
(cloned) at construction time, so later mutation or disposal of whatever JSON
object or document the caller built it from cannot alter the workspace afterward.

`Workspace.InitializeAsync` creates the root directory (building the whole
artifact set in a private staging directory and moving it into place with a
single directory rename when the root doesn't already exist, so a reader never
observes a partially initialized workspace) and refuses to run if a workspace is
already initialized there or an existing file/directory would conflict with one
of its artifacts. `Workspace.LoadAsync` validates that `workspace.json` exists,
declares a supported schema version and a non-blank adapter type, and that the
`target/`, `traces/`, `frontier.md`, and `journal.md` artifacts all exist,
throwing a specific exception for whichever check fails.

A workspace performs no orchestration and includes no adapter implementation or
CLI: it only creates, validates, and resolves paths, and exposes
`TracesDirectory` for `TraceRecorder` to write into.

See `tests/Specmine.Tests` for recorder, persistence, workspace, and TaskWorkflow
integration tests; run them with:

```powershell
dotnet test tools\specmine\tests\Specmine.Tests\Specmine.Tests.csproj
```
