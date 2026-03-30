// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    public class EmptyExpectedOutcomesException : Exception
    {
        public EmptyExpectedOutcomesException()
            : base("Expected outcomes object must have at least one expected outcome")
        {
        }
    }
}
