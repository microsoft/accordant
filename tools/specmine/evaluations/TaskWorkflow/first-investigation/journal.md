# Journal

## Initialization

Created a fresh black-box workspace from the target's public OpenAPI document. No experiments have been run.

## 2026-08-17: Idempotency / terminal-state transition probe

**Setup note:** `workspace.json` as found used camelCase top-level keys
(`schemaVersion`, `targetAdapter`, `adapterType`, `settings`), but
`Specmine.Workspace.LoadAsync` requires the PascalCase keys `SchemaVersion`,
`TargetAdapter`, `AdapterType`, `Settings` at the top level (confirmed via a
throwaway scratch copy before touching the real file: PascalCase loaded and
connected successfully; the original camelCase threw
`InvalidDataException: ... does not declare an integer 'SchemaVersion'`).
The nested adapter settings (`document`, `baseUrl`) remained camelCase and
loaded fine either way. Corrected only the four top-level keys in
`workspace.json` in place so `Workspace.LoadAsync` /
`WorkspaceActivator.ConnectAsync` would work as documented; no other content
changed. This is an SDK/workspace-config observation, not a finding about the
target service itself.

**Question investigated:** For the Task Workflow service, is repeating the
same completing operation on an already-terminal task idempotent, and is
invoking the other completing operation on an already-terminal task a
conflict?

**Operation sequence run** (via `TraceRecorder.RunAsync` against the live
OpenAPI session, connected through `WorkspaceActivator.ConnectAsync`):
1. `Reset`
2. `CreateTask` (title "Idempotency probe task") -> task A, `pending`
3. `CompleteTask` A -> `completed`
4. `CompleteTask` A again (repeat)
5. `CancelTask` A (already `completed`)
6. `GetTask` A (confirm resting state)
7. `CreateTask` (title "Cancel idempotency probe task") -> task B, `pending`
8. `CancelTask` B -> `canceled`
9. `CancelTask` B again (repeat)

**Trace:** `traces\8680b90db2c54d92a63e20e3f0f2e988.json` (TraceId
`8680b90d-b2c5-4d92-a63e-20e3f0f2e988`, status `completed`, calls 1-9).

**Observation:**
- Call 3 -> `200`, body `status: "completed"`. Call 4 (exact repeat) -> `200`,
  identical body. Repeating `CompleteTask` on an already-completed task did
  not error and did not change the observed state.
- Call 8 -> `200`, body `status: "canceled"`. Call 9 (exact repeat) -> `200`,
  identical body. Repeating `CancelTask` on an already-canceled task did not
  error and did not change the observed state.
- Call 5 (`CancelTask` on task A, which was `completed`) -> `409`, body
  `{"code":"invalid_transition","message":"The requested task transition
  conflicts with its current status."}`. Call 6 (`GetTask` A right after)
  confirmed task A was still `status: "completed"` - the rejected cancel had
  no visible side effect.

**Limitations:** Single run, single pair of tasks; only the
complete-then-cancel direction of cross-transition conflict was exercised
(did not test `CompleteTask` on an already-canceled task, the mirrored case).
No concurrency/interleaving was tested. No assertion is made about whether
this idempotency holds for every task or is guaranteed by contract - only
that it was observed in this one sequence. See `frontier.md` for the next
questions this opens up.

## 2026-08-17: Mirror probe - CompleteTask on an already-canceled task

**Reconstructed prior state:** Loaded `frontier.md` and `journal.md` from
this workspace (fresh agent, no prior conversational context). Prior work
established (trace `8680b90db2c54d92a63e20e3f0f2e988.json`) that repeating
the same terminal operation (`CompleteTask`->`CompleteTask`,
`CancelTask`->`CancelTask`) is idempotent (200, unchanged body), and that
`CancelTask` on an already-`completed` task returns `409 invalid_transition`
with no side effect. The open question selected here, taken directly from
`frontier.md`'s "Useful next question(s)": does `CompleteTask` on an
already-`canceled` task also return 409, mirroring the observed
`completed` -> `cancel` conflict?

**Question investigated:** Is the cross-transition conflict on a terminal
task symmetric - i.e. does attempting `CompleteTask` on a task that is
already `canceled` behave the same way (409, no state change) as the
previously observed `CancelTask` on an already-`completed` task?

**Operation sequence run** (via `TraceRecorder.RunAsync`, reusing/modifying
the workspace-local `runner\Program.cs` against the live OpenAPI session):
1. `Reset`
2. `CreateTask` (title "Complete-after-cancel mirror probe task") -> task,
   `pending`
3. `CancelTask` -> `canceled`
4. `CompleteTask` on the same (already-`canceled`) task
5. `GetTask` (confirm resting state)

**Trace:** `traces\70b4478c957d409f917540296e2a4524.json` (TraceId
`70b4478c-957d-409f-9175-40296e2a4524`, status `completed`, calls 1-4 in the
recorded body; call numbering: 1=Reset, 2=CreateTask, 3=CancelTask,
4=CompleteTask, 5=GetTask).

**Observation:**
- Call 3 (`CancelTask`) -> `200`, body `status: "canceled"`, as expected.
- Call 4 (`CompleteTask` on the now-`canceled` task) -> `409`, body
  `{"code":"invalid_transition","message":"The requested task transition
  conflicts with its current status."}` - byte-for-byte the same error
  code/message shape as the earlier `CancelTask`-on-`completed` conflict.
- Call 5 (`GetTask` right after) confirmed the task remained
  `status: "canceled"` - the rejected `CompleteTask` had no observable side
  effect, mirroring the earlier finding for the opposite direction.

**Conclusion (bounded):** The cross-transition conflict on a terminal task
is symmetric in both directions observed so far: `completed` -> `cancel`
(prior trace) and `canceled` -> `complete` (this trace) both yield
`409 invalid_transition` with an unchanged, side-effect-free task state.
This still reflects only two single-run cases (one task per direction, no
repetition or concurrency), so it is an observation, not a proven
universal contract guarantee - but it is consistent with modeling
`completed` and `canceled` as mutually exclusive, permanent absorbing
states for a task's lifecycle in this service.

**Limitations:** Single run for this direction; no concurrency/interleaving
tested; did not test 404 semantics for nonexistent ids, nor whether a
second `Reset` fully clears prior tasks; did not check byte-for-byte
stability of idempotent repeats across many repetitions. See `frontier.md`
for the remaining open questions.
