// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    /// <summary>
    /// A contract indicates whether the observed response is valid given
    /// the request and state. If the observed response is valid, the contract
    /// returns a (potentially) updated state as well as an optional step function
    /// that runs concurrently with the rest of the system.
    /// 
    /// The contract atomically changes the starting state to the updated state,
    /// if the response is deemed valid given the request and starting state.
    /// </summary>
    public interface IContract
    {
        /// <summary>
        /// Indicates whether the observed response is valid given the request
        /// and state. If valid, it also returns a (potentially) updated states
        /// as well as an optional step function that runs concurrently with the
        /// rest of the system.
        /// </summary>
        public (bool, StateProfile) Verify(
            object request,
            State state,
            object observedResponse);

        /// <summary>
        /// This method returns an explanation of why the observed response
        /// did not match the expected response.
        /// </summary>
        public string ExplainInvalidResponse(
            object request,
            State state,
            object observedResponse);

        /// <summary>
        /// Indicates whether the observed response is valid given the request
        /// and state profile. If valid, it also returns a (potentially) updated states
        /// as well as an optional step function that runs concurrently with the
        /// rest of the system.
        /// </summary>
        public (bool, StateProfile) Verify(
            object request,
            StateProfile stateProfile,
            object observedResponse);

        /// <summary>
        /// This method returns an explanation of why the observed response
        /// did not match the expected response.
        /// </summary>
        public string ExplainInvalidResponse(
            object request,
            StateProfile stateProfile,
            object observedResponse);
    }

    /// <inheritdoc/>
    public interface IContract<TRequest, TResponse> : IContract
    {
        /// <inheritdoc/>
        public (bool, StateProfile) Verify(
            TRequest request,
            State state,
            object observedResponse);

        /// <inheritdoc/>
        public string ExplainInvalidResponse(
            TRequest request,
            State state,
            object observedResponse);
    }
}
