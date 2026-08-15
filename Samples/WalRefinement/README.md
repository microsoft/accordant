# WalRefinement

A single-node store with a **write-ahead log** — durable log, durable data
pages, volatile memory, crashes, restart, recovery and replay — refined against
an **atomic key-value transaction**.

The specification says one interesting thing: a transaction installs its whole
**write set** — one target value per key — in a single indivisible step. The
implementation can do nothing in a single step. It appends a redo record,
flushes a commit record, acknowledges the client, writes data pages back one at
a time, and may crash between any two of those. Refinement is the statement
that the client cannot tell.

```csharp
Refinement
    .Between<WalState, StoreState>(WriteAheadLog.Explore(config), AtomicStore.Explore(config))
    .Map(StoreRefinement.ToStore)                // what recovery would install
    .MapTransition(StoreRefinement.Declarations) // which store action each WAL action is
    .CheckTemporal(
        concreteFairness: WalFairness.Implementation,
        abstractFairness: WalFairness.StoreLiveness);
```

## The payload

A transaction is a named write set of **absolute** target values, never deltas:

```csharp
WriteSet.Of("topup", 1, 2)   // key 0 -> 1, key 1 -> 2
WriteSet.Of("swap",  2, 1)   // key 0 -> 2, key 1 -> 1
```

Two consequences. Each transaction writes something *different* to each key, so
"half of it is installed" — `[1, 0]` — is a state the model can name and reject.
And the committed store is always one of the finitely many snapshots
`WalConfig.Snapshots = { initial, topup, swap }`, so the model stays finite
although transactions repeat forever. `WriteSet` is a nested `[State]` class:
build one with `WriteSet.Of(...)`, hand it to another state with `.Copy()`, and
never modify one in place.

## The state

| | Fields | Fate at a crash |
|---|---|---|
| durable | `Data[k]`, `LogRedo`, `LogRecord`, `LogCommit` | survives |
| volatile | `Server` | lost — including the server's only copy of an in-flight write |
| client | `Client`, `Request`, `Reported` | another process, so it outlives every crash |

`ServerPhase.Active` *is* the server holding the client's write in memory. A
crash leaves that phase, so the write is gone and the transaction is doomed —
not a modelling trick, but the reason an in-doubt transaction aborts.

## The one decision

**The linearization point is the instant the commit record becomes durable.**
Everything else follows:

```text
Recovered(c, k)  =  c.LogCommit ? c.LogRecord[k] : c.Data[k]
```

`Recovered` *is* the recovery algorithm — replay a committed log over the data
pages, discard an uncommitted one — and the abstract store is defined to be its
result at **every** concrete state, whether or not a crash actually happens
there. The redo record holds the *whole* write set, so one durable commit record
decides the fate of every key at once.

That is why this sample needs no `.Augment(...)` and no `.WithWitness(...)`.
Both exist for facts the concrete state does not hold: something the past
decided and the present forgot, or something only the future reveals. A
durability contract is precisely the design decision that moves such a fact
*into durable state*, so here the abstract state is a total function of one
concrete state and the mapping is an ordinary `.Map(...)`. Choosing a
linearization point that is not durably recorded is what forces the other two
mechanisms — and `StoreRefinement.PagesOnly` shows the cost of choosing the
wrong point at all.

## The mapping

```text
Values[k]   = LogCommit ? LogRecord[k] : Data[k]   // what recovery would install
Phase       = Client = Idle     -> Idle
              LogCommit         -> Committed       // decided, and durably so
              Server = Active   -> Pending         // still in doubt
              otherwise         -> Aborted         // nobody can commit it any more
Request     = the client's outstanding write set
LastOutcome = what the client was told
```

`.Map(...)` **is** the hiding: dirty pages, the redo record, the restart and the
recovery pass are simply not carried over. The three `Phase` lines are the whole
in-doubt story — a durable commit record is irrevocable; while the server holds
the write it can still go either way; once the write is gone, because the server
aborted or crashed or is recovering, the transaction is settled as an abort
whether or not anybody has been told.

## Visible, hidden, and the crash that is both

| Implementation action | Store response |
|---|---|
| `submit-{txn}` | `spec-submit-{txn}` |
| `flush-commit` | `spec-commit` |
| `abort` | `spec-abort` |
| `ack-commit` / `ack-abort` | `spec-report-commit` / `spec-report-abort` |
| `append-redo`, `install-data-k{k}`, `truncate-log`, `restart`, `recover`, `reconnect` | `AbstractResponse.Hidden` |
| `crash` | **`Hidden`, or `spec-abort`** |

