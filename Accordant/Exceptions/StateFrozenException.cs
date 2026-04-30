// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    public class StateFrozenException : Exception
    {
        public StateFrozenException(string message = "State is frozen and cannot be modified.") : base(message)
        {
        }
    }
}
