// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A test operation step function represents the effect of applying the model of an operation.
/// It is enabled based on the shouldApply predicate. It returns all possible updated states returned by
/// <see cref="IOperation.Invoke(object, IState)"/>
/// as the set of updated states the system can transition to.
/// </summary>
public class InputStepFunction : BaseStepFunction
{
    private bool addNonInputStepFunctions;

    private Func<OperationInput, IState, bool> shouldApply;

    private Func<OperationInput, IList<OperationInput>, bool> shouldPreserveOperation;

    private Func<UnwindContext, bool> shouldUnwindStepFunction;

    private ISpec spec;

    private Dictionary<string, int> operationCount;

    private Dictionary<string, object> operationCallRequests;

    private Dictionary<string, object> operationCallResponses;

    private Dictionary<string, Func<object>> requestTemplates;

    private IList<DerivationSelector> derivationSelectors;

    public OperationInput OperationInput { get; private set; }

    public override string StepFunctionId => OperationInput.Name;

    /// <summary>
    /// Constructs an instance of this class given a request and a model definition.
    /// </summary>
    public InputStepFunction(
        OperationInput operationInput,
        bool addNonInputStepFunctions,
        Func<OperationInput, IState, bool> shouldApply,
        Func<OperationInput, IList<OperationInput>, bool> shouldPreserveOperation,
        Func<UnwindContext, bool> shouldUnwindStepFunction,
        ISpec spec,
        Dictionary<string, int> operationCount,
        Dictionary<string, object> operationCallRequests,
        Dictionary<string, object> operationCallResponses,
        Dictionary<string, Func<object>> requestTemplates,
        IList<DerivationSelector> derivationSelectors)
    {
        this.addNonInputStepFunctions = addNonInputStepFunctions;
        this.shouldApply = shouldApply;
        this.shouldPreserveOperation = shouldPreserveOperation;
        this.shouldUnwindStepFunction = shouldUnwindStepFunction;
        this.spec = spec;
        this.operationCount = operationCount;
        this.operationCallRequests = operationCallRequests;
        this.operationCallResponses = operationCallResponses;
        this.requestTemplates = requestTemplates;
        this.derivationSelectors = derivationSelectors;
        OperationInput = operationInput;
    }

    /// <summary>
    /// Returns whether the step is enabled based on the shouldApply predicate
    /// and a set of updated states (and optional step functions) the system should
    /// transition to.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="path">The path of step functions taken to reach this state.</param>
    /// <returns>List of step results, or null if not enabled.</returns>
    protected override IList<StepResult> ApplyInternal(IState state, IReadOnlyList<(IStepFunction, StateGraphNode)> path)
    {
        var operation = OperationInput.Operation;
        if (!shouldApply(OperationInput, state))
        {
            return null;
        }

        object request = OperationInput.Request;

        var responseAndStateProfiles = operation.Invoke(request, state);

        var stepResults = new List<StepResult>();

        int responseCount = 0;
        foreach (var (response, stateProfile) in responseAndStateProfiles)
        {
            responseCount++;

            foreach (var (nextState, nextStepFunctions) in stateProfile.StatesAndStepFunctions)
            {
                var operationName = OperationInput.Name;

                if (!operationCount.ContainsKey(operationName))
                {
                    operationCount[operationName] = 0;
                }

                var count = operationCount[operationName];

                operationCount[operationName]++;

                var arbitraryLabel = GetArbitraryLabel(count);
                var namePrefix = responseAndStateProfiles.Count == 1 ?
                    $"[{arbitraryLabel}]" :
                    $"[{arbitraryLabel}-{responseCount}]";

                var operationCall = new OperationCall(
                    $"{namePrefix} {operationName}",
                    OperationInput);

                var currentOperationName = spec.GetOperationName(operation);

                operationCallRequests[operationCall.Name] = request;
                operationCallResponses[operationCall.Name] = response;

                var finalStepFunctions = new List<IStepFunction>();

                var inputList = ConstructInputList(path);
                inputList.Add(OperationInput);

                if (shouldPreserveOperation(OperationInput, inputList))
                {
                    finalStepFunctions.Add(this);
                }

                var spawnedStepFunctions = SpawnDerivedInputStepFunctions(
                    currentOperationName,
                    operationCall,
                    request,
                    response);

                finalStepFunctions.AddRange(spawnedStepFunctions);

                foreach (var nextStepFunction in nextStepFunctions)
                {
                    var unwindContext = new UnwindContext(
                        operation,
                        request,
                        nextState,
                        nextStepFunction);

                    // Only unwind TerminatingStepFunction instances that pass the filter
                    if (nextStepFunction is TerminatingStepFunction tsf &&
                        shouldUnwindStepFunction(unwindContext))
                    {
                        var terminalStates = UnwindTerminatingStepFunction(
                            nextState,
                            tsf);

                        foreach (var terminalState in terminalStates)
                        {
                            stepResults.Add(new StepResult()
                            {
                                State = terminalState,
                                StepFunctions = finalStepFunctions,
                                EdgeMetadata = operationCall
                            });
                        }
                    }

                    if (addNonInputStepFunctions)
                    {
                        finalStepFunctions.Add(nextStepFunction);
                    }
                }

                stepResults.Add(new StepResult()
                {
                    State = nextState,
                    StepFunctions = finalStepFunctions,
                    EdgeMetadata = operationCall
                });
            }
        }

        return stepResults;
    }

