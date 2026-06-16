// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

public class SequentialTestCase : TestCase
{
    public IList<OperationCall> OperationCalls { get; set; }
}
