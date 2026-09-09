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

    private IReadOnlyCollection<string> _predecessorIds;

    public ContractStepFunction(
        object request,
        object observedResponse,
        VerifyFunc verify,
        IReadOnlyCollection<string> predecessorIds = null)
    {
        Request = request;
        ObservedResponse = observedResponse;
        Verify = verify ?? throw new ArgumentNullException(nameof(verify));
        _predecessorIds = NormalizePredecessorIds(predecessorIds);
    }

    /// <summary>
    /// Sets the happens-before predecessor IDs after construction.
    /// Used to wire edges without replacing the step object (which would change its GUID).
    /// </summary>
    public void SetPredecessorIds(IReadOnlyCollection<string> ids)
    {
        if (HasBeenApplied)
        {
            throw new InvalidOperationException(
                "Predecessor IDs cannot be changed after the contract step function has been applied.");
        }

        _predecessorIds = NormalizePredecessorIds(ids);
    }

    private static IReadOnlyCollection<string> NormalizePredecessorIds(
        IReadOnlyCollection<string> ids)
    {
        if (ids == null)
        {
            return Array.Empty<string>();
        }

        var snapshot = ids.ToList();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in snapshot)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException(
                    "Predecessor IDs cannot contain null or empty values.",
                    nameof(ids));
            }

            if (!seenIds.Add(id))
            {
                throw new ArgumentException(
                    $"Predecessor IDs contain duplicate ID '{id}'.",
                    nameof(ids));
            }
        }

        return snapshot.AsReadOnly();
    }

    private static void ValidateVerifiedStateProfile(StateProfile stateProfile)
    {
        if (stateProfile == null)
        {
            throw new InvalidOperationException(
                "A verification result marked valid must include a non-null StateProfile.");
        }

        if (stateProfile.StatesAndStepFunctions == null ||
            stateProfile.StatesAndStepFunctions.Count == 0)
        {
            throw new InvalidOperationException(
                "A valid verification result must include at least one state outcome.");
        }

        foreach (var (state, stepFunctions) in stateProfile.StatesAndStepFunctions)
        {
            if (state == null)
            {
                throw new InvalidOperationException(
                    "A valid verification result cannot include a null state outcome.");
            }

            if (stepFunctions == null)
            {
                throw new InvalidOperationException(
                    "A valid verification result cannot include a null step-function collection.");
            }

            StateGraph.ValidateStepFunctionList(
                stepFunctions,
                "A valid verification result");
        }
    }

    protected override IList<StepResult> ApplyInternal(
        IState state,
        IReadOnlyList<(IStepFunction, StateGraphNode)> path)
    {
        // ponytail: gate on happens-before predecessors; disabled until all are in path.
        if (_predecessorIds.Count > 0)
        {
            var appliedIds = new HashSet<string>(path.Select(p => p.Item1?.StepFunctionId).Where(id => id != null));
            if (!_predecessorIds.All(id => appliedIds.Contains(id)))
                return null;
        }

        var (valid, stateProfile) = Verify(
            Request,
            state,
            ObservedResponse);

        if (!valid)
        {
            return null;
        }

        ValidateVerifiedStateProfile(stateProfile);

        return
            stateProfile.StatesAndStepFunctions.Select(stateAndStepFunctions => new StepResult()
            {
                State = stateAndStepFunctions.State,
                StepFunctions = stateAndStepFunctions.StepFunctions
            }).ToList();
    }
}
