// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

public class InvalidSpecException : Exception
{
    public InvalidSpecException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}
