// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

/// <summary>
/// This class represents the file layout of <see cref="ConcurrentTestCase"/> when serialized.
/// </summary>
public class ConcurrentTestCaseFileRecord : TestCaseFileRecord
{
    /// <summary>
    /// The segments of this concurrent test case when serialized.
    /// </summary>
    public IList<TestCaseSegmentFileRecord> Segments { get; set; }
}
