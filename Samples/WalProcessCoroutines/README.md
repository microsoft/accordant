# WalProcessCoroutines

**Experimental.** A small write-ahead-log *implementation design* written as
**processes**. Two concurrent clients contend for one server; the server keeps
a durable write-ahead log; a crash can strike at any point and recovery cleans up
and reports. The whole thing is compiled to an ordinary Accordant state graph and
checked for refinement against a compact **guarded-action** specification.

The sample exercises runtime primitives layered onto
`Microsoft.Accordant.ModelChecking.Experimental.Coroutines`. That package is
unpackaged and carries no compatibility promise; see
[Model-Checking Frontends](../../docs/concepts/model-checking-frontends.md) and
the **experimental limits** section below.

## The composition root

The system reads like a small design. Two one-shot clients are registered
*outside* the failure domain (they survive crashes); the page writer, recovery
worker, and a guarded request-handler launch are registered *through* the
`server` domain object (a crash discards them):

```csharp
var model = new ProcessSystemModel<WalProcessState>(InitialState(config));

var server = model.FailureDomain(
    "server",
    crashEnabled:   s => s.Server.Mode != ServerMode.Down,
    onCrash:        s => s.Server.MarkCrashed(),
    restartEnabled: s => s.Server.Mode == ServerMode.Down,
    onRestart:      s => s.Server.MarkRecovering());

model.Process("alice", ctx => Client(ctx, config, ClientId.Alice));   // topup [1, 2]
model.Process("bob",   ctx => Client(ctx, config, ClientId.Bob));     // swap  [2, 1]

server.Process("page-writer", PageWriter);
server.Process("recovery", Recovery);
server.On("request-handler",
    guard: s => s.Server.Mode == ServerMode.Running &&
                s.Exchange.Pending != null && !s.Wal.LogRedo,
    workflow: ctx => Handler(ctx, config));
```

Two clients are enough. The capacity-one WAL serializes accepted requests, so
both admission orders — Alice→Bob and Bob→Alice — are already reachable; a third
client would only add permutations and state, not a new behavior.

Each **client is one-shot** — no `while` loop. It atomically claims the
capacity-one request slot for its fixed request, waits for its persistent reply,
and completes:

```csharp
async ModelTask Client(ModelContext<WalProcessState> ctx, WalConfig config, ClientId client)
{
    var request = config.RequestOf(client);

    await ctx.StepWhen(
        ClientAction.Submit,
        when: s => s.CanAdmit(client),                 // atomic guarded resource claim
        then: s => s.Exchange.Submit(client, request),
        subject: client);

    await ctx.When("reply-ready", s => s.Exchange.HasReply(client));
    // completes; the reply persists in the exchange as an observable result
}
```

The **handler stays blissfully sequential and unaware of crashes** — three plain
`Step`s with no guards between them:

```csharp
async ModelTask Handler(ModelContext<WalProcessState> ctx, WalConfig config)
{
    var client = await ctx.Read("owner", s => s.Exchange.Pending.Client);
    var request = config.Find(await ctx.Read("transaction", s => s.Exchange.Pending.TransactionName));

    await ctx.Step(WalAction.AppendRedo,  s => s.Wal.Append(request));
    await ctx.Step(WalAction.FlushCommit, s => s.Wal.Commit());              // linearization point
    await ctx.Step(WalAction.AckCommit,   s => s.Exchange.Publish(client, Outcome.Committed));
}
```

**Recovery captures its decision as a local**, recovers durable state, and
reports only if a request is still outstanding — with no persistent reporter and
no recovery-only shared phase:

