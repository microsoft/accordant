// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    /// <summary>
    /// This class represents an invocation of an operation.
    /// </summary>
    public class OperationCall
    {
        /// <summary>
        /// A unique name for this operation call.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The operation input along with its associated request whose call
        /// is represented by this object.
        /// </summary>
        public OperationInput OperationInput { get; set; }

        public OperationCall(
            string name,
            OperationInput operationInput)
        {
            Name = name;
            OperationInput = operationInput;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// Returns a selective deep clone of this object.
        /// </summary>
        public OperationCall Clone()
        {
            return new OperationCall(
                Name,
                OperationInput.Clone());
        }
    }
}
