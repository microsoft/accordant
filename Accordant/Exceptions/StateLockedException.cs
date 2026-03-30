// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    public class StateLockedException : Exception
    {
        public StateLockedException(string message = "State is locked for modifications.") : base(message)
        {
        }
    }
}
