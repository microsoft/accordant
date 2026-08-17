# TaskWorkflow black-box investigation: first evaluation

This is a durable record of the first two-agent black-box investigation of the
`TaskWorkflow` benchmark (`tools\specmine\benchmarks\TaskWorkflow`). It captures the
staged workspace's immutable evidence (traces, frontier, journal) plus an evaluation
of that evidence against maintainer ground truth, and the tooling friction observed
while producing it. Nothing here modifies the SDK, adapters, benchmarks, `solution`,
`packages`, or the staged investigation workspace itself - all of that is read-only
input to this record.

## Evaluation setup

Two agents worked in sequence against the same on-disk `Workspace`
(`src\Specmine` schema: `workspace.json` + `target/` + `traces/` + `frontier.md` +
`journal.md`), each in its own conversation with no shared context:

1. **First agent** started from a freshly initialized workspace (`target/openapi.json`
   only, empty `traces/`, template `frontier.md`/`journal.md`) pointed at a running
   `TaskWorkflow.Api` instance, formed a question, ran one experiment, and recorded a
   trace plus its frontier/journal updates.
2. **Second agent** started from nothing but the *workspace on disk* as left by the
   first agent - `frontier.md`, `journal.md`, and the one existing trace file were its
   only sources of prior context. It picked the next open question directly off
   `frontier.md`'s "Useful next question(s)" list, ran one more experiment, and
   appended to `journal.md` and `frontier.md`.

**Cooperative, not enforced, black-box boundary.** Per `tools\specmine\README.md`'s
"Black-box investigations" section, source and ground-truth tests were never placed
inside the investigating agents' accessible filesystem, and neither agent ever
expected or requested them. But nothing sandboxed them from the rest of the
repository the way a hard boundary would; this evaluation, run afterward with
explicit permission to read the hidden `README.md`/test file, is what actually
scores what the agents produced against ground truth. The two agents' isolation was
real (separate workspaces/conversations, no shared memory) but not
process-enforced - consistent with the benchmark's own documented caveat that this
level of separation "is useful for early development, but it is not an enforced
black-box evaluation."

## Workspace / artifact shape used

