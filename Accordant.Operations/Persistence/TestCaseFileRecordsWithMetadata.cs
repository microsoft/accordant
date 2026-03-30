// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;

    public class TestCaseFileRecordsWithMetadata<TMetadata, TTestCaseFileRecord>
        where TMetadata : ITestingMetadata
        where TTestCaseFileRecord : TestCaseFileRecord
    {
        public TMetadata Metadata { get; set; }

        public List<TTestCaseFileRecord> TestCases { get; set; }
    }
}
