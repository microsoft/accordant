// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

/// <summary>
/// This class represents the file layout of <see cref="TestCase"/> when serialized.
/// </summary>
public class TestCaseFileRecord
{
    /// <summary>
    /// A descriptive name for this test case.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// This field can be used by developers to add comments and additional information
    /// that's helpful to explain a test case.
    /// </summary>
    public string Comments { get; set; }
}
