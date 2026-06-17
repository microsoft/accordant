// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

public class ConcurrentTestCase : TestCase
{
    /// <summary>
    /// The segments of this concurrent test case. Each segment contains one or more
    /// operation calls. Segments with a single call are executed sequentially;
    /// segments with multiple calls are executed concurrently.
    /// </summary>
    public IList<TestCaseSegment> Segments { get; set; }
}
