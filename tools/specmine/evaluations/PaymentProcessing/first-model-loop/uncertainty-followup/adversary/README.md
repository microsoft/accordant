# Provisional-targeted adversary pass

One bounded black-box pass targeted the stable provisional IDs in
`PaymentResearchSpec.cs`. It used the OpenAPI adapter and `TraceRecorder`, with
a budget of three traces and fifteen total calls.

- Used: **3 traces / 14 calls**, including three resets.
- `authorize-validation-boundary`: corroborated and sharpened to an observed
  amount boundary of `0` rejected and `0.01 USD` accepted; empty and lowercase
  currency were rejected.
- `first-capture-transition`: corroborated at the newly observed low valid
  amount `0.01 USD`; identity/data remained stable and status became `captured`.
- `authorize-live-replay`: corroborated after both OpenAPI-exposed lifecycle
  transitions used by the pass. Replays reflected `captured` and `voided`.
- Counterexamples: **0**.

The markers materially improved selection: the agent chose boundary pairs,
crossed the capture experiment with the newly found amount boundary, and used a
distinct void transition instead of merely repeating capture.

`summary.json` contains the machine-readable disposition. `traces\` preserves
the immutable evidence. The disposable scratch runner and execution logs were
not promoted.
