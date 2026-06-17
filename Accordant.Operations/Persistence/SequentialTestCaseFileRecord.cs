// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

/// <summary>
/// This class represents the file layout of <see cref="SequentialTestCase"/> when serialized.
/// </summary>
public class SequentialTestCaseFileRecord : TestCaseFileRecord
{
    public IList<OperationCallFileRecord> OperationCalls { get; set; }
}
