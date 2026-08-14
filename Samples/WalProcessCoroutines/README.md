# WalProcessCoroutines

**Experimental.** The [WalRefinement](../WalRefinement) write-ahead log, rewritten
as *processes*. The abstract specification stays a handful of **guarded atomic
actions**; the implementation becomes a set of **independently active replay
coroutines** — an external client, a request handler, a page writer and a
recovery worker — composed by a scheduler with an explicit **server failure
domain** whose crash discards the domain's continuations. The two compiled
graphs are then checked for refinement, exactly as in the hand-written sample.

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
| process WAL (2 keys, `topup` / `swap`) | 568 | 1331 |
| atomic store | 20 | 32 |

The whole suite — safety refinement, the temporal fairness ladder, the checked
hiding claims, the corrected-design tests and the runtime tests (25 tests) — runs
in about two seconds.

```bash
cd Samples/WalProcessCoroutines
dotnet test
```

## The guiding principle

A local request handler should read like ordinary local implementation code,
**blissfully unaware of scheduling and crashes**:

```csharp
private static async ModelTask Handler(ModelContext<WalProcessState> ctx, WalConfig config)
{
    // An intentional local snapshot of the request, resolved through immutable
    // configuration — not a fresh read of shared state at each action.
    var requestName = await ctx.Read("request-name", s => s.Request.Name);
    var request = config.Find(requestName);

    await ctx.Step("append-redo", WalAction.AppendRedo,
        s => { s.LogRedo = true; s.LogRecord = request.Copy(); });

    await ctx.Step("flush-commit", WalAction.FlushCommit,
        s => { s.LogCommit = true; s.Server = ServerPhase.Committed; });

    await ctx.Step("ack-commit", WalAction.AckCommit,
        s => { s.Client = ClientPhase.Idle; s.Request = null; s.Reported = Outcome.Committed; });
}
```

Each `Step` is atomic and interleavings may occur between the awaits. Ordinary
interleavings are allowed; **there are no protective `When` guards restating an
expected local precondition** between naturally sequential statements. If some
other process could invalidate the next step's assumption, the model should
*expose* that as a bug — not silently prevent it. The one thing that removes this
handler is a **crash**, modelled separately: it atomically destroys the handler
continuation, so the handler never resumes after a server crash.

`When` belongs only where the implementation genuinely **waits** for external
work or state — a client waiting until the server is ready, the page writer
waiting for a committed dirty page, the recovery worker waiting until the server
is recovering — never between sequential handler statements.

## What a process is

A **process is a replay coroutine**. Its serialized continuation — a replay tape
plus a pending checkpoint — is *control state*, and it lives in the scheduler
node configuration, **never** in the domain `[State]`. Refinement's state
semantics therefore see only durable/volatile/client fields, exactly as they did
for the hand-written WAL.

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
| `model.Process(role, workflow)` | An independently active process **outside** every failure domain; it survives every crash. |
| `model.FailureDomain(name, ...)` | Registers a named failure domain and returns it. A crash (enabled while the server is up) clears volatile state **and discards every continuation registered through the domain object**; a restart relaunches the domain's persistent workers fresh. |
| `domain.Process(role, workflow)` | A persistent process **inside** the domain; discarded at a crash, relaunched at the next restart. |
| `domain.On(role, guard, workflow)` | A **guarded launch** inside the domain: when `guard` holds and no instance of `role` is live, one atomic transition adds a fresh process. The no-duplicate rule uses control state the scheduler owns, so a launch cannot fire unboundedly while its guard stays true. A crash discards it; recovery — not an automatic relaunch — replaces it. |

A captured local (`Read`, `WaitUntil`, `Choose`) is *intentionally historical* —
it is what the process read earlier and remembers across an `await`. Only a
still-pending guard is evaluated against live shared state. The two are never
confused: a value already on the tape is replayed verbatim; a value on the
frontier is recomputed. The handler carries the **transaction name** — an
immutable scalar — and re-resolves the (mutable, nested) write set through frozen
configuration, rather than retaining the write set as an untracked closure.

## Structural failure-domain ownership

