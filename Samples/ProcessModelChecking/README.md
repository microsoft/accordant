# Structured process model checking

`ProcessSystemModel<TState>` compiles independently scheduled structured
processes and recurring atomic actions into an ordinary Accordant state graph.
Process continuation is exact graph configuration, not hidden domain state.

Run the sample:

```powershell
dotnet run --project Samples\ProcessModelChecking
```

The model is a reusable one-job slot:

* `submitter` is a guarded `RepeatedAction` that publishes work.
* `worker` uses `Forever("attempt-loop", ...)`.
* each iteration atomically claims work with `StepWhen`;
* `Call("execute-attempt", ...)` creates an explicit helper frame;
* the helper uses `ChooseStep` to select and record `Retry` or `Success` on one
  semantic edge, with no intermediate state-neutral choice configuration;
* `acknowledger` is another guarded `RepeatedAction` that frees completed work.

```csharp
var outcome = await context.Call("execute-attempt", ExecuteAttempt);

await context.Step(
    WorkAction.Settle,
    state => state.Phase = outcome == WorkOutcome.Success
        ? WorkPhase.Completed
        : WorkPhase.Pending,
    subject: "job");
```

The executable intentionally checks its complete graph at **6 exact
configurations, 7 edges, and 6 domain states**. It also prints
`ProcessGraphDiagnostics.ContinuationFormsByRole`, including the worker's root,
`foreveriteration:attempt-loop`, and `call:execute-attempt` frames; recurring
actions report `<stateless recurring action>`.

Administrative call/return and iteration reset never create graph edges.
Iteration-local continuation data is discarded when an iteration completes. State reads,
guards, and state-derived choices are evaluated only when their process is
actually scheduled, so an interleaving cannot leave a stale precomputed
checkpoint.

## Current limits

The runtime remains experimental and unpackaged. Checkpoint-history values are
restricted to the existing immutable scalar whitelist. Arbitrary foreign or
direct nested `ModelTask` awaits are unsupported; checkpoint-bearing helpers
must use `Call`. Only one failure domain is currently supported.
