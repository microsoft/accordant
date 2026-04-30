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
    /// </summary>
    public interface IOperation
    {
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

        /// <summary>
        /// Returns all possible next states and optional step functions given a request and state.
        /// </summary>
        IList<(object, StateProfile)> Invoke(object request, IState state);

        /// <summary>
        /// Indicates whether the observed response is valid given the request and state.
        /// If valid, it also returns a (potentially) updated state as well as an optional
        /// step function that runs concurrently with the rest of the system.
        /// </summary>
        (bool, StateProfile) Verify(
            object request,
            IState state,
            object observedResponse);

        /// <summary>
        /// Returns an explanation of why the observed response did not match the expected response.
        /// </summary>
        string ExplainInvalidResponse(
            object request,
            IState state,
            object observedResponse);

        /// <summary>
        /// Indicates whether the observed response is valid given the request and state profile.
        /// If valid, it also returns a (potentially) updated state as well as an optional
        /// step function that runs concurrently with the rest of the system.
        /// </summary>
        (bool, StateProfile) Verify(
            object request,
            StateProfile stateProfile,
            object observedResponse);

        /// <summary>
        /// Returns an explanation of why the observed response did not match the expected response.
        /// </summary>
        string ExplainInvalidResponse(
            object request,
            StateProfile stateProfile,
            object observedResponse);

        /// <summary>
        /// Executes the operation against the system and returns the response.
        /// Error conditions such as exceptions, network timeouts, internal server errors etc
        /// should be converted to appropriate 'error' response objects and returned to the caller.
        /// </summary>
        /// <param name="context">The testing context.</param>
        /// <param name="request">The request to execute.</param>
        /// <returns>The response from execution.</returns>
        Task<object> ExecuteAsync(TestingContext context, object request);
    }
}
