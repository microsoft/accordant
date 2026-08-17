# Controller Journal

## Planned model-driven loop

1. Investigator gathers bounded authorization/idempotency evidence.
2. Modeler encodes the smallest justified partial Accordant model and replays traces.
3. Adversary receives read-only model access and attempts to falsify its strongest claims.
4. Modeler classifies and minimally refines any counterexample, then replays all traces.
5. Only after the model is accepted, create one live model-driven conformance test.

Scratch source is disposable. Durable artifacts are traces, frontier/journal, model source, and promoted model-driven tests.

## Transaction 1 - Investigator

Selected the investigator because the workspace had no evidence or model. Bounded the pass to authorization/idempotency, three traces, and fifteen calls.

The investigator produced three traces supporting same-payload replay and successful-key conflict behavior. It also observed that a declined request did not prevent a changed-payload retry from succeeding. The next role is a modeler because there is now a coherent evidence-backed region, but the decline region remains intentionally uncertain and must not be made precise merely to replay every trace.

An interactive controller would pause here to let the user choose whether to model only the successful-key region or gather more decline evidence. This unattended run selects the narrower successful-key model and retains decline behavior as an explicit frontier unknown.

## Transaction 2 - Modeler

Selected the modeler because the investigator had produced a coherent successful-key region. The modeler created revision M-001 covering identical replay and changed-payload conflicts after an observed successful authorization.

The model deliberately excludes fresh-key outcome selection and the decline region. Because Accordant currently has no first-class per-region `UNKNOWN`, the replay runner bootstraps state from an observed successful response without asking `Apply` to judge that fresh-key call. This preserves honesty but is an important bridge limitation: partial model scope and state bootstrapping are currently model-specific code rather than a shared abstraction.

All applicable evidence conformed; the decline trace remained explicitly not applicable. The next role is an adversary because M-001 contains strong generalizations beyond the exact observations, especially simultaneous amount-and-currency changes and semantic payload equivalence.

An interactive controller would pause before accepting M-001 and show the excluded steps and bootstrap mechanism. This unattended run accepts M-001 only as a partial candidate and immediately subjects it to adversarial testing.

## Transaction 3 - Adversary

Selected the adversary because M-001 had accepted applicable evidence but contained unchallenged generalizations. The adversary used its full four-trace/twenty-call budget and falsified both principal claims.

CX-1 showed that request validation precedes idempotency-conflict detection: malformed changed payloads returned 400 rather than the model's unconditional 409. CX-2 showed that an identical replay returns the current live payment record after CapturePayment, not the frozen authorization snapshot predicted by M-001.

The adversary minimized CX-1 to Reset, successful AuthorizePayment, and one malformed replay. It also separated the lifecycle-related CX-2 as a scope-boundary failure rather than conflating it with payload equality.

An interactive controller would pause here because refinement has two choices: narrow M-001's scope to exclude lifecycle operations, or incorporate the newly observed CapturePayment transition. This unattended run chooses the smallest useful cross-operation refinement: model the observed capture transition and live-record replay, while leaving VoidPayment and unobserved lifecycle regions unknown.

## Transaction 4 - Model refiner

Selected the refiner because the adversary produced minimized, classified counterexamples. M-002 added validation precedence, separated stable payment identity/data from mutable lifecycle status, modeled the single observed CapturePayment transition, and narrowed changed-payload conflict to valid payloads while the payment remains authorized.

All applicable accumulated evidence now conforms; fresh-key success/decline selection and the decline region remain excluded rather than accepted. The refiner also made model scope executable instead of leaving preconditions only in prose.

The next step is test promotion because the model now precedes the test and contains a stable modeled sequence. The selected sequence establishes a fresh successful payment as setup, then validates identical replay, capture, and post-capture replay exclusively through the model oracle.

An interactive controller would pause here to approve M-002 and the promotion candidate. This unattended run accepts the partial revision because every applicable trace conforms, both counterexamples are explained by compact rules, mutated traces are rejected, and unknown regions remain explicit.

## Transaction 5 - Test promoter

Selected test promotion only after M-002 was accepted against all applicable evidence. The promoter created one live NUnit conformance test for Reset, fresh successful authorization setup, identical replay, capture using the response-derived payment ID, and post-capture identical replay.

The fresh authorization remains explicit setup outside M-002 scope. Every behavioral step after setup is judged through `Specmine.Accordant.TraceReplayer`, which delegates to `spec.Allows`; the test contains no duplicated response-field assertions. The same live execution was recorded as a new immutable trace rather than rerun for evidence.

The promoted test passed. This completes the intended loop: evidence, partial model, adversarial falsification, minimized counterexamples, refinement preserving all applicable evidence, then a model-driven live regression sequence.

An interactive controller would show the test sequence and excluded setup call before promotion. In auto mode, promotion was allowed because the model revision had zero applicable replay violations and the sequence touched only explicit M-002 claims after setup.
