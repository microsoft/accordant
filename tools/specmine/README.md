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

## Partial Accordant models (`src/Specmine.Accordant`)

Mining models can mark incomplete knowledge next to executable behavior:

```csharp
return Understanding.Unknown<Response>(
    "fresh-key-outcome",
    "The success-versus-decline rule has not been characterized.");

return Understanding.Provisional(
    "replay-live-status",
    "Does replay reflect every lifecycle transition?",
    Expect.That<Response>(IsLiveRecord).SameState());
```

There is also `Understanding.Assume(condition, id, reason)` for the request side: call it at
the top of an operation's model function to restrict its claims to requests/states it actually
covers. When `condition` is false it throws `AssumptionViolatedException` - a distinct,
always-on signal (no permissive mode) that the model was never asked to cover this case,
rather than a claim it got wrong. `TraceReplayer` reports this as `OutOfScope` and stops.

`Unknown`/`Provisional` wrap the outcome's `ResponseValidator` itself rather than the
`ExpectedOutcome` type, so the marker is evaluated exactly once, inside whatever single call
(`Spec<TState>.Allows`, Accordant's own verification, or a hand-written check) happens to
invoke it - there is no separate inspection pass and no reliance on the
`IExpectedOutcomesProvider` interface or any other side channel. Behavior is governed by the
ambient `Understanding.CurrentStrictness` (set directly, or scoped with
`Understanding.UseStrictness`):

- **`Reject`** (default) - `Unknown` fails with an `UNKNOWN[id]` explanation; `Provisional`
  validates its wrapped expectation honestly (a match passes, a mismatch is an ordinary
  `ModelViolation`).
- **`Accept`** - `Unknown` passes, state unchanged; `Provisional` behaves as under `Reject`.
- **`Strict`** - `Unknown` throws `UnknownRegionEncounteredException` unconditionally;
  `Provisional` throws `ProvisionalMatchEncounteredException` only when its wrapped
  expectation actually matches (a mismatch still surfaces as an ordinary rejection).

In the non-throwing modes, reaching either marker records an `Understanding.LastEncounter`
(cleared via `Understanding.ClearLastEncounter()`) that any caller can inspect immediately
after its validation call - this is how `TraceReplayer` reports `Unknown`/`ProvisionalMatch`
without a second call into the model. If a caller runs `TraceReplayer.Replay` under
`UnderstandingStrictness.Strict`, an encountered marker's exception propagates out of `Replay`
rather than being converted into a step result - that is the point of strict mode.

Because an understanding marker throws from inside the validator that `Spec<TState>.Allows` invokes
while exploring the state graph, Accordant's own state-graph machinery catches it first and
re-throws it as an `InvalidSpecException` wrapping a `StepFunctionApplicationException`
wrapping the original exception - the same thing that happens to any accidental exception
from a model's `Apply` function. `TraceReplayer` unwraps this automatically and re-throws (or
reports) the original `AssumptionViolatedException`/`UnknownRegionEncounteredException`/
`ProvisionalMatchEncounteredException`, so callers of `TraceReplayer.Replay` never see the
wrapper. Any other caller that invokes `Spec<TState>.Allows` directly under `Strict` mode
should expect the same wrapping and unwrap it the same way if it needs the specific marker
exception type.

`TraceReplayResult` reports accepted, provisional, unknown, out-of-scope, and violation
counts.

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

### Adapter registry and workspace activation (`src/Specmine`)

Turning a workspace's declared `AdapterType` string into a live session requires
a host - a test harness, a future CLI, an IDE extension - that knows which
concrete `ITargetAdapter` implementations exist. `TargetAdapterRegistry` is a
small, explicit registry the host builds by hand: there is no reflection,
assembly scanning, package loading, or dependency injection container - every
adapter type a host can activate is registered in code the host controls.
`WorkspaceActivator.ConnectAsync` is the bridge from a loaded workspace to a live
session: it resolves `workspace.Document.TargetAdapter.AdapterType` in the
registry and connects that adapter with the declaration's own opaque `Settings`.

```csharp
// Host startup: register exactly the adapters this host supports.
var registry = new TargetAdapterRegistry();
registry.Register(new OpenApiTargetAdapter());
registry.Register(new CommandTargetAdapter());

// Load a previously initialized workspace and activate its declared adapter.
var workspace = await Workspace.LoadAsync(workspaceRoot);
await using var target = await WorkspaceActivator.ConnectAsync(workspace, registry, cancellationToken);

// The returned session is live and caller-owned: enumerate its operations,
// execute them directly, or hand it to TraceRecorder to record an experiment.
foreach (var operation in target.Operations)
{
    Console.WriteLine($"{operation.Name}: {operation.RequestSchema} -> {operation.ResponseSchema}");
}

await TraceRecorder.RunAsync(workspace.TracesDirectory, target, async recordingTarget =>
{
    await recordingTarget.ExecuteAsync("CreateTask", createTaskRequestJson);
});
```

