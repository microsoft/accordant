// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A contract step function uses a contract, request and observed response to define
    /// a step function. It is enabled if the contract deems the observed response to be
    /// valid given the request and the state on which the step function is evaluated.
    /// If the contract step function is valid (thus enabled), it invokes the contract passing
    /// in the request, state and observed state to product the updated state and an optional
    /// step function that runs concurrently with the rest of the system.
    /// </summary>
    public class ContractStepFunction : BaseStepFunction
    {
        public object Request { get; private set; }

        public object ObservedResponse { get; private set; }

        public IContract Contract { get; private set; }

        public ContractStepFunction(
            object request,
            object observedResponse,
            IContract contract)
        {
            Request = request;
            ObservedResponse = observedResponse;
            Contract = contract;
        }

        protected override IList<StepResult> ApplyInternal(State state)
        {
            var (valid, stateProfile) = Contract.Verify(
                Request,
                state,
                ObservedResponse);

            if (!valid)
            {
                return null;
            }
            else
            {
                return
                    stateProfile.StatesAndStepFunctions.Select(stateAndStepFunctions => new StepResult()
                    {
                        State = stateAndStepFunctions.State,
                        StepFunctions = stateAndStepFunctions.StepFunctions
                    }).ToList();
            }
        }
    }
}