```csharp
async ModelTask Recovery(ModelContext<WalProcessState> ctx)
{
    while (true)
    {
        await ctx.Loop("recovery-loop");
        await ctx.When("recovering", s => s.Server.Mode == ServerMode.Recovering);

        var decision = await ctx.Read("decision", s =>
            s.Exchange.Pending == null ? RecoveryDecision.None
            : s.Wal.LogCommit ? RecoveryDecision.Commit : RecoveryDecision.Abort);
        var owed = decision == RecoveryDecision.None
            ? default : await ctx.Read("owed-client", s => s.Exchange.Pending.Client);

        switch (decision)
        {
            case RecoveryDecision.None:   await ctx.Step(WalAction.Recover,   s => s.Server.MarkRunning()); break;
            case RecoveryDecision.Commit: await ctx.Step(WalAction.AckCommit, s => { s.Exchange.Publish(owed, Outcome.Committed); s.Server.MarkRunning(); }); break;
            case RecoveryDecision.Abort:  await ctx.Step(WalAction.AckAbort,  s => { s.Wal.DiscardRedo(); s.Exchange.Publish(owed, Outcome.Aborted); s.Server.MarkRunning(); }); break;
        }
    }
}
```

| Model | Nodes | Edges |
|---|---|---|
| process WAL (2 keys, two clients) | 928 | 2 505 |
| atomic store | 28 | 30 |

The whole suite — safety refinement, the temporal fairness ladder, the checked
declaration claims, the design tests, the multi-client contract tests, the
property showcase (SafeRegex, regex-prefix + temporal suffix, and direct LTL)
and the runtime tests (46 tests) — runs in a few seconds.

```bash
cd Samples/WalProcessCoroutines
dotnet test
```

## Why one request slot (and not a single-writer KV store)

Only **one transaction is admitted at a time**, but this is **not** a fundamental
single-writer key-value-store assumption. It is a bounded model of a server/WAL
implementation with **one redo record and one in-flight transaction**. The two
clients are genuinely **concurrent** and contend for admission; the capacity-one
request slot *serializes* accepted transactions, exactly as one redo record
does. There is deliberately no unbounded queue and never two simultaneous WAL
records.

`CanAdmit` is what serializes them: a client is admitted only when the server is
up, the log is clean (the previous transaction has fully drained), and the slot
is free and the client has not already been answered.

## Honest communication state

The client/server boundary is a shared **exchange** — a request/reply table (a
mailbox / external DB) that **survives every server crash**:

```csharp
[State] RequestEnvelope { ClientId Client; string TransactionName; }   // the one accepted request

[State] Exchange {
    RequestEnvelope Pending;   // capacity one — survives a crash so recovery knows who is owed
    Outcome[]       Replies;   // one persistent result per fixed client — clients are one-shot
}
```

The `Pending` row survives a server crash because it lives in the shared table,
not in server memory; it remains until the handler or recovery publishes the
reply, so recovery always knows which client is still owed a result. The
per-client `Replies` persist because clients are one-shot — a completed client
never resubmits, so its outcome must stay observable. Client *completion* is
process control (the coroutine finishing), never a shared flag.

## Encapsulated state ownership

State reads naturally through nested `[State]` objects with focused methods:

```csharp
state.Wal.Append(request);            // durable redo record
state.Wal.Commit();                   // durable commit record (the linearization point)
state.Exchange.Submit(client, req);   // claim the capacity-one slot
state.Exchange.Publish(client, res);  // persistent reply + clear the slot
state.Server.MarkCrashed();           // server lifecycle only
```

| Partition | Fields | Fate at a crash |
|---|---|---|
| `DurableWal` | `Data[k]`, `LogRedo`, `LogRecord`, `LogCommit` | **survives** |
| `Exchange` | `Pending`, `Replies` | **survives** — external/shared table |
| `ServerState` | `Mode` (`Running` / `Down` / `Recovering`) | reset to `Down` |
| server-domain continuations | handler, page writer, recovery | **discarded** |
| client continuations | the two clients | survive — outside the domain |

`WalProcessState` composes the three; the nested `[State]` objects clone, freeze,
and hash correctly (the ownership/copying tests check this).

### Method names: `Mark*` setters vs. action-like mutations

Two kinds of nested method live side by side, named on purpose:

