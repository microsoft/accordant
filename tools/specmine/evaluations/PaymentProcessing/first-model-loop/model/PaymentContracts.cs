namespace PaymentModel;

// Typed envelopes matching the workspace's OpenAPI trace shape:
//   request  -> { path, query, headers, body }
//   response -> { status, body }
// (see target\openapi.json and sdk\docs\request-derivations.md / conformance-testing.md).
// Path/query/headers are unused by Reset and AuthorizePayment, so they are modeled as
// optional dictionaries that stay null when the recorded call omitted them.
// CapturePayment carries no body at all -- its only input is the path parameter `id`.

/// <summary>Request envelope for the "Reset" operation. It carries no body.</summary>
public sealed class ResetRequestEnvelope
{
    public Dictionary<string, string>? Path { get; set; }
    public Dictionary<string, string>? Query { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public object? Body { get; set; }
}

/// <summary>Response envelope for "Reset": HTTP 204, no body.</summary>
public sealed class ResetResponseEnvelope
{
    public int Status { get; set; }
    public object? Body { get; set; }
}

/// <summary>
/// Request body for "AuthorizePayment", matching OpenAPI's AuthorizePaymentRequest schema
/// (idempotencyKey, amount, currency all required).
/// </summary>
public sealed class AuthorizePaymentRequestBody
{
    public string? IdempotencyKey { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}

/// <summary>Request envelope for "AuthorizePayment".</summary>
public sealed class AuthorizePaymentRequestEnvelope
{
    public Dictionary<string, string>? Path { get; set; }
    public Dictionary<string, string>? Query { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public AuthorizePaymentRequestBody? Body { get; set; }
}

/// <summary>
/// Union response body shared by every payment operation this model touches. The real
/// API's OpenAPI document declares three distinct schemas that can appear in these
/// positions (PaymentResponse | DeclinedPaymentResponse for AuthorizePayment's HTTP 200,
/// PaymentResponse for HTTP 201 and for CapturePayment's HTTP 200, ErrorResponse for
/// HTTP 400/404/409). All fields are nullable and grouped by the schema they belong to;
/// only the subset relevant to the observed HTTP status is ever populated by the real
/// target.
/// </summary>
public sealed class PaymentOperationResponseBody
{
    // PaymentResponse fields (HTTP 200 replay / capture, or HTTP 201 fresh authorization).
    public string? Id { get; set; }
    public string? IdempotencyKey { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }

    // Business/lifecycle status: "authorized" or "captured" (PaymentResponse), or
    // "declined" (DeclinedPaymentResponse). Distinct from the envelope's HTTP Status
    // below, which is the transport status code.
    public string? Status { get; set; }

    // DeclinedPaymentResponse-only field (HTTP 200 business decline).
    public string? Reason { get; set; }

    // ErrorResponse fields (HTTP 400/404/409).
    public string? Type { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
}

/// <summary>Response envelope for "AuthorizePayment".</summary>
public sealed class AuthorizePaymentResponseEnvelope
{
    /// <summary>HTTP status code: 200, 201, 400, or 409.</summary>
    public int Status { get; set; }
    public PaymentOperationResponseBody? Body { get; set; }
}

/// <summary>
/// Request envelope for "CapturePayment". The operation takes no body; the payment being
/// captured is identified by the OpenAPI path parameter `id`
/// (POST /payments/{id}/capture).
/// </summary>
public sealed class CapturePaymentRequestEnvelope
{
    public Dictionary<string, string>? Path { get; set; }
    public Dictionary<string, string>? Query { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public object? Body { get; set; }
}

/// <summary>Response envelope for "CapturePayment".</summary>
public sealed class CapturePaymentResponseEnvelope
{
    /// <summary>HTTP status code: 200, 404, or 409 per the OpenAPI document.</summary>
    public int Status { get; set; }
    public PaymentOperationResponseBody? Body { get; set; }
}
