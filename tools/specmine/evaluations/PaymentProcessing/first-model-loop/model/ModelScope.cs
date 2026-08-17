namespace PaymentModel;

/// <summary>Payment lifecycle status wire values this model revision knows about.</summary>
public static class PaymentLifecycle
{
    public const string Authorized = "authorized";
    public const string Captured = "captured";
}

/// <summary>Where a recorded AuthorizePayment call falls relative to M-002's modeled region.</summary>
public enum AuthorizeScope
{
    /// <summary>Claim V: the payload is invalid on a modeled axis, so validation preempts everything else.</summary>
    ModeledInvalidPayload,

    /// <summary>Claim R: valid payload, identical to the captured payload of an already-successful key.</summary>
    ModeledIdenticalReplay,

    /// <summary>Claim C: valid but changed payload against a key whose payment is still `authorized`.</summary>
    ModeledConflictReplay,

    /// <summary>Unknown: the request shape itself is outside the modeled region (missing field, blank key).</summary>
    UnknownRequestShape,

    /// <summary>Unknown: valid payload on a key with no captured prior success -- the success/decline selection rule.</summary>
    UnknownKeyOutcomeSelection,

    /// <summary>Unknown: valid changed payload against a key whose payment has already moved past `authorized`.</summary>
    UnknownChangedPayloadAfterLifecycleChange,
}

/// <summary>Where a recorded CapturePayment call falls relative to M-002's modeled region.</summary>
public enum CaptureScope
{
    /// <summary>Claim L: first capture of a payment this model knows and believes is `authorized`.</summary>
    ModeledFirstCapture,

    /// <summary>Unknown: no usable `id` path parameter on the recorded request.</summary>
    UnknownRequestShape,

    /// <summary>Unknown: the id is not a payment this model captured, so the 404 region applies.</summary>
    UnknownPayment,

    /// <summary>Unknown: the payment exists but is no longer `authorized` (re-capture / post-void region).</summary>
    UnknownNonAuthorizedPayment,
}

/// <summary>
/// The single source of truth for M-002's scope boundary. Both <see cref="PaymentSpec"/>
/// (whose Apply is only defined inside the modeled region and throws outside it) and
/// <see cref="ReplayRunner"/> (which must decide, before touching spec.Allows, whether a
/// recorded call may be checked at all) route through the classifiers here, so the model's
/// declared scope and the replay runner's exclusions can never drift apart.
///
/// M-001's lesson, from counterexample CX-2: a claim written without the precondition its
/// evidence actually had gets checked anyway and fails. Every claim below therefore states
/// its precondition as an explicit scope value.
/// </summary>
public static class ModelScope
{
    public static bool IsModeled(AuthorizeScope scope) =>
        scope is AuthorizeScope.ModeledInvalidPayload
              or AuthorizeScope.ModeledIdenticalReplay
              or AuthorizeScope.ModeledConflictReplay;

    public static bool IsModeled(CaptureScope scope) => scope is CaptureScope.ModeledFirstCapture;

