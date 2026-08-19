using Microsoft.Accordant;
using PaymentModel;
using Specmine.Accordant;

namespace PaymentResearchModel;

/// <summary>
/// M-002 expressed with first-class unknown and provisional regions. Accepted claims remain
/// ordinary Accordant expectations.
/// </summary>
public static class PaymentResearchSpec
{
    public const string ResetOperation = "Reset";
    public const string AuthorizePaymentOperation = "AuthorizePayment";
    public const string CapturePaymentOperation = "CapturePayment";
    public const string VoidPaymentOperation = "VoidPayment";

    public static Spec<PaymentModelState> Build()
    {
        var spec = Spec.For<PaymentModelState>();

        spec.Operation<ResetRequestEnvelope, ResetResponseEnvelope>(ResetOperation, (_, _) =>
            Expect.That<ResetResponseEnvelope>(response => response.Status == 204, "Reset must return HTTP 204.")
                .ThenState<PaymentModelState>(next =>
                {
                    next.SuccessfulKeys.Clear();
                    next.LifecycleStatuses.Clear();
                }));

        spec.Operation<AuthorizePaymentRequestEnvelope, AuthorizePaymentResponseEnvelope>(
            AuthorizePaymentOperation,
            ApplyAuthorize);

        spec.Operation<CapturePaymentRequestEnvelope, CapturePaymentResponseEnvelope>(
            CapturePaymentOperation,
            ApplyCapture);

        spec.Operation<CapturePaymentRequestEnvelope, CapturePaymentResponseEnvelope>(
            VoidPaymentOperation,
            (_, _) => Research.Unknown<CapturePaymentResponseEnvelope>(
                "void-lifecycle-transition",
                "Void behavior is not modeled; an observed successful transition may recover the live status for downstream replay."));

        return spec;
    }

    private static ExpectedOutcomes ApplyAuthorize(
        AuthorizePaymentRequestEnvelope request,
        PaymentModelState state)
    {
        var body = request.Body;
        var scope = ModelScope.ClassifyAuthorize(body, state);

        if (!ModelScope.IsModeled(scope))
        {
            return Research.Unknown<AuthorizePaymentResponseEnvelope>(
                UnknownId(scope),
                ModelScope.Describe(scope, body));
        }

        var key = body!.IdempotencyKey!;
        var amount = body.Amount!.Value;
        var currency = body.Currency!;

        if (scope == AuthorizeScope.ModeledInvalidPayload)
        {
            return Research.Provisional(
                "authorize-validation-boundary",
                "Which exact amount and currency boundaries define a valid authorization request?",
                Expect.That<AuthorizePaymentResponseEnvelope>(
                        response =>
                            response.Status == 400 &&
                            response.Body?.Type == "validation_error" &&
                            response.Body.Code == "invalid_request",
                        "The currently inferred invalid amount/currency region must return validation HTTP 400.")
                    .SameState());
        }

        var prior = state.SuccessfulKeys[key];
        var liveStatus = ModelScope.RequireStatus(state, prior.PaymentId);

        if (scope == AuthorizeScope.ModeledIdenticalReplay)
        {
            return Research.Provisional(
                "authorize-live-replay",
                "Does identical replay reflect the live record after every lifecycle transition?",
                Expect.That<AuthorizePaymentResponseEnvelope>(
                        response =>
                            response.Status == 200 &&
                            response.Body?.Id == prior.PaymentId &&
                            response.Body.IdempotencyKey == key &&
                            response.Body.Amount == prior.Amount &&
                            response.Body.Currency == prior.Currency &&
                            response.Body.Status == liveStatus,
                        "Identical replay must return the stable payment data and current modeled status.")
                    .SameState());
        }

        return Expect.That<AuthorizePaymentResponseEnvelope>(
                response =>
                    response.Status == 409 &&
                    response.Body?.Type == "conflict" &&
                    response.Body.Code == "idempotency_conflict",
                "A valid changed payload against an authorized payment must return idempotency conflict HTTP 409.")
            .SameState();
    }

    private static ExpectedOutcomes ApplyCapture(
        CapturePaymentRequestEnvelope request,
        PaymentModelState state)
    {
        var scope = ModelScope.ClassifyCapture(request, state);
        if (!ModelScope.IsModeled(scope))
        {
            return Research.Unknown<CapturePaymentResponseEnvelope>(
                UnknownId(scope),
                ModelScope.Describe(scope, request));
        }

        var paymentId = ModelScope.ExtractPaymentId(request)!;
        var payment = ModelScope.TryFindPayment(state, paymentId)!;

        return Research.Provisional(
            "first-capture-transition",
            "Does first capture behave this way across more payments and boundary conditions?",
            Expect.That<CapturePaymentResponseEnvelope>(
                    response =>
                        response.Status == 200 &&
                        response.Body?.Id == payment.PaymentId &&
                        response.Body.IdempotencyKey == payment.IdempotencyKey &&
                        response.Body.Amount == payment.Amount &&
                        response.Body.Currency == payment.Currency &&
                        response.Body.Status == PaymentLifecycle.Captured,
                    "First capture must preserve payment data and transition status to captured.")
                .ThenState<PaymentModelState>(next =>
                    next.LifecycleStatuses[payment.PaymentId] = PaymentLifecycle.Captured));
    }

    private static string UnknownId(AuthorizeScope scope) => scope switch
    {
        AuthorizeScope.UnknownRequestShape => "authorize-request-shape",
        AuthorizeScope.UnknownKeyOutcomeSelection => "authorize-fresh-key-outcome",
        AuthorizeScope.UnknownChangedPayloadAfterLifecycleChange => "authorize-changed-after-lifecycle",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    private static string UnknownId(CaptureScope scope) => scope switch
    {
        CaptureScope.UnknownRequestShape => "capture-request-shape",
        CaptureScope.UnknownPayment => "capture-unknown-payment",
        CaptureScope.UnknownNonAuthorizedPayment => "capture-after-lifecycle",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
