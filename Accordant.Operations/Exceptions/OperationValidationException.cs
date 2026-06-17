// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
public class OperationValidationException : Exception
{
    public OperationValidationException(string message)
        : base(message)
    {
    }
}
