using System.Text.Json;
using Microsoft.Accordant;
using PaymentModel;
using Specmine;
using Specmine.Accordant;

namespace PaymentConformance.Tests;

/// <summary>How this replay treated one recorded call.</summary>
internal enum CallTreatment
{
    /// <summary>Checked against M-002 via <see cref="TraceReplayer"/> (backed by
    /// <c>Spec&lt;PaymentModelState&gt;.Allows</c>). <see cref="CallReplayOutcome.CheckedResult"/>
    /// carries Accordant's own verdict.</summary>
    CheckedAgainstModel,

    /// <summary>Excluded from model checking because it is the documented bootstrap
    /// precondition -- a fresh idempotency key's first successful (HTTP 201) authorization.
    /// Never asserted as conforming; its real response only seeds model state.</summary>
    ExcludedAsSetupBootstrap,
}

/// <param name="CheckedResult">Non-null only when <see cref="Treatment"/> is
/// <see cref="CallTreatment.CheckedAgainstModel"/>.</param>
/// <param name="BootstrappedPaymentId">Non-null only when <see cref="Treatment"/> is
/// <see cref="CallTreatment.ExcludedAsSetupBootstrap"/> and the bootstrap succeeded.</param>
internal sealed record CallReplayOutcome(
    int CallId,
    string OperationName,
    CallTreatment Treatment,
    string Region,
    ReplayStepResult? CheckedResult,
    string? BootstrappedPaymentId);

/// <summary>
/// Replays one already-recorded live trace against M-002 (<see cref="PaymentSpec"/>), one call
/// at a time, exactly the way <c>model\ReplayRunner.cs</c> replays the durable trace corpus:
/// <see cref="ModelScope"/> -- M-002's own scope classifier -- decides whether a call falls
/// inside the modeled region; <see cref="TraceReplayer"/> (itself built on
/// <c>Spec&lt;PaymentModelState&gt;.Allows</c>) checks the calls that do; and the one call M-002
/// documents as an explicit out-of-scope precondition -- the first successful AuthorizePayment
/// for a fresh idempotency key -- is captured into state from its real observed response
/// instead of being judged (see model\README.md, "Why not Expect.OneOf").
///
/// This class adds no behavioral claims of its own. Every conformance verdict it reports is
/// M-002's, obtained through the SDK's existing replay oracle; this file only orchestrates
/// which call goes through that oracle and which is the documented bootstrap exception. It
/// throws instead of reporting a verdict when a call does not match what the promoted sequence
/// expects (e.g. the setup authorization was declined, or a capture targets an unexpected
/// payment) -- those are infrastructure/setup failures, not conformance results.
/// </summary>
internal static class PartialModelReplay
{
    public static IReadOnlyList<CallReplayOutcome> Replay(
        Spec<PaymentModelState> spec,
        RecordedTrace trace,
        JsonSerializerOptions jsonOptions)
    {
        var state = new PaymentModelState();
        var outcomes = new List<CallReplayOutcome>();

        foreach (var call in trace.Calls)
        {
            outcomes.Add(call.OperationName switch
            {
                PaymentSpec.ResetOperation => Check(spec, ref state, trace, call, jsonOptions, "Reset scaffolding"),
                PaymentSpec.AuthorizePaymentOperation => ReplayAuthorize(spec, ref state, trace, call, jsonOptions),
                PaymentSpec.CapturePaymentOperation => ReplayCapture(spec, ref state, trace, call, jsonOptions),
                _ => throw new InvalidOperationException(
                    $"Call {call.CallId} ({call.OperationName}) is not part of the promoted sequence this test " +
                    "replays (Reset, AuthorizePayment, CapturePayment only); refusing to guess how to treat it."),
            });
        }

        return outcomes;
    }

