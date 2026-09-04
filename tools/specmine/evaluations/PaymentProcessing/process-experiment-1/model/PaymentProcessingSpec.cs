using Microsoft.Accordant;
using Specmine.Accordant;

namespace PaymentProcessing.ProcessExperiment1.Model;

internal static class PaymentProcessingSpec
{
    public static Spec<PaymentProcessingState> Create()
    {
        var spec = Spec.For<PaymentProcessingState>();
        spec.Add(new AuthorizePaymentOperation());
        spec.Add(new GetPaymentOperation());
        spec.Add(new CapturePaymentOperation());
        spec.Add(new VoidPaymentOperation());
        return spec;
    }

    private sealed class AuthorizePaymentOperation : Operation<AuthorizePaymentCall, ApiCallResponse, PaymentProcessingState>
    {
        public AuthorizePaymentOperation()
            : base("AuthorizePayment")
        {
        }

        public override ExpectedOutcomes Apply(AuthorizePaymentCall request, PaymentProcessingState state)
        {
            Understanding.Assume(
                request.Body is not null,
                "authorize-payment-body-present",
                "This feature model only covers adapter-generated AuthorizePayment requests that include a body object.");

            var body = request.Body!;

            if (!IsSemanticallyValid(body))
            {
                return Expect.That(ResponseChecks.ValidateInvalidRequest)
                    .SameState();
            }

            if (state.TryGetPaymentByKey(body.IdempotencyKey!, out var existing))
            {
                if (IsSameRequest(body, existing))
                {
                    return Expect.That(response => ResponseChecks.ValidatePaymentReplay(response, existing))
                        .SameState();
                }

                return Expect.That(ResponseChecks.ValidateIdempotencyConflict)
                    .SameState();
            }

            if (body.Amount > 1000m)
            {
                return Expect.That(ResponseChecks.ValidateDeclined)
                    .SameState();
            }

            return Expect.That(response => ResponseChecks.ValidateCreatedAuthorization(response, state, body))
                .ThenState(
                    (response, nextState) => nextState.AddOrUpdate(ResponseReaders.ReadPayment(response.Body)),
                    mock: () => ResponseReaders.MockPaymentResponse(
                        "00000000000000000000000000000001",
                        body.IdempotencyKey!,
                        body.Amount!.Value,
                        body.Currency!,
                        PaymentStatuses.Authorized,
                        201));
        }

        private static bool IsSemanticallyValid(AuthorizePaymentBody body) =>
            !string.IsNullOrWhiteSpace(body.IdempotencyKey) &&
            body.Amount is > 0m &&
            !string.IsNullOrWhiteSpace(body.Currency) &&
            body.Currency!.Length == 3 &&
            body.Currency.All(static ch => ch is >= 'A' and <= 'Z');

        private static bool IsSameRequest(AuthorizePaymentBody requestBody, PaymentSnapshot existing) =>
            string.Equals(requestBody.IdempotencyKey, existing.IdempotencyKey, StringComparison.Ordinal) &&
            requestBody.Amount == existing.Amount &&
            string.Equals(requestBody.Currency, existing.Currency, StringComparison.Ordinal);
    }

    private sealed class GetPaymentOperation : Operation<PaymentByIdCall, ApiCallResponse, PaymentProcessingState>
    {
        public GetPaymentOperation()
            : base("GetPayment")
        {
        }

        public override ExpectedOutcomes Apply(PaymentByIdCall request, PaymentProcessingState state)
        {
            var id = RequirePaymentId(request, "get-payment-id-present");

            if (!state.TryGetPaymentById(id, out var payment))
            {
                return Expect.That(response => ResponseChecks.ValidatePaymentNotFound(response, id))
                    .SameState();
            }

            return Expect.That(response => ResponseChecks.ValidatePaymentById(response, payment))
                .SameState();
        }
    }

