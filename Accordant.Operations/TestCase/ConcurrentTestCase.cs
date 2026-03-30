// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;

    public class ConcurrentTestCase : TestCase
    {
        public IList<OperationCall> SequentialOperationCalls { get; set; }

        public IList<OperationCall> ConcurrentOperationCalls { get; set; }
    }
}
