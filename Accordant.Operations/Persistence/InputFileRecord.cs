// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;

    /// <summary>
    /// This class represents the file layout of an <see cref="OperationInput"/> when serialized.
    /// </summary>
    public class InputFileRecord
    {
        /// <summary>
        /// The name of the <see cref="IOperation"/> under which it is
        /// registered with the <see cref="Spec"/>. The spec
        /// itself is not serialized and the spec must contain an operation
        /// with this name when this file record is deserialized.
        /// </summary>
        public string OperationName { get; set; }

        /// <summary>
        /// A unique name representing this input.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Serialized request for this operation.
        /// </summary>
        public string SerializedRequest { get; set; }

        /// <summary>
        /// The names of the operation calls whose request/response can be used to
        /// determine the request for this operation.
        /// </summary>
        public IList<string> DerivedFromOperationCalls { get; set; }

        /// <summary>
        /// The variant label if this operation's request is derived from
        /// the requests/responses of other operations. The label is used to identify
        /// the right derivation in <see cref="IOperation.DerivedFrom"/>
        /// in case the operation can produce more than one derived request.
        /// </summary>
        public string DerivationVariant { get; set; }

        /// <summary>
        /// The polling setup for this operation input.
        /// If set, overrides the operation's <see cref="IOperation.Polling"/> property.
        /// </summary>
        public PollingSetup Polling { get; set; }

        /// <summary>
        /// If true, polling is skipped for this input even if the operation
        /// has polling configured.
        /// </summary>
        public bool SkipPolling { get; set; }
    }
}
