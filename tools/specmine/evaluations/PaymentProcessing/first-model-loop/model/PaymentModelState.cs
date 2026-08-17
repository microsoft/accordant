using Microsoft.Accordant;

namespace PaymentModel;

/// <summary>
/// Model state for M-002. Stable payment identity/data and mutable lifecycle status are
/// deliberately held in two separate maps, because counterexample CX-2 showed they behave
/// differently: an idempotent replay returns stable id/idempotencyKey/amount/currency but
/// a status that tracks the payment's current lifecycle (see model\README.md).
///
/// Scope: <see cref="SuccessfulKeys"/> records ONLY idempotency keys whose observed real
/// outcome, as captured by <see cref="ReplayRunner"/>, was a successful authorization
/// (HTTP 201). A key that has never been used, or whose only observed outcome so far has
/// been a business decline (HTTP 200 DeclinedPaymentResponse), is deliberately absent.
/// This model makes no claim about which of those two outcomes a fresh valid request
/// produces -- that is an explicit unknown region, not something represented (accurately
/// or otherwise) by an empty/default state.
/// </summary>
[State]
public partial class PaymentModelState
{
    /// <summary>
    /// idempotency key -> the stable identity/data fields captured from the one real 201
    /// response observed for that key. Nothing in here is ever invented or predicted.
    /// </summary>
    public Dictionary<string, CapturedAuthorization> SuccessfulKeys { get; set; } = new();

    /// <summary>
    /// payment id -> that payment's current modeled lifecycle status, one of
    /// <see cref="PaymentLifecycle.Authorized"/> or <see cref="PaymentLifecycle.Captured"/>.
    /// Set to "authorized" when a 201 is captured and advanced to "captured" by the
    /// modeled CapturePayment transition. VoidPayment is not modeled, so no "voided"
    /// status is ever produced -- a trace containing VoidPayment stays outside this
    /// revision's scope (see model\README.md, "Unknown regions").
    /// </summary>
    public Dictionary<string, string> LifecycleStatuses { get; set; } = new();
}

/// <summary>
/// The stable identity/data of one payment, captured verbatim from one real 201
/// AuthorizePayment response (see ReplayRunner's bootstrap step). Lifecycle status is
/// intentionally NOT stored here -- it lives in
/// <see cref="PaymentModelState.LifecycleStatuses"/> because it is the one field observed
/// to change over a payment's life.
/// </summary>
[State]
public partial class CapturedAuthorization
{
    public string PaymentId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
