// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    /// <summary>
    /// Configuration for polling behavior when an operation triggers background work
    /// via a <see cref="TerminatingStepFunction"/>.
    /// 
    /// Polling uses a derivation to create the polling request from the original
    /// operation's request and response. The derivation must be defined on the
    /// polling operation's <see cref="IOperation.DerivedFrom"/> property.
    /// </summary>
    public class PollingSetup
    {
        /// <summary>
        /// The name of the operation to call for polling (e.g., "GetJob").
        /// This operation must have a derivation defined that derives from the
        /// source operation.
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// The derivation variant to use when creating the polling request.
        /// Defaults to "Default". Must match a variant defined in the polling
        /// operation's <see cref="IOperation.DerivedFrom"/> derivations.
        /// </summary>
        public string Variant { get; set; } = DerivationLabels.Default;

        /// <summary>
        /// The delay in milliseconds between each polling attempt.
        /// </summary>
        public int WaitTimeInMs { get; set; } = 1000;

        /// <summary>
        /// The maximum number of times to retry. If the background asynchrony still
        /// hasn't terminated after these many retries, the system is deemed to have
        /// a 'liveness bug' indicating the system is stuck and not making forward progress.
        /// </summary>
        public int MaxRetryCount { get; set; } = 50;

        /// <summary>
        /// Creates a deep clone of this polling setup.
        /// </summary>
        public PollingSetup Clone()
        {
            return new PollingSetup()
            {
                Operation = Operation,
                Variant = Variant,
                WaitTimeInMs = WaitTimeInMs,
                MaxRetryCount = MaxRetryCount
            };
        }
    }
}
