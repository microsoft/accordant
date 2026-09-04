# Investigation process: Features, Roadmap, Past Roadmap, Journal

This document describes a lightweight process for incrementally building a partial
Accordant model of a black-box target system, in bounded, resumable slices - by a
human, an AI agent, or an orchestrator coordinating sub-agents. It is intentionally
informal and thin: the goal is to make an overwhelming "model this whole API"
problem into a sequence of small, checkable steps, not to impose heavy process for
its own sake.

This is a process description, not (yet) a schema `Workspace` enforces. It builds
on the existing building blocks documented elsewhere in this README - the
adapter/session SDK, `TraceRecorder`/`TraceStore`, the OpenAPI adapter, and
`Understanding.Assume`/`Unknown`/`Provisional` - without changing any of them.

## Terms

- **Contracts** - the raw request/response shapes for the whole target surface.
  For a service that already publishes something like an OpenAPI document (as
  every benchmark in this repo does), Contracts is mostly *sourced*, not
  authored: point at/cache the existing document rather than re-deriving it by
  hand. Only a target with no such document needs actual exploratory mapping.
  Contracts sits below Features - it is ground truth about what operations and
  fields exist, independent of how they get grouped.

- **Feature** - an end-user-meaningful grouping of operations (borrowed from the
  BDD sense of the word: a cohesive unit of user-visible functionality). A
  Feature's boundary is deliberately allowed to be ill-defined - "a bunch of
  operations, some subset of request fields, similar responses" is a fine
  starting definition. Features are proposed after a first pass over Contracts,
  then human-curated (or human-guided) and locked in before per-feature work
  starts. Mapping every request/response field is the eventual goal for a
  Feature, not a precondition to start one - `Assume`/`Unknown`/`Provisional`
  exist precisely so incomplete coverage can be stated honestly while work is
  in progress.

- **Roadmap** - one active entry per Feature not yet finished. Each entry has:
  - an id and short description;
  - a **bounded**, free-form current note (a sentence or two) - mutable while
    the Feature is being worked, describing what's going on / what's next.

  There is deliberately no separate status field beyond "which list is this
  entry in" (Roadmap vs. Past Roadmap - see below). Finer distinctions like
  "not started yet" vs. "actively being worked" are transient working-memory
  for whichever orchestrator/agent picks up the Roadmap next; they are not
  worth persisting.

- **Past Roadmap** - an immutable archive. When a Feature is done, its Roadmap
  entry moves here, carrying its final bounded note forward as a completion
  summary. Past Roadmap entries are never edited afterward. Revisiting a
  finished Feature later does not reopen or mutate its Past Roadmap entry -
  it creates a **new** Roadmap entry (optionally referencing the old Past
  Roadmap entry's id, so the history stays traceable), and that new entry goes
  through the same one-attempt lifecycle again.

- **Journal** - an append-only, unbounded log of everything tried and observed,
  across the whole investigation (not scoped to one Feature). It is a forensic
  record, not something a future agent is expected to re-read as a source of
  direction - Roadmap/Past Roadmap's bounded notes are what carry that forward.
  Past hypotheses and raw observations that haven't yet been promoted into a
  real `Assume`/`Unknown`/`Provisional` marker in the model are welcome to just
  live here as ordinary entries.

There is no separate persisted "Frontier" artifact. A catalog of open/unknown
questions is mostly derivable on demand by scanning the model source for
`Understanding.Assume`/`Unknown`/`Provisional` call sites (each already carries an
`id` and `reason`/`question`) and, optionally, cross-referencing which ones a
`TraceReplayer` run actually hit. A persisted, hand-maintained frontier file
would just be a second copy of that same information, prone to drifting out of
sync with the model.

## Bootstrapping (one time)

1. Build **Contracts** for the target - reuse an existing OpenAPI document if
   the target publishes one; otherwise explore and record request/response
   shapes by hand.
2. Propose a candidate **Feature** list from Contracts.
3. A human reviews/curates the proposal and locks in the initial **Roadmap**.

## Per-Feature loop

Each Roadmap entry is picked up once and worked in a single continuous attempt
until exit - not revisited piecemeal across many disconnected slices:

1. Pick the next Feature off the Roadmap.
2. Run experiments against the target (via the adapter/session SDK), recording
   traces with `TraceRecorder`.
3. Incrementally build/refine the Accordant model for the Feature's operations.
   Use `Understanding.Assume` to state which requests/states the model actually
   claims to cover; use `Understanding.Unknown`/`Provisional` to state honestly
   which response outcomes aren't characterized yet, rather than guessing or
   silently narrowing scope.
4. Replay recorded traces (`TraceReplayer.Replay`) to check the model against
   real observed behavior; refine and repeat.
5. Append Journal entries along the way describing what was tried and learned -
   this is the detailed record; it does not need to be curated.
6. Decide exit criteria. A reasonable default: no remaining `Unknown` markers
   for the Feature's operations (or each remaining one is an explicit, reviewed
   decision to waive it), and the fields the Feature set out to cover are each
   either exercised or deliberately `Assume`d out of scope. A pragmatic
   "good enough, stopping here" override is always allowed - completeness may
   never fully arrive, and that's fine.
7. On exit: write the final bounded note, move the entry from Roadmap to Past
   Roadmap, and pick the next Feature.

## What this does not change

`Workspace`'s current fixed artifact set (`workspace.json`, `target/`,
`traces/`, `frontier.md`, `journal.md`) is untouched by this document. This is
a process description to be exercised experimentally first; if it proves out,
a follow-up would evolve `Workspace` to match (e.g. replacing `frontier.md`
with `roadmap.md`/`past-roadmap.md`, keeping `journal.md`).
