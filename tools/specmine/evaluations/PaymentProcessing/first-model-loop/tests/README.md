# Payment Conformance Tests

This directory holds the one durable, live, model-driven Payment conformance test promoted
after M-002 was accepted (see `..\journal.md`, "Test promotion", and `..\frontier.md`'s
"Promotion candidate" entry for the full rationale, sequence and result). The project is
`PaymentConformance.Tests\` -- a buildable .NET 10 NUnit project that runs against the real,
already-running target through `Specmine.Adapters.OpenApi.OpenApiTargetAdapter` and validates
every behavioral claim exclusively through M-002 (`..\model\PaymentSpec.cs`, unmodified).

## The promoted test

`AuthorizeCaptureLifecycleConformanceTests.FreshAuthorize_IdenticalReplay_Capture_IdenticalReplay_ConformsToM002`
executes, once, against the live target:

```
Reset
  -> AuthorizePayment(fresh key, 100.00 USD)              [setup / precondition -- see below]
  -> AuthorizePayment(same key, identical payload)        [Claim R, status: authorized]
  -> CapturePayment(id read from the setup response)      [Claim L]
  -> AuthorizePayment(same key, identical payload)        [Claim R again, status: captured]
```

This is the exact operation history that motivated M-002's Claim R / Claim L split
(counterexample CX-2, `..\model\README.md`): an idempotent replay must reflect the payment's
*current* lifecycle status, not a frozen snapshot of the original authorization. The extra
identical replay before the capture (beyond `..\frontier.md`'s original promotion candidate)
exercises Claim R in both lifecycle states -- `authorized` and `captured` -- in one live
execution instead of relying on the historical trace corpus for the `authorized` case.

## Why the setup authorization is outside the model, and how the test still uses it

Fresh-key success-vs-decline selection is M-002's largest named unknown region
(`..\model\README.md`, "Unknown regions" #1): the model does not claim to know whether a
never-before-seen idempotency key will be authorized or declined, and `PaymentSpec.Apply`
throws if asked to judge such a call. That is why the very first `AuthorizePayment` in this
test's sequence -- the one that actually creates the payment -- is treated as an **explicit
setup precondition**, not as a step this test claims conforms to M-002.

Concretely: `PartialModelReplay.cs` classifies every recorded call with M-002's own
`ModelScope.ClassifyAuthorize`/`ClassifyCapture` (the same classifier `..\model\ReplayRunner.cs`
uses for the durable trace corpus). For the one call it classifies as
`AuthorizeScope.UnknownKeyOutcomeSelection`, it does exactly what `ReplayRunner.cs` documents as
the model's bootstrap mechanism: if the real observed response was a genuine HTTP `201`, the
server-generated id/amount/currency and an initial `authorized` lifecycle status are captured
directly into a fresh `PaymentModelState` -- never invented, never checked against `Apply`. If
that response were anything other than a real `201` (e.g. the target declined it), or if the
call classified into any other unmodeled region, `PartialModelReplay` **throws** instead of
reporting a pass/fail verdict, because that would be an infrastructure/setup failure, not a
conformance result -- disguising it as either "pass" or "fail" would misrepresent what the test
actually checked.

## No manual behavioral assertions

Every step *after* setup -- `Reset` and the three `AuthorizePayment`/`CapturePayment` calls that
follow -- is validated **solely** by handing a single-call `RecordedTrace` to
`Specmine.Accordant.TraceReplayer.Replay(spec, state, subTrace, jsonOptions)`, where `spec` is
`PaymentModel.PaymentSpec.Build()` (M-002 itself). `TraceReplayer.Replay` is backed by
`Microsoft.Accordant.Spec<PaymentModelState>.Allows` -- the same primitive
`sdk\docs\conformance-testing.md` documents (`(bool isValid, string message, StateProfile)`).

The test file (`AuthorizeCaptureLifecycleConformanceTests.cs`) asserts only:
- `ReplayStepResult.Outcome == ReplayStepOutcome.Conforming` for each checked step, surfacing
  `ReplayStepResult.Message` (Accordant's own explanation) as the assertion message on failure;
- bookkeeping about *which* calls were checked vs. excluded (`CallTreatment`, an enum local to
  this test project, not a behavioral claim) and that the setup call produced a non-empty
  bootstrapped payment id.

Nowhere in `tests\` does any code compare an HTTP status code, an error `type`/`code`, a payment
`status`, an `id`, an `amount`, or a `currency` against an expected value. Every such comparison
lives exactly once, inside `..\model\PaymentSpec.cs`'s `Expect.That` predicates -- this test
suite reuses that oracle instead of duplicating it.

## Durable trace

The one live execution above was recorded (not re-executed) via `Specmine.TraceRecorder`, which
both records and saves the resulting trace under `..\traces\`:

```
..\traces\trace-8-live-authorize-replay-capture-replay\2a03ec1481fc462ca73ae0c8943c3634.json
(TraceId 2a03ec14-81fc-462c-a73a-e0c8943c3634)
```

`PartialModelReplay` validates this exact `RecordedTrace` object in memory; nothing is executed
against the target a second time to produce it.

## Running the test

The target must already be running (per `..\workspace.json`, `http://127.0.0.1:5088`):

```powershell
cd tests\PaymentConformance.Tests
dotnet test
```

`WorkspaceLocator` resolves the workspace root by walking up from the test assembly's own build
output directory looking for `workspace.json` -- no source file hardcodes an absolute path. Set
the `PAYMENT_CONFORMANCE_WORKSPACE_ROOT` environment variable to override this if the project is
ever built or run from a different location.

## Files

| File | Purpose |
|---|---|
| `PaymentConformance.Tests.csproj` | Buildable .NET 10 NUnit project. References `..\..\model\PaymentModel.csproj` (M-002) and the `sdk\*.dll` assemblies directly. |
| `WorkspaceLocator.cs` | Resolves the workspace root without any hardcoded absolute path. |
| `PartialModelReplay.cs` | Replays one already-recorded trace against M-002 one call at a time, using `PaymentModel.ModelScope` to classify each call and `Specmine.Accordant.TraceReplayer` to check the ones inside the model's scope -- mirroring `..\model\ReplayRunner.cs`'s own treatment of the durable trace corpus, applied here to a freshly recorded live trace. |
| `AuthorizeCaptureLifecycleConformanceTests.cs` | The promoted test: executes the live sequence once, records it, and asserts only on the model's own conformance verdicts. |
