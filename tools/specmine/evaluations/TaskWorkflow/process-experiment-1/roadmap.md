# Roadmap

Self-review note: `PROCESS.md` expects human curation before per-feature work. No human was available in this run, so I self-reviewed the proposed feature list, logged that fact in `journal.md`, and proceeded without blocking.

## Locked feature list for process experiment 1

- `twf-f1` - Task lifecycle and terminal transitions.
  - Scope: `CreateTask`, `GetTask`, `CompleteTask`, `CancelTask`, plus the validation/error branches needed to explain the observed lifecycle behavior honestly.
  - Why this is one feature: the create/get/complete/cancel rules are tightly coupled through task identity and terminal state handling.

## Active entries

_None._ `twf-f1` was completed in this run and moved to `past-roadmap.md`.
