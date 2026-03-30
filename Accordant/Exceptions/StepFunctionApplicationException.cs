// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;

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
        public StateGraphNode ExceptionEncounteringNode { get; set; }

        /// <summary>
        /// The path from the root node to the node at which the exception
        /// was encountered. The initial step function is null for the starting node.
        /// </summary>
        public IList<(IStepFunction stepFunction, StateGraphNode node)> PathToNode { get; set; }

        /// <summary>
        /// The step function that lead to the exception.
        /// </summary>
        public IStepFunction ExceptionEncounteringStepFunction { get; set; }

        public StepFunctionApplicationException(
            Exception exception,
            StateGraphNode node,
            IList<(IStepFunction stepFunction, StateGraphNode node)> pathToNode,
            IStepFunction stepFunction)
            : base("Encountered an exception when applying a step function at a node", exception)
        {
            ExceptionEncounteringNode = node;
            PathToNode = pathToNode;
            ExceptionEncounteringStepFunction = stepFunction;
        }
    }
}
