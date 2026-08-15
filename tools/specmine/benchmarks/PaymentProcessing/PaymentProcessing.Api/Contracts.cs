// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PaymentProcessing.Api;

public sealed record AuthorizePaymentRequest(
    string? IdempotencyKey,
    decimal Amount,
    string? Currency);

public sealed record GetPaymentRequest(string Id);

public sealed record CapturePaymentRequest(string Id);

public sealed record VoidPaymentRequest(string Id);

public sealed record PaymentResponse(
    string Id,
    string IdempotencyKey,
    decimal Amount,
    string Currency,
    string Status);

public sealed record DeclinedPaymentResponse(string Status, string Reason);

public sealed record ErrorResponse(string Type, string Code, string Message);

public sealed record HealthResponse(string Status);
