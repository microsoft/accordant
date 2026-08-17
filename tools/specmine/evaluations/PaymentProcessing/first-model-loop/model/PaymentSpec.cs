using Microsoft.Accordant;

namespace PaymentModel;

/// <summary>
/// M-002: the partial Accordant model for the Payment target's AuthorizePayment
/// idempotency region, refined after adversarial falsification of M-001. See
/// model\README.md for the full scope statement, evidence, unknowns and falsification
/// strategies.
///
/// Four claims are made -- Claim V (request validation precedes idempotency handling),
/// Claim R (an identical-payload replay returns the *live* payment record), Claim C (a
/// valid changed payload conflicts while the payment is still authorized) and Claim L (the
/// first capture of an authorized payment). Every claim states its precondition as an
/// explicit <see cref="AuthorizeScope"/>/<see cref="CaptureScope"/> value computed by
/// <see cref="ModelScope"/>, which is also what <see cref="ReplayRunner"/> uses to decide
/// whether a recorded call may be checked at all. That shared classifier is the direct fix
/// for counterexample CX-2, where M-001 stated a claim unconditionally over a region whose
/// evidence had a precondition the model never wrote down.
///
/// Deliberate exclusion, by design: Apply throws for any request outside the modeled
/// region rather than guessing. The largest such region is the success-vs-decline
/// selection for a valid payload on a key with no captured prior success. Modeling it as
/// `Expect.OneOf(success, decline)` was considered and rejected: OneOf is Accordant's
/// construct for genuine external nondeterminism -- the same request to the same state
/// legitimately producing different real outcomes because of timing outside either party's
/// control (see sdk\docs\operations-and-expect.md's network-timeout example). Whether a
/// specific amount is authorized or declined is not that kind of nondeterminism -- it is
/// almost certainly a deterministic business rule this investigation has not
/// characterized. Encoding our own ignorance as OneOf would blur "we don't know the rule"
/// with "the target may answer either way", and would make the model unable to ever flag a
/// bug in that region. Since Accordant has no per-region "unknown" outcome, the exclusion
/// lives in <see cref="ReplayRunner"/>: it never invokes spec.Allows for an out-of-region
/// call, and instead reads the real observed response out of the trace, capturing the
/// server-generated id/amount/currency (and an initial 'authorized' lifecycle status) only
/// when that response was a real 201.
/// </summary>
public static class PaymentSpec
{
    public const string ResetOperation = "Reset";
    public const string AuthorizePaymentOperation = "AuthorizePayment";
    public const string CapturePaymentOperation = "CapturePayment";

