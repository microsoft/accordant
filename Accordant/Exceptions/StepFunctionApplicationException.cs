// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// This is an exception thrown during state graph exploration
/// where applying a step function on the state in a state graph node
/// leads to an exception thrown from the step function application code.
/// The step function application code is written by users of this framework
/// and can throw arbitrary exceptions due to bugs. That exception is caught
/// and wrapped up in this exception. It also contains a path from the root node
/// to the target node where the exception was thrown that can help in
/// debugging the issue.
/// </summary>
public class StepFunctionApplicationException : Exception
{
    /// <summary>
    /// The state graph node at which applying one of its step
    /// functions lead to the exception.
    /// </summary>
    public StateGraphNode ExceptionEncounteringNode { get; }

    /// <summary>
    /// The path from the root node to the node at which the exception
    /// was encountered. The initial step function is null for the starting node.
    /// </summary>
    public IReadOnlyList<(IStepFunction stepFunction, StateGraphNode node)> PathToNode { get; }

    /// <summary>
    /// The step function that lead to the exception.
    /// </summary>
    public IStepFunction ExceptionEncounteringStepFunction { get; }

    public StepFunctionApplicationException(
        Exception exception,
        StateGraphNode node,
        IReadOnlyList<(IStepFunction stepFunction, StateGraphNode node)> pathToNode,
        IStepFunction stepFunction)
        : base("Encountered an exception when applying a step function at a node", exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        ExceptionEncounteringNode = node ?? throw new ArgumentNullException(nameof(node));
        PathToNode = (pathToNode ?? throw new ArgumentNullException(nameof(pathToNode)))
            .ToList()
            .AsReadOnly();
        ExceptionEncounteringStepFunction = stepFunction ??
            throw new ArgumentNullException(nameof(stepFunction));
    }
}