The last row is the point of the sample. A crash with nothing in doubt is
invisible: durable state is untouched, so the mapped store does not move. A
crash while the server holds an unflushed write *is* the abort of that
transaction — it destroys the only copy of the write, and recovery will find no
commit record.

Hiding is a **checked** claim, not a way to suppress a transition. Declaring
every crash hidden fails:

```text
Refinement failed: a concrete transition has no coherent abstract match.
  --crash--> ...; declared hidden (abstract stutter); candidates 0
      state-consistent abstract responses: step spec-abort
  The declaration hides this concrete transition, but the mapping does not:
  the mapped abstract state is not the one the abstract model stays at.
```

Nothing in this model *forces* a declaration: no store action is state-neutral
and no two store actions perform the same state change, so temporal alignment is
already deterministic without one. The declarations are here because they turn
"these actions are internal" from a comment into a proof obligation.

## Atomicity: what is checked, and what is definitional

That the recoverable store equals the logged write set once the commit record is
durable is *definitional* — it is how `Recovered` reads a committed log. Two
neighbouring facts are not, and those are the ones the tests assert:

```csharp
// no-steal: while no commit record is durable, the durable pages are
// already one whole snapshot, so there is nothing to undo
LogCommit || config.IsSnapshot(Data)

// only the commit-record flush ever moves the recoverable store; write-back,
// truncation, abort and recovery each have a guard that earns this
after.SequenceEqual(before) || WalStep.ActionOf(step) == WalAction.FlushCommit
```

The durable data pages satisfy neither: they really do pass through `[1, 0]` on
the way to `[1, 2]`, always under the cover of a durable commit record. Three of
the four broken variants below move the recoverable store at exactly one of
those "other" steps; the fourth tells the client about a commit the store has
not reached.

## Hidden work is not progress

Under `Fairness.None`, refinement fails with a lasso whose repeating part is
crash, restart, crash, restart, recover, … — every transition hidden:

```text
Refinement failed: a fair concrete behavior has no fair aligned abstract behavior.
Concrete behavior projected onto the abstract model by the mapping:
  --submit-swap--> ...           abstract step spec-submit-swap
  [cycle] --abort--> ...         abstract step spec-abort
  [cycle] --crash--> ...         hidden action (abstract stutter)
  [cycle] --restart--> ...       hidden action (abstract stutter)
  [cycle] --recover--> ...       hidden action (abstract stutter)
  Every concrete transition in the repeating part is hidden, so the abstract
  model stutters forever there.
  Hidden actions never discharge an abstract fairness obligation.
```

The store owes the client a report of the abort it already performed, and the
implementation crashes and restarts forever instead. Accordant adds no
divergence assumption and no implicit weak fairness on internal actions, so the
loop stays in the model until a **concrete fairness assumption** excludes it.

Abstract obligation: every submitted transaction is eventually decided and
eventually reported.

| Concrete fairness | Verdict | Why |
|---|---|---|
| none | `TemporalFairnessMismatch` | crash / restart / recover forever |
| **weak** `recover` + strong reconnect/ack | **still fails** | the process crashes *during* recovery, so analysis is never continuously enabled |
| strong `recover` + **weak** reconnect/ack | **still fails** | the outcome is recovered and lost again to the next crash |
| strong `recover` + strong reconnect/ack | **refines** | strong fairness only needs enabledness *infinitely often*, which the crash loop provides |

The rule the ladder teaches is cycle-based: **an action a crash repeatedly
disables may need strong fairness.** Three entries say what is *not* needed:

* **No fairness is needed to leave the in-doubt window.** From
  `ServerPhase.Active` the model can only commit, abort or crash, and a crash
  decides the transaction by dooming it. An infinite Accordant behavior is an
  infinite *path*, and that window contains no cycle — so, unlike a TLA+
  specification where `[Next]_vars` always permits stuttering forever, there is
  nothing to exclude. `WalFairness.Decides` exists to show that adding it
  changes no verdict.
* **No fairness is needed to restart**, because every down state has `restart`
  as its only successor; `WalFairness.Restarts` is redundant for the same
  reason. Weak fairness is evaluated over a complete cycle, not over the
  interval during which an action happens to be enabled.
