# WalRefinement

A single-node store with a **write-ahead log** — durable log, durable data
pages, volatile memory, crashes, restart, recovery and replay — checked
against an **atomic key-value transaction**.

The specification has exactly one interesting property: a transaction writes
every key in one indivisible step. The implementation cannot do anything in
one step. It appends a redo record, flushes a commit record, acknowledges the
client, writes data pages back one at a time, and may crash between any two of
those. Refinement is the statement that the client cannot tell.

```csharp
Refinement
    .Between<WalState, StoreState>(WriteAheadLog.Explore(config), TransactionStore.Explore(config))
    .Map(WalRefinementCheck.MapToStore)
    .MapTransition(WalRefinementCheck.StoreActions)
    .CheckTemporal(
        concreteFairness: WalFairness.Implementation,
        abstractFairness: WalFairness.StoreLiveness);
```

## The durability contract

**The linearization point is the instant the commit record becomes durable.**
Everything follows from that one choice:

```text
Recovered(c, k)  =  c.LogCommit ? c.LogValue : c.Data[k]
```

`Recovered` *is* the recovery algorithm — replay a committed log over the data
pages, discard an uncommitted one — and the refinement mapping is defined to be
its result. The abstract store is therefore **the store a recovery would
install right now**, at every single concrete state, whether or not a crash
actually happens there.

That is why this sample needs no `.Augment(...)` and no `.WithWitness(...)`.
Both exist for facts the concrete state does not hold: something the past
decided and the present forgot, or something only the future reveals. A
durability contract is precisely the design decision that moves such a fact
*into durable state*, so here the abstract state is a total function of one
concrete state and the mapping is an ordinary `.Map(...)`. Choosing a
linearization point that is **not** durably recorded is what forces the other
two mechanisms — and the sample shows the cost of choosing the wrong point at
all (`MapDurablePagesOnly`, below).

## The two models

**`StoreState` — the specification.** One in-flight transaction, `Keys = 2`,
values `{0, 1}`.

```text
Idle --spec-submit-v{v}--> Pending --spec-commit--> Committed --spec-report-commit--> Idle
                                   \--spec-abort--> Aborted  --spec-report-abort---> Idle
```

`spec-commit` sets **every** key to `v` at once. `Committed` and `Aborted` mean
"decided, not yet reported"; the report is what the client observes, and
`LastOutcome` records it. No reachable specification state has keys that
disagree — the tests assert that, and it is the property the implementation has
to earn.

**`WalState` — the implementation.** Twelve actions over three kinds of state:

| | Fields | Fate at a crash |
|---|---|---|
| durable | `Data[k]`, `LogRedo`, `LogValue`, `LogCommit` | survives |
| volatile | `Server` | lost |
| client | `Client`, `Request`, `Reported` | external process, survives |

The server's in-memory copy of the client's write is modeled by
`ServerPhase.Active`: a crash leaves that phase, so the write is gone and the
transaction is doomed. That is not a modelling trick, it is the reason an
in-doubt transaction aborts.

| Action | What it does |
|---|---|
| `submit-v{v}` | the client asks for a transaction; the server takes it into volatile memory |
| `append-redo` | the redo record becomes durable |
| `flush-commit` | **the commit record becomes durable** — guarded by `LogRedo`, the force-at-commit rule |
| `ack-commit` / `ack-abort` | the outcome is delivered to the client |
| `install-data-k{k}` | one page is written back; also the redo replay during recovery |
| `truncate-log` | the log is reclaimed once installed *and* acknowledged |
| `abort-txn` | the server rolls an in-doubt transaction back |
| `crash` / `restart` / `recover` | the process dies, comes back, and inspects only the durable log |
| `reconnect-client` | the waiting client reconnects after recovery discarded an uncommitted request |

`recover` reads nothing but the durable log: a commit record rolls forward;
its absence discards the redo record and returns the server to idle.
`reconnect-client` separately restores the volatile client/server knowledge
needed to report that an uncommitted request aborted. Roll-forward does not
replay anything itself — it hands the work to the ordinary
`install-data-k{k}` action, which is idempotent, so crashing in the middle of a
replay is harmless and is not a special case anywhere in the model.

The correct model is deliberately **no-steal**: an uncommitted page is never
written to durable data, because this redo-only design has no undo record.
The redo record is forced before the commit record, and data-page write-back
starts only after that commit record is durable.

## The mapping

```csharp
Values[k]   = LogCommit ? LogValue : Data[k]     // what recovery would install
Phase       = Client = Idle    -> Idle
              LogCommit        -> Committed      // decided, and durably so
              Server = Active  -> Pending        // still in doubt
              otherwise        -> Aborted        // nobody can commit it any more
Request     = the client's outstanding request
LastOutcome = what the client was told
```

