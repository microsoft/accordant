// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A delegate that verifies whether an observed response is valid given a request and state.
/// Returns (isValid, stateProfile) where stateProfile contains the updated state(s) if valid.
/// </summary>
public delegate (bool IsValid, StateProfile StateProfile) VerifyFunc(
    object request,
    IState state,
    object observedResponse);

/// <summary>
/// A step function that verifies an observed response against an expected behavior.
/// It is enabled if the verify function deems the observed response to be valid given
/// the request and the state on which the step function is evaluated.
/// If valid, it produces the updated state and any optional step functions that run
/// concurrently with the rest of the system.
/// </summary>
public class ContractStepFunction : BaseStepFunction
{
    public object Request { get; private set; }

    public object ObservedResponse { get; private set; }

    public VerifyFunc Verify { get; private set; }

    public ContractStepFunction(
        object request,
        object observedResponse,
        VerifyFunc verify)
    {
        Request = request;
        ObservedResponse = observedResponse;
        Verify = verify ?? throw new ArgumentNullException(nameof(verify));
    }

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var (valid, stateProfile) = Verify(
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