    public override string ToString()
    {
        return OperationInput.Name;
    }

    private IList<IStepFunction> SpawnDerivedInputStepFunctions(
        string sourceOperationName,
        OperationCall operationCall,
        object request,
        object response)
    {
        var newStepFunctions = new List<IStepFunction>();

        bool matchAllDerivations = derivationSelectors == null;
        IList<DerivationSelector> matchingSelectors = null;

        foreach (var otherOperation in spec.Operations)
        {
            var otherOperationName =
                spec.GetOperationName(otherOperation);

            if (!matchAllDerivations)
            {
                matchingSelectors = derivationSelectors
                    .Where(s => s.TargetOperation == otherOperationName)
                    .ToList();

                if (matchingSelectors.Count == 0)
                {
                    continue;
                }
            }

            foreach (var derivation in otherOperation.DerivedFrom)
            {
                if (derivation.FromOperations.Count > 0 &&
                    derivation.FromOperations[0] == sourceOperationName)
                {
                    if (!matchAllDerivations)
                    {
                        matchingSelectors = matchingSelectors
                            .Where(s =>
                                s.FromOperations == null ||
                                (s.FromOperations.Count == 1 &&
                                    s.FromOperations[0] == sourceOperationName))
                            .ToList();

                        if (matchingSelectors.Count == 0)
                        {
                            continue;
                        }
                    }

                    var derivedOperation = otherOperation;
                    var derivedOperationName = spec.GetOperationName(derivedOperation);

                    object template = null;
                    if (requestTemplates.ContainsKey(derivedOperationName))
                    {
                        var templateGenerator = requestTemplates[derivedOperationName];
                        template = templateGenerator();
                    }

                    var derivedRequestSet = derivation.Invoke(
                        new Dictionary<string, (object Request, object Response)>()
                        {
                            [sourceOperationName] = (request, response)
                        },
                        template);

                    foreach (var variant in derivedRequestSet.Keys)
                    {
                        if (!matchAllDerivations)
                        {
                            if (!matchingSelectors.Any(
                                    s => s.Variant == null || s.Variant == variant))
                            {
                                continue;
                            }
                        }

                        var derivedRequest = derivedRequestSet[variant];

                        var otherOperationInput = new OperationInput(
                            OperationInput.ConstructDerivedOperationName(
                                operationCall.Name,
                                derivedOperationName,
                                variant),
                            derivedOperation,
                            derivedRequest,
                            new List<OperationCall>() { operationCall },
                            variant)
                        {
                            DerivationVariant = variant
                        };

                        newStepFunctions.Add(new InputStepFunction(
                                otherOperationInput,
                                addNonInputStepFunctions,
                                shouldApply,
                                shouldPreserveOperation,
                                shouldUnwindStepFunction,
                                spec,
                                operationCount,
                                operationCallRequests,
                                operationCallResponses,
                                requestTemplates,
                                derivationSelectors));
                    }
                }
            }
        }

