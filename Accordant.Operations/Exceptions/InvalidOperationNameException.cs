// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

public class InvalidOperationNameException : Exception
{
    public InvalidOperationNameException(string message) : base(message)
    {
    }
}
