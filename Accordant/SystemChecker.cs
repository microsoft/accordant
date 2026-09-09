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
        // Validate and snapshot the entire externally-owned sequence before
        // invoking a step function or hook. This prevents a malformed later
        // batch from causing partially-applied validation.
        var sequenceSnapshot = SnapshotAndValidateSequence(sequenceOfConcurrentSteps);
        ValidateStateProfile(stateProfile);

        try
        {
            return ValidateInternal(
                sequenceSnapshot,
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

    private static IList<IList<IStepFunction>> SnapshotAndValidateSequence(
        IList<IList<IStepFunction>> sequenceOfConcurrentSteps)
    {
        if (sequenceOfConcurrentSteps == null)
        {
            throw new ArgumentNullException(nameof(sequenceOfConcurrentSteps));
        }

        var snapshot = new List<IList<IStepFunction>>(sequenceOfConcurrentSteps.Count);
        for (var i = 0; i < sequenceOfConcurrentSteps.Count; i++)
        {
            var concurrentSteps = sequenceOfConcurrentSteps[i];
            if (concurrentSteps == null)
            {
                throw new ArgumentException(
                    $"Concurrent step batch at index {i} cannot be null.",
                    nameof(sequenceOfConcurrentSteps));
            }

            var batchSnapshot = concurrentSteps.ToList();
            StateGraph.ValidateStepFunctionList(
                batchSnapshot,
                $"Concurrent step batch at index {i}");
            snapshot.Add(batchSnapshot);
        }

        return snapshot;
    }

    private static void ValidateStateProfile(StateProfile stateProfile)
    {
        if (stateProfile == null)
        {
            throw new ArgumentNullException(nameof(stateProfile));
        }

        if (stateProfile.StatesAndStepFunctions == null)
        {
            throw new InvalidOperationException(
                "A state profile must contain a non-null state collection.");
        }

        foreach (var (state, stepFunctions) in stateProfile.StatesAndStepFunctions)
        {
            if (state == null)
            {
                throw new InvalidOperationException(
                    "A state profile cannot contain a null state.");
            }

            StateGraph.ValidateStepFunctionList(
                stepFunctions,
                "State profile step functions");
        }
    }

    private static StateProfile ValidateInternal(
        IList<IList<IStepFunction>> sequenceOfConcurrentSteps,
        StateProfile stateProfile,
        Action<IState, IList<IStepFunction>> hook = null)
    {
        foreach (var concurrentSteps in sequenceOfConcurrentSteps)
        {
            stateProfile = AdvanceProfile(concurrentSteps, stateProfile, hook);
        }

        return stateProfile;
    }

    /// <summary>
    /// Applies one concurrent operation batch to every possible state in a
    /// profile and returns the deduplicated successor profile. The optional
    /// hook observes every explored node but does not participate in choosing
    /// the returned profile.
    /// </summary>
    internal static StateProfile AdvanceProfile(
        IList<IStepFunction> concurrentSteps,
        StateProfile stateProfile,
        Action<IState, IList<IStepFunction>> hook = null)
    {
        if (concurrentSteps == null)
        {
            throw new ArgumentNullException(nameof(concurrentSteps));
        }

        if (stateProfile == null)
        {
            throw new ArgumentNullException(nameof(stateProfile));
        }

        ValidateStateProfile(stateProfile);

        // Validate all combined sets before exploring the first state. This
        // catches duplicate IDs between the current batch and active steps
        // without partially advancing the profile.
        var profileSnapshot = stateProfile.StatesAndStepFunctions.ToList();
        foreach (var (state, stepFunctions) in profileSnapshot)
        {
            var allConcurrentStepFunctions =
                concurrentSteps.Concat(stepFunctions).ToList();
            StateGraph.ValidateStepFunctionList(
                allConcurrentStepFunctions,
                "Concurrent and active step functions");
        }

        var updatedStatesAndStepFunctions = new List<(IState, IList<IStepFunction>)>();

        foreach (var (state, stepFunctions) in profileSnapshot)
        {
            var allConcurrentStepFunctions =
                concurrentSteps.Concat(stepFunctions).ToList();

            _ = StateGraph.ExploreStateGraph(
                allConcurrentStepFunctions,
                state,
                maxDepth: -1,
                generateStateGraph: false,
                hook: node =>
                {
                    var updatedState = node.State;
                    var updatedStepFunctions = node.StepFunctions.ToList();

                    hook?.Invoke(updatedState, updatedStepFunctions);

                    if (updatedStepFunctions.Any(sf => sf is ContractStepFunction))
                    {
                        return;
                    }

                    updatedStatesAndStepFunctions.Add((updatedState, updatedStepFunctions));
                });
        }

        if (updatedStatesAndStepFunctions.Count == 0)
        {
            throw new InvalidSpecException("Model cannot explain the behavior of the system.");
        }

        var dedupedUpdatedStatesAndStepFunctions =
            DeduplicateStatesAndStepFunctions(updatedStatesAndStepFunctions);

        return new StateProfile(dedupedUpdatedStatesAndStepFunctions);
    }

    private static IList<(IState, IList<IStepFunction>)> DeduplicateStatesAndStepFunctions(
        IList<(IState, IList<IStepFunction>)> statesAndStepFunctions)
    {
        var deduped = new List<(IState, IList<IStepFunction>)>();
        var processedBuckets = new Dictionary<string, List<(IState, IList<IStepFunction>)>>(
            StringComparer.Ordinal);

        foreach (var stateAndStepFunctions in statesAndStepFunctions)
        {
            var fastKey = StateGraphNode.GetFastNodeKey(
                stateAndStepFunctions.Item1,
                stateAndStepFunctions.Item2);

            if (!processedBuckets.TryGetValue(fastKey, out var candidates))
            {
                candidates = new List<(IState, IList<IStepFunction>)>();
                processedBuckets[fastKey] = candidates;
            }

            if (candidates.Count == 0)
            {
                candidates.Add(stateAndStepFunctions);
                deduped.Add(stateAndStepFunctions);
                continue;
            }

            var currentRepresentation = StateGraphNode.GetCanonicalStateRepresentation(
                stateAndStepFunctions.Item1);
            var duplicate = candidates.Any(candidate =>
                string.Equals(
                    StateGraphNode.GetCanonicalStateRepresentation(candidate.Item1),
                    currentRepresentation,
                    StringComparison.Ordinal));

            if (!duplicate)
            {
                candidates.Add(stateAndStepFunctions);
                deduped.Add(stateAndStepFunctions);
            }
        }

        return deduped;
    }
}
