// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

public class TestCaseGenerationException : Exception
{
    public TestCaseGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
