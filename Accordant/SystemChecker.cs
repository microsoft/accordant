// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// This class validates the system behavior by explaining it in terms of
/// stateful contracts.
/// </summary>
public class SystemChecker
{
    public static StateProfile Validate(
        IList<IList<IStepFunction>> sequenceOfConcurrentSteps,
        IState startingState,
        Action<IState, IList<IStepFunction>> hook = null)
    {
        return Validate(
            sequenceOfConcurrentSteps,
            new StateProfile(startingState),
            hook);
    }

    public static StateProfile Validate(
        IList<IList<IStepFunction>> sequenceOfConcurrentSteps,
        StateProfile stateProfile,
        Action<IState, IList<IStepFunction>> hook = null)
    {
        try
        {
            return ValidateInternal(
                sequenceOfConcurrentSteps,
                stateProfile,
                hook);
        }
        catch (InvalidSpecException)
        {
            throw;
        }
        catch (StepFunctionApplicationException ex)
        {
            throw new InvalidSpecException(
                "Encountered an uncaught exception when using a behavior to explain the observed responses; " +
                "this indicates a bug in the behavior/spec.",
                ex);
        }
    }

    private static StateProfile ValidateInternal(
        IList<IList<IStepFunction>> sequenceOfConcurrentSteps,
        StateProfile stateProfile,
        Action<IState, IList<IStepFunction>> hook = null)
    {
        foreach (var concurrentSteps in sequenceOfConcurrentSteps)
        {
            var updatedStatesAndStepFunctions = new List<(IState, IList<IStepFunction>)>();

            foreach (var (state, stepFunctions) in stateProfile.StatesAndStepFunctions)
            {
                var allConcurrentStepFunctions =
                    concurrentSteps.Concat(stepFunctions).ToList();

                _ = StateGraph.ExploreStateGraph(
                    allConcurrentStepFunctions,
                    state,
                    maxDepth: -1,
                    generateStateGraph: false,
                    hook: (node) =>
                    {
                        var updatedState = node.State;
                        var stepFunctions = node.StepFunctions;

                        if (hook != null)
                        {
                            hook(updatedState, stepFunctions);
                        }

                        if (stepFunctions.Any(sf => sf is ContractStepFunction))
                        {
                            return;
                        }

                        updatedStatesAndStepFunctions.Add((updatedState, stepFunctions));
                    });
            }

            if (updatedStatesAndStepFunctions.Count == 0)
            {
                throw new InvalidSpecException("Model cannot explain the behavior of the system.");
            }

            var dedupedUpdatedStatesAndStepFunctions = new List<(IState, IList<IStepFunction>)>();

            var processedFingerprints = new HashSet<string>();
            for (int i = 0; i < updatedStatesAndStepFunctions.Count; i++)
            {
                var ssf = updatedStatesAndStepFunctions[i];
                var fingerprint = StateGraphNode.GetNodeFingerprint(ssf.Item1, ssf.Item2);

                if (processedFingerprints.Contains(fingerprint))
                {
                    continue;
                }

                processedFingerprints.Add(fingerprint);
                dedupedUpdatedStatesAndStepFunctions.Add(ssf);
            }

            stateProfile = new StateProfile(dedupedUpdatedStatesAndStepFunctions);
        }

        return stateProfile;
    }
}
