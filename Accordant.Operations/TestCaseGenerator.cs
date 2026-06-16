// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

public class TestCaseGenerator
{
    /// <summary>
    /// This method generates test cases by gathering all distinct paths starting from rootNode,
    /// where each path consists of the step function labels for the transitions in that path.
    /// If a path loop backs into itself, this method stops generating that path and doesn't follow the
    /// cycles.
    /// This method returns a dictionary whose key is the unique identifier for the test case
    /// and whose value is the list of operations comprising a test case.
    /// </summary>
    public static IList<SequentialTestCase> GenerateSequentialTestCases(
        TestingContext context,
        IState startingState,
        InputSet inputSet,
        TestGenerationOptions options = null)
    {
        if (options == null)
        {
            options = new TestGenerationOptions();
        }

        ValidateAndConstructNameOperationMap(
            context,
            inputSet);

        var rootNode = ConstructStateSpaceGraph(
            context.Spec,
            inputSet,
            startingState,
            options,
            addNonInputStepFunctions: false);

        var testCases = options.SequentialTestCaseAlgorithm(
            context,
            rootNode);

        // Filter out empty test cases (those with no operations)
        testCases = testCases.Where(tc => tc.OperationCalls.Count > 0).ToList();

        if (options.SimplifyOperationCallNames)
        {
            SimplifyOperationCallNames(context.Spec, testCases);
        }

        return testCases;
    }

    /// <summary>
    /// This method generates test cases by gathering all test sequences starting from the rootNode,
    /// where each test case is a (sequentialPrefix, concurrentSteps) pair. The step functions in
    /// "sequentialPrefix" are meant to be run one after another. The step functions in
    /// "concurrentSteps" are all meant to be run concurrently.
    /// A test case here contains the labels of the step functions in consideration.
    /// This method returns a dictionary whose key is the
    /// </summary>
    public static IList<ConcurrentTestCase> GenerateConcurrentTestCases(
        TestingContext context,
        IState startingState,
        InputSet inputSet,
        TestGenerationOptions options = null)
    {
        if (options == null)
        {
            options = new TestGenerationOptions();
        }

        ValidateAndConstructNameOperationMap(
            context,
            inputSet);

        var rootNode = ConstructStateSpaceGraph(
            context.Spec,
            inputSet,
            startingState,
            options,
            addNonInputStepFunctions: false);

        var testCases = options.ConcurrentTestCaseAlgorithm(
            context,
            rootNode);

        // Filter out empty test cases (those with no segments or all empty segments)
        testCases = testCases
            .Where(tc => tc.Segments != null && tc.Segments.Count > 0 &&
                tc.Segments.Any(s => s.OperationCalls.Count > 0))
            .ToList();

        return testCases;
    }

    /// <summary>
    /// This methods explores the state graph in the exact same way as it is explored
    /// during test case generation. It returns a GraphViz visualization of the state
    /// graph to the caller.
    /// </summary>
    public static string VisualizeStateSpace(
        TestingContext context,
        IState startingState,
        InputSet inputSet,
        TestGenerationOptions options = null,
        VisualizationOptions visualizationOptions = null)
    {
        if (options == null)
        {
            options = new TestGenerationOptions();
        }

        if (visualizationOptions == null)
        {
            visualizationOptions = new VisualizationOptions();
        }

        var rootNode = ConstructStateSpaceGraph(
            context.Spec,
            inputSet,
            startingState,
            options,
            addNonInputStepFunctions: false);

        return StateGraphNode.GenerateDotFileContent(
            rootNode,
            visualizationOptions.NodeLabelLambda,
            showStepFunctionsInNode: false);
    }

    /// <summary>
    /// This methods explores the state graph in the same way as its explored during
    /// test case generation. One difference is that it currently also fully explores the
    /// interleaving of step functions spawned the behavior (say, to model background
    /// asynchrony) with the rest of the operations, so its exploration is more exhaustive.
    /// It returns the root node of the resulting state graph to the caller.
    /// </summary>
    public static StateGraphNode ExploreStateSpace(
        TestingContext context,
        IState startingState,
        InputSet inputSet,
        TestGenerationOptions options = null)
    {
        if (options == null)
        {
            options = new TestGenerationOptions();
        }

        var rootNode = ConstructStateSpaceGraph(
            context.Spec,
            inputSet,
            startingState,
            options,
            addNonInputStepFunctions: true);

        return rootNode;
    }

