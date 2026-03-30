// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Non-generic interface for operations, allowing type-erased storage in collections.
    /// This interface is implemented by <see cref="Operation{TReq, TResp, TState}"/>.
    /// 
    /// An IOperation provides Model (state exploration), Contract (response validation),
    /// and Execution functionality.
    /// </summary>
    public interface IOperation : IContract
    {
        /// <summary>
        /// Returns all possible (response, next-state) pairs for state exploration.
        /// Used during test generation to explore the state space.
        /// </summary>
        /// <param name="request">The request to apply.</param>
        /// <param name="state">The current state.</param>
        /// <returns>List of possible (response, stateProfile) pairs.</returns>
        IList<(object, StateProfile)> Invoke(object request, State state);

        /// <summary>
        /// Executes the operation against the real system.
        /// </summary>
        /// <param name="context">The testing context.</param>
        /// <param name="request">The request to execute.</param>
        /// <returns>The response from execution.</returns>
        Task<object> ExecuteAsync(TestingContext context, object request);

        /// <summary>
        /// The name of the operation.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The <see cref="Type"/> of the request sent to this operation.
        /// </summary>
        Type RequestType { get; }

        /// <summary>
        /// The <see cref="Type"/> of the response received from this operation.
        /// </summary>
        Type ResponseType { get; }

        /// <summary>
        /// The request derivations for this operation.
        /// Defines how this operation's requests can be derived from other operations' responses.
        /// </summary>
        IReadOnlyList<RequestDerivation> DerivedFrom { get; }

        /// <summary>
        /// The polling setup for this operation.
        /// Defines how to poll for completion when this operation triggers a <see cref="TerminatingStepFunction"/>.
        /// The polling request is created using a derivation defined on the polling operation.
        /// </summary>
        PollingSetup Polling { get; }

        /// <summary>
        /// Creates an <see cref="OperationInput"/> with the given request bound to this operation.
        /// </summary>
        /// <param name="request">The request to bind.</param>
        /// <param name="label">An optional label for the input.</param>
        /// <returns>An <see cref="OperationInput"/> instance.</returns>
        OperationInput With(object request, string label = null);
    }
}
