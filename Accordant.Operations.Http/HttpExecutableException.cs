// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    public class HttpExecutableException : Exception
    {
        public HttpExecutableException(string message) : base(message)
        {
        }
    }
}