    private static CallReplayOutcome ReplayAuthorize(
        Spec<PaymentModelState> spec,
        ref PaymentModelState state,
        RecordedTrace trace,
        RecordedCall call,
        JsonSerializerOptions jsonOptions)
    {
        if (call.Response is not { } response)
        {
            throw new InvalidOperationException($"Call {call.CallId} (AuthorizePayment) has no recorded response.");
        }

        var requestEnvelope = JsonSerializer.Deserialize<AuthorizePaymentRequestEnvelope>(call.Request.GetRawText(), jsonOptions)
            ?? throw new InvalidOperationException($"Call {call.CallId} (AuthorizePayment): request envelope failed to deserialize.");

        var scope = ModelScope.ClassifyAuthorize(requestEnvelope.Body, state);

        if (ModelScope.IsModeled(scope))
        {
            return Check(spec, ref state, trace, call, jsonOptions, ModelScope.RegionLabel(scope));
        }

        if (scope != AuthorizeScope.UnknownKeyOutcomeSelection)
        {
            // Fresh authorization selection is genuinely outside M-002 (see model\README.md,
            // "Unknown regions" #1), but every other unmodeled AuthorizeScope is a shape this
            // fixed promoted sequence should never produce. Throw rather than silently excluding.
            throw new InvalidOperationException(
                $"Call {call.CallId} (AuthorizePayment) fell into '{ModelScope.RegionLabel(scope)}', which the " +
                "promoted sequence does not expect. " + ModelScope.Describe(scope, requestEnvelope.Body));
        }

        var responseEnvelope = JsonSerializer.Deserialize<AuthorizePaymentResponseEnvelope>(response.GetRawText(), jsonOptions)
            ?? throw new InvalidOperationException($"Call {call.CallId} (AuthorizePayment): response envelope failed to deserialize.");

        if (responseEnvelope is not { Status: 201 }
            || responseEnvelope.Body is not { Id: { Length: > 0 } id, Amount: { } amount, Currency: { Length: > 0 } currency })
        {
            // The setup call is explicit setup/precondition, not something M-002 claims about --
            // but it must still have really succeeded, or nothing downstream is meaningful.
            throw new InvalidOperationException(
                $"Call {call.CallId} (AuthorizePayment) was meant to be the promoted sequence's fresh setup " +
                $"authorization but the real target did not return a successful 201 (observed status " +
                $"{responseEnvelope?.Status}). This is a setup precondition failure, not a conformance result.");
        }

        // The model's own documented bootstrap (model\README.md, "Why not Expect.OneOf"): a
        // fresh key's real 201 seeds identity/data and an initial 'authorized' status. The
        // establishment call itself is never checked against the model.
        var key = requestEnvelope.Body!.IdempotencyKey!;
        state = new PaymentModelState
        {
            SuccessfulKeys = new Dictionary<string, CapturedAuthorization>(state.SuccessfulKeys)
            {
                [key] = new CapturedAuthorization
                {
                    PaymentId = id,
                    IdempotencyKey = key,
                    Amount = amount,
                    Currency = currency,
                },
            },
            LifecycleStatuses = new Dictionary<string, string>(state.LifecycleStatuses)
            {
                [id] = PaymentLifecycle.Authorized,
            },
        };

        return new CallReplayOutcome(
            call.CallId,
            call.OperationName,
            CallTreatment.ExcludedAsSetupBootstrap,
            ModelScope.RegionLabel(scope),
            CheckedResult: null,
            BootstrappedPaymentId: id);
    }

    private static CallReplayOutcome ReplayCapture(
        Spec<PaymentModelState> spec,
        ref PaymentModelState state,
        RecordedTrace trace,
        RecordedCall call,
        JsonSerializerOptions jsonOptions)
    {
        var requestEnvelope = JsonSerializer.Deserialize<CapturePaymentRequestEnvelope>(call.Request.GetRawText(), jsonOptions)
            ?? throw new InvalidOperationException($"Call {call.CallId} (CapturePayment): request envelope failed to deserialize.");

        var scope = ModelScope.ClassifyCapture(requestEnvelope, state);
        if (scope != CaptureScope.ModeledFirstCapture)
        {
            throw new InvalidOperationException(
                $"Call {call.CallId} (CapturePayment) fell into '{ModelScope.RegionLabel(scope)}', which the " +
                "promoted sequence does not expect: it must be the first capture of the payment the setup " +
                "authorization just created. " + ModelScope.Describe(scope, requestEnvelope));
        }

        return Check(spec, ref state, trace, call, jsonOptions, ModelScope.RegionLabel(scope));
    }

    /// <summary>
    /// Checks exactly one recorded call against M-002 through Specmine.Accordant.TraceReplayer
    /// -- the same oracle model\ReplayRunner.cs uses -- by wrapping it in a single-call
    /// sub-trace so state is always the state this replay has accumulated so far, not a replay
    /// of the whole original trace. Advances <paramref name="state"/> to the single resulting
    /// state (this spec never produces a nondeterministic branch on a conforming step).
    /// </summary>
    private static CallReplayOutcome Check(
        Spec<PaymentModelState> spec,
        ref PaymentModelState state,
        RecordedTrace trace,
        RecordedCall call,
        JsonSerializerOptions jsonOptions,
        string region)
    {
        var subTrace = new RecordedTrace(trace.SchemaVersion, trace.TraceId, trace.StartedAt, trace.CompletedAt, trace.Status, new[] { call });
        var result = TraceReplayer.Replay(spec, state, subTrace, jsonOptions);
        var step = result.Steps.Single();

        if (result.FinalStateProfile is { } profile)
        {
            state = (PaymentModelState)(profile.SingleState()
                ?? throw new InvalidOperationException(
                    $"Call {call.CallId} ({call.OperationName}): TraceReplayer did not resolve a single " +
                    "deterministic next state."));
        }

        return new CallReplayOutcome(call.CallId, call.OperationName, CallTreatment.CheckedAgainstModel, region, step, null);
    }
}
