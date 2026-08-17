# Frontier

## Question 1: Is same-transition repetition idempotent, and is cross-transition on a terminal task a hard conflict?

**Status:** Answered (bounded, single trace).

**Evidence:** `traces\8680b90db2c54d92a63e20e3f0f2e988.json` (calls 2-9) and
`traces\70b4478c957d409f917540296e2a4524.json` (calls 2-4). Journal entries
dated 2026-08-17.

**Observation (not a universal claim):** In this one run, on a task created via `CreateTask` (status `pending`):
- Calling `CompleteTask` twice in a row on the same task returned `200` both times, with the body unchanged at `status: "completed"` (call 3 vs call 4). No error or conflict was observed on the repeat.
- Calling `CancelTask` twice in a row on a different, freshly-created task (never completed) returned `200` both times, with the body unchanged at `status: "canceled"` (call 8 vs call 9). No error or conflict was observed on the repeat.
- Calling `CancelTask` on the task that was already `completed` (a *different*, terminal-to-terminal transition) returned `409` with `code: "invalid_transition"` (call 5), and a subsequent `GetTask` confirmed the task remained `status: "completed"` (call 6) - the failed cancel had no observable side effect.
- **New (mirror direction):** Calling `CompleteTask` on a task that was already `canceled` also returned `409` with the identical `code: "invalid_transition"` / message body (trace `70b4478c...`, call 3), and a subsequent `GetTask` confirmed the task remained `status: "canceled"` (call 4) - again no observable side effect from the rejected transition.

**Bounded hypothesis:** Repeating the *same* completing operation (`CompleteTask`->`CompleteTask` or `CancelTask`->`CancelTask`) on a task already in that terminal state appears idempotent (200, no state change) in this service. Invoking the *other* terminal operation once a task is already terminal is rejected with `409 invalid_transition` in **both** directions now observed: `completed` -> `cancel` (first trace) and `canceled` -> `complete` (second trace, this entry). The error body (`code`/`message`) was identical in both directions, and in neither case did the rejected call mutate the task's state. This still rests on single-run evidence per direction (one task per case, no repetition, no concurrency), so it does not establish universal guarantees, but the symmetry strengthens confidence that both terminal states (`completed`, `canceled`) are treated as mutually exclusive absorbing states with the same conflict semantics.

**Useful next question(s):**
- Is the idempotent repeat response byte-for-byte stable (e.g., same task fields) across many repetitions, or could a hidden field (timestamp/version) differ that a single JSON-body comparison would miss?
- What does `CompleteTask`/`CancelTask` on a *nonexistent* id return - 404 as declared, and does that take precedence over 409 when both a bad id and a bad transition could apply?
- Does `GetTask` on a canceled task's id ever transition or mutate anything (sanity check that reads are side-effect free), and does `Reset` fully clear tasks created before it (implicitly relied upon here but not directly verified across a second reset)?
- Does `CompleteTask` on a *pending* task that was never touched, immediately followed by `CancelTask`, behave consistently regardless of which terminal state is reached first (order-independence of the "terminal absorbing state" model), or could some other hidden state/field differ based on path taken to reach `completed`/`canceled`?
- Is there any operation in this API (none currently declared in the OpenAPI document beyond `Health`, `CreateTask`, `GetTask`, `CompleteTask`, `CancelTask`, `Reset`) that can move a task *out* of a terminal state, or are `completed`/`canceled` truly permanent for the life of the task?
