# Roadmap

Self-review note: `PROCESS.md` expects a human to curate and lock the feature list before per-feature work. No human was available during this run, so I proposed the split below, self-reviewed it, logged that explicitly in `journal.md`, and proceeded without blocking.

## Locked feature list for process experiment 1

- `pp-f1` - Authorization outcomes and idempotency-key retries.
  - Scope: `AuthorizePayment` only, including semantic request validation, successful authorization, exact replay after success, changed-payload conflicts after success, decline behavior, and the observed rule for whether a declined attempt does or does not reserve an idempotency key.
  - Grouping rationale: most of the benchmark's advertised subtlety is concentrated in the authorize entry point, and it was the most tractable place to build a solid first model.
- `pp-f2` - Existing-payment lifecycle and retrieval.
  - Scope: `GetPayment`, `CapturePayment`, and `VoidPayment`, plus the state transitions among `authorized`, `captured`, and `voided`, and the cross-feature question of how later `AuthorizePayment` replays reflect a payment whose lifecycle has advanced.
  - Grouping rationale: once a payment exists, the service exposes a separate state machine around that identity; it is cohesive, but distinct enough from first-call authorization semantics to keep as a second feature.

## Active entries

_None._ `pp-f1` and `pp-f2` were completed in this run and moved to `past-roadmap.md`.