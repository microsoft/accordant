namespace Microsoft.Accordant.ModelChecking.Operations;

using System;
using System.Collections.Generic;

/// <summary>
/// Adapts one request-bound <see cref="OperationInput"/> to an ordinary
/// <see cref="IStepFunction"/>. The operation's finite mock responses and
/// response-dependent state profiles become ordinary nondeterministic
/// <see cref="StepResult"/> values.
/// </summary>
public sealed class OperationModelStep : BaseStepFunction
{
    private readonly Func<IState, bool> enabled;
    private readonly string stepFunctionId;

    /// <summary>
    /// Creates a model step for one operation input.
    /// </summary>
    /// <param name="input">The operation and finite request to model.</param>
    /// <param name="enabled">
    /// Optional source-state guard. Operations do not otherwise have a
    /// model-checking enabledness predicate.
    /// </param>
    /// <param name="repeat">
    /// Whether the operation remains available after it is taken. When false,
    /// the ordinary step-function consumption rule makes it one-shot.
    /// </param>
    public OperationModelStep(
        OperationInput input,
        Func<IState, bool> enabled = null,
        bool repeat = true)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new ArgumentException(
                "A model-checked operation input requires a stable name.",
                nameof(input));
        }
        if (input.Operation == null)
        {
            throw new ArgumentException(
                "A model-checked operation input requires an operation.",
                nameof(input));
        }

        this.enabled = enabled ?? (_ => true);
        OperationName = input.Name;
        Operation = input.Operation;
        Request = input.Request;
        stepFunctionId = $"operation:{OperationName}";
        Repeat = repeat;
    }

    /// <summary>The stable name of the bound operation input.</summary>
    public string OperationName { get; }

    /// <summary>The operation represented by this step.</summary>
    public IOperation Operation { get; }

    /// <summary>
    /// The request represented by this step. Requests should be immutable:
    /// arbitrary operation request objects do not participate in Accordant's
    /// state freezing or graph identity.
    /// </summary>
    public object Request { get; }

    /// <summary>Whether this input remains enabled after one invocation.</summary>
    public bool Repeat { get; }

    /// <summary>
    /// Stable action identity. Unlike the test-generation adapter, this never
    /// uses path-dependent counters or generated call labels.
    /// </summary>
    public override string StepFunctionId => stepFunctionId;

    /// <inheritdoc/>
    protected override IList<StepResult> ApplyInternal(IState state)
    {
        if (!enabled(state))
        {
            return null;
        }

        var outcomes = Operation.Invoke(Request, state);
        if (outcomes == null || outcomes.Count == 0)
        {
            return null;
        }

        var results = new List<StepResult>();
        foreach (var (response, profile) in outcomes)
        {
            if (profile?.StatesAndStepFunctions == null)
            {
                throw new InvalidOperationException(
                    $"Operation '{OperationName}' returned a null state profile.");
            }

            foreach (var (nextState, spawned) in profile.StatesAndStepFunctions)
            {
                if (nextState == null)
                {
                    throw new InvalidOperationException(
                        $"Operation '{OperationName}' returned a null next state.");
                }

                var nextSteps = new List<IStepFunction>();
                if (spawned != null)
                {
                    foreach (var step in spawned)
                    {
                        if (step != null)
                        {
                            nextSteps.Add(step);
                        }
                    }
                }
                if (Repeat)
                {
                    nextSteps.Add(this);
                }

                results.Add(new StepResult
                {
                    State = nextState,
                    StepFunctions = nextSteps,
                    EdgeMetadata = new OperationModelTransition(
                        OperationName,
                        Operation,
                        Request,
                        response)
                });
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public override string ToString() => OperationName;
}
