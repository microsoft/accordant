// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    /// <summary>
    /// This interface should be implemented by classes that store metadata that
    /// is persisted along with serialized test cases to disk that contains the
    /// information required to re-run those test cases (information that might
    /// not be contained in the test cases themselves).
    /// </summary>
    public interface ITestingMetadata
    {
    }
}
