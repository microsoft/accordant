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
    {
        if (initialState == null)
        {
            throw new ArgumentNullException(nameof(initialState));
        }
        if (operations == null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

        var steps = operations.ToArray();
        if (steps.Any(step => step == null))
        {
            throw new ArgumentException(
                "Operation model steps cannot contain null.",
                nameof(operations));
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

        return StateGraph.ExploreStateGraph(
            steps,
            initialState,
            maxDepth: maxDepth,
            lazy: lazy);
    }
}
