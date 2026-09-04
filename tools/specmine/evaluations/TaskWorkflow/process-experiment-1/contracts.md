# Contracts: TaskWorkflow public surface

Source: `tools\specmine\benchmarks\TaskWorkflow\TaskWorkflow.Api\openapi.json`

## Public operations

- `GET /health` (`Health`)
  - `200` -> `{ status: string }`
- `POST /tasks` (`CreateTask`)
  - request body: `{ title: string }` (`title` required)
  - `201` -> `{ id: uuid-string, title: string, status: "pending" | "completed" | "canceled" }`
  - `400` -> `{ code: string, message: string }`
- `GET /tasks/{id}` (`GetTask`)
  - path param: `id: string`
  - `200` -> `Task`
  - `404` -> `ErrorResponse`
- `POST /tasks/{id}/complete` (`CompleteTask`)
  - path param: `id: string`
  - `200` -> `Task`
  - `404` -> `ErrorResponse`
  - `409` -> `ErrorResponse`
- `POST /tasks/{id}/cancel` (`CancelTask`)
  - path param: `id: string`
  - `200` -> `Task`
  - `404` -> `ErrorResponse`
  - `409` -> `ErrorResponse`
- `POST /__test/reset` (`Reset`)
  - `204`, no body

## Contract notes relevant to the experiment

- The public contract exposes statuses but does not say which transitions are allowed.
- The path parameter type is only `string`; the contract does not require callers to send a GUID even though the `Task.id` response field is documented as UUID-formatted.
- `ErrorResponse` is intentionally generic; behavior-specific `code`/`message` values had to be learned by probing.
- For recorded traces, the model uses the OpenAPI adapter's portable envelope types:
  - request: `{ path?, query?, headers?, body? }`
  - response: `{ status, body }`