    public static Spec<PaymentModelState> Build()
    {
        var spec = Spec.For<PaymentModelState>();

        spec.Operation<ResetRequestEnvelope, ResetResponseEnvelope>(ResetOperation, (request, state) =>
            Expect.That<ResetResponseEnvelope>(
                    r => r.Status == 204,
                    "Reset must acknowledge with HTTP 204 and clear every captured payment -- both the " +
                    "successful-key authorizations and their lifecycle statuses -- regardless of what was " +
                    "recorded before it ran.")
                .ThenState<PaymentModelState>(nextState =>
                {
                    nextState.SuccessfulKeys.Clear();
                    nextState.LifecycleStatuses.Clear();
                }));

        spec.Operation<AuthorizePaymentRequestEnvelope, AuthorizePaymentResponseEnvelope>(AuthorizePaymentOperation, (request, state) =>
        {
            var body = request.Body
                ?? throw new InvalidOperationException("AuthorizePayment request envelope must carry a body.");

            var scope = ModelScope.ClassifyAuthorize(body, state);

            if (!ModelScope.IsModeled(scope))
            {
                // Outside the modeled region by construction. Reaching this means
                // spec.Allows was invoked for a request ReplayRunner should have excluded
                // -- a usage error, not a modeling gap, so it fails loudly instead of
                // fabricating a claim.
                throw new InvalidOperationException(
                    ModelScope.Describe(scope, body) +
                    " Such a request must be excluded from spec.Allows checking by the replay runner.");
            }

            var key = body.IdempotencyKey!;
            var amount = body.Amount!.Value;
            var currency = body.Currency!;

            if (scope == AuthorizeScope.ModeledInvalidPayload)
            {
                // Claim V. Evidence: trace 7 call 3 (successful key, currency "usd"), call 4
                // (fresh, never-used key, same malformed currency) and call 5 (successful
                // key, amount -5.00); plus trace 5 call 5. The 400 is a property of the
                // request alone: it preempts both the idempotency lookup (no 409 for a
                // changed-but-malformed payload) and the success/decline selection (no 201
                // for a fresh key), which is why this branch is checked before the key is
                // ever consulted and applies whatever the key's history is.
                return Expect.That<AuthorizePaymentResponseEnvelope>(
                        r => r.Status == 400 &&
                             r.Body != null &&
                             r.Body.Type == "validation_error" &&
                             r.Body.Code == "invalid_request" &&
                             !string.IsNullOrEmpty(r.Body.Message),
                        $"AuthorizePayment with amount={amount} and currency='{currency}' is invalid on a modeled " +
                        "axis (amount must be positive; currency must be three uppercase letters), so request " +
                        "validation must reject it with HTTP 400 { type: validation_error, code: invalid_request } " +
                        $"before idempotency handling for key '{key}' is reached -- not a 409 conflict, not a new " +
                        "payment, and with nothing recorded against the key.")
                    .SameState();
            }

            var prior = state.SuccessfulKeys[key];
            var liveStatus = ModelScope.RequireStatus(state, prior.PaymentId);

            if (scope == AuthorizeScope.ModeledIdenticalReplay)
            {
                // Claim R (M-001's Claim 1, corrected by counterexample CX-2). Evidence for
                // the stable identity/data fields: trace 1 call 3, trace 4 calls 3, 4 and 6,
                // trace 5 call 3. Evidence that `status` is live rather than frozen: trace 6
                // call 4, where the replay reported "captured" after a CapturePayment while
                // id/idempotencyKey/amount/currency were all unchanged.
                return Expect.That<AuthorizePaymentResponseEnvelope>(
                        r => r.Status == 200 &&
                             r.Body != null &&
                             r.Body.Id == prior.PaymentId &&
                             r.Body.IdempotencyKey == key &&
                             r.Body.Amount == prior.Amount &&
                             r.Body.Currency == prior.Currency &&
                             r.Body.Status == liveStatus,
                        $"Replaying idempotency key '{key}' with an unchanged payload (amount={prior.Amount}, " +
                        $"currency={prior.Currency}) must return HTTP 200 carrying the *current* record of payment " +
                        $"{prior.PaymentId}: the identity/data fields exactly as first authorized, and " +
                        $"status='{liveStatus}' as the lifecycle now stands -- not a new payment, not an error, " +
                        "and not a frozen snapshot of the original authorization.")
                    .SameState();
            }

            // Claim C. Evidence: trace 2 call 3 (amount changed), call 4 (currency changed),
            // and trace 4 call 5 (both changed simultaneously -- the extrapolation M-001
            // flagged, now corroborated by a direct adversarial probe). Every one of those
            // observations was made while the payment was still 'authorized', which is why
            // this branch carries that precondition instead of ranging over every lifecycle
            // status the way M-001's Claim 2 did.
            return Expect.That<AuthorizePaymentResponseEnvelope>(
                    r => r.Status == 409 &&
                         r.Body != null &&
                         r.Body.Type == "conflict" &&
                         r.Body.Code == "idempotency_conflict" &&
                         !string.IsNullOrEmpty(r.Body.Message),
                    $"Reusing idempotency key '{key}' with a valid but changed payload (amount={amount}, " +
                    $"currency={currency}) against its captured, still-authorized authorization " +
                    $"(amount={prior.Amount}, currency={prior.Currency}, id={prior.PaymentId}) must be rejected " +
                    "with HTTP 409 { type: conflict, code: idempotency_conflict } -- not a new or updated payment.")
                .SameState();
        });

        spec.Operation<CapturePaymentRequestEnvelope, CapturePaymentResponseEnvelope>(CapturePaymentOperation, (request, state) =>
        {
            var scope = ModelScope.ClassifyCapture(request, state);

            if (!ModelScope.IsModeled(scope))
            {
                throw new InvalidOperationException(
                    ModelScope.Describe(scope, request) +
                    " Such a request must be excluded from spec.Allows checking by the replay runner.");
            }

            var paymentId = ModelScope.ExtractPaymentId(request)!;
            var payment = ModelScope.TryFindPayment(state, paymentId)!;

            // Claim L. Evidence: trace 6 call 3 (n=1) -- the smallest transition needed to
            // explain CX-2, i.e. to know that the payment behind an idempotency key is no
            // longer 'authorized' when a later identical replay is checked by Claim R. The
            // 404 (unknown id), re-capture and post-void regions are NOT modeled, and
            // VoidPayment is not modeled at all.
            return Expect.That<CapturePaymentResponseEnvelope>(
                    r => r.Status == 200 &&
                         r.Body != null &&
                         r.Body.Id == payment.PaymentId &&
                         r.Body.IdempotencyKey == payment.IdempotencyKey &&
                         r.Body.Amount == payment.Amount &&
                         r.Body.Currency == payment.Currency &&
                         r.Body.Status == PaymentLifecycle.Captured,
                    $"Capturing the still-authorized payment {payment.PaymentId} (idempotencyKey=" +
                    $"'{payment.IdempotencyKey}', amount={payment.Amount}, currency={payment.Currency}) must " +
                    "return HTTP 200 with every identity/data field unchanged and status='captured'.")
                .ThenState<PaymentModelState>(nextState =>
                    nextState.LifecycleStatuses[payment.PaymentId] = PaymentLifecycle.Captured);
        });

        return spec;
    }
}