* **`Mark*` lifecycle setters** — `Server.MarkRunning()`, `MarkCrashed()`,
  `MarkRecovering()` — only *assign* the `ServerMode` enum. They deliberately do
  **not** read like they launch behavior: the real crash/restart transition is
  owned by failure-domain scheduling, and these methods just record the mode it
  moved to.
* **Action-like storage/mailbox mutations** — `Wal.Append`, `Wal.Commit`,
  `Wal.Install`, `Wal.Truncate`, `Exchange.Submit`, `Exchange.Publish` — are the
  modeled operations themselves, so their verbs are natural. They are *not*
  mechanically prefixed with `Mark`; only the misleading lifecycle setters are.

## Minimal shared state via process locals

There is **no** `ClientPhase`, and **no** `RecoveredCommit` / `RecoveredAbort`.
The server lifecycle is just:

```csharp
enum ServerMode { Running, Down, Recovering }
```

Everything else is *derived* from durable + exchange + mode state:

```text
Pending == null                                  -> Idle
Pending != null && LogCommit                      -> Committed
Pending != null && !LogCommit && Running          -> Pending
Pending != null && !LogCommit && (Down|Recovering)-> Aborted
```

Recovery's decision (`None` / `Commit` / `Abort`) and the owed client are
captured as **immutable replay locals** *before* the state-changing step. A crash
kills those locals; the next restart recomputes them from durable state. This
preserves the earlier no-stale / no-double-report guarantee **without any
recovery-only shared phase**: because a crash killed the handler, recovery is the
one that reports, and it and the handler stay strictly alternative reporters.

Recovery completes and publishes the owed reply in **one atomic step** — rolling
an uncommitted redo back, or leaving the durable commit in place, and clearing
the slot. That atomicity is what keeps a fresh handler from ever observing a
half-rolled-back aborted transaction: the same safety the old design bought with
a recovery-only phase, here bought with atomicity instead. A crash before that
step simply retries on the next restart.

## `StepWhen` — an atomic guarded resource claim

Two clients cannot safely do a separate `When(slot-free)` then `Step(Submit)`:
both could pass the wait, one interleaves, and the other's *historical* passed
guard would overwrite `Pending`. `StepWhen` fuses the two:

```csharp
ctx.StepWhen(
    ClientAction.Submit,
    when: s => s.CanAdmit(client),                 // re-evaluated live every time
    then: s => s.Exchange.Submit(client, request), // guard + mutation are one edge
    subject: client);
```

The guard is re-evaluated against the **live** state every time the process is
considered; while it is false the process is blocked and contributes **no edge or
tape entry**; when it holds, the guard and the mutation are one visible
transition. Because no passed-guard entry is written to the tape until the step
is taken, a historical guard can never fire stale after an interleaving.

**`StepWhen` is only for claiming a shared resource** (like the request slot),
never a defensive guard between naturally sequential steps of one workflow. The
distinction between the three primitives is the whole readability point:

| Primitive | Use |
|---|---|
| `ctx.Step(action, mutation)` | plain sequential local code (the handler) — expose invalidated assumptions as bugs |
| `ctx.When(name, predicate)` | passive waiting for external state (client waiting for its reply) |
| `ctx.StepWhen(action, when, then)` | **atomic claim** of a contended shared resource (admission) |

## Action identity

Three things stay separate: the **process role** (`request-handler`), the
**typed semantic action** (`WalAction.FlushCommit`), and the opaque generated
runtime step id. The enum-named `Step`/`StepWhen` overloads derive a
collision-safe checkpoint name from the enum type and value
(`WalAction.FlushCommit`), but **fairness and refinement read the typed
`ProcessTransition.SemanticAction`, never that generated name**.

## The refinement

**The state mapping alone is the refinement mapping.** It is written entirely
outside the process code — no coroutine carries a `.Linearizes(...)` annotation —
and it is *enough*: `StoreRefinement.Build(...)` calls only `.Map(ToStore)`, with
**no `.MapTransition(...)`**, and both `.Check()` (safety) and
`.CheckTemporal(...)` (the whole fairness ladder) pass.

