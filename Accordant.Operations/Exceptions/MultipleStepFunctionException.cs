// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    /// <summary>
    /// This exception is thrown by <see cref="TestCaseExecutor"/> when it encounters
    /// a next state in which more than one step function becomes available.
    /// </summary>
    public class MultipleStepFunctionException : Exception
    {
        public MultipleStepFunctionException()
            : base("More than one step function given but no more than one is currently supported.")
        {
        }
    }
}
