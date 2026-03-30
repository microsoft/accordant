// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    /// <summary>
    /// This exception is thrown when the user expected the system to transition
    /// to a single state but the system can transition to multiple states.
    /// </summary>
    public class MultipleStateException : Exception
    {
        public MultipleStateException(string message = "") : base(message)
        {
        }
    }
}