```text
Values[k]   = LogCommit ? LogRecord[k] : Data[k]   // what recovery would install
Phase       = derived, as above
Pending     = the Exchange's outstanding envelope (copied)
Replies     = the Exchange's per-client results (copied)
```

### Why no transition mapping is needed here

Refinement aligns each concrete transition with an abstract response — an
abstract edge, or abstract *stutter* when the concrete step does not move the
mapped store. Transition declarations only exist to **resolve ambiguity**, when
one mapped concrete transition could align with several abstract responses. This
sample has none:

* every concrete step that *changes* the mapped store changes it in a way exactly
  **one** abstract edge produces (a submit lands a distinct `Pending`; the commit
  flush is the only `Pending -> Committed`; an acknowledgement is the only
  `Committed/Aborted -> Idle` with that reply; a dooming crash is the only
  `Pending -> Aborted`), and
* every other concrete step (append-redo, install, truncate, recover, launch,
  restart, completion, and a crash outside the in-doubt window) is
  **state-neutral**, and there are **no state-neutral abstract edges** for it to
  be confused with, so it aligns with abstract stutter unambiguously.

So the abstract actions are all distinguishable by their mapped endpoints, and
`Unconstrained` (the default for any transition the mapping does not name)
infers the right response everywhere.

### The optional action declarations (a checked explanation)

`StoreRefinement.Declared(...)` layers an *optional* `.MapTransition(...)` on top
of the same state mapping. It is **not required** for the refinement to hold; it
is a secondary demonstration that *asserts* which store action each meaningful
transition performs, and those assertions are checked:

| Process transition | Declared store response |
|---|---|
| client `StepWhen` submit | `spec-submit-{client}` |
| `flush-commit` | `spec-commit` |
| `ack-commit` (handler **or** recovery) | `spec-report-commit` |
| `ack-abort` (recovery) | `spec-report-abort` |
| **crash** in the in-doubt window | **`spec-abort`** |
| **crash** outside it | **`Hidden`** (an intentional checked claim) |
| everything else | `Unconstrained` (left to inference) |

Only the crash-outside claim uses `Hidden`: hiding is a *checked assertion* that
the abstract model stands still, worth stating for a dramatic event like a crash.
The internal storage steps are left `Unconstrained` — the default inference
already aligns them with stutter, so there is nothing to assert. `Unconstrained`
means *infer any state-consistent response*; `Hidden` explicitly *requires*
abstract stutter.

Both claims are genuinely checked: declaring **every** crash hidden, or calling
the commit flush an abort, each fail with a transition mismatch — the tests keep
both wrong declarations to prove it.

The abstract spec is the matching two-client contract: one outstanding
`RequestEnvelope`, one persistent reply per client, atomic `Submit(client)`,
`Commit`, `Abort`, `ReportCommit`, `ReportAbort`, with a client whose reply is
already set unable to submit again.

### The fairness ladder

The abstract obligation is *every submitted transaction is eventually decided and
reported* — there is deliberately **no** obligation to submit, so no client is
required to win admission. The crash loop keeps that from holding for free:

| Concrete fairness | Verdict | Why |
|---|---|---|
| none | fails | crash / restart forever |
| weak recover **and** weak report | fails | a crash resets the server before either is *continuously* enabled |
| **strong recovery** (alone or bundled) | **refines** | the atomic recovery step is `Recovering -> Running`, enabled infinitely often |
| **strong report** (alone or bundled) | **refines** | the same atomic step also clears the slot |
| strong recover **and** strong report | **refines** | the implementation bundle |

Because recovery completes and publishes the owed reply in one atomic step, that
single transition is at once the server returning to `Running` and the slot being
cleared — so strong fairness on **either** characterization already closes the
crash loop. The `Implementation` bundle asks for both, to mirror the hand-written
WAL's separate recover and report obligations. The scheduler is a single
Accordant step function, so fairness is stated over **domain-state transitions**
(`Recovers` names `Recovering -> Running`; `Reports` names the slot clearing); it
is non-vacuous, and crashing itself carries no fairness constraint at all —
assuming it away would assume the problem away.

