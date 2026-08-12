namespace Microsoft.Accordant.ModelChecking.Operations;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Entry points for exploring Accordant operation models with the ordinary
/// state-graph and model-checking APIs.
/// </summary>
public static class OperationModel
{
    /// <summary>
    /// Explores a finite set of request-bound operation inputs.
    /// </summary>
    public static StateGraphNode Explore<TState>(
        TState initialState,
        IEnumerable<OperationModelStep> operations,
        int maxDepth = -1,
        bool lazy = false)
        where TState : State
        => Explore(
            initialState,
            operations,
            additionalSteps: null,
            maxDepth: maxDepth,
            lazy: lazy);

    /// <summary>
    /// Explores a finite set of request-bound operation inputs together with
    /// independently active step functions — background workers, environment
    /// actions, or any other hand-written model process.
    ///
    /// <para>Composition needs no special support: an
    /// <see cref="OperationModelStep"/> is an ordinary
    /// <see cref="IStepFunction"/> whose result is a function of the state it is
    /// applied to, so the ordinary exploration rules already interleave it with
    /// every other active step. This overload only adds the same
    /// duplicate-identity validation the operation inputs already get. A repeated
    /// <see cref="IStepFunction.StepFunctionId"/> would both split graph identity
    /// and cause applying either instance to consume every active instance with
    /// that id.</para>
    /// </summary>
    /// <param name="initialState">The initial model state.</param>
    /// <param name="operations">The bound operation inputs.</param>
    /// <param name="additionalSteps">
    /// Step functions that are active from the initial state alongside the
    /// operations. Operations may also introduce further steps through their
    /// <c>Triggers</c> clause; those are produced during exploration and are
    /// therefore not validated here.
    /// </param>
    /// <param name="maxDepth">The exploration depth bound, or -1 for unbounded.</param>
    /// <param name="lazy">Whether the graph is expanded on demand.</param>
    public static StateGraphNode Explore<TState>(
        TState initialState,
        IEnumerable<OperationModelStep> operations,
        IEnumerable<IStepFunction> additionalSteps,
        int maxDepth = -1,
        bool lazy = false)
        where TState : State
    {
        if (initialState == null)
        {
            throw new ArgumentNullException(nameof(initialState));
        }
        if (operations == null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

        var steps = new List<IStepFunction>();
        foreach (var step in operations)
        {
            if (step == null)
            {
                throw new ArgumentException(
                    "Operation model steps cannot contain null.",
                    nameof(operations));
            }

            steps.Add(step);
        }

        var duplicate = steps
            .GroupBy(step => step.StepFunctionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new ArgumentException(
                $"Operation model step id '{duplicate.Key}' is not unique. " +
                "Give each bound operation input a distinct name.",
                nameof(operations));
        }

        if (additionalSteps != null)
        {
            foreach (var step in additionalSteps)
            {
                if (step == null)
                {
                    throw new ArgumentException(
                        "Additional model steps cannot contain null.",
                        nameof(additionalSteps));
                }

                steps.Add(step);
            }

            var collision = steps
                .GroupBy(step => step.StepFunctionId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (collision != null)
            {
                throw new ArgumentException(
                    $"Model step id '{collision.Key}' is not unique in the composed " +
                    "model. Two active step functions sharing an id would split " +
                    "graph identity and be consumed together.",
                    nameof(additionalSteps));
            }
        }

        return StateGraph.ExploreStateGraph(
            steps,
            initialState,
            maxDepth: maxDepth,
            lazy: lazy);
    }
}
