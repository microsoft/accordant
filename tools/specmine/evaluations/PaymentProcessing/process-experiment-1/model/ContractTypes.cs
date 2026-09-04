using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Accordant;

namespace PaymentProcessing.ProcessExperiment1.Model;

internal static class ContractJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    public static JsonSerializerOptions IndentedSerializerOptions { get; } = CreateIndentedSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions() =>
        new(JsonSerializerDefaults.Web);

    private static JsonSerializerOptions CreateIndentedSerializerOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
}

internal sealed record AuthorizePaymentCall(
    [property: JsonPropertyName("body")] AuthorizePaymentBody? Body);

internal sealed record AuthorizePaymentBody(
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey,
    [property: JsonPropertyName("amount")] decimal? Amount,
    [property: JsonPropertyName("currency")] string? Currency);

internal sealed record PaymentByIdCall(
    [property: JsonPropertyName("path")] PaymentPath? Path);

internal sealed record PaymentPath(
    [property: JsonPropertyName("id")] string? Id);

internal sealed record ApiCallResponse(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("body")] JsonElement Body);

internal sealed record PaymentDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status);

internal sealed record DeclinedPaymentDocument(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string Reason);

internal sealed record ErrorDocument(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

internal sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status);

internal sealed record PaymentSnapshot(
    string Id,
    string IdempotencyKey,
    decimal Amount,
    string Currency,
    string Status);

internal static class PaymentStatuses
{
    public const string Authorized = "authorized";
    public const string Captured = "captured";
    public const string Voided = "voided";
    public const string Declined = "declined";
}

internal static class DeclineReasons
{
    public const string LimitExceeded = "limit_exceeded";
}

internal static class ResponseReaders
{
    public static PaymentSnapshot ReadPayment(JsonElement body)
    {
        var document = body.Deserialize<PaymentDocument>(ContractJson.SerializerOptions)
            ?? throw new InvalidOperationException("Response body did not deserialize into a payment.");

        return new PaymentSnapshot(
            document.Id,
            document.IdempotencyKey,
            document.Amount,
            document.Currency,
            document.Status);
    }

    public static DeclinedPaymentDocument ReadDeclined(JsonElement body) =>
        body.Deserialize<DeclinedPaymentDocument>(ContractJson.SerializerOptions)
        ?? throw new InvalidOperationException("Response body did not deserialize into a decline payload.");

    public static ErrorDocument ReadError(JsonElement body) =>
        body.Deserialize<ErrorDocument>(ContractJson.SerializerOptions)
        ?? throw new InvalidOperationException("Response body did not deserialize into an error payload.");

    public static ApiCallResponse MockPaymentResponse(
        string id,
        string idempotencyKey,
        decimal amount,
        string currency,
        string status,
        int responseStatus) =>
        new(
            responseStatus,
            JsonSerializer.SerializeToElement(
                new PaymentDocument(id, idempotencyKey, amount, currency, status),
                ContractJson.SerializerOptions));
}

internal static class ResponseChecks
{
    private const string InvalidRequestMessage =
        "idempotencyKey must be nonblank, amount must be positive, and currency must be three uppercase letters.";

    private const string IdempotencyConflictMessage =
        "The idempotency key was already used with a different request.";

    public static ValidationResult ValidatePaymentById(ApiCallResponse response, PaymentSnapshot expected) =>
        ValidatePaymentReplay(response, expected, 200);

