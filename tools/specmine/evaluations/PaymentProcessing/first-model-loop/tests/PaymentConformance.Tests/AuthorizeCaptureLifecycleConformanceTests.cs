using System.Text.Json;
using Microsoft.Accordant;
using NUnit.Framework;
using PaymentModel;
using Specmine;
using Specmine.Accordant;
using Specmine.Adapters.OpenApi;

namespace PaymentConformance.Tests;

/// <summary>
/// The one durable, live, model-driven Payment conformance test promoted after M-002 was
/// accepted (see workspace journal.md/frontier.md). It preserves an important operation
/// history -- Reset, then a fresh successful authorization used only as an explicit setup
/// precondition, then an identical-payload replay while the payment is still 'authorized'
/// (Claim R), then a capture of that same payment identified by its response-derived id
/// (Claim L), then a second identical-payload replay after the capture (Claim R again, now
/// checked against the live 'captured' status) -- executed exactly once against the real
/// running target.
///
/// Every step after setup is validated solely through M-002 (<see cref="PaymentSpec"/>) via
/// <see cref="PartialModelReplay"/>, which itself validates only through
/// <see cref="TraceReplayer"/> (backed by <c>Spec&lt;PaymentModelState&gt;.Allows</c>). This
/// file contains no manual assertion of HTTP status, error code, payment status, id, amount,
/// or currency -- the only assertions below are over Accordant's own conformance verdicts and
/// this test's own scope-classification bookkeeping. See tests\README.md for why the setup
/// call is excluded rather than asserted as conforming.
/// </summary>
[TestFixture]
public class AuthorizeCaptureLifecycleConformanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = ReplayJsonOptions.CreateDefault();

    [Test]
    public async Task FreshAuthorize_IdenticalReplay_Capture_IdenticalReplay_ConformsToM002()
    {
        var workspaceRoot = WorkspaceLocator.ResolveWorkspaceRoot();
        var workspace = await Workspace.LoadAsync(workspaceRoot);

        var registry = new TargetAdapterRegistry();
        registry.Register(new OpenApiTargetAdapter());
        var adapter = registry.Resolve(workspace.Document.TargetAdapter.AdapterType);

        var session = await adapter.ConnectAsync(workspace.Document.TargetAdapter.Settings, CancellationToken.None);
        try
        {
            var idempotencyKey = $"live-promoted-{Guid.NewGuid():N}";
            var authorizeRequest = JsonSerializer.SerializeToElement(new
            {
                body = new { idempotencyKey, amount = 100.00m, currency = "USD" },
            }, JsonOptions);

            // Execute the promoted sequence exactly once against the real target.
            // TraceRecorder wraps the session and, on completion, saves the resulting
            // RecordedTrace under traces\ itself -- so the same single execution validated
            // below is also preserved as durable evidence; nothing here is executed twice.
            var tracesSubdirectory = Path.Combine(workspace.TracesDirectory, "trace-8-live-authorize-replay-capture-replay");
            var (trace, tracePath) = await TraceRecorder.RunAsync(tracesSubdirectory, session, async recording =>
            {
                await recording.ExecuteAsync(
                    PaymentSpec.ResetOperation, JsonSerializer.SerializeToElement(new { }, JsonOptions), CancellationToken.None);

                // Fresh authorization selection is outside M-002 (see model\README.md, "Unknown
                // regions" #1). This call is explicit setup/precondition: its own response is
                // never claimed to conform to the model, only used to seed state.
                var setupResponse = await recording.ExecuteAsync(
                    PaymentSpec.AuthorizePaymentOperation, authorizeRequest, CancellationToken.None);

                // Identical-payload replay while still 'authorized' -- Claim R.
                await recording.ExecuteAsync(PaymentSpec.AuthorizePaymentOperation, authorizeRequest, CancellationToken.None);

                var paymentId = setupResponse.GetProperty("body").GetProperty("id").GetString()
                    ?? throw new InvalidOperationException(
                        "Setup AuthorizePayment did not return a payment id; cannot construct CapturePayment's " +
                        "request from it.");

                // CapturePayment addressed by the response-derived payment id -- Claim L.
                var captureRequest = JsonSerializer.SerializeToElement(new
                {
                    path = new Dictionary<string, string> { ["id"] = paymentId },
                }, JsonOptions);
                await recording.ExecuteAsync(PaymentSpec.CapturePaymentOperation, captureRequest, CancellationToken.None);

                // Identical-payload replay again, now after the capture -- Claim R against the
                // live 'captured' status (the exact CX-2 shape M-002 was refined to explain).
                await recording.ExecuteAsync(PaymentSpec.AuthorizePaymentOperation, authorizeRequest, CancellationToken.None);
            });

            TestContext.Out.WriteLine($"Recorded live trace {trace.TraceId} at {tracePath}");

            Assert.That(trace.Calls, Has.Count.EqualTo(5),
                "Expected exactly: Reset, setup AuthorizePayment, identical replay, CapturePayment, identical replay.");

            var spec = PaymentSpec.Build();
            var outcomes = PartialModelReplay.Replay(spec, trace, JsonOptions);

            // The setup authorization must be excluded from the model's conformance claim, not
            // asserted as conforming -- and it must really have bootstrapped a live payment, or
            // the rest of the sequence proves nothing. PartialModelReplay throws instead of
            // reporting a verdict if this precondition was not met (an infra/setup failure).
            var setupOutcome = outcomes[1];
            Assert.That(setupOutcome.Treatment, Is.EqualTo(CallTreatment.ExcludedAsSetupBootstrap),
                "The first AuthorizePayment must be treated as out-of-model setup, not checked against M-002.");
            Assert.That(setupOutcome.BootstrappedPaymentId, Is.Not.Null.And.Not.Empty,
                "Setup must have bootstrapped a real payment id from a genuine 201 response.");

            // Every behavioral step after setup -- Reset, both identical replays, and the
            // capture -- is validated solely through M-002 via Specmine.Accordant.TraceReplayer
            // (Spec<PaymentModelState>.Allows under the hood). The assertions below check only
            // Accordant's own isValid/message verdict; nothing here re-derives or duplicates an
            // expected HTTP status, error code, payment status, id, amount, or currency.
            var checkedSteps = outcomes.Where(o => o.Treatment == CallTreatment.CheckedAgainstModel).ToList();
            Assert.That(checkedSteps, Has.Count.EqualTo(4),
                "Expected Reset plus the three modeled AuthorizePayment/CapturePayment steps to be checked.");

            Assert.Multiple(() =>
            {
                foreach (var step in checkedSteps)
                {
                    Assert.That(
                        step.CheckedResult!.Outcome,
                        Is.EqualTo(ReplayStepOutcome.Conforming),
                        $"Call {step.CallId} ({step.OperationName}, {step.Region}): {step.CheckedResult!.Message}");
                }
            });
        }
        finally
        {
            // Dispose the target session explicitly. The recorded trace above already preserved
            // everything durable about this execution.
            await session.DisposeAsync();
        }
    }
}
