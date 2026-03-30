// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Non-generic interface for specs, allowing type-erased usage.
    /// </summary>
    public interface ISpec
    {
        /// <summary>
        /// The operations registered in this spec.
        /// </summary>
        IEnumerable<IOperation> Operations { get; }

        /// <summary>
        /// Indexer to get/set operations by name.
        /// </summary>
        IOperation this[string name] { get; set; }

        /// <summary>
        /// Registers an operation with this spec.
        /// </summary>
        void Add(IOperation operation);

        /// <summary>
        /// Gets an operation by name.
        /// </summary>
        IOperation GetOperation(string name);

        /// <summary>
        /// Gets the name the given operation was registered under.
        /// </summary>
        string GetOperationName(IOperation operation);

        /// <summary>
        /// Creates a new testing context for this spec.
        /// </summary>
        /// <param name="testDirectoryPath">Optional path for test output.</param>
        /// <returns>A new testing context.</returns>
        TestingContext CreateTestingContext(string testDirectoryPath = null);
    }
}
