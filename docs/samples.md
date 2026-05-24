# Samples

Complete working examples demonstrating Accordant features.

| Sample | Description |
|--------|-------------|
| [BankAccount](../Samples/BankAccount/) | Simple account with deposit/withdraw. Used in the [Quick Start](quickstart.md). |
| [TodoList](../Samples/TodoList/) | Basic CRUD operations for todos. Used in [Your First Spec](tutorials/01-your-first-spec.md) tutorial. |
| [TodoList-Extended](../Samples/TodoList-Extended/) | TodoList with users, multiple todos per user, and richer state modeling. |
| [TodoList-FaultInjection](../Samples/TodoList-FaultInjection/) | Demonstrates indefinite failure handling with server-side and client-side fault injection. See [Indefinite Failures](concepts/indefinite-failures.md). |
| [Booking](../Samples/Booking/) | Reservation system with availability constraints and conflict handling. |
| [JobQueue](../Samples/JobQueue/) | Async job processing with polling and state transitions. |
| [FuzzedData](../Samples/FuzzedData/) | Integration with fuzzers for randomized input generation. See [Integrating with Fuzzers](how-to/integrating-with-fuzzers.md). |
| [PetImages](../Samples/PetImages/) | File upload/download with binary data handling. |

## Running a Sample

```bash
cd Samples/<SampleName>/<SampleName>.Tests
dotnet test
```

Most samples include both an API project and a Tests project. The tests demonstrate the Accordant spec and can be run directly.
