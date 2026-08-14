# WalProcessCoroutines

**Experimental.** The [WalRefinement](../WalRefinement) write-ahead log, rewritten
as *processes*. The abstract specification stays a handful of **guarded atomic
actions**; the implementation becomes a set of **independently active replay
coroutines** — an external client, a request handler, a page writer, a reporter
and a recovery worker — composed by a scheduler with an explicit **server
failure domain** whose crash discards the server-domain continuations. The two
compiled graphs are then checked for refinement, exactly as in the hand-written
sample.

This sample exercises new runtime primitives layered onto
`Microsoft.Accordant.ModelChecking.Experimental.Coroutines`. That package is
unpackaged and carries no compatibility promise; see
[Model-Checking Frontends](../../docs/concepts/model-checking-frontends.md) and
the **experimental limits** section below.

```csharp
StoreRefinement
    .Build(WalConfig.Default)                 // .Map(ToStore).MapTransition(Declarations)
    .CheckTemporal(
        concreteFairness: WalFairness.Implementation,   // strong recover + strong report
        abstractFairness: WalFairness.StoreLiveness);
```

| Model | Nodes | Edges |
|---|---|---|
| process WAL (2 keys, `topup` / `swap`) | 488 | 1131 |
| atomic store | 20 | 32 |

The whole suite — safety refinement, the temporal fairness ladder, the checked
hiding claims and the runtime tests — runs in about two seconds.

```bash
cd Samples/WalProcessCoroutines
dotnet test
```

## What a process is

A **process is a replay coroutine**. Its serialized continuation — a replay
tape plus a pending checkpoint — is *control state*, and it lives in the
scheduler node configuration, **never** in the domain `[State]`. Refinement's
state semantics therefore see only durable/volatile/client fields, exactly as
they did for the hand-written WAL.

```csharp
private static async ModelTask Handler(ModelContext<WalProcessState> ctx)
{
    await ctx.When("appendable",
        s => s.Server == ServerPhase.Active && s.Request != null && !s.LogRedo);
    await ctx.Step("append-redo", WalAction.AppendRedo,
        s => { s.LogRedo = true; s.LogRecord = s.Request.Copy(); });

    await ctx.When("flushable",
        s => s.Server == ServerPhase.Active && s.LogRedo && !s.LogCommit);
    await ctx.Step("flush-commit", WalAction.FlushCommit,
        s => { s.LogCommit = true; s.Server = ServerPhase.Committed; });
    // done — folded out of the live set
}
```

The scheduler advances **every** live process against the *current* shared state
at each step. That is the one property the previous single-workflow frontend
lacked: a compiled process step is a function of the state it is applied to, so
independently active processes interleave soundly, and a guarded wait is
re-evaluated live after another process moves.

### The primitives this sample adds

| Primitive | Meaning |
|---|---|
| `ctx.When(name, s => predicate)` | Suspend until the predicate holds on the live state. Internal (no edge); once passed it is historical and never re-blocks. |
| `ctx.WaitUntil(name, ready, capture)` | The same wait, atomically capturing an immutable scalar when enabled. |
| `model.On(role, guard, workflow)` | A **guarded launch**: when `guard` holds and no instance of `role` is live, one atomic transition adds a fresh process. The no-duplicate rule uses control state the scheduler owns, so a launch cannot fire unboundedly while its guard stays true. |
| `model.WithFailureDomain(...)` | A crash (enabled while the server is up) clears volatile state **and discards every server-domain continuation**; a restart relaunches the persistent server-domain workers fresh. |

A captured local (`WaitUntil`, `Choose`, `Read`) is *intentionally historical* —
it is what the process read earlier and remembers across an `await`. Only a
still-pending guard is evaluated against live shared state. The two are never
confused: a value already on the tape is replayed verbatim; a value on the
frontier is recomputed.

## The composition root

```csharp
new ProcessSystemModel<WalProcessState>(InitialState(config))
    .AddProcess(Roles.Client,     ctx => Client(ctx, config), serverDomain: false)  // outside the domain
    .AddProcess(Roles.PageWriter, PageWriter, serverDomain: true)   // persistent server-domain workers
    .AddProcess(Roles.Reporter,   Reporter,   serverDomain: true)
    .AddProcess(Roles.Recovery,   Recovery,   serverDomain: true)
    .On(Roles.Handler,                                             // a guarded launch in the domain
        guard: s => s.Server == ServerPhase.Active && s.Request != null && !s.LogRedo,
        workflow: Handler)
    .WithFailureDomain(new FailureDomain<WalProcessState>(
        crashEnabled:   s => s.Server != ServerPhase.Down,
        onCrash:        s => s.Server = ServerPhase.Down,
        restartEnabled: s => s.Server == ServerPhase.Down,
        onRestart:      s => s.Server = ServerPhase.Recovering));
```

* The **client** is an independently active process *outside* the failure
  domain: choose a finite transaction, wait for the server to be ready, submit
  it atomically, wait for the reported outcome, and loop. It survives every
  crash with its continuation intact.
* The **request handler** is a *guarded launch* — at most one per in-flight
  transaction. It appends the whole redo record and flushes the commit record
  (the linearization point). There is **no implementation abort action**; a
  precommit crash discards the handler and recovery produces the abstract abort.
* The **page writer** waits for a committed dirty key, installs one page, and
  loops. Once every page is installed and the client no longer depends on the
  log, the same worker truncates it in one guarded atomic action.
* The **reporter** waits until a waiting client's transaction is decided, then
  delivers the outcome (reconnecting first for an abort).
* The **recovery** worker, launched fresh at each restart, reads durable state
  only: a commit record is rolled forward, its absence discards the redo record.

### Fate at a crash

