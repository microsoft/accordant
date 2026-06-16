// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

public class PollingException : Exception
{
    public PollingException(string message)
        : base(message)
    {
    }
}
