// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// This class represents an operation bound with a request,
    /// or an operation whose request can be derived from another
    /// operation call.
    /// </summary>
    public class OperationInput
    {
        /// <summary>
        /// The name of the operation.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The operation associated with this input.
        /// </summary>
        public IOperation Operation { get; set; }

        /// <summary>
        /// The request to use when invoking the operation.
        ///
        /// If <see cref="DerivedFromOperationCalls"/> is set, then this value
        /// is passed to the derivation lambda as a template that it can
        /// use as a base value to build on top of.
        /// </summary>
        public object Request { get; set; }

        /// <summary>
        /// The operation calls whose request/response this operation's request
        /// is derived from.
        /// </summary>
        public IList<OperationCall> DerivedFromOperationCalls { get; set; }

        /// <summary>
        /// The variant label if this operation's request is derived from
        /// other operations. The label is used to identify which variant
        /// was generated (e.g., "IfMatch" vs "IfNoneMatch").
        /// </summary>
        public string DerivationVariant { get; set; }

        /// <summary>
        /// The polling setup for this input. If set, overrides the operation's
        /// <see cref="IOperation.Polling"/> property.
        /// </summary>
        public PollingSetup Polling { get; set; }

        /// <summary>
        /// If true, polling is skipped for this input even if the operation
        /// has polling configured.
        /// </summary>
        public bool SkipPolling { get; set; }

        private OperationInput()
        {
        }

        /// <summary>
        /// Constructs an instance of this class given a name and an operation.
        /// The request is set to an instance of the <see cref="Unit"/> object to
        /// denote the fact that this operation does not take any request.
        /// </summary>
        public OperationInput(
            string name,
            IOperation operation)
        {
            Name = name;
            Operation = operation;
            Request = Unit.Value;
        }

        /// <summary>
        /// Constructs an instance of this class given a name, an operation
        /// and request.
        /// </summary>
        public OperationInput(
            string name,
            IOperation operation,
            object request)
        {
            Name = name;
            Operation = operation;
            Request = request;
        }

        /// <summary>
        /// Constructs an instance of this class given a name, an operation
        /// and the operation call that this request is derived from.
        /// </summary>
        public OperationInput(
            string name,
            IOperation operation,
            IList<OperationCall> derivedFromOperationCalls,
            string derivationVariant = DerivationLabels.Default)
        {
            Name = name;
            Operation = operation;
            DerivedFromOperationCalls = derivedFromOperationCalls;
            DerivationVariant = derivationVariant;
        }

        /// <summary>
        /// Constructs an instance of this class given a name, an operation,
        /// a template request and the operation calls this request is derived from.
        /// </summary>
        public OperationInput(
            string name,
            IOperation operation,
            object request,
            IList<OperationCall> derivedFromOperationCalls,
            string derivationVariant = DerivationLabels.Default)
        {
            Name = name;
            Operation = operation;
            Request = request;
            DerivedFromOperationCalls = derivedFromOperationCalls;
            DerivationVariant = derivationVariant;
        }

        /// <summary>
        /// Sets the polling setup for this input and returns the input for fluent chaining.
        /// This overrides the operation's default polling setup.
        /// </summary>
        /// <param name="polling">The polling setup to use for this input.</param>
        /// <returns>This input for fluent chaining.</returns>
        public OperationInput WithPolling(PollingSetup polling)
        {
            Polling = polling;
            return this;
        }

        /// <summary>
        /// Disables polling for this input, even if the operation has polling configured.
        /// </summary>
        /// <returns>This input for fluent chaining.</returns>
        public OperationInput WithoutPolling()
        {
            SkipPolling = true;
            return this;
        }

        /// <summary>
        /// Performs a selective deep clone of this object.
        /// </summary>
        public OperationInput Clone()
        {
            return new OperationInput()
            {
                Name = Name,
                Operation = Operation,
                Request = Request,
                Polling = Polling?.Clone(),
                SkipPolling = SkipPolling,
                DerivationVariant = DerivationVariant,
                DerivedFromOperationCalls = DerivedFromOperationCalls?.Select(c => c.Clone()).ToList()
            };
        }

        public static string ConstructDerivedOperationName(
            string sourceOperationCallName,
            string derivedOperationName,
            string derivationVariant)
        {
            if (derivationVariant == DerivationLabels.Default)
            {
                return $"({sourceOperationCallName} -> {derivedOperationName})";
            }
            else
            {
                return $"({sourceOperationCallName} -> {derivedOperationName}: {derivationVariant})";
            }
        }
    }
}