    public static ValidationResult ValidateCreatedAuthorization(
        ApiCallResponse response,
        PaymentProcessingState state,
        AuthorizePaymentBody requestBody)
    {
        if (response.Status != 201)
        {
            return ValidationResult.Invalid($"Expected HTTP 201 but received {response.Status}.");
        }

        if (!TryReadPayment(response.Body, out var payment, out var error))
        {
            return ValidationResult.Invalid(error);
        }

        if (string.IsNullOrWhiteSpace(payment.Id))
        {
            return ValidationResult.Invalid("Expected a nonblank payment id for a successful authorization.");
        }

        if (!string.Equals(payment.IdempotencyKey, requestBody.IdempotencyKey, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected idempotencyKey '{requestBody.IdempotencyKey}' but received '{payment.IdempotencyKey}'.");
        }

        if (payment.Amount != requestBody.Amount)
        {
            return ValidationResult.Invalid(
                $"Expected amount '{requestBody.Amount}' but received '{payment.Amount}'.");
        }

        if (!string.Equals(payment.Currency, requestBody.Currency, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected currency '{requestBody.Currency}' but received '{payment.Currency}'.");
        }

        if (!string.Equals(payment.Status, PaymentStatuses.Authorized, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected payment status '{PaymentStatuses.Authorized}' but received '{payment.Status}'.");
        }

        if (state.TryGetPaymentByKey(payment.IdempotencyKey, out _))
        {
            return ValidationResult.Invalid(
                $"Expected a fresh authorization for key '{payment.IdempotencyKey}', but the key already exists in model state.");
        }

        if (state.ContainsPaymentId(payment.Id))
        {
            return ValidationResult.Invalid(
                $"Expected a fresh payment id, but '{payment.Id}' already exists in model state.");
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidatePaymentReplay(
        ApiCallResponse response,
        PaymentSnapshot expected,
        int expectedStatus = 200)
    {
        if (response.Status != expectedStatus)
        {
            return ValidationResult.Invalid($"Expected HTTP {expectedStatus} but received {response.Status}.");
        }

        if (!TryReadPayment(response.Body, out var payment, out var error))
        {
            return ValidationResult.Invalid(error);
        }

        if (!string.Equals(payment.Id, expected.Id, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected payment id '{expected.Id}' but received '{payment.Id}'.");
        }

        if (!string.Equals(payment.IdempotencyKey, expected.IdempotencyKey, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected idempotencyKey '{expected.IdempotencyKey}' but received '{payment.IdempotencyKey}'.");
        }

        if (payment.Amount != expected.Amount)
        {
            return ValidationResult.Invalid($"Expected amount '{expected.Amount}' but received '{payment.Amount}'.");
        }

        if (!string.Equals(payment.Currency, expected.Currency, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected currency '{expected.Currency}' but received '{payment.Currency}'.");
        }

        if (!string.Equals(payment.Status, expected.Status, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected status '{expected.Status}' but received '{payment.Status}'.");
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidateDeclined(ApiCallResponse response)
    {
        if (response.Status != 200)
        {
            return ValidationResult.Invalid($"Expected HTTP 200 but received {response.Status}.");
        }

        if (!TryReadDeclined(response.Body, out var declined, out var error))
        {
            return ValidationResult.Invalid(error);
        }

        if (!string.Equals(declined.Status, PaymentStatuses.Declined, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected decline status '{PaymentStatuses.Declined}' but received '{declined.Status}'.");
        }

        if (!string.Equals(declined.Reason, DeclineReasons.LimitExceeded, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected decline reason '{DeclineReasons.LimitExceeded}' but received '{declined.Reason}'.");
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidateInvalidRequest(ApiCallResponse response) =>
        ValidateError(response, 400, "validation_error", "invalid_request", InvalidRequestMessage);

    public static ValidationResult ValidateIdempotencyConflict(ApiCallResponse response) =>
        ValidateError(response, 409, "conflict", "idempotency_conflict", IdempotencyConflictMessage);

    public static ValidationResult ValidatePaymentNotFound(ApiCallResponse response, string id) =>
        ValidateError(response, 404, "not_found", "payment_not_found", $"Payment '{id}' was not found.");

    public static ValidationResult ValidateCapturedCannotBeVoided(ApiCallResponse response, string id) =>
        ValidateError(
            response,
            409,
            "conflict",
            "invalid_payment_state",
            $"Payment '{id}': A captured payment cannot be voided.");

    public static ValidationResult ValidateVoidedCannotBeCaptured(ApiCallResponse response, string id) =>
        ValidateError(
            response,
            409,
            "conflict",
            "invalid_payment_state",
            $"Payment '{id}': A voided payment cannot be captured.");

    private static ValidationResult ValidateError(
        ApiCallResponse response,
        int expectedStatus,
        string expectedType,
        string expectedCode,
        string expectedMessage)
    {
        if (response.Status != expectedStatus)
        {
            return ValidationResult.Invalid($"Expected HTTP {expectedStatus} but received {response.Status}.");
        }

        if (!TryReadError(response.Body, out var error, out var message))
        {
            return ValidationResult.Invalid(message);
        }

        if (!string.Equals(error.Type, expectedType, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected error type '{expectedType}' but received '{error.Type}'.");
        }

        if (!string.Equals(error.Code, expectedCode, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected error code '{expectedCode}' but received '{error.Code}'.");
        }

        if (!string.Equals(error.Message, expectedMessage, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected error message '{expectedMessage}' but received '{error.Message}'.");
        }

        return ValidationResult.Valid();
    }

    private static bool TryReadPayment(JsonElement body, out PaymentSnapshot payment, out string error)
    {
        try
        {
            payment = ResponseReaders.ReadPayment(body);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            payment = default!;
            error = $"Expected a payment payload but failed to parse it: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadDeclined(JsonElement body, out DeclinedPaymentDocument declined, out string error)
    {
        try
        {
            declined = ResponseReaders.ReadDeclined(body);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            declined = default!;
            error = $"Expected a decline payload but failed to parse it: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadError(JsonElement body, out ErrorDocument errorDocument, out string error)
    {
        try
        {
            errorDocument = ResponseReaders.ReadError(body);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorDocument = default!;
            error = $"Expected an error payload but failed to parse it: {ex.Message}";
            return false;
        }
    }
}
