// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    /// <summary>
    /// This class represents the file layout of an <see cref="OperationCall"/> when serialized.
    /// </summary>
    public class OperationCallFileRecord
    {
        /// <summary>
        /// A unique name for this operation call.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The associated input for this call.
        /// </summary>
        public InputFileRecord Input { get; set; }
    }
}