        return newStepFunctions;
    }

    /// <summary>
    /// Unwinds a TerminatingStepFunction until it reaches its terminal state
    /// as defined by <see cref="TerminatingStepFunction.IsTerminalState"/>.
    /// </summary>
    private IList<IState> UnwindTerminatingStepFunction(
        IState startingState,
        TerminatingStepFunction startingStepFunction)
    {
        var terminalStates = new Dictionary<ulong, IState>();

        var queue = new Queue<(IState, IStepFunction)>();

        queue.Enqueue((startingState, startingStepFunction));

        while (queue.Count > 0)
        {
            var (currentState, currentStepFunction) = queue.Dequeue();

            // Check if we've reached the terminal state for the original step function
            if (startingStepFunction.IsTerminalState(currentState))
            {
                var stateHash = currentState.GetStateHash();
                terminalStates[stateHash] = currentState;
                continue;
            }

            var stepResults = currentStepFunction.Apply(
                currentState,
                // TODO: We're giving an empty path here. Ideally, we
                // should take the path and augment it with the list of
                // applied step functions to construct a new path
                Array.Empty<(IStepFunction, StateGraphNode)>());

            if (stepResults == null || stepResults.Count == 0)
            {
                // Step function didn't fire, but we haven't reached terminal state
                // This shouldn't happen for well-designed TerminatingStepFunctions
                var stateHash = currentState.GetStateHash();
                terminalStates[stateHash] = currentState;
                continue;
            }

            foreach (var stepResult in stepResults)
            {
                var nextState = stepResult.State;
                var newStepFunctions = stepResult.StepFunctions;

                // Check if the new state is terminal
                if (startingStepFunction.IsTerminalState(nextState))
                {
                    var nextStateHash = nextState.GetStateHash();
                    terminalStates[nextStateHash] = nextState;
                }
                else if (newStepFunctions == null || newStepFunctions.Count == 0)
                {
                    // No more step functions but not terminal - add anyway
                    var nextStateHash = nextState.GetStateHash();
                    terminalStates[nextStateHash] = nextState;
                }
                else
                {
                    // Continue unwinding with spawned step functions
                    foreach (var newStepFunction in newStepFunctions)
                    {
                        queue.Enqueue((nextState, newStepFunction));
                    }
                }
            }
        }

        return terminalStates.Values.ToList();
    }

    private IList<OperationInput> ConstructInputList(
        IReadOnlyList<(IStepFunction, StateGraphNode)> path)
    {
        var result = new List<OperationInput>();

        if (path.Count <= 1)
        {
            return result;
        }

        for (int i = 1; i < path.Count; i++)
        {
            var stepFunction = path[i].Item1;
            if (stepFunction is InputStepFunction operationStepFunction)
            {
                result.Add(operationStepFunction.OperationInput);
            }
        }

        return result;
    }

    private static char[] randomChars = new char[]
    {
        's', 'u', 'p', 'e', 'r', 'g', 'y', 'a', 'q', 'z', 'c', 'o',
        'i', 'b', 't', 'd', 'l', 'm', 'v', 'f', 'w', 'j', 'n',
        'x', 'k', 'h'
    };

    internal static string GetArbitraryLabel(int num)
    {
        var randomStringsLength = randomChars.Length;

        num = num + 1;

        string result = string.Empty;
        while (num > 0)
        {
            num--;
            var part = randomChars[num % randomStringsLength];
            result = part + result;
            num /= randomStringsLength;
        }

        return result;
    }
}