`.Map(...)` **is** the hiding: dirty pages, the redo record, the restart and
the recovery pass are simply not carried over, so they are invisible by
construction. The three lines of `Phase` are the whole in-doubt story:

* a durable commit record is irrevocable — nothing in the implementation moves
  the store back;
* while the server holds the write it can still go either way;
* once the write is gone — because the server aborted, or crashed, or is
  recovering — the transaction is settled as an abort, whether or not anybody
  has been told yet.

## Visible steps, hidden steps, and the crash that is neither

`.MapTransition(WalRefinementCheck.StoreActions)` declares what each
implementation action is abstractly:

| Implementation action | Store response |
|---|---|
| `submit-v{v}` | `spec-submit-v{v}` |
| `flush-commit` | `spec-commit` |
| `abort-txn` | `spec-abort` |
| `ack-commit` / `ack-abort` | `spec-report-commit` / `spec-report-abort` |
| `append-redo`, `install-data-k{k}`, `truncate-log`, `restart`, `recover`, `reconnect-client` | `AbstractResponse.Hidden` |
| `crash` | **`Hidden`, or `spec-abort`** |

The last row is the point of the sample. A crash with nothing in doubt is
invisible — durable state is untouched, so the mapped store does not move. A
crash while the server holds an unflushed write *is* the abort of that
transaction: it destroys the only copy of the write, and recovery will find no
commit record.

`WalProjectionTests.DeclaringEveryCrashHiddenIsAMismatch` pins that down.
Declaring every crash hidden fails, because hiding is a **checked** claim:

```text
Refinement failed: a concrete transition has no coherent abstract match.
  --crash--> ...; declared hidden (abstract stutter); candidates 0
      state-consistent abstract responses: step spec-abort
  The declaration hides this concrete transition, but the mapping does not:
  the mapped abstract state is not the one the abstract model stays at.
```

Nothing in this model *forces* a declaration: no store action is state-neutral
and no two store actions perform the same state change, so temporal alignment
is already deterministic without one (`DeclarationsAreOptionalForAlignmentAndStillWorthMaking`).
The declarations are here because they turn "these actions are internal" from a
comment into a proof obligation.

## Hidden work is not progress

Under `Fairness.None`, refinement fails with a lasso whose repeating part is
crash, restart, crash, restart, recover, … — every transition hidden:

```text
Refinement failed: a fair concrete behavior has no fair aligned abstract behavior.
Concrete behavior projected onto the abstract model by the mapping:
  start ...
  --submit-v0--> ...            abstract step spec-submit-v0
  [cycle] --abort-txn--> ...    abstract step spec-abort
  [cycle] --crash--> ...        hidden action (abstract stutter)
  [cycle] --restart--> ...      hidden action (abstract stutter)
  [cycle] --crash--> ...        hidden action (abstract stutter)
  [cycle] --restart--> ...      hidden action (abstract stutter)
  [cycle] --recover--> ...      hidden action (abstract stutter)
  Every concrete transition in the repeating part is hidden, so the abstract
  model stutters forever there.
  Hidden actions never discharge an abstract fairness obligation.
```

The store owes the client a report of the abort it already performed, and the
implementation crashes and restarts forever instead. An infinite crash loop is
a real behavior of a real system. Accordant adds no divergence assumption and
no implicit weak fairness on internal actions, so the loop stays in the model
until a **concrete fairness assumption** excludes it.

## The fairness ladder

Abstract obligation: every submitted transaction is eventually decided and
eventually reported (weak fairness on the four specification actions).

| Concrete fairness | Verdict | Why |
|---|---|---|
| none | `TemporalFairnessMismatch` | crash / restart / recover forever |
| **weak** `recover` + strong reconnect/report | **still fails** | the process crashes *during* recovery, so analysis is never continuously enabled |
| strong `recover` + **weak** reconnect/report | **still fails** | the process reconnects or recovers and loses the opportunity to the next crash |
| strong `recover` + strong reconnect/report | **refines** | strong fairness only needs enabledness *infinitely often*, which the crash loop provides |

The rule the ladder teaches is cycle-based: **an action a crash repeatedly
disables may need strong fairness**, because it is enabled infinitely often
without being continuously enabled around the cycle. Recovery, client
reconnection and acknowledgements have that shape.

Restart is different for a structural reason, not because of weak fairness:
every down state has exactly one successor, `restart`. No infinite graph path
can remain down. `WalFairness.Restarts` is intentionally redundant, and a test
shows that adding it changes no verdict. Weak fairness is evaluated over a
complete cycle, not merely over the interval during which an action happens to
be enabled.

Two more entries are worth stating because they say what is *not* needed:

