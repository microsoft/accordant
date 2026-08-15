// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TaskWorkflow.Api;

public sealed record CreateTaskRequest(string? Title);

public sealed record TaskResponse(string Id, string Title, string Status);

public sealed record ErrorResponse(string Code, string Message);

public sealed record HealthResponse(string Status);
