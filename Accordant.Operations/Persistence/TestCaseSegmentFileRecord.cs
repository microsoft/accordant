// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

/// <summary>
/// This class represents the file layout of <see cref="TestCaseSegment"/> when serialized.
/// </summary>
public class TestCaseSegmentFileRecord
{
    /// <summary>
    /// The operation calls in this segment.
    /// </summary>
    public IList<OperationCallFileRecord> OperationCalls { get; set; }
}
