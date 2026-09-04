# Past Roadmap

- `twf-f1` - Task lifecycle and terminal transitions.
  - Completed in this run. The final model covers task creation, retrieval, completion, cancellation, repeated completion/cancellation as idempotent retries, opposite-terminal conflicts, blank-title rejection, and not-found behavior for unknown ids. Exit decision: good enough for experiment 1; no `Unknown` or `Provisional` markers remain, and the remaining `Assume` markers only scope the adapter-generated request envelopes the feature intentionally models.