* **No fairness at all is needed to leave the in-doubt window.** From
  `ServerPhase.Active` the model can only commit, abort or crash, and a crash
  decides the transaction by dooming it. An infinite Accordant behavior is an
  infinite *path*, and the in-doubt window contains no cycle, so — unlike a
  TLA+ specification, where `[Next]_vars` always permits stuttering forever —
  there is no "the server sits there and does nothing" behavior to exclude.
  `WalFairness.Decides` exists to make the point that adding it changes
  nothing.
* **`crash` carries no fairness constraint.** Crashing is not something the
  implementation must do; assuming it away would be assuming the problem away.

## Four broken implementations

Each is one flag on `WalOptions`, each is a well-known failure mode of
crash-recovery storage, and each fails with a counterexample of at most six
steps.

| Flag | The bug | Diagnosed at |
|---|---|---|
| `InstallUncommittedPages` | violates no-steal by writing an uncommitted page despite having no undo record | `install-data-k0`: the recoverable store is torn, `Values=[1, 0]`, while the transaction is still `Pending` |
| `AckBeforeCommitIsDurable` | acknowledge on the redo flush | `ack-commit`: the client is told `Committed` while the mapped store is still `Pending`; the specification has no such action |
| `RecoveryIgnoresCommitRecord` | recovery rolls back unconditionally | `recover`, after a genuine crash and restart: the mapped store goes `Committed → Aborted`, and no specification action un-commits |
| `TruncateBeforeAcknowledgement` | reclaim the log before the outcome is delivered | `truncate-log`: the commit record was the last durable evidence, so the mapped store drops to `Aborted` while a client is still waiting |

The first one is also visible without refinement at all, as a plain invariant
violation of `□ (Recovered(k0) = Recovered(k1))`, and the test asserts both.

A fifth failure is a broken **mapping** rather than a broken implementation:
`MapDurablePagesOnly` reads the data pages instead of the recovered store,
which puts the linearization point at the write-back. It fails at
`flush-commit`, where the specification commits and the mapped values have not
moved.

## Relationship to TLA+ refinement mappings

| TLA+ | Here |
|---|---|
| `Spec` and `Impl` as state machines | two explored `StateGraphNode` models |
| `INSTANCE Spec WITH values <- Recovered(...)` | `.Map(concrete => new StoreState { ... })` |
| `Impl => Spec` (a theorem) | `Check()` — every concrete edge is one abstract edge or abstract stutter |
| stuttering steps of the refined spec | `AbstractResponse.Hidden` / `Stutter` — checked, never assumed |
| `WF_vars(A)` / `SF_vars(A)` | `Fairness.Weak(...)` / `Fairness.Strong(...)`, over changing edges |
| `Impl /\ Fairness => Spec /\ Fairness` | `CheckTemporal(concreteFairness, abstractFairness)` |
| auxiliary (history / prophecy) variables | `.Augment(...)` / `.WithWitness(...)` — **not needed here** |

Two differences are worth knowing. Accordant's mapping is state-valued and
step-aligned: one concrete edge matches one abstract edge or abstract stutter,
never a finite abstract path. And an infinite behavior is an infinite path in
the graph rather than a behavior over `[Next]_vars`, so a state that has
outgoing actions cannot simply stutter forever — which is why this model needs
no fairness assumption to leave the in-doubt window.

## Measured sizes

| Model | Nodes | Edges |
|---|---|---|
| WAL implementation (2 keys, values `{0,1}`) | 95 | 184 |
| atomic store | 15 | 24 |
| refinement proof configurations | 95 | — |
| implementation with a third value | 219 | 433 |
| implementation with a third key | 143 | 288 |

With no auxiliary state and no witnesses there is exactly one proof
configuration per concrete state, so the mapping is invoked 95 times. The whole
suite, including every broken variant and the temporal ladder, runs in about a
second, and eager and lazy exploration are checked to agree.

## Running

```bash
cd Samples/WalRefinement
dotnet test
```

## Files

| File | Contents |
|---|---|
| `TransactionStore.cs` | `WalConfig`, `StoreState` and the five specification actions |
| `WriteAheadLog.cs` | `WalState`, the twelve implementation actions, `Recovered(...)`, and `WalOptions` — the four broken variants |
| `WalRefinementCheck.cs` | the mapping, the action declarations, the broken declarations, and the fairness bundles |
| `ModelGraph.cs` | a breadth-first walk used by the tests |
| `WalRefinementTests.cs` | safety refinement, graph invariants, the four broken implementations, the broken mapping, eager/lazy parity, sizes |
| `WalTemporalFairnessTests.cs` | the crash-loop counterexample and the weak/strong fairness ladder |
| `WalProjectionTests.cs` | which actions are hidden, the checked-hiding failure, and the projected trace |
