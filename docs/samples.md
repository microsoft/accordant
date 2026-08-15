# Samples

Complete working examples demonstrating Accordant features.

| Sample | Description |
|--------|-------------|
| [BankAccount](https://github.com/microsoft/accordant/tree/main/Samples/BankAccount) | Simple account with deposit/withdraw. Used in the [Overview](index.md). |
| [TodoList](https://github.com/microsoft/accordant/tree/main/Samples/TodoList) | Basic CRUD operations for todos. Used in [Your First Spec](tutorials/01-your-first-spec.md) tutorial. |
| [TodoList-Extended](https://github.com/microsoft/accordant/tree/main/Samples/TodoList-Extended) | Demonstrates response-dependent state (server timestamps) and server-generated IDs with request derivations. |
| [TodoList-FaultInjection](https://github.com/microsoft/accordant/tree/main/Samples/TodoList-FaultInjection) | Demonstrates indefinite failure handling with server-side and client-side fault injection. See [Indefinite Failures](how-to/indefinite-failures.md). |
| [Booking](https://github.com/microsoft/accordant/tree/main/Samples/Booking) | Demonstrates concurrency testing — the "double-booking" scenario where two customers try to book the same slot. |
| [JobQueue](https://github.com/microsoft/accordant/tree/main/Samples/JobQueue) | Demonstrates async operations with step functions, polling for completion, and server-generated result paths. |
| [DurableJobs](https://github.com/microsoft/accordant/tree/main/Samples/DurableJobs) | A complete contract/design/implementation case study: one atomic durable-job contract, checked safety and action-aware fair liveness, a structured process design using `Forever`, `Call`, recurring actions, and a worker failure domain, direct refinement, a structurally mirrored thread-safe C# service, and generated sequential/concurrent conformance. |
| [WorkQueueRefinement](https://github.com/microsoft/accordant/tree/main/Samples/WorkQueueRefinement) | A leased work queue with competing workers, retries, cancellation and purging, refined against a client ledger. Combines `.Augment(...)`, `.WithWitness(...)`, `.Map(...)` and `.MapTransition(...)`, and contrasts weak and strong fairness. See [Checking Refinement](how-to/checking-refinement.md). |
| [WalRefinement](https://github.com/microsoft/accordant/tree/main/Samples/WalRefinement) | A write-ahead log with durable and volatile state, crashes, restart, recovery and replay, refined against an atomic key-value transaction that installs a whole per-key write set in one step. The commit-record flush is the linearization point, so the mapping is a plain `.Map(...)` over durable state; `.MapTransition(...)` declares which implementation actions the store hides, and which store action a crash performs when it dooms an in-flight transaction. Four deliberately broken implementations, a broken mapping, and a crash-loop fairness ladder. |
| [WalProcessCoroutines](https://github.com/microsoft/accordant/tree/main/Samples/WalProcessCoroutines) | **Experimental:** a write-ahead log written as structured processes — two external clients, sequential handler/page-writer/recovery workflows, and a server failure domain whose crash discards owned frames. Uses `Forever`, `Call`, `StepWhen`, typed `ProcessTransition` actions, per-client action fairness, exact graph diagnostics (427 configurations / 1,094 edges / 174 domain states), and direct refinement to the guarded-action atomic store. |
| [OperationsModelChecking](https://github.com/microsoft/accordant/tree/main/Samples/OperationsModelChecking) | Compiles finite response-dependent `Operation` inputs to ordinary model-checking step functions, including `ENABLED`, fairness, and refinement. |
| [ProcessModelChecking](https://github.com/microsoft/accordant/tree/main/Samples/ProcessModelChecking) | **Experimental:** a focused structured-process model demonstrating `Forever`, `Call`, `ChooseStep`, `RepeatedAction`, typed semantic metadata, exact configurations, and `ProcessGraphDiagnostics`. |
| [OrderFulfillment](https://github.com/microsoft/accordant/tree/main/Samples/OrderFulfillment) | Model checks an implementation-shaped web application through hand-written steps, the `Operation` adapter, and a fully composed structured `ProcessSystemModel`. The process gateway uses atomic `ChooseStep`, making the strong external-outcome action fairness explicit while preserving safety, liveness, projected behavior, and refinement. |

## Running a Sample

```bash
cd Samples/<SampleName>/<SampleName>.Tests
dotnet test
```

Most samples include both an API project and a Tests project. The tests demonstrate the Accordant spec and can be run directly.