`Register` throws if an adapter is already registered for the same adapter type
(comparing type strings with ordinal, case-sensitive semantics throughout, since
the same string is also the literal value written into `workspace.json`);
`Resolve`/`ConnectAsync` throw `UnknownAdapterTypeException` if the workspace
declares a type nothing is registered for. `ConnectAsync` passes cancellation
through to the adapter, never swallows a settings-validation or connection
exception the adapter throws, and throws `InvalidOperationException` if a
misbehaving adapter returns a null session. Workspace paths (`RootPath`,
`TargetDirectory`, ...) are machine-resolved runtime properties of `Workspace`
itself and are never implicitly added into the settings handed to the adapter -
an adapter that needs a workspace-relative path (for example, a future OpenAPI
adapter's document path) must have that path included explicitly, by whoever
authored the declaration, inside its own `Settings`.

## Built-in OpenAPI adapter (`src/Specmine.Adapters.OpenApi`)

`OpenApiTargetAdapter` (`AdapterType = "openapi"`) turns a committed OpenAPI 3.0.x
JSON document plus a base URL into a live `ITargetSession`, using only
`System.Text.Json` - no external OpenAPI parsing library. It is built for the
documents the three benchmarks commit, not as a general-purpose OpenAPI 3.x
implementation; see Limitations below for what it deliberately does not handle.

### Settings

```json
{ "document": "C:\\path\\to\\openapi.json", "baseUrl": "https://localhost:5001" }
```

- `document` - path to a local OpenAPI JSON file. A relative path is resolved
  against the current process's working directory; resolving it relative to the
  workspace root instead is deferred (per the note above, a workspace-relative
  path is the declaration author's responsibility to resolve into `Settings`
  before it reaches the adapter - no core-layer leakage).
- `baseUrl` - an absolute `http`/`https` URL the adapter's own `HttpClient` sends
  requests against.

Both fields are required and validated eagerly in `ConnectAsync`
(`OpenApiSettingsException` for a malformed settings shape, `FileNotFoundException`
if `document` does not exist). No credential settings exist yet.

### Operation discovery

Every path/method pair with a nonblank, unique `operationId` becomes one
`Operation`, named after that `operationId`. A missing or duplicate
`operationId` fails document parsing outright (`OpenApiDocumentException`)
rather than inventing a synthesized name. Local `#/components/schemas/...`
references are resolved recursively and inlined; a reference to a nonexistent
schema, a non-local reference (e.g. a remote URL), or a reference cycle is
rejected explicitly instead of hanging or silently truncating.

Only `path`, `query`, and `header` parameters and `application/json` request
bodies are supported; `cookie` parameters, any other parameter location, and any
non-JSON request body content type fail document parsing with a message naming
the unsupported location/content type.

### Composite request/response representation

Every operation's request and response are one concrete, portable JSON shape,
regardless of what the operation actually declares:

```jsonc
// Request
{ "path": { /* path params */ }, "query": { /* query params */ }, "headers": { /* header params */ }, "body": /* JSON request body, or omitted */ }

// Response - always exactly this shape, every execution
{ "status": 200, "body": /* one of the operation's documented JSON response schemas, or JSON null */ }
```

- The request schema only includes the `path`/`query`/`headers`/`body` sections
  an operation actually declares (an operation with no parameters and no body has
  an empty request object). A section is `required` in the schema if the
  operation has any required parameter in that location (`path` is required
  whenever any path parameters exist, since OpenAPI path parameters are always
  required); `body` is required exactly when `requestBody.required` is `true`.
- The response schema is always `{ "status": integer, "body": <schema> }`. `body`
  is a `oneOf` of every distinct JSON schema documented across the operation's
  responses (deduplicated, and with a response that is itself a bare top-level
  `oneOf` flattened into the aggregate instead of nesting), plus a trailing
  `null` option - because real execution can return an undocumented status or
  an empty body regardless of what is documented. If an operation documents no
  JSON response schemas at all, `body` is simply `{"type": "null"}`.
- HTTP failure responses (4xx/5xx) are an ordinary `{ "status": ..., "body": ... }`
  result, not a thrown exception - only network/client failures, JSON document
  parsing failures, and non-JSON response bodies are execution exceptions.
- Runtime validation before sending a request checks section/parameter
  *presence* (required sections and required parameters must be present) but
  does **not** perform full JSON Schema validation of a request body's nested
  properties; malformed body content is only caught once the server itself
  rejects it.

### Limitations

- No credentials/authentication support.
- Only `path`, `query`, and `header` parameters; only `application/json` request
  bodies. Anything else fails at document-parse time.
- Only local `#/components/schemas/...` references are resolved; remote refs and
  reference cycles are rejected rather than followed.
- Response headers are not represented in the response schema.
- `document` path resolution is relative to the current process only, not the
  workspace root.
- Request validation checks presence of required sections/parameters, not full
  JSON Schema conformance of the body's contents.

See `tests/Specmine.OpenApi.Tests` for settings validation, malformed/unsupported
document handling, TaskWorkflow discovery and schema-shape assertions, a real
end-to-end Reset -> CreateTask -> CompleteTask -> CompleteTask trace recorded
against a live Kestrel-hosted TaskWorkflow instance, path/query/header/body
parameter-mapping coverage against a small fixture API, and PaymentProcessing/
InventoryReservation discovery coverage (proving generality without duplicating
either benchmark's ground truth); run them with:

```powershell
dotnet test tools\specmine\tests\Specmine.OpenApi.Tests\Specmine.OpenApi.Tests.csproj
```

See `tests/Specmine.Tests` for recorder, persistence, workspace, and TaskWorkflow
integration tests; run them with:

```powershell
dotnet test tools\specmine\tests\Specmine.Tests\Specmine.Tests.csproj
```

## Accordant bridge (`src/Specmine.Accordant`)

`Specmine.Accordant` replays an immutable `RecordedTrace` through an Accordant
`Spec<TState>` acting purely as an oracle: it never executes anything, it only
asks, call by call, "does the model allow this?" This is the sequential
conformance-checking pattern documented for cross-language trace validation (see
`agent/skills/cross-language/SKILL.md` and `docs/how-to/testing-any-system.md`),
wired directly to Specmine's own `RecordedTrace`/`RecordedCall` shape instead of a
hand-rolled trace format.

```csharp
var spec = TaskWorkflowSpec.Create();               // your Spec<TState>
var trace = await TraceStore.LoadAsync(tracePath);  // or replay the object directly

var result = TraceReplayer.Replay(spec, new TaskWorkflowState(), trace);
// or: var result = await TraceReplayer.ReplayAsync(spec, new TaskWorkflowState(), tracePath);

foreach (var step in result.Steps)
{
    Console.WriteLine($"{step.CallId} {step.OperationName}: {step.Outcome} {step.Message}");
}
```

`TraceReplayer.Replay`/`ReplayAsync` walk `RecordedTrace.Calls` in order, matching
each call's `OperationName` to a spec operation by exact name, deserializing its
recorded request/response `JsonElement`s into that operation's declared
`RequestType`/`ResponseType`, and feeding them through `spec.Allows` - the same
`StateProfile`-threading validation path Accordant itself uses, never
reimplemented. Deserialization uses caller-supplied `JsonSerializerOptions` when
given, otherwise `ReplayJsonOptions.CreateDefault()` (`JsonSerializerDefaults.Web`
plus a camelCase string enum converter, matching how Specmine snapshots traces).

Rather than a bare pass/fail, `TraceReplayResult` reports one `ReplayStepResult`
per replayed call, each with a `ReplayStepOutcome`:

| Outcome | Meaning |
| --- | --- |
| `Conforming` | The operation is modeled and `spec.Allows` accepted the response. |
| `ModelViolation` | The operation is modeled but the response was rejected; `Message` carries Accordant's own explanation. |
| `OperationNotModeled` | The call's `OperationName` matches no operation in the spec - reported explicitly, never silently accepted and never counted as a violation. A partial model is legitimate. |
| `ExecutionError` | The recorded call captured an execution error rather than a response. |
| `RequestDeserializationFailed` / `ResponseDeserializationFailed` | The recorded JSON did not deserialize into the operation's declared type. |

The overall `TraceReplayResult.Status` is `Conforming` only if every call in the
trace replayed as `Conforming`; `UnsupportedSchemaVersion` or
`InvalidTraceStructure` if the trace itself failed an integrity check (an
unrecognized `SchemaVersion`, or a non-positive/non-increasing `CallId`) before
any call was replayed; otherwise `Stopped`.

**Replay conservatively stops** at the first call that is not `Conforming`. For a
`ModelViolation` this is forced - `Verify` has no successor state to offer once a
response is rejected. For an unmodeled operation, an execution error, or a
deserialization failure, replay could in principle keep validating later calls
against the same state profile, but this slice stops there anyway: a call that
couldn't be explained might have changed real system state the model tracks, so
later "conforming" results would rest on an unverified assumption. `Steps` is
therefore a strict prefix of `RecordedTrace.Calls` whenever `Status` is
`Stopped`, and `FinalStateProfile` is the `StateProfile` after the last
`Conforming` call - the most advanced point at which the state is still reliably
known (`null` only when the trace itself failed an integrity check). Neither the
trace nor the initial state is ever mutated.

**Sequential only.** `RecordedTrace` has no concurrency segments yet, so this
bridge only calls `Spec<TState>.Allows`, never `Spec<TState>.AllowsConcurrent`.
Supporting concurrent replay waits on a future trace representation for
concurrent segments - this slice does not invent one.

Out of scope for this slice: no live target execution, no model generation, no
promotion of a trace into a ground-truth test, no CLI, no agent orchestration -
just the replay-as-oracle bridge described above.

See `tests/Specmine.Accordant.Tests` for coverage (a small Accordant spec with
response-derived state, conforming/violating/unmodeled/error/deserialization-
failure/malformed-trace scenarios, the trace-path overload, custom serializer
options, and non-mutation/repeatability); run them with:

```powershell
dotnet test tools\specmine\tests\Specmine.Accordant.Tests\Specmine.Accordant.Tests.csproj
```
