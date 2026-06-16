// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

/// <summary>
/// Represents a segment within a concurrent test case. A segment contains
/// one or more operation calls. If the segment contains a single operation
/// call, it is executed sequentially. If it contains multiple operation calls,
/// they are executed concurrently.
/// </summary>
public class TestCaseSegment
{
    /// <summary>
    /// The operation calls in this segment. A single call means sequential
    /// execution; multiple calls means concurrent execution.
    /// </summary>
    public IList<OperationCall> OperationCalls { get; set; }

    /// <summary>
    /// Creates a new segment with the given operation calls.
    /// </summary>
    public TestCaseSegment(IList<OperationCall> operationCalls)
    {
        OperationCalls = operationCalls;
    }

    /// <summary>
    /// Creates a new segment with a single operation call (sequential).
    /// </summary>
    public TestCaseSegment(OperationCall operationCall)
    {
        OperationCalls = new List<OperationCall> { operationCall };
    }

    /// <summary>
    /// Returns true if this segment has a single operation call (sequential execution).
    /// </summary>
    public bool IsSequential => OperationCalls.Count == 1;

    /// <summary>
    /// Returns true if this segment has multiple operation calls (concurrent execution).
    /// </summary>
    public bool IsConcurrent => OperationCalls.Count > 1;
}
