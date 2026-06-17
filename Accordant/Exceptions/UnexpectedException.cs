// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

public class UnexpectedException : Exception
{
    public UnexpectedException(string message = null) : base(message)
    {
    }
}