    private sealed class CapturePaymentOperation : Operation<PaymentByIdCall, ApiCallResponse, PaymentProcessingState>
    {
        public CapturePaymentOperation()
            : base("CapturePayment")
        {
        }

        public override ExpectedOutcomes Apply(PaymentByIdCall request, PaymentProcessingState state) =>
            ApplyLifecycleTransition(request, state, TransitionKind.Capture);
    }

    private sealed class VoidPaymentOperation : Operation<PaymentByIdCall, ApiCallResponse, PaymentProcessingState>
    {
        public VoidPaymentOperation()
            : base("VoidPayment")
        {
        }

        public override ExpectedOutcomes Apply(PaymentByIdCall request, PaymentProcessingState state) =>
            ApplyLifecycleTransition(request, state, TransitionKind.Void);
    }

    private static ExpectedOutcomes ApplyLifecycleTransition(
        PaymentByIdCall request,
        PaymentProcessingState state,
        TransitionKind transition)
    {
        var assumptionId = transition == TransitionKind.Capture
            ? "capture-payment-id-present"
            : "void-payment-id-present";
        var id = RequirePaymentId(request, assumptionId);

        if (!state.TryGetPaymentById(id, out var existing))
        {
            return Expect.That<ApiCallResponse>(response => ResponseChecks.ValidatePaymentNotFound(response, id))
                .SameState();
        }

        if (transition == TransitionKind.Capture)
        {
            if (string.Equals(existing.Status, PaymentStatuses.Captured, StringComparison.Ordinal))
            {
                return Expect.That<ApiCallResponse>(response => ResponseChecks.ValidatePaymentById(response, existing))
                    .SameState();
            }

            if (string.Equals(existing.Status, PaymentStatuses.Voided, StringComparison.Ordinal))
            {
                return Expect.That<ApiCallResponse>(response => ResponseChecks.ValidateVoidedCannotBeCaptured(response, id))
                    .SameState();
            }

            var captured = existing with { Status = PaymentStatuses.Captured };
            return Expect.That<ApiCallResponse>(response => ResponseChecks.ValidatePaymentById(response, captured))
                .ThenState<PaymentProcessingState>(
                    (response, nextState) => nextState.AddOrUpdate(ResponseReaders.ReadPayment(response.Body)),
                    mock: () => ResponseReaders.MockPaymentResponse(
                        existing.Id,
                        existing.IdempotencyKey,
                        existing.Amount,
                        existing.Currency,
                        PaymentStatuses.Captured,
                        200));
        }

        if (string.Equals(existing.Status, PaymentStatuses.Voided, StringComparison.Ordinal))
        {
            return Expect.That<ApiCallResponse>(response => ResponseChecks.ValidatePaymentById(response, existing))
                .SameState();
        }

        if (string.Equals(existing.Status, PaymentStatuses.Captured, StringComparison.Ordinal))
        {
            return Expect.That<ApiCallResponse>(response => ResponseChecks.ValidateCapturedCannotBeVoided(response, id))
                .SameState();
        }

        var voided = existing with { Status = PaymentStatuses.Voided };
        return Expect.That<ApiCallResponse>(response => ResponseChecks.ValidatePaymentById(response, voided))
            .ThenState<PaymentProcessingState>(
                (response, nextState) => nextState.AddOrUpdate(ResponseReaders.ReadPayment(response.Body)),
                mock: () => ResponseReaders.MockPaymentResponse(
                    existing.Id,
                    existing.IdempotencyKey,
                    existing.Amount,
                    existing.Currency,
                    PaymentStatuses.Voided,
                    200));
    }

    private static string RequirePaymentId(PaymentByIdCall request, string assumptionId)
    {
        Understanding.Assume(
            request.Path is not null && !string.IsNullOrWhiteSpace(request.Path.Id),
            assumptionId,
            "This feature model only covers adapter-generated payment-by-id requests that include a nonblank path id.");

        return request.Path!.Id!;
    }

    private enum TransitionKind
    {
        Capture,
        Void,
    }
}