## Property showcase: LTL, SafeRegex, and regex-prefix + temporal suffix

`WalProcessProperties.cs` collects a few high-value properties in three
deliberately different shapes so the division of labour is visible. Every
"action" is observed **as the state change it makes**. The SafeRegex surface
deliberately accepts state and source/target observations rather than edge
metadata; moreover, this model's public step identity is the composite scheduler
while its semantic action is metadata. So `AppendRedo` is "the redo bit goes
`false → true`", `FlushCommit` is "the commit bit goes `false → true`", and so
on. The safe lowering then ignores every unchanged transition.

```csharp
static readonly FormulaBuilder<WalProcessState> F = Formula.For<WalProcessState>();

TransitionObservation AppendedRedo  = F.ObserveTransition((a, b) => !a.Wal.LogRedo   && b.Wal.LogRedo);
TransitionObservation FlushedCommit = F.ObserveTransition((a, b) => !a.Wal.LogCommit && b.Wal.LogCommit);
```

**1. SafeRegex ordering within an episode.** A stutter-safe regular pattern reads
like a protocol trace and is the natural way to say *this happens before that*.
Redo-before-commit as a forbidden trace prefix — *after a submit, no commit flush
with no intervening redo append*:

```csharp
F.Whenever(
    AnySteps                                       // Σ* — floats the anchor
        .Then(F.ChangingStep(AnySubmit))
        .Then(F.ChangingStep(!AppendedRedo).Star())
        .Then(F.ChangingStep(FlushedCommit)),
    F.False);                                       // …never completes
```

The same shape gives *commit-before-the-committed-reply* and, as the abort
alternative, *a client is told `Aborted` only after a `PrecommitCrash` doomed its
transaction* — together pinning each episode to the committed path or the
crash/recovery abort path.

**2. A SafeRegex prefix followed by a temporal (LTL/RLTL) suffix.** This is the
shape SafeRegex makes clearest: correlate a concrete, finite, per-client episode
with a liveness promise. *After `Submit(Alice) · … · FlushCommit(Alice)`, Alice
is eventually `Committed`* — under the same strong recover/report fairness the
refinement liveness ladder uses:

```csharp
F.Whenever(
    AnySteps.Then(F.ChangingStep(Submitted(Alice)))
            .Then(AnySteps)
            .Then(F.ChangingStep(FlushedCommit & PendingIs(Alice))),
    F.Eventually(Committed(Alice)));                // needs WalFairness.Implementation
```

Its dual correlates a precommit crash with an eventual `Aborted`. Both genuinely
need the fairness: under `Fairness.None` the crash loop starves the `◇` suffix,
and the tests assert exactly that refutation.

**3. Direct LTL.** Plain invariants and a leads-to are exactly what LTL expresses
cleanly; no pattern is needed:

```csharp
F.Always(F.Observe(s => !s.Wal.LogCommit || s.Wal.LogRedo));   // commit ⇒ redo
F.Always(F.ObserveTransition(RepliesNeverChange));             // replies are final
F.LeadsTo(PendingFor(Alice), HasReply(Alice));                 // needs fairness
```

**What each shape is for.** A durable-commit-implies-durable-redo invariant or a
replies-are-final transition invariant is a plain `□` — LTL states it directly and
a regular pattern would only obscure it. What SafeRegex adds is *ordering within
an episode* and *finite trace prefixes that trigger an obligation*: writing
"submit, then (visibly) append the redo, then flush, then acknowledge" as a
trace, and hanging a `◇` promise off the end of a matched prefix. LTL can express
these too (with nested until/next), but far less legibly.

