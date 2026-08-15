// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PaymentProcessing.Api;

internal sealed class PaymentStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Payment> _payments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _paymentIdsByIdempotencyKey = new(StringComparer.Ordinal);

    public IResult Authorize(AuthorizePaymentRequest request)
    {
        if (!IsValid(request))
        {
            return TypedResults.BadRequest(new ErrorResponse(
                "validation_error",
                "invalid_request",
                "idempotencyKey must be nonblank, amount must be positive, and currency must be three uppercase letters."));
        }

        lock (_lock)
        {
            if (_paymentIdsByIdempotencyKey.TryGetValue(request.IdempotencyKey!, out var existingId))
            {
                var existing = _payments[existingId];
                if (existing.Amount != request.Amount ||
                    !StringComparer.Ordinal.Equals(existing.Currency, request.Currency))
                {
                    return TypedResults.Conflict(new ErrorResponse(
                        "conflict",
                        "idempotency_conflict",
                        "The idempotency key was already used with a different request."));
                }

                return TypedResults.Ok(existing.ToResponse());
            }

            if (request.Amount > 1000m)
            {
                return TypedResults.Ok(new DeclinedPaymentResponse("declined", "limit_exceeded"));
            }

            var payment = new Payment(
                Guid.NewGuid().ToString("N"),
                request.IdempotencyKey!,
                request.Amount,
                request.Currency!,
                "authorized");

            _payments.Add(payment.Id, payment);
            _paymentIdsByIdempotencyKey.Add(payment.IdempotencyKey, payment.Id);

            return TypedResults.Created($"/payments/{payment.Id}", payment.ToResponse());
        }
    }

    public IResult Get(GetPaymentRequest request)
    {
        lock (_lock)
        {
            return _payments.TryGetValue(request.Id, out var payment)
                ? TypedResults.Ok(payment.ToResponse())
                : NotFound(request.Id);
        }
    }

    public IResult Capture(CapturePaymentRequest request)
    {
        lock (_lock)
        {
            if (!_payments.TryGetValue(request.Id, out var payment))
            {
                return NotFound(request.Id);
            }

            if (payment.Status == "voided")
            {
                return InvalidState(request.Id, "A voided payment cannot be captured.");
            }

            payment.Status = "captured";
            return TypedResults.Ok(payment.ToResponse());
        }
    }

    public IResult Void(VoidPaymentRequest request)
    {
        lock (_lock)
        {
            if (!_payments.TryGetValue(request.Id, out var payment))
            {
                return NotFound(request.Id);
            }

            if (payment.Status == "captured")
            {
                return InvalidState(request.Id, "A captured payment cannot be voided.");
            }

            payment.Status = "voided";
            return TypedResults.Ok(payment.ToResponse());
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _payments.Clear();
            _paymentIdsByIdempotencyKey.Clear();
        }
    }

    private static bool IsValid(AuthorizePaymentRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
               request.Amount > 0m &&
               request.Currency is { Length: 3 } currency &&
               currency.All(character => character is >= 'A' and <= 'Z');
    }

    private static IResult NotFound(string id)
    {
        return TypedResults.NotFound(new ErrorResponse(
            "not_found",
            "payment_not_found",
            $"Payment '{id}' was not found."));
    }

    private static IResult InvalidState(string id, string message)
    {
        return TypedResults.Conflict(new ErrorResponse(
            "conflict",
            "invalid_payment_state",
            $"Payment '{id}': {message}"));
    }

    private sealed record Payment(
        string Id,
        string IdempotencyKey,
        decimal Amount,
        string Currency,
        string InitialStatus)
    {
        public string Status { get; set; } = InitialStatus;

        public PaymentResponse ToResponse()
        {
            return new PaymentResponse(Id, IdempotencyKey, Amount, Currency, Status);
        }
    }
}
