// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;

    /// <summary>
    /// This class represents the file layout of <see cref="ConcurrentTestCase"/> when serialized.
    /// </summary>
    public class ConcurrentTestCaseFileRecord : TestCaseFileRecord
    {
        /// <summary>
        /// List of sequential operation calls that are executed before the concurrent calls.
        /// </summary>
        public IList<OperationCallFileRecord> SequentialOperationCalls { get; set; }

        /// <summary>
        /// List of concurrent operation calls executed after the sequential operation calls.
        /// </summary>
        public IList<OperationCallFileRecord> ConcurrentOperationCalls { get; set; }
    }
}