**Non-vacuity, controls, and honest stuttering.** Every holding property is
paired with a check that keeps it from passing for free: witnesses that the
constrained events actually occur, a deliberately reversed ordering claim that is
refuted, and the `Fairness.None` refutations for the liveness suffixes. One test
proves the erasure is real: this graph has **state-neutral control transitions**
(handler launches and process completions leave the domain state unchanged), and
a handler launch sits *physically* between `Submit` and `AppendRedo`. The
SafeRegex `After`/`Whenever` compile through the erasure of unchanged steps, so
those control edges never change a verdict — while a raw, stutter-**sensitive**
`Next` counts the launch and gives a different answer. We do not claim raw regex
or raw `Next` are insensitive; only the SafeRegex wrapper and its lowering are.

## Experimental limits

* The runtime is unpackaged and prototype. The determinism audit and the other
  soundness caveats of the coroutine frontend still apply.
* Refinement is external. The scheduler is one Accordant step function, so
  fairness is expressed over domain-state changes rather than over a per-process
  step type. Here that pushed the recovery **and** report into one atomic step so
  a state predicate could name it; a two-step recover-then-report would have left
  a no-op analysis step that state-transition fairness cannot force.
* `When`/`StepWhen` guards, `Choose` sets and `Read` values are trusted to be
  pure functions of the frozen state; the runtime checks this only under the
  opt-in determinism audit. The handler and clients capture `config` by reference
  (compared by identity only) and never mutate it.
* Two fixed one-shot clients, one in-flight transaction, single instances of
  each server role and a single failure domain keep the model finite: **928
  nodes / 2 505 edges**, complete with no depth frontier. The design avoids
  unbounded launch duplication and unbounded crash generations rather than
  bounding them after the fact.

## Files

| File | Contents |
|---|---|
| `AtomicStore.cs` | `ClientId`, `RequestEnvelope`, `WriteSet`, `WalConfig` (the payload) and the guarded-action `StoreState` specification |
| `WriteAheadLog.cs` | `DurableWal`, `Exchange`, `ServerState`, `WalProcessState`, the processes, the guarded handler launch and the failure domain — the composition root |
| `Refinement.cs` | the state mapping, the optional action declarations, and the fairness bundles |
| `WalProcessProperties.cs` | the property showcase: SafeRegex ordering, regex-prefix + temporal suffix, and direct LTL, each observed as a state change |
| `ModelGraph.cs` | a breadth-first walk and live-process inspection used by the tests |
| `WalProcessRefinementTests.cs` | finiteness/size, state-only safety refinement, the optional declared refinement and the crash-as-abort claim, the rejected wrong declarations, at-most-one handler |
| `WalProcessDesignTests.cs` | structural ownership, the sequential handler order, the normal/recovery reporting split, the no-double-report guarantee, no recovery-only phase |
| `WalProcessMultiClientTests.cs` | two-client contention, no slot overwrite, one-shot completion, correct persistent replies, both admission orders, draining admits the next |
| `WalProcessPropertyTests.cs` | the showcase checks: SafeRegex ordering + witnesses, the regex-prefix/temporal-suffix episodes + fairness controls, direct LTL, and the state-neutral-erasure test |
| `WalProcessLivenessTests.cs` | the temporal fairness ladder |
| `ProcessRuntimeTests.cs` | focused runtime tests: interleaving, live `When`/`StepWhen` re-evaluation, the `StepWhen` race, structural crash continuation disposal |

## Runtime extensions

The primitives live in `Microsoft.Accordant.ModelChecking.Experimental.Coroutines`:

* `CoroutineModel.cs` adds the enum-named `Step` overload and `ctx.StepWhen(...)`,
  the atomic guarded step, alongside the existing `When` / `WaitUntil` guarded
  waits.
* `ProcessModel.cs` provides `ProcessSystemModel<TState>` (the composition root
  and scheduler), `ProcessFailureDomain<TState>` (the structural domain), and the
  `ProcessTransition` edge metadata. The scheduler is a single composite step
  function that owns the whole live-process set — which is what lets a crash
  discard several continuations atomically.
