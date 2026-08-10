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
| [WorkQueueRefinement](https://github.com/microsoft/accordant/tree/main/Samples/WorkQueueRefinement) | A leased work queue with competing workers, retries, cancellation and purging, refined against a client ledger. Combines `.Augment(...)`, `.WithWitness(...)`, `.Map(...)` and `.MapTransition(...)`, and contrasts weak and strong fairness. See [Checking Refinement](how-to/checking-refinement.md). |
| [OperationsModelChecking](https://github.com/microsoft/accordant/tree/main/Samples/OperationsModelChecking) | Compiles finite response-dependent `Operation` inputs to ordinary model-checking step functions, including `ENABLED`, fairness, and refinement. |
| [CoroutineModelChecking](https://github.com/microsoft/accordant/tree/main/Samples/CoroutineModelChecking) | **Experimental:** compiles finite replayable `async ModelTask` workflows to ordinary model-checking graph steps, and measures the replay-safety boundary the runtime can and cannot enforce. Not a supported API — see [Model-Checking Frontends](concepts/model-checking-frontends.md). |

## Running a Sample

```bash
cd Samples/<SampleName>/<SampleName>.Tests
dotnet test
```

Most samples include both an API project and a Tests project. The tests demonstrate the Accordant spec and can be run directly.