| | Fields / continuations | Fate at a crash |
|---|---|---|
| durable | `Data[k]`, `LogRedo`, `LogRecord`, `LogCommit` | survives |
| volatile | `Server` phase | reset to `Down` |
| server-domain processes | handler, page writer, reporter, recovery **continuations** | **discarded** — the scheduler removes them from the live set |
| client | client process **continuation**, `Client`, `Request`, `Reported` | survives — a separate process outside the domain |

A crash is a real transition that removes continuations, not a flag that leaves
them disabled and leaking into the graph. Restart re-adds the persistent
server-domain workers with fresh (`start`) continuations, so two crash/restart
cycles land on the *same* node — there are **no unbounded crash generations**,
and the graph stays finite.

## Action identity

Three things are kept separate: the **process role** (`request-handler`), the
**semantic action** (`WalAction.FlushCommit`), and the generated runtime
step-function id (opaque). Every edge carries a typed `ProcessTransition`:

```csharp
public string ProcessRole { get; }          // "request-handler"
public bool   ServerDomain { get; }
public ProcessControlKind Control { get; }   // None | Launch | Crash | Restart | Completion
public CoroutineTransition Checkpoint { get; } // kind, stable name, value, replay prefix
public object SemanticAction { get; }        // WalAction.FlushCommit
public object Subject { get; }               // optional transaction/key
```

Refinement declarations select the **typed action** and subject directly, never
by parsing a checkpoint name, replay tape, or generated id. The single scheduler
step function is one Accordant action, so fairness is stated over
**domain-state changes** (`Fairness.Strong<WalProcessState>((a, b) => ...)`),
which is exactly Accordant's changing-edge fairness. Control-only edges (the
client's `Choose`, a launch, a completion) are state-neutral or hidden and never
carry a fairness obligation.

## The refinement

The state mapping and the transition declarations are written **entirely outside**
the process code — no coroutine carries a `.Linearizes(...)` annotation.

```text
Values[k]   = LogCommit ? LogRecord[k] : Data[k]   // what recovery would install
Phase       = Client = Idle     -> Idle
              LogCommit         -> Committed
              Server = Active   -> Pending
              otherwise         -> Aborted
Request     = the client's outstanding write set
LastOutcome = what the client was told
```

| Process transition | Store response |
|---|---|
| `submit` | `spec-submit-{txn}` |
| `flush-commit` | `spec-commit` |
| `ack-commit` / `ack-abort` | `spec-report-commit` / `spec-report-abort` |
| `append-redo`, `install-data`, `truncate-log`, `reconnect`, `recover`, `txn` choice | `Hidden` |
| launch, restart, process completion | `Hidden` |
| **crash** | **`Hidden`, or `spec-abort`** |

The last row is the point: a crash while the server holds an unflushed write
*is* the abort of that transaction — it destroys the only copy and recovery finds
no commit record. Hiding is checked, not assumed: declaring every crash hidden,
or calling the commit flush an abort, both fail with a transition mismatch
(`WalProcessRefinementTests`).

### The fairness ladder

The abstract obligation is *every submitted transaction is eventually decided and
reported*. As in the hand-written sample, the crash loop keeps that from holding
for free, and only **strong** recovery **and** strong reporting close it:

| Concrete fairness | Verdict | Why |
|---|---|---|
| none | fails | crash / restart / recover forever |
| weak recover + strong report | fails | the process crashes *during* recovery |
| strong recover + weak report | fails | the outcome is recovered and lost to the next crash |
| **strong recover + strong report** | **refines** | strong fairness needs enabledness only *infinitely often*, which the crash loop provides |

Crashing carries no fairness constraint at all: assuming it away would assume the
problem away.

## Experimental limits

* The runtime is unpackaged and prototype. The determinism audit and the other
  soundness caveats of the coroutine frontend still apply.
* Refinement is external. The scheduler is one Accordant step function, so
  fairness is expressed over domain-state changes rather than over a per-process
  step type; this is sufficient here because the recovery and report state
  changes are distinct, but it is a real constraint on what fairness can name.
* `When`/`WaitUntil` guards, `Choose` sets and `Read` values are trusted to be
  pure functions of the frozen state; the runtime checks this only under the
  opt-in determinism audit.
* One in-flight transaction and single instances of each role keep the model
  finite. The design deliberately avoids unbounded launch duplication and
  unbounded crash generations rather than bounding them after the fact.

## Files

| File | Contents |
|---|---|
| `AtomicStore.cs` | `WriteSet`, `WalConfig` (the payload) and the guarded-action `StoreState` specification |
| `WriteAheadLog.cs` | `WalProcessState`, the five processes, the guarded handler launch and the failure domain — the composition root |
| `Refinement.cs` | the state mapping, the transition declarations, and the fairness bundles |
| `ModelGraph.cs` | a breadth-first walk and live-process inspection used by the tests |
| `WalProcessRefinementTests.cs` | finiteness, safety refinement, the crash-as-abort claim, the rejected wrong declarations, at-most-one handler |
| `WalProcessLivenessTests.cs` | the temporal fairness ladder |
| `ProcessRuntimeTests.cs` | focused runtime tests: interleaving, live `When` re-evaluation, crash continuation disposal |

## Runtime extensions

The primitives live in `Microsoft.Accordant.ModelChecking.Experimental.Coroutines`:

* `CoroutineModel.cs` gains the `When` checkpoint kind and `ModelContext.When` /
  `ModelContext.WaitUntil`.
* `ProcessModel.cs` adds `ProcessSystemModel<TState>` (the composition root and
  scheduler), the `On` guarded launch, `FailureDomain<TState>`, and the
  `ProcessTransition` edge metadata. The scheduler is a single composite step
  function that owns the whole live-process set — which is what lets a crash
  discard several continuations atomically.
