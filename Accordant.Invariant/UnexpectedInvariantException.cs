// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    public class UnexpectedInvariantException : Exception
    {
        public UnexpectedInvariantException(string messge = null) : base(messge)
        {
        }
    }
}
