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

### Adapter/session SDK

A target is anything an investigation can call operations against - an HTTP API, a
CLI, or (as a proof, see `tests/Specmine.Tests/InMemoryTaskAdapter.cs`) an
in-process dictionary. Two interfaces describe that boundary, and JSON Schema is
the portable contract everything else is built on:

- `OperationDefinition` - a stable operation name plus request and response JSON
  Schemas. `OperationDefinition.Create<TRequest, TResponse>(name)` derives both
  schemas from ordinary C# request/response types using .NET's built-in
  `System.Text.Json.Schema.JsonSchemaExporter`, so an adapter with concrete types
  doesn't need to hand-author JSON Schema; hand-authored schemas work too.
- `ITargetAdapter` - identifies itself with a stable `AdapterType` string and
  connects opaque, adapter-owned `JsonElement` settings (the same settings a
  `TargetAdapterDeclaration` carries) to a live `ITargetSession`.
- `ITargetSession : IAsyncDisposable` - exposes a stable `Operations` catalog and
  `ExecuteAsync(operationName, request, cancellationToken)`, taking and returning
  concrete `JsonElement` values. A session owns its own client/in-memory target
  lifetime and validates tightly: an unrecognized operation name throws
  `UnknownOperationException`, and an undefined/invalid JSON request throws.
  `TargetSessionBase` is an abstract base that centralizes this validation so an
  adapter's session only implements its own operation dispatch.

An adapter's internal implementation is free to use whatever C# types (or none)
it likes; only the JSON Schemas it declares and the JSON values it exchanges need
to be portable.

### Recording and running an experiment

`TraceRecorder.RunAsync` takes an existing, caller-owned `ITargetSession` and
wraps it in a `RecordingTargetSession` - a transparent decorator that delegates
operation discovery and execution to the wrapped session while automatically
recording every attempted call, in order:

```csharp
var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory, target, async recordingTarget =>
{
    var created = await recordingTarget.ExecuteAsync("CreateTask", createTaskRequestJson);

    // Response-dependent code works naturally: the server-generated ID from
    // `created` flows straight into the next call.
    await recordingTarget.ExecuteAsync("CompleteTask", ToCompleteRequestJson(created));
});
```

`RunAsync` never disposes `target`: the caller or adapter that produced it owns
its lifetime (typically via `await using`) and disposes it once done running
experiments against it, not once any single `RunAsync` call returns. If the body
runs to completion, a `Completed` trace of every recorded call is persisted; if it
throws - for an unrecognized operation, an invalid request, a genuine execution
failure, or a cancellation - the failure is recorded as an execution error, an
`Interrupted` trace of the calls recorded so far is persisted, and the original
exception is always rethrown, never swallowed.

C# callers who would rather work with typed request/response values than raw
`JsonElement` can use the `ExecuteAsync<TRequest, TResponse>` extension method on
`ITargetSession`: it is a thin layer that serializes/deserializes over the same
JSON boundary and records exactly the same portable JSON a raw-JSON caller would
have produced.

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

// Connect the declared adapter to a live session, then point the recorder at the
// workspace's traces directory.
var adapter = ResolveAdapter(reloaded.Document.TargetAdapter.AdapterType);
await using var target = await adapter.ConnectAsync(reloaded.Document.TargetAdapter.Settings);
await TraceRecorder.RunAsync(reloaded.TracesDirectory, target, async recordingTarget => { /* ... */ });
```

(Adapter discovery - resolving an `AdapterType` string like `"openapi"` to a
concrete `ITargetAdapter` - is out of scope for this slice; `ResolveAdapter` above
is illustrative only.)

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