Ownership is expressed **structurally**, by *where* each process is registered,
not by a boolean flag:

```csharp
var model = new ProcessSystemModel<WalProcessState>(InitialState(config));

var server = model.FailureDomain(
    "server",
    crashEnabled:   s => s.Server != ServerPhase.Down,
    onCrash:        s => s.Server = ServerPhase.Down,
    restartEnabled: s => s.Server == ServerPhase.Down,
    onRestart:      s => s.Server = ServerPhase.Recovering);

model.Process(Roles.Client, ctx => Client(ctx, config));   // outside the domain — survives
server.Process(Roles.PageWriter, PageWriter);              // inside — dies / restarts
server.Process(Roles.Recovery, Recovery);
server.On(Roles.Handler,                                   // a guarded launch in the domain
    guard: s => s.Server == ServerPhase.Active && s.Request != null && !s.LogRedo,
    workflow: ctx => Handler(ctx, config));
```

Registering through the `server` object *is* what places a process in the
domain. The failure domain has a stable name that appears on every process and
control edge (`ProcessInstance.Domain`, `ProcessTransition.Domain`).

* The **client** is an independently active process *outside* the failure
  domain: choose a finite transaction, wait for the server to be ready, submit
  it atomically, wait for the reported outcome, and loop. It survives every
  crash with its continuation intact.
* The **request handler** is a *guarded launch* — at most one per in-flight
  transaction — that reads like the sequential local code above. There is **no
  implementation abort action**; a precommit crash discards the handler and
  recovery produces the abstract abort.
* The **page writer** waits for a committed dirty key, installs one page, and
  loops. Once every page is installed and the client no longer depends on the
  log, the same worker truncates it in one guarded atomic action.
* The **recovery** worker, launched fresh at each restart, reads durable state
  only, and — because a crash kills the handler — also **replaces the killed
  handler as the reporter** (see below).

### Fate at a crash

| | Fields / continuations | Fate at a crash |
|---|---|---|
| durable | `Data[k]`, `LogRedo`, `LogRecord`, `LogCommit` | survives |
| volatile | `Server` phase | reset to `Down` |
| server-domain processes | handler, page writer, recovery **continuations** | **discarded** — the scheduler removes every continuation registered through the domain |
| client | client process **continuation**, `Client`, `Request`, `Reported` | survives — a separate process outside the domain |

A crash is a real transition that removes continuations, not a flag that leaves
them disabled and leaking into the graph. Restart re-adds the domain's
persistent workers with fresh (`start`) continuations, so two crash/restart
cycles land on the *same* node — there are **no unbounded crash generations**,
and the graph stays finite. The launched handler is **not** relaunched; recovery
replaces it.

## Normal vs recovery reporting

There is deliberately **no permanent independent reporter process** obscuring the
protocol. The normal handler reports the commit as its third sequential atomic
step. If a crash kills the handler, a fresh recovery worker reports instead:

| When the crash lands | Who reports | Outcome |
|---|---|---|
| before durable commit | recovery | determines abort, reports abort |
| after durable commit, before the report | recovery | determines committed, reports commit |
| after the report | nobody | the client is already idle; recovery says nothing |

So **either the original handler or the recovery path reports, never both** in
one surviving attempt. The report owed to a still-waiting client is captured
atomically at recover time as a recovery-only server phase (`RecoveredCommit` /
`RecoveredAbort`) that no other process ever produces — which is what keeps the
handler and recovery strictly alternative reporters even as fresh transactions
run. A crash between `recover` and the acknowledgement kills recovery too; the
next restart retries and, under strong fairness, eventually reports. A client
already reported before the crash is left idle, so recovery never reports twice.

## Action identity

Three things are kept separate: the **process role** (`request-handler`), the
**semantic action** (`WalAction.FlushCommit`), and the generated runtime
step-function id (opaque). Every edge carries a typed `ProcessTransition`:

```csharp
public string ProcessRole { get; }            // "request-handler"
public string Domain { get; }                 // "server", or null outside every domain
public ProcessControlKind Control { get; }    // None | Launch | Crash | Restart | Completion
public CoroutineTransition Checkpoint { get; }// kind, stable name, value, replay prefix
public object SemanticAction { get; }         // WalAction.FlushCommit
public object Subject { get; }                // optional transaction/key
```

Refinement declarations select the **typed action** and subject directly, never
by parsing a checkpoint name, replay tape, or generated id.

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
| `ack-commit` (handler **or** recovery) | `spec-report-commit` |
| `ack-abort` (recovery) | `spec-report-abort` |
| `append-redo`, `install-data`, `truncate-log`, `recover`, `txn` choice | `Hidden` |
| launch, restart, process completion | `Hidden` |
| **crash** | **`Hidden`, or `spec-abort`** |

The last row is the point: a crash while the server holds an unflushed write
*is* the abort of that transaction — it destroys the only copy and recovery finds
no commit record (the mapping moves `Pending -> Aborted` there). Hiding is
checked, not assumed: declaring every crash hidden, or calling the commit flush
an abort, both fail with a transition mismatch (`WalProcessRefinementTests`).

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

The scheduler is a single Accordant step function, so fairness is stated over
**domain-state transitions** — `Recovers` names *server `Recovering` → a decided
phase*, and `Reports` names *client `Waiting` → `Idle`* (the handler's or
recovery's acknowledgement). This is honest: the runtime cannot select a typed
`WalAction` at the `IStepFunction` level, so fairness names state changes rather
than a per-process action type. It is non-vacuous — each predicate matches a
specific, enabled-intermittently transition — and crashing itself carries no
fairness constraint at all: assuming it away would assume the problem away.

## Experimental limits

* The runtime is unpackaged and prototype. The determinism audit and the other
  soundness caveats of the coroutine frontend still apply.
* Refinement is external. The scheduler is one Accordant step function, so
  fairness is expressed over domain-state changes rather than over a per-process
  step type; this is sufficient here because the recovery and report state
  changes are distinct, but it is a real constraint on what fairness can name.
* `When`/`WaitUntil` guards, `Choose` sets and `Read` values are trusted to be
  pure functions of the frozen state; the runtime checks this only under the
  opt-in determinism audit. The handler captures `config` by reference (compared
  by identity only) and never mutates it.
* One in-flight transaction, single instances of each role and a single failure
  domain keep the model finite. The design deliberately avoids unbounded launch
  duplication and unbounded crash generations rather than bounding them after
  the fact.

## Files

| File | Contents |
|---|---|
| `AtomicStore.cs` | `WriteSet`, `WalConfig` (the payload) and the guarded-action `StoreState` specification |
| `WriteAheadLog.cs` | `WalProcessState`, the four processes, the guarded handler launch and the failure domain — the composition root |
| `Refinement.cs` | the state mapping, the transition declarations, and the fairness bundles |
| `ModelGraph.cs` | a breadth-first walk and live-process inspection used by the tests |
| `WalProcessRefinementTests.cs` | finiteness, safety refinement, the crash-as-abort claim, the rejected wrong declarations, at-most-one handler |
| `WalProcessDesignTests.cs` | structural ownership, the sequential handler order, the normal/recovery reporting split, and the no-double-report guarantee |
| `WalProcessLivenessTests.cs` | the temporal fairness ladder |
| `ProcessRuntimeTests.cs` | focused runtime tests: interleaving, live `When` re-evaluation, structural crash continuation disposal |

## Runtime extensions

The primitives live in `Microsoft.Accordant.ModelChecking.Experimental.Coroutines`:

* `CoroutineModel.cs` gains the `When` checkpoint kind and `ModelContext.When` /
  `ModelContext.WaitUntil`.
* `ProcessModel.cs` adds `ProcessSystemModel<TState>` (the composition root and
  scheduler), `ProcessFailureDomain<TState>` (the structural domain returned from
  `model.FailureDomain(...)`, through which processes and the `On` guarded launch
  are registered), and the `ProcessTransition` edge metadata. The scheduler is a
  single composite step function that owns the whole live-process set — which is
  what lets a crash discard several continuations atomically.
