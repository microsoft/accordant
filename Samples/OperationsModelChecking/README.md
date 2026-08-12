# Model checking operation specifications

`Accordant.ModelChecking.Operations` adapts a finite, request-bound
`Operation<TRequest,TResponse,TState>` to the ordinary `IStepFunction`
interface. Each mock response produced by `ExpectedOutcomes` becomes a normal
graph branch, including response-dependent state changes and spawned step
functions.

```csharp
var submit = new SubmitOperation();
var root = OperationModel.Explore(
    new JobState(),
    new[]
    {
        new OperationModelStep(
            submit.With(7, "submit-job-7"),
            state => ((JobState)state).Pending)
    });
```

The resulting `StateGraphNode` is not a special operations graph. Formula
checking, `ENABLED`, fairness, refinement, traces, and bounds use the same
machinery as hand-written step functions.

The first adapter deliberately accepts an explicit finite set of bound
requests. It does not execute the system under test, derive later requests,
poll, or use the test generator's path-dependent operation-call labels.
Operations persist by default and can be guarded; set `repeat: false` for a
one-shot input. Bound requests should be immutable because arbitrary request
objects are not part of Accordant's frozen state or graph identity.

## Composing with independently active steps

An `OperationModelStep` is an ordinary step function whose outcome is a function
of the state it is applied to, so nothing special is needed to interleave it
with a background process, an environment action, or any other hand-written
model step. `Explore` has an overload that takes them alongside the operations:

```csharp
var root = OperationModel.Explore(
    new PipelineState(),
    new[] { new OperationModelStep(enqueue.With(request, "enqueue")) },
    additionalSteps: new IStepFunction[] { new DeliverStep() });
```

The overload adds nothing to the semantics. It only extends the adapter's
duplicate-identity validation to the whole active set, because two active step
functions sharing a `StepFunctionId` would silently change graph node identity.
Steps introduced later by an operation's `Triggers` clause are produced during
exploration and are not validated here.

`OperationCompositionTests.cs` checks the interleaving, `ENABLED`, and a
liveness property that fails without fairness and holds under
`Fairness.WeakAll`. `Samples/OrderFulfillment` uses the same composition for a
transactional controller plus background payment workers, and shows the composed
graph refines the equivalent hand-written model.
