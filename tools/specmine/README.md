# Specmine

`specmine` is the working name for experimental specification-mining tooling.
The initial benchmark services provide small systems with known semantics that
an investigator can explore through externally visible operations.

## Benchmarks

| Benchmark | Behavioral focus |
| --- | --- |
| `TaskWorkflow` | Server-generated IDs, terminal states, and idempotent retries |
| `PaymentProcessing` | Idempotency keys, changed retries, and payment lifecycle |
| `InventoryReservation` | Finite capacity, failed reservations, release, and concurrency |

Each benchmark contains:

- an in-memory ASP.NET Core API;
- a public OpenAPI document that describes operation shapes without disclosing
  behavioral rules;
- health and benchmark reset endpoints;
- ground-truth tests and maintainer documentation.

## Black-box investigations

Benchmark source and ground-truth tests are available to maintainers but should
not be exposed to the investigating agent. A black-box run should:

1. Build and start the selected API.
2. Create a separate investigation directory.
3. Copy only the benchmark's OpenAPI document into that directory.
4. Give the investigator the service URL and investigation directory.
5. Keep this source tree and the ground-truth tests outside the investigator's
   accessible filesystem.

Running an agent elsewhere in this repository and merely asking it not to read
the benchmark is useful for early development, but it is not an enforced
black-box evaluation.

`POST /__test/reset` is benchmark infrastructure rather than a domain
operation. It is intentionally externally invocable so an investigation can
record when reset was attempted and can experimentally check its effects.
