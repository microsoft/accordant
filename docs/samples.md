# Samples

Complete working examples demonstrating Accordant features.

| Sample | Description |
|--------|-------------|
| [BankAccount](../Samples/BankAccount/) | Simple account with deposit/withdraw. Used in the [Quick Start](quickstart.md). |
| [TodoList](../Samples/TodoList/) | Basic CRUD operations for todos. Used in [Your First Spec](tutorials/01-your-first-spec.md) tutorial. |
| [TodoList-Extended](../Samples/TodoList-Extended/) | Demonstrates response-dependent state (server timestamps) and server-generated IDs with request derivations. |
| [TodoList-FaultInjection](../Samples/TodoList-FaultInjection/) | Demonstrates indefinite failure handling with server-side and client-side fault injection. See [Indefinite Failures](concepts/indefinite-failures.md). |
| [Booking](../Samples/Booking/) | Demonstrates concurrency testing — the "double-booking" scenario where two customers try to book the same slot. |
| [JobQueue](../Samples/JobQueue/) | Demonstrates async operations with step functions, polling for completion, and server-generated result paths. |

## Running a Sample

```bash
cd Samples/<SampleName>/<SampleName>.Tests
dotnet test
```

Most samples include both an API project and a Tests project. The tests demonstrate the Accordant spec and can be run directly.