The staged workspace at
`...\blackbox-task-1\workspace\` followed the `Specmine.Workspace` layout exactly:

```
workspace.json   - schema version + one TargetAdapter declaration (AdapterType "openapi")
target/openapi.json - the benchmark's committed, behavior-silent OpenAPI document
traces/          - two RecordedTrace JSON files, one per agent's experiment (copied here)
frontier.md      - free-form open-questions note, overwritten in place by each agent
journal.md       - free-form running log, appended to by each agent
runner/          - NOT part of the Workspace schema; a throwaway console project the
                   agents wrote themselves to drive TraceRecorder (see "Tooling
                   friction" below). Not copied into this record - see Exclusions.
sdk/             - compiled Specmine.dll / Specmine.Adapters.OpenApi.dll the runner
                   referenced via HintPath. Not copied - see Exclusions.
```

Each trace is a `RecordedTrace`: `SchemaVersion`, `TraceId`, `StartedAt`/`CompletedAt`,
`Status`, and an ordered `Calls[]` list, each call recording `OperationName`, the
composite `{path, query, headers, body}` `Request`, the composite `{status, body}`
`Response`, and `Error` (always `null` in both traces here - both runs completed
without an execution exception).

## First agent: starting frontier and selected question

`frontier.md` began empty of prior findings (freshly initialized workspace: "no
experiments have been run"). The first agent formed its own opening question rather
than refining an existing one:

> **Is same-transition repetition idempotent, and is cross-transition on a terminal
> task a hard conflict?**

i.e., does repeating `CompleteTask`/`CancelTask` on an already-terminal task behave
the same way twice (idempotent) or error, and does calling the *other* terminal
operation on an already-terminal task conflict.

## First trace sequence and bounded observations

Trace `8680b90db2c54d92a63e20e3f0f2e988.json` (9 calls, `Status: "completed"`):

| Call | Operation | Result |
| --- | --- | --- |
| 1 | `Reset` | 204 |
| 2 | `CreateTask` ("Idempotency probe task") | 201, task A `pending` |
| 3 | `CompleteTask` A | 200, `completed` |
| 4 | `CompleteTask` A (repeat) | 200, identical body to call 3 |
| 5 | `CancelTask` A (already `completed`) | 409 `invalid_transition` |
| 6 | `GetTask` A | 200, still `completed` (confirms call 5 had no side effect) |
| 7 | `CreateTask` ("Cancel idempotency probe task") | 201, task B `pending` |
| 8 | `CancelTask` B | 200, `canceled` |
| 9 | `CancelTask` B (repeat) | 200, identical body to call 8 |

The first agent's journal entry explicitly bounded its claim: single run, single
pair of tasks, only the complete-then-cancel direction of the cross-transition
conflict exercised, no concurrency tested, and no assertion that the behavior is
contractually guaranteed rather than merely observed once.

## Frontier after first agent

`frontier.md`'s "Bounded hypothesis" after this trace: same-operation repeats on an
already-terminal task look idempotent (200, unchanged body); the *other* terminal
operation on an already-terminal task is rejected 409 `invalid_transition` with no
state mutation - but only demonstrated in the `completed` -> `cancel` direction. It
listed five "Useful next question(s)", including the untested mirror direction
(`canceled` -> `complete`), byte-for-byte stability of idempotent repeats across many
repetitions, 404-vs-409 precedence for a nonexistent id, whether `GetTask` is
side-effect free, order-independence of which terminal state is reached first, and
whether any operation can leave a terminal state.

## Second agent: reconstruction, selected experiment, trace, frontier refinement

The second agent had no conversational memory of the first; its journal entry opens
by explicitly reconstructing state by reading `frontier.md` and `journal.md` from
the workspace and reused the workspace-local `runner\Program.cs` (editing it in
place) to drive a new experiment. It selected the first listed open question
verbatim - the untested mirror direction - rather than any of the other four:

> Does `CompleteTask` on an already-`canceled` task also return 409, mirroring the
> observed `CancelTask`-on-`completed` conflict?

Trace `70b4478c957d409f917540296e2a4524.json` (5 calls, `Status: "completed"`):

| Call | Operation | Result |
| --- | --- | --- |
| 1 | `Reset` | 204 |
| 2 | `CreateTask` ("Complete-after-cancel mirror probe task") | 201, `pending` |
| 3 | `CancelTask` | 200, `canceled` |
| 4 | `CompleteTask` (already `canceled`) | 409 `invalid_transition` |
| 5 | `GetTask` | 200, still `canceled` (confirms call 4 had no side effect) |

Call 4's error body was byte-for-byte the same `code`/`message` shape as the first
trace's call 5. The second agent updated `frontier.md`'s "Bounded hypothesis" to
state the cross-transition conflict is now observed symmetric in **both**
directions (`completed` -> `cancel` and `canceled` -> `complete`), still explicitly
qualified as resting on "single-run evidence per direction (one task per case, no
repetition, no concurrency)" and therefore an observation rather than a proven
universal guarantee. The five open questions were re-stated with the mirror-
direction question resolved and removed, one added about order-independence of
which terminal state is reached first.

## Comparison against hidden ground truth

Ground truth is `tools\specmine\benchmarks\TaskWorkflow\README.md`'s "Hidden ground
truth" section plus `TaskWorkflow.Tests\TaskWorkflowApiTests.cs`.

**Correct claims** (directly supported by the two traces and matching ground truth
exactly):

- `CompleteTask` transitions `pending` -> `completed`; repeating it afterward is
  idempotent (200, unchanged body). Matches ground truth's `CompleteTask` rule and
  `CompleteIsIdempotent` test, and was directly observed (calls 3-4, trace 1).
- `CancelTask` transitions `pending` -> `canceled`; repeating it afterward is
  idempotent. Matches ground truth's `CancelTask` rule and `CancelIsIdempotent`
  test (calls 8-9, trace 1).
- `CompleteTask` on an already-`canceled` task, and `CancelTask` on an already-
  `completed` task, both return a typed `409 invalid_transition` with no state
  mutation, in **both** directions. Matches ground truth's `CompleteTask`/
  `CancelTask` rules and the ground truth's own `OppositeTerminalTransitionsConflict`
  parameterized test (both `["complete","cancel"]` and `["cancel","complete"]`
  cases) almost exactly, down to the identical `code`/`message` error body (observed
  in trace 1 call 5 and trace 2 call 4).

**Unsupported / overstrong claims:** none found. Both agents consistently hedged
their language ("observation, not a universal claim", "bounded hypothesis", "does
not establish universal guarantees") and never asserted more than their traces
directly showed. No claim in `frontier.md` or `journal.md` contradicts or overreaches
past the ground truth.

**Remaining unknowns** (correctly left open by the investigation, per its own
"Useful next question(s)" list, and confirmed genuinely untested against the two
traces):

- Typed `404` behavior for a nonexistent id on `GetTask`/`CompleteTask`/`CancelTask`,
  and whether 404 takes precedence over 409 when both could apply. (Ground truth:
  "Both terminal operations return a typed `404` for an unknown ID"; exercised by
  `MissingIdsReturnTypedNotFound` - never invoked by either agent.)
- Blank-title validation on `CreateTask` (ground truth: `validation_error` 400,
  exercised by `BlankTitlesReturnTypedValidationError`) - never attempted; both
  traces only ever created tasks with nonblank titles.
- Whether `Reset` atomically clears *all* prior tasks, including ones from an
  earlier trace/run, rather than merely being invoked at the start of each trace
  (ground truth: "Reset atomically clears the in-memory task collection", exercised
  by `ResetClearsAllTasks`) - both agents called `Reset` and relied on a clean slate
  but never verified a previously created id 404s after a `Reset`.
- Byte-for-byte stability of idempotent-repeat responses across more than one
  repetition, and whether `GetTask` itself is provably side-effect free beyond the
  single confirming read used here - both flagged as open by the investigation
  itself, not exercised in either trace.

No test in `TaskWorkflowApiTests.cs` was contradicted by the investigation's claims.

## Tooling friction observed

- **No CLI existed**, so each agent had to write (or, in the second agent's case,
  find and edit in place) its own throwaway `runner\Program.cs` console app just to
  call `Workspace.LoadAsync` -> `WorkspaceActivator.ConnectAsync` ->
  `TraceRecorder.RunAsync`. The second agent reused/overwrote the first agent's
  runner code rather than starting a new file, so the *exact* source that produced
  the first trace was not preserved as a separate artifact - only the journal's
  narrative description of the operation sequence survives for trace 1's runner
  code, while trace 2's runner source happens to still exist on disk in the staged
  workspace (`runner\Program.cs`) because it was the last one written.
- **`workspace.json` casing mismatch found during staging.** The workspace as found
  used camelCase top-level keys (`schemaVersion`, `targetAdapter`, `adapterType`,
  `settings`), but `Specmine.Workspace.LoadAsync` requires PascalCase
  (`SchemaVersion`, `TargetAdapter`, `AdapterType`, `Settings`) at the top level;
  the camelCase file threw `InvalidDataException: ... does not declare an integer
  'SchemaVersion'`. The first agent diagnosed this against a throwaway scratch copy
  before correcting the four top-level keys of the real file in place (nested
  adapter settings, e.g. `document`/`baseUrl`, remained camelCase and loaded fine
  either way). This is friction in how the workspace was staged/authored, not a
  finding about the target service.
- **Absolute document path portability.** `workspace.json`'s adapter `Settings.document`
  is an absolute, machine-specific path into the session's staging directory (per
  the SDK docs, `document` resolution is relative to the current process only, not
  the workspace root) - this workspace is not portable to another machine/session
  without editing that path, and the same absolute-path pattern appears in the
  runner's hardcoded `workspaceRoot` string.
- **Compiled SDK staging.** The workspace's `sdk/` directory held prebuilt
  `Specmine.dll`/`Specmine.Adapters.OpenApi.dll`, referenced by the runner project
  via `<Reference><HintPath>`, rather than the runner referencing SDK source
  projects directly - a build-time dependency on whatever binaries happened to be
  staged, with no visible provenance (commit/build id) recorded alongside them in
  the workspace.
- **Custom request envelope.** Every call requires hand-building the adapter's
  composite `{ path, query, headers, body }` request `JsonElement` (e.g.
  `JsonSerializer.SerializeToElement(new { path = new { id } })`) and reading the
  composite `{ status, body }` response back out by navigating `JsonElement`
  properties - there is no typed/generated client for `TaskWorkflow`'s specific
  operations, so both agents wrote this boilerplate by hand in their runner code.
- **Cooperative source isolation.** As noted above, nothing prevented either agent
  from reading the benchmark's source or ground-truth test file - the boundary
  held only because each agent was scoped to the staged workspace directory and
  chose not to look elsewhere. A stronger evaluation would need a process/
  filesystem boundary that makes this unenforceable rather than merely undone.

## Questions / abstractions for the next iteration

- Should the harness snapshot (or otherwise preserve) the exact runner/driver code
  that produced *each* trace, rather than letting the workspace's single
  `runner/` slot be overwritten between experiments? Right now trace-to-code
  provenance is lossy for any but the most recent run.
- Should `Workspace`/the OpenAPI adapter resolve `document` (and any future
  workspace-relative adapter setting) against the workspace root instead of the
  process working directory, to remove the absolute-path portability problem
  entirely?
- Would a minimal typed or generated client per benchmark (even just per-operation
  request/response POCOs layered over the existing JSON envelope) reduce the
  boilerplate/error surface of hand-building `{path, query, headers, body}`
  requests, without giving up the adapter's schema-driven portability?
- Is a lightweight `specmine` CLI (`init`, `run <question-script>`, `resume`)
  warranted so an agent doesn't need to scaffold a throwaway console project (with
  its own `.csproj`, `HintPath` references to prebuilt SDK binaries, `bin`/`obj`)
  just to drive one experiment?
- Given the second agent picked the *first* listed open question verbatim, is
  `frontier.md`'s flat "Useful next question(s)" list sufficient, or would tagging
  questions (e.g. by expected effort, or by which ground-truth rule they'd
  validate) help a fresh agent prioritize across multiple candidates rather than
  defaulting to list order?
- Should the cooperative black-box boundary documented in `tools\specmine\README.md`
  be upgraded to something enforced (a separate process/container/filesystem root
  the investigating agent cannot escape) before results like this are used to
  compare tools or techniques, given that nothing here actually prevented either
  agent from reading ground truth?
- Neither trace exercised 404 or validation-error paths at all; should a
  black-box protocol require (or at least prompt) an agent to probe declared-but-
  unobserved status codes from the OpenAPI document (`400`, `404` appear in the
  committed schema) before concluding an investigation, rather than leaving them
  entirely to the next agent's discretion?

## Exclusions

Per the evaluation scope, this record does **not** include: the `sdk/` compiled
DLLs, the `runner/` project (source, `bin/`, or `obj/`), `workspace.json` (contains
an absolute, session-specific staging path), or anything from the benchmark's
`TaskWorkflow.Api`/`TaskWorkflow.Tests` source or build output. The two trace files
and `frontier.md`/`journal.md` were inspected for machine-specific or sensitive
content before copying (none found: no absolute paths, hostnames, or secrets) and
are reproduced here byte-for-byte, unmodified.