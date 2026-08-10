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