    /// <summary>
    /// Classifies one AuthorizePayment request against the current model state. The order
    /// of the tests below is itself a modeled claim (validation precedes idempotency
    /// lookup, evidence: trace 7 calls 3-5, trace 5 call 5) -- except for the request-shape
    /// test, which comes first because a request this model cannot even interpret must
    /// never be given a verdict.
    /// </summary>
    public static AuthorizeScope ClassifyAuthorize(AuthorizePaymentRequestBody? body, PaymentModelState state)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.IdempotencyKey)
            || body.Amount is null
            || body.Currency is null)
        {
            // A blank/whitespace idempotencyKey lands here on purpose. The target's own 400
            // message also mentions a nonblank-key rule, but no recorded call has ever
            // exercised it, so M-002 does not model that axis and stays silent instead of
            // borrowing the rule from the target's prose.
            return AuthorizeScope.UnknownRequestShape;
        }

        if (!IsModeledValidPayload(body.Amount.Value, body.Currency))
        {
            return AuthorizeScope.ModeledInvalidPayload;
        }

        if (!state.SuccessfulKeys.TryGetValue(body.IdempotencyKey, out var prior))
        {
            return AuthorizeScope.UnknownKeyOutcomeSelection;
        }

        if (PayloadMatches(prior, body.Amount.Value, body.Currency))
        {
            return AuthorizeScope.ModeledIdenticalReplay;
        }

        return RequireStatus(state, prior.PaymentId) == PaymentLifecycle.Authorized
            ? AuthorizeScope.ModeledConflictReplay
            : AuthorizeScope.UnknownChangedPayloadAfterLifecycleChange;
    }

    /// <summary>Classifies one CapturePayment request against the current model state.</summary>
    public static CaptureScope ClassifyCapture(CapturePaymentRequestEnvelope? request, PaymentModelState state)
    {
        var paymentId = ExtractPaymentId(request);
        if (paymentId is null)
        {
            return CaptureScope.UnknownRequestShape;
        }

        var payment = TryFindPayment(state, paymentId);
        if (payment is null)
        {
            return CaptureScope.UnknownPayment;
        }

        return RequireStatus(state, payment.PaymentId) == PaymentLifecycle.Authorized
            ? CaptureScope.ModeledFirstCapture
            : CaptureScope.UnknownNonAuthorizedPayment;
    }

    /// <summary>
    /// The payload validity rule M-002 claims, and nothing more: a positive amount and a
    /// three-uppercase-letter currency. Evidence: `-5.00` and `usd` were each rejected with
    /// 400 (trace 7 calls 5 and 3-4, trace 5 call 5). The zero-amount boundary, non-3-letter
    /// and non-alphabetic currencies, and the blank-key axis were never observed; see
    /// model\README.md for how far this is extrapolated and how to falsify it.
    /// </summary>
    public static bool IsModeledValidPayload(decimal amount, string currency) =>
        amount > 0m && IsThreeUppercaseLetters(currency);

    /// <summary>
    /// Payload equality for idempotent-replay purposes: decimal *value* equality on amount
    /// (so `100.0` and `100.00` are the same payload -- corroborated by trace 5 call 3) and
    /// ordinal equality on currency (only ever exercised between well-formed uppercase
    /// currencies, since anything else is rejected by validation first).
    /// </summary>
    public static bool PayloadMatches(CapturedAuthorization prior, decimal amount, string currency) =>
        prior.Amount == amount && string.Equals(prior.Currency, currency, StringComparison.Ordinal);

    /// <summary>The `id` path parameter of a recorded CapturePayment call, or null if absent.</summary>
    public static string? ExtractPaymentId(CapturePaymentRequestEnvelope? request) =>
        request?.Path is { } path
        && path.TryGetValue("id", out var id)
        && !string.IsNullOrWhiteSpace(id)
            ? id
            : null;

    /// <summary>
    /// The captured record for a payment id, or null when this model has never seen that
    /// payment. Ids are server-generated and unique per 201, so at most one key maps to one.
    /// </summary>
    public static CapturedAuthorization? TryFindPayment(PaymentModelState state, string paymentId) =>
        state.SuccessfulKeys.Values.FirstOrDefault(p => string.Equals(p.PaymentId, paymentId, StringComparison.Ordinal));

    /// <summary>
    /// The current modeled lifecycle status of a payment this model has captured. Every
    /// captured authorization gets a status at bootstrap time, so a missing entry is a
    /// broken internal invariant (a bug in the runner or spec), not an unknown region --
    /// it fails loudly rather than defaulting to a status that would silently change a
    /// verdict.
    /// </summary>
    public static string RequireStatus(PaymentModelState state, string paymentId) =>
        state.LifecycleStatuses.TryGetValue(paymentId, out var status) && !string.IsNullOrEmpty(status)
            ? status
            : throw new InvalidOperationException(
                $"Model state invariant broken: payment '{paymentId}' appears in SuccessfulKeys but has no " +
                "lifecycle status. Identity/data and lifecycle status must always be recorded together.");

    /// <summary>Short region label used in the replay report's excluded-region summary.</summary>
    public static string RegionLabel(AuthorizeScope scope) => scope switch
    {
        AuthorizeScope.ModeledInvalidPayload => "Claim V (request validation precedence)",
        AuthorizeScope.ModeledIdenticalReplay => "Claim R (identical-payload replay of the live record)",
        AuthorizeScope.ModeledConflictReplay => "Claim C (changed-payload conflict while authorized)",
        AuthorizeScope.UnknownRequestShape => "Unknown region: uninterpretable/blank-key request shape",
        AuthorizeScope.UnknownKeyOutcomeSelection => "Unknown region: fresh/declined-key success-vs-decline selection",
        AuthorizeScope.UnknownChangedPayloadAfterLifecycleChange => "Unknown region: changed payload after a lifecycle transition",
        _ => scope.ToString(),
    };

    /// <summary>Short region label used in the replay report's excluded-region summary.</summary>
    public static string RegionLabel(CaptureScope scope) => scope switch
    {
        CaptureScope.ModeledFirstCapture => "Claim L (first capture of an authorized payment)",
        CaptureScope.UnknownRequestShape => "Unknown region: CapturePayment without a usable id",
        CaptureScope.UnknownPayment => "Unknown region: capture of a payment this model never captured",
        CaptureScope.UnknownNonAuthorizedPayment => "Unknown region: capture of a payment that is no longer authorized",
        _ => scope.ToString(),
    };

    /// <summary>Full sentence explaining why a call is outside the modeled region.</summary>
    public static string Describe(AuthorizeScope scope, AuthorizePaymentRequestBody? body)
    {
        var key = body?.IdempotencyKey is { Length: > 0 } k ? k : "(blank/absent)";
        return scope switch
        {
            AuthorizeScope.UnknownRequestShape =>
                "AuthorizePayment request is outside the modeled region: it lacks an interpretable nonblank " +
                "idempotencyKey, amount, or currency. M-002 models validation only on the amount-positivity and " +
                "currency-format axes it has observed, so it stays silent here rather than borrowing the " +
                "target's own prose about blank keys (see model\\README.md, \"Unknown regions\").",
            AuthorizeScope.UnknownKeyOutcomeSelection =>
                $"AuthorizePayment for idempotency key '{key}' carries a valid payload but the key has no captured " +
                "prior successful authorization. Whether such a request is authorized (201) or declined (200 " +
                "DeclinedPaymentResponse) is an uncharacterized business rule; M-002 makes no claim about it " +
                "(see model\\README.md, \"Unknown regions\" and \"Why not Expect.OneOf\").",
            AuthorizeScope.UnknownChangedPayloadAfterLifecycleChange =>
                $"AuthorizePayment for idempotency key '{key}' carries a valid *changed* payload against a payment " +
                "that has already moved past 'authorized'. Every observed 409 idempotency_conflict happened while " +
                "the payment was still authorized, so M-002 deliberately does not extend Claim C across a " +
                "lifecycle transition (see model\\README.md, \"Unknown regions\").",
            _ => $"AuthorizePayment for idempotency key '{key}' is inside the modeled region ({RegionLabel(scope)}).",
        };
    }

    /// <summary>Full sentence explaining why a call is outside the modeled region.</summary>
    public static string Describe(CaptureScope scope, CapturePaymentRequestEnvelope? request)
    {
        var id = ExtractPaymentId(request) ?? "(absent)";
        return scope switch
        {
            CaptureScope.UnknownRequestShape =>
                "CapturePayment request is outside the modeled region: it carries no usable 'id' path parameter, " +
                "so it cannot be related to any captured payment.",
            CaptureScope.UnknownPayment =>
                $"CapturePayment targets payment id '{id}', which this model never captured from a real 201 " +
                "response. The not-found (404) region of CapturePayment has never been observed and is not " +
                "modeled (see model\\README.md, \"Unknown regions\").",
            CaptureScope.UnknownNonAuthorizedPayment =>
                $"CapturePayment targets payment id '{id}', which this model believes is no longer 'authorized'. " +
                "Re-capture and post-void capture have never been observed; M-002 models only the first capture " +
                "of an authorized payment (see model\\README.md, \"Unknown regions\").",
            _ => $"CapturePayment for payment id '{id}' is inside the modeled region ({RegionLabel(scope)}).",
        };
    }

    private static bool IsThreeUppercaseLetters(string currency)
    {
        if (currency.Length != 3)
        {
            return false;
        }

        foreach (var c in currency)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
