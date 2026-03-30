// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    public class TestCasePersistenceException : Exception
    {
        public TestCasePersistenceException(string message)
            : base(message)
        {
        }
    }
}