* **`crash` carries no fairness constraint at all.** Crashing is not something
  the implementation must do; assuming it away would assume the problem away.

## The broken variants

Each is one flag on `WalOptions`, each is a well-known failure mode of
crash-recovery storage, and each fails with a short counterexample.

| Flag | The bug | Diagnosed at |
|---|---|---|
| `InstallUncommittedPages` | violates no-steal by writing an uncommitted page although there is no undo record | `install-data-k0`: the recoverable store is `[1, 0]`, no write set at all, while the transaction is still `Pending` |
| `AckBeforeCommitIsDurable` | acknowledge on the redo flush | `ack-commit`: the client is told `Committed` while the mapped store is still `Pending` |
| `RecoveryIgnoresCommitRecord` | recovery rolls back unconditionally | `recover`, after a genuine crash and restart: the store goes `Committed → Aborted`, and no specification action un-commits |
| `TruncateBeforeAcknowledgement` | reclaim the log before the outcome is delivered | `truncate-log`: the commit record was the last durable evidence, so the store drops to `Aborted` while a client waits |

The first is also visible without refinement at all, as a violation of
`□ IsSnapshot(Recovered)`. A fifth failure is a broken **mapping** rather than a
broken implementation: `StoreRefinement.PagesOnly` reads the data pages instead
of the recovered store, which puts the linearization point at the write-back and
fails at `flush-commit`, where the specification commits and the mapped values
have not moved.

## Accordant and TLA+

| TLA+ | Here |
|---|---|
| `Spec` and `Impl` as state machines | two explored `StateGraphNode` models |
| a constant set of actions | every `Step<TState>` re-emits itself, so the action set never changes |
| `INSTANCE Spec WITH values <- Recovered(...)` | `.Map(wal => new StoreState { ... })` |
| `Impl => Spec` (a theorem) | `Check()` — every concrete edge is one abstract edge or abstract stutter |
| stuttering steps of the refined spec | `AbstractResponse.Hidden` — checked, never assumed |
| `WF_vars(A)` / `SF_vars(A)` | `Fairness.Weak(...)` / `Fairness.Strong(...)`, over changing edges |
| `Impl /\ Fairness => Spec /\ Fairness` | `CheckTemporal(concreteFairness, abstractFairness)` |
| auxiliary (history / prophecy) variables | `.Augment(...)` / `.WithWitness(...)` — **not needed here** |

Two differences are worth knowing. Accordant's mapping is state-valued and
step-aligned: one concrete edge matches one abstract edge or abstract stutter,
never a finite abstract path. And an infinite behavior is an infinite path in
the graph rather than a behavior over `[Next]_vars`, so a state with outgoing
actions cannot simply stutter forever.

## Files

Actions are declared inline — a stable id, a guard, one atomic mutation — and
tagged with a `WalAction` or `StoreAction`, so fairness bundles and the
transition mapping select them by name rather than by class.

| File | Contents |
|---|---|
| `Step.cs` | `Step<TState>`: how an action is declared, and how its id is derived |
| `AtomicStore.cs` | `WriteSet` and `WalConfig` — the payload — plus `StoreState` and the five specification actions |
| `WriteAheadLog.cs` | `WalState`, `Recovered(...)`, the twelve implementation actions grouped by client / log / data pages / crash and recovery, and `WalOptions` |
| `Refinement.cs` | the state mapping, the action declarations, the deliberately wrong versions of both, and the fairness bundles |
| `WalRefinementTests.cs` | safety refinement, the protocol invariants, what is hidden, the four broken implementations, the broken mapping, sizes |
| `WalLivenessTests.cs` | the crash-loop counterexample and the weak/strong fairness ladder |
| `ModelGraph.cs` | a breadth-first walk used by the tests |

| Model | Nodes | Edges |
|---|---|---|
| WAL implementation (2 keys, `topup` / `swap`) | 150 | 292 |
| atomic store | 20 | 32 |
| implementation with a third write set (`clear = [0, 0]`) | 219 | 433 |
| implementation with a third key | 198 | 396 |

With no auxiliary state and no witnesses there is exactly one proof
configuration per concrete state, so the mapping is invoked 150 times. The whole
suite, including every broken variant and the temporal ladder, runs in about a
second.

```bash
cd Samples/WalRefinement
dotnet test
```