    public static SequentialTestCase CreateManualSequentialTestCase(
        TestingContext context,
        InputSet inputSet,
        params string[] operationNames)
    {
        ValidateAndConstructNameOperationMap(
            context,
            inputSet);

        foreach (var operationName in operationNames)
        {
            if (!inputSet.ContainsInput(operationName))
            {
                throw new InvalidOperationNameException(
                    $"No operation with name {operationName} found in {nameof(inputSet)}.");
            }
        }

        var operationCalls = operationNames
            .Select((name, index) => new OperationCall(
                InputStepFunction.GetArbitraryLabel(index) + name,
                inputSet[name]))
            .ToList();

        var testCase = new SequentialTestCase()
        {
            Description = ConstructDescriptionForSequentialTestCase(operationCalls),
            OperationCalls = operationCalls
        };

        SimplifyOperationCallNames(context.Spec, testCase);

        return testCase;
    }

    public static ConcurrentTestCase CreateManualConcurrentTestCase(
        TestingContext context,
        InputSet inputSet,
        IList<string> sequentialPrefixOperationNames,
        IList<string> concurrentOperationNames)
    {
        ValidateAndConstructNameOperationMap(
            context,
            inputSet);

        foreach (var operationName in sequentialPrefixOperationNames.Concat(concurrentOperationNames))
        {
            if (!inputSet.ContainsInput(operationName))
            {
                throw new InvalidOperationNameException(
                    $"No operation with name {operationName} found in {nameof(inputSet)}.");
            }
        }

        IList<OperationCall> ConvertOperationsToOperationCalls(
            IList<string> operationNames,
            int baseIndex = 0)
        {
            return operationNames
                .Select((name, index) => new OperationCall(
                    InputStepFunction.GetArbitraryLabel(baseIndex + index) + name,
                    inputSet[name]))
                .ToList();
        }

        var sequentialOperationCalls = ConvertOperationsToOperationCalls(
                sequentialPrefixOperationNames,
                baseIndex: 0);

        var concurrentOperationCalls = ConvertOperationsToOperationCalls(
                concurrentOperationNames,
                baseIndex: sequentialPrefixOperationNames.Count);

        var segments = new List<TestCaseSegment>();

        foreach (var call in sequentialOperationCalls)
        {
            segments.Add(new TestCaseSegment(call));
        }

        if (concurrentOperationCalls.Count > 0)
        {
            segments.Add(new TestCaseSegment(concurrentOperationCalls));
        }

        var testCase = new ConcurrentTestCase()
        {
            Description = ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        return testCase;
    }

    public static void SaveSequentialTestCases(
        TestingContext context,
        string filePath,
        IList<SequentialTestCase> testCases)
    {
        File.WriteAllText(
            filePath,
            SerializeSequentialTestCases(context, testCases));
    }

    public static void SaveSequentialTestCases<T>(
        TestingContext context,
        T testingMetadata,
        string filePath,
        IList<SequentialTestCase> testCases)
        where T : ITestingMetadata
    {
        File.WriteAllText(
            filePath,
            SerializeSequentialTestCases<T>(context, testingMetadata, testCases));
    }

    public static IList<SequentialTestCase> LoadSequentialTestCases(
        TestingContext context,
        string filePath)
    {
        var serializedTestCases = File.ReadAllText(filePath);

        return DeserializeSequentialTestCases(context, serializedTestCases);
    }

    public static (T, IList<SequentialTestCase>) LoadSequentialTestCases<T>(
        TestingContext context,
        string filePath)
        where T : ITestingMetadata
    {
        var serializedTestCases = File.ReadAllText(filePath);

        return DeserializeSequentialTestCases<T>(context, serializedTestCases);
    }

    public static void SaveConcurrentTestCases(
        TestingContext context,
        string filePath,
        IList<ConcurrentTestCase> testCases)
    {
        File.WriteAllText(
            filePath,
            SerializeConcurrentTestCases(context, testCases));
    }

    public static void SaveConcurrentTestCases<T>(
        TestingContext context,
        T testingMetadata,
        string filePath,
        IList<ConcurrentTestCase> testCases)
        where T : ITestingMetadata
    {
        File.WriteAllText(
            filePath,
            SerializeConcurrentTestCases<T>(context, testingMetadata, testCases));
    }

    public static IList<ConcurrentTestCase> LoadConcurrentTestCases(
        TestingContext context,
        string filePath)
    {
        var serializedTestCases = File.ReadAllText(filePath);

        return DeserializeConcurrentTestCases(context, serializedTestCases);
    }

    public static (T, IList<ConcurrentTestCase>) LoadConcurrentTestCases<T>(
        TestingContext context,
        string filePath)
        where T : ITestingMetadata
    {
        var serializedTestCases = File.ReadAllText(filePath);

        return DeserializeConcurrentTestCases<T>(context, serializedTestCases);
    }

    public static string ConstructDescriptionForSequentialTestCase(
        IList<OperationCall> operationCalls)
    {
        return string.Join("; ", operationCalls.Select(c => c.Name));
    }

    public static string ConstructDescriptionForConcurrentTestCase(
        IList<TestCaseSegment> segments)
    {
        var parts = new List<string>();

        foreach (var segment in segments)
        {
            if (segment.IsSequential)
            {
                parts.Add(segment.OperationCalls[0].Name);
            }
            else
            {
                parts.Add(string.Join(" || ", segment.OperationCalls.Select(c => c.Name)));
            }
        }

        return string.Join(" --> ", parts);
    }

    public static string SerializeSequentialTestCases(
        TestingContext context,
        IList<SequentialTestCase> testCases)
    {
        var testCaseFileRecords = testCases.Select(
            tc => tc.ToSequentialTestCaseFileRecord(context.Spec)).ToList();

        return JsonSerializer.Serialize(testCaseFileRecords, new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });
    }

