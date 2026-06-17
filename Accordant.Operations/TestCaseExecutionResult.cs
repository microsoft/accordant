// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

/// <summary>
/// This class contains the result of executing a test case.
/// </summary>
public class TestCaseExecutionResult
{
    /// <summary>
    /// This indicates whether the test case executed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// This is the last failure message encountered during test case execution
    /// if it did not execute successfully.
    /// </summary>
    public string LastFailureMessage { get; set; }

    /// <summary>
    /// The path to the log file containing more details about the test case.
    /// The log file may contain information about other test cases as well.
    /// </summary>
    public string LogFilePath { get; set; }
}
