# Partial-model uncertainty follow-up

This follow-up preserves the original M-002 evaluation while expressing the same behavioral
model through the first-class `Research.Unknown` and `Research.Provisional` API.

- Accepted claims remain ordinary Accordant expectations.
- Validation boundaries, live-record replay, and first capture are provisional because the
  evidence still leaves explicit refinement questions.
- Fresh-key selection and other out-of-scope regions return `Research.Unknown` from the model
  itself instead of requiring the runner to duplicate `ModelScope` before calling Accordant.
- The one remaining special case is intentional state recovery: an observed fresh-key `201`
  seeds its server-generated payment identity for downstream calls, while that establishment
  call remains Unknown.

Run with:

```powershell
dotnet run --project tools\specmine\evaluations\PaymentProcessing\first-model-loop\uncertainty-followup\PaymentResearchModel.csproj
```

The generated `replay-report.md` replays both the original eight traces and the three
provisional-targeted adversary traces. Current totals are **14 accepted matches, 21
provisional matches, 15 unknown calls, and 0 violations**. Unknown successful
authorizations recover response-derived identity for downstream replay. The newly observed
void transition remains Unknown itself, but its observed live status is recovered so the
subsequent provisional replay claim can be checked rather than asserted.