    public static string SerializeSequentialTestCases<T>(
        TestingContext context,
        T testingMetadata,
        IList<SequentialTestCase> testCases)
        where T : ITestingMetadata
    {
        var testCaseFileRecords = testCases.Select(
            tc => tc.ToSequentialTestCaseFileRecord(context.Spec)).ToList();

        var testCaseFileRecordsWithMetadata = new TestCaseFileRecordsWithMetadata<T, SequentialTestCaseFileRecord>()
        {
            Metadata = testingMetadata,
            TestCases = testCaseFileRecords
        };

        return JsonSerializer.Serialize(testCaseFileRecordsWithMetadata, new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });
    }

    public static IList<SequentialTestCase> DeserializeSequentialTestCases(
        TestingContext context,
        string serializedTestCases)
    {
        var testCaseFileRecords = JsonSerializer.Deserialize<List<SequentialTestCaseFileRecord>>(
            serializedTestCases);

        return testCaseFileRecords
            .Select(fr => fr.ToSequentialTestCase(context.Spec))
            .ToList();
    }

    public static (T, IList<SequentialTestCase>) DeserializeSequentialTestCases<T>(
        TestingContext context,
        string serializedTestCases)
        where T : ITestingMetadata
    {
        var testCaseFileRecordsWithMetadata =
            JsonSerializer.Deserialize<TestCaseFileRecordsWithMetadata<T, SequentialTestCaseFileRecord>>(
                serializedTestCases);

        return (
            testCaseFileRecordsWithMetadata.Metadata,
            testCaseFileRecordsWithMetadata.TestCases
                .Select(fr => fr.ToSequentialTestCase(context.Spec))
                .ToList());
    }

    public static string SerializeConcurrentTestCases(
        TestingContext context,
        IList<ConcurrentTestCase> testCases)
    {
        var testCaseFileRecords = testCases.Select(
            tc => tc.ToConcurrentTestCaseFileRecord(context.Spec)).ToList();

        return JsonSerializer.Serialize(testCaseFileRecords, new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });
    }


    public static string SerializeConcurrentTestCases<T>(
        TestingContext context,
        T testingMetadata,
        IList<ConcurrentTestCase> testCases)
        where T : ITestingMetadata
    {
        var testCaseFileRecords = testCases.Select(
            tc => tc.ToConcurrentTestCaseFileRecord(context.Spec)).ToList();

        var testCaseFileRecordsWithMetadata = new TestCaseFileRecordsWithMetadata<T, ConcurrentTestCaseFileRecord>()
        {
            Metadata = testingMetadata,
            TestCases = testCaseFileRecords
        };

        return JsonSerializer.Serialize(testCaseFileRecordsWithMetadata, new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });
    }

    public static IList<ConcurrentTestCase> DeserializeConcurrentTestCases(
        TestingContext context,
        string serializedTestCases)
    {
        var testCaseFileRecords = JsonSerializer.Deserialize<List<ConcurrentTestCaseFileRecord>>(
            serializedTestCases);

        return testCaseFileRecords
            .Select(fr => fr.ToConcurrentTestCase(context.Spec))
            .ToList();
    }

    public static (T, IList<ConcurrentTestCase>) DeserializeConcurrentTestCases<T>(
        TestingContext context,
        string serializedTestCases)
        where T : ITestingMetadata
    {
        var testCaseFileRecordsWithMetadata =
            JsonSerializer.Deserialize<TestCaseFileRecordsWithMetadata<T, ConcurrentTestCaseFileRecord>>(
                serializedTestCases);

        return (
            testCaseFileRecordsWithMetadata.Metadata,
            testCaseFileRecordsWithMetadata.TestCases
                .Select(fr => fr.ToConcurrentTestCase(context.Spec))
                .ToList());
    }

    private static StateGraphNode ConstructStateSpaceGraph(
        ISpec spec,
        InputSet inputSet,
        IState startingState,
        TestGenerationOptions options,
        bool addNonInputStepFunctions)
    {
        var operationCount = new Dictionary<string, int>();

        var operationCallRequests = new Dictionary<string, object>();
        var operationCallResponses = new Dictionary<string, object>();

        var stepFunctions = new List<IStepFunction>();
        foreach (var input in inputSet.Inputs)
        {
            var stepFunction = new InputStepFunction(
                input,
                addNonInputStepFunctions,
                options.ShouldApply,
                options.ShouldPreserveOperation,
                options.ShouldUnwindStepFunction,
                spec,
                operationCount,
                operationCallRequests,
                operationCallResponses,
                options.RequestTemplates,
                options.DerivationSelectors);

            stepFunctions.Add(stepFunction);
        }

        try
        {
            var rootNode = StateGraph.ExploreStateGraph(
                stepFunctions,
                startingState,
                options.MaxDepth,
                generateStateGraph: true,
                stateConstraint: options.StateConstraint,
                shouldIncludeStepFunctionResult: options.ShouldIncludeTransition == null ?
                    null :
                    (sourceState, stepFunction, stepResult) =>
                    {
                        var targetState = stepResult.State;
                        var operationCall = (OperationCall)stepResult.EdgeMetadata;

                        return options.ShouldIncludeTransition(sourceState, operationCall, targetState);
                    });

            return rootNode;
        }
        catch (StepFunctionApplicationException ex)
        {
            var path = ex.PathToNode;
            var stepStrings = new List<string>();
            for (int i = 1; i < path.Count; i++)
            {
                stepStrings.Add(path[i].stepFunction.ToString());
            }
            stepStrings.Add(ex.ExceptionEncounteringStepFunction.ToString());

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("Encountered an exception when exploring the state graph for test case generation.");
            messageBuilder.AppendLine("The path from the root node to the node at which the exception happened is: " + string.Join(" --> ", stepStrings));
            messageBuilder.AppendLine("The state of the node at which the exception happened is: " + ex.ExceptionEncounteringNode.State.ToString());
            messageBuilder.AppendLine("You can look at the exact place where the exception happened by looking at the stack trace dump towards the end.");

            throw new TestCaseGenerationException(
                messageBuilder.ToString(),
                ex.InnerException);
        }
    }

    private static void SimplifyOperationCallNames(
        ISpec spec,
        IList<SequentialTestCase> testCases)
    {
        foreach (var testCase in testCases)
        {
            SimplifyOperationCallNames(spec, testCase);
        }
    }

    private static void SimplifyOperationCallNames(
        ISpec spec,
        SequentialTestCase testCase)
    {
        var (operationCallNameMap, operationNameMap) = ConstructSimplifiedOperationCallNameMap(
            spec,
            testCase.OperationCalls);

        OperationCall Recurse(OperationCall call)
        {
            var name = call.Name;

            var cloned = call.Clone();
            cloned.Name = operationCallNameMap[name];
            cloned.OperationInput.Name = operationNameMap[call.OperationInput.Name];

            if (cloned.OperationInput.DerivedFromOperationCalls != null)
            {
                cloned.OperationInput.DerivedFromOperationCalls =
                    cloned.OperationInput.DerivedFromOperationCalls.Select(call =>
                        Recurse(call)).ToList();
            }

            return cloned;
        }

        testCase.OperationCalls = testCase
            .OperationCalls
            .Select(call => Recurse(call))
            .ToList();

        testCase.Description = ConstructDescriptionForSequentialTestCase(testCase.OperationCalls);
    }

    /// <summary>
    /// This method simplifies the operation call names. As an example, if the generated
    /// test case contains operation call names like: "[u] Add, [g] Count", they will be simplified
    /// to "Add, Count" as the labels aren't necessary. A test case like "[u] Add, [g] Count, [b] Add"
    /// will however be simplified to "[u] Add, Count, [b] Add" where the labels are preserved on Add
    /// as there are two instances but it can be dropped for Count. This method also takes into account
    /// operation calls that depend on other operations. As an example, "[u] Add, [g][[u]Add -> Delete]"
    /// is simplified to "Add, Add -> Delete". On the other hand, "[u] Add, [z] Add, [g][[u] Add -> Delete]"
    /// is simplified to "[u] Add, [z] Add, [u] Add -> Delete". Note how while the Add calls are not simplified,
    /// the "[u] Add -> Delete" is simplified by dropped the [g] prefix as there is no ambiguity.
    ///
    /// Finally, it is interesting to note that while the state space graph might contain test cases
    /// like "[u-0] Add" or "[u-1] Add" representing cases where an operation non-deterministically
    /// returns multiple responses. If a generated test case contains both these operations, say
    /// "[u-0] Add, [u-1] Add", they won't be simplified and remain unchanged as we have two Add operations
    /// and we need the prefix labels to disambiguate them.
    /// </summary>
    internal static
        (Dictionary<string, string> operationCallNameMap, Dictionary<string, string> operationNameMap)
        ConstructSimplifiedOperationCallNameMap(
        ISpec spec,
        IList<OperationCall> operationCalls)
    {
        var operationCallNameMap = new Dictionary<string, string>();
        var operationNameMap = new Dictionary<string, string>();

        while (operationCallNameMap.Count != operationCalls.Count)
        {
            var nameCountMap = new Dictionary<string, int>();

            foreach (var operationCall in operationCalls)
            {
                var callName = operationCall.Name;

                if (operationCallNameMap.ContainsKey(callName))
                {
                    continue;
                }

                var derivedFromCalls =
                    operationCall.OperationInput.DerivedFromOperationCalls;

                Invariant.Assert(
                    derivedFromCalls == null ||
                    derivedFromCalls.Count <= 1);

                var derivedFromCallName = derivedFromCalls != null && derivedFromCalls.Count > 0 ?
                    derivedFromCalls[0].Name :
                    null;

                if (derivedFromCallName != null &&
                    !operationCallNameMap.ContainsKey(derivedFromCallName))
                {
                    continue;
                }

                var simplifiedOperationName = GetSimplifiedOperationName(
                    operationCall,
                    derivedFromCallName);

                if (!nameCountMap.ContainsKey(simplifiedOperationName))
                {
                    nameCountMap[simplifiedOperationName] = 0;
                }

                nameCountMap[simplifiedOperationName]++;
            }

            var regex = new Regex(@"\[(.*?)\] (.*)");

            foreach (var operationCall in operationCalls)
            {
                var callName = operationCall.Name;
                var derivedFromCalls = operationCall.OperationInput.DerivedFromOperationCalls;

                var matchResult = regex.Match(callName);
                var prefix = matchResult.Groups[1].Value;

                Invariant.Assert(
                    derivedFromCalls == null ||
                    derivedFromCalls.Count <= 1);

                var derivedFromCallName = derivedFromCalls != null && derivedFromCalls.Count > 0 ?
                    derivedFromCalls[0].Name :
                    null;

                if (operationCallNameMap.ContainsKey(callName) ||
                    (derivedFromCallName != null &&
                    !operationCallNameMap.ContainsKey(derivedFromCallName)))
                {
                    continue;
                }

                var simplifiedOperationName = GetSimplifiedOperationName(
                    operationCall,
                    derivedFromCallName);

                if (!nameCountMap.ContainsKey(simplifiedOperationName))
                {
                    continue;
                }

                Invariant.Assert(
                    nameCountMap[simplifiedOperationName] >= 1,
                    $"Expected name count map for {simplifiedOperationName} to be >= 1.");

                operationCallNameMap[callName] = nameCountMap[simplifiedOperationName] > 1 ?
                    $"[{prefix}] {simplifiedOperationName}" :
                    simplifiedOperationName;

                operationNameMap[operationCall.OperationInput.Name] = simplifiedOperationName;
            }
        }

        string GetSimplifiedOperationName(
            OperationCall operationCall,
            string derivedFromCallName)
        {
            var derivationVariant = operationCall.OperationInput.DerivationVariant;

            return derivedFromCallName == null ?
                operationCall.OperationInput.Name :
                OperationInput.ConstructDerivedOperationName(
                    operationCallNameMap[derivedFromCallName],
                    spec.GetOperationName(operationCall.OperationInput.Operation),
                    derivationVariant);
        }

        return (operationCallNameMap, operationNameMap);
    }

    private static void ValidateAndConstructNameOperationMap(
        TestingContext context,
        InputSet inputSet)
    {
        var spec = context.Spec;

        var operationNames = new HashSet<string>();
        foreach (var operation in spec.Operations)
        {
            var operationName = spec.GetOperationName(operation);
            operationNames.Add(operationName);
        }

        foreach (var input in inputSet.Inputs)
        {
            var operation = input.Operation;
            var operationName = spec.GetOperationName(operation);

            var fromOperationSet = new HashSet<string>();

            foreach (var derivation in operation.DerivedFrom)
            {
                if (derivation.FromOperations.Count == 0)
                {
                    throw new OperationValidationException(
                        $"Operation '{operationName}' specified {nameof(RequestDerivation)} " +
                        $"but {nameof(RequestDerivation.FromOperations)} list is empty.");
                }

                if (derivation.FromOperations.Count > 1)
                {
                    throw new OperationValidationException(
                        $"Operation '{operationName}' specified more than one operation in " +
                        $"{nameof(RequestDerivation.FromOperations)} but only single-source derivations are currently supported.");
                }

                if (!operationNames.Contains(derivation.FromOperations[0]))
                {
                    throw new OperationValidationException(
                        $"Operation '{operationName}' specified '{derivation.FromOperations[0]}' in " +
                        $"{nameof(derivation.FromOperations)} but no such operation was found.");
                }

                if (fromOperationSet.Contains(derivation.FromOperations[0]))
                {
                    throw new OperationValidationException(
                        $"Operation '{operationName}' specified more than one derivation " +
                        $"from the same source operation '{derivation.FromOperations[0]}'.");
                }

                fromOperationSet.Add(derivation.FromOperations[0]);
            }
        }
    }
}
