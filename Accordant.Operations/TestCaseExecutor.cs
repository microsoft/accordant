// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class TestCaseExecutor
    {
        #region Sequential Test Cases

        /// <summary>
        /// Executes the given sequential test cases with the specified initial state.
        /// The provided context is used for all tests with registered services.
        /// </summary>
        /// <param name="context">The testing context with registered services.</param>
        /// <param name="testCases">The test cases to execute.</param>
        /// <param name="initialState">The initial state for each test.</param>
        /// <param name="options">Execution options.</param>
        /// <returns>The execution results for each test case.</returns>
        public static async Task<IList<TestCaseExecutionResult>> ExecuteSequentialTestCases(
            TestingContext context,
            IList<SequentialTestCase> testCases,
            IState initialState,
            TestExecutionOptions options)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));
            options ??= new TestExecutionOptions();

            var spec = context.Spec;
            var runStartTime = DateTime.UtcNow;

            Logger.Log($"Executing {testCases.Count} sequential tests.");
            Logger.Log(string.Empty);

            using (new Logger(indent: true))
            {
                foreach (var testCase in testCases)
                {
                    Logger.Log(testCase.Description);
                }
            }

            // Invoke BeforeAll hook
            var beforeAllInfo = new BeforeAllInfo(testCases.Count, spec);
            await options.InvokeBeforeAllAsync(beforeAllInfo);

            Logger.Log("Running test cases");

            var results = new List<TestCaseExecutionResult>();

            for (int i = 0; i < testCases.Count; i++)
            {
                var testCase = testCases[i];

                Logger.Log($"Test Case: {i + 1} of {testCases.Count}");

                var result = await ExecuteSequentialTestCaseInternal(
                    context,
                    testCase,
                    initialState,
                    i,
                    testCases.Count,
                    options);

                results.Add(result);

                if (options.StopOnFirstFailure == true && !result.Success)
                {
                    break;
                }
            }

            // Invoke AfterAll hook
            var runDuration = DateTime.UtcNow - runStartTime;
            var passed = results.Count(r => r.Success);
            var failed = results.Count(r => !r.Success);
            var skipped = testCases.Count - results.Count;
            var afterAllInfo = new AfterAllInfo(
                testCases.Count, passed, failed, skipped, runDuration, results, spec);
            await options.InvokeAfterAllAsync(afterAllInfo);

            return results;
        }

        /// <summary>
        /// Executes a single sequential test case.
        /// </summary>
        private static async Task<TestCaseExecutionResult> ExecuteSequentialTestCaseInternal(
            TestingContext context,
            SequentialTestCase testCase,
            IState initialState,
            int testIndex,
            int totalTests,
            TestExecutionOptions options)
        {
            var testStartTime = DateTime.UtcNow;
            StateProfile stateProfile = null;
            bool success = false;
            string failureMessage = null;
            Exception exception = null;

            try
            {
                Logger.Log($"Executing {testCase.Description}");

                using (new Logger(indent: true))
                {
                    // Invoke BeforeEach hook
                    var beforeInfo = new BeforeTestInfo(testCase, testIndex, totalTests, initialState, context);
                    await options.InvokeBeforeEachAsync(beforeInfo);

                    var operationCallRequests = new Dictionary<string, object>();
                    var operationCallResponses = new Dictionary<string, object>();

                    (stateProfile, success, failureMessage) = await ExecuteSequentialTestCaseInternal(
                        context,
                        initialState,
                        testCase.OperationCalls,
                        operationCallRequests,
                        operationCallResponses,
                        options);
                }

                var message = success ?
                    "SUCCEEDED: Executed sequential test case successfully." :
                    "FAILED: Failed to execute sequential test case successfully.";

                Logger.Log(message);
            }
            catch (Exception ex)
            {
                exception = ex;
                success = false;
                failureMessage = ex.Message;
                Logger.Log($"FAILED: Exception during test execution: {ex}");
            }

            // Invoke AfterEach hook (always runs)
            var testDuration = DateTime.UtcNow - testStartTime;
            var afterInfo = new AfterTestInfo(
                testCase, testIndex, totalTests, success, failureMessage, exception, stateProfile, testDuration, context);
            await options.InvokeAfterEachAsync(afterInfo);

            return new TestCaseExecutionResult()
            {
                Success = success,
                LastFailureMessage = failureMessage,
                LogFilePath = Logger.OutputDirectory
            };
        }

        #endregion

        #region Concurrent Test Cases

        /// <summary>
        /// Executes the given concurrent test cases with the specified initial state.
        /// The provided context is used for all tests with registered services.
        /// </summary>
        /// <param name="context">The testing context with registered services.</param>
        /// <param name="testCases">The test cases to execute.</param>
        /// <param name="initialState">The initial state for each test.</param>
        /// <param name="options">Execution options.</param>
        /// <returns>The execution results for each test case.</returns>
        public static async Task<IList<TestCaseExecutionResult>> ExecuteConcurrentTestCases(
            TestingContext context,
            IList<ConcurrentTestCase> testCases,
            IState initialState,
            TestExecutionOptions options)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));
            options ??= new TestExecutionOptions();

            var spec = context.Spec;
            var runStartTime = DateTime.UtcNow;

            Logger.Log(string.Empty);
            Logger.Log($"Executing {testCases.Count} concurrent tests.");
            using (new Logger(indent: true))
            {
                foreach (var testCase in testCases)
                {
                    Logger.Log(testCase.Description);
                }
            }
            Logger.Log(string.Empty);

            // Invoke BeforeAll hook
            var beforeAllInfo = new BeforeAllInfo(testCases.Count, spec);
            await options.InvokeBeforeAllAsync(beforeAllInfo);

            var results = new List<TestCaseExecutionResult>();

            for (int i = 0; i < testCases.Count; i++)
            {
                var testCase = testCases[i];

                Logger.Log(string.Empty);
                Logger.Log($"Test Case: {i + 1} of {testCases.Count}");

                var result = await ExecuteConcurrentTestCaseInternal(
                    context,
                    testCase,
                    initialState,
                    i,
                    testCases.Count,
                    options);

                results.Add(result);

                if (options.StopOnFirstFailure && !result.Success)
                {
                    break;
                }
            }

            // Invoke AfterAll hook
            var runDuration = DateTime.UtcNow - runStartTime;
            var passed = results.Count(r => r.Success);
            var failed = results.Count(r => !r.Success);
            var skipped = testCases.Count - results.Count;
            var afterAllInfo = new AfterAllInfo(
                testCases.Count, passed, failed, skipped, runDuration, results, spec);
            await options.InvokeAfterAllAsync(afterAllInfo);

            return results;
        }

        /// <summary>
        /// Executes a single concurrent test case.
        /// </summary>
        private static async Task<TestCaseExecutionResult> ExecuteConcurrentTestCaseInternal(
            TestingContext context,
            ConcurrentTestCase testCase,
            IState initialState,
            int testIndex,
            int totalTests,
            TestExecutionOptions options)
        {
            var testStartTime = DateTime.UtcNow;
            StateProfile stateProfile = null;
            bool success = false;
            string failureMessage = null;
            Exception exception = null;

            try
            {
                Logger.Log($"Executing {testCase.Description}");
                Logger.Log(string.Empty);

                // Invoke BeforeEach hook
                var beforeInfo = new BeforeTestInfo(testCase, testIndex, totalTests, initialState, context);
                await options.InvokeBeforeEachAsync(beforeInfo);

                using (new Logger(indent: true))
                {
                    (stateProfile, success, failureMessage) = await ExecuteConcurrentTestCaseInternal(
                        context,
                        initialState,
                        testCase,
                        options);
                }

                var message = success ?
                    "SUCCEEDED: Executed concurrent test case successfully." :
                    $"FAILED: Failed to execute concurrent test case successfully. {failureMessage}";

                Logger.Log(string.Empty);
                Logger.Log(message);
            }
            catch (Exception ex)
            {
                exception = ex;
                success = false;
                failureMessage = ex.Message;
                Logger.Log($"FAILED: Exception during test execution: {ex}");
            }

            // Invoke AfterEach hook (always runs)
            var testDuration = DateTime.UtcNow - testStartTime;
            var afterInfo = new AfterTestInfo(
                testCase, testIndex, totalTests, success, failureMessage, exception, stateProfile, testDuration, context);
            await options.InvokeAfterEachAsync(afterInfo);

            return new TestCaseExecutionResult()
            {
                Success = success,
                LastFailureMessage = failureMessage,
                LogFilePath = Logger.OutputDirectory
            };
        }

        #endregion

        /// <summary>
        /// This method calls an operation with the given request, retrying if needed
        /// and returns the response.
        /// </summary>
        public static async Task<(StateProfile, bool, string, object)> ExecuteOperationCallAsync(
            TestingContext context,
            StateProfile stateProfile,
            string operationCallName,
            IOperation operation,
            object request,
            TestExecutionOptions options,
            bool emitDiagnosticLogsDuringValidation = false)
        {
            int numExecutions = 1;

            while (true)
            {
                Logger.Log($"Executing \"{operationCallName}\"");
                Logger.Log(string.Empty);

                using (new Logger(indent: true))
                {
                    Logger.Log($"Request: {context.RequestPrinter(request)}");

                    object response = await operation.SafeExecuteAsync(context, request);

                    Logger.Log($"Response: {context.ResponsePrinter(response)}");

                    // Invoke OnStepExecuted hook (only allocate context if hook is set)
                    if (options.HasOnStepExecutedHook)
                    {
                        var stepInfo = new StepExecutedInfo(
                            stateProfile,
                            new List<(IOperation, object, object)> { (operation, request, response) },
                            context);
                        await options.InvokeOnStepExecutedAsync(stepInfo);
                    }

                    bool valid = true;
                    StateProfile newStateProfile = null;

                    try
                    {

                        newStateProfile = SystemChecker.Validate(
                        [
                            [
                                new ContractStepFunction(
                                    request,
                                    response,
                                    operation.Verify)
                            ]
                        ],
                        stateProfile,
                        hook: !emitDiagnosticLogsDuringValidation ? null : (updatedState, stepFunctions) =>
                        {
                            Logger.Log($"Considering state: {updatedState}");
                            Logger.Log($"Step functions at state node: {string.Join(", ", stepFunctions.Select(f => f.GetType().Name))}");

                            var (validResult, _) = operation.Verify(
                                    request,
                                    updatedState,
                                    response);

                            if (validResult)
                            {
                                Logger.Log($"Response valid according to operation.");
                            }
                            else
                            {
                                Logger.Log($"Response is NOT according to operation.");
                                Logger.Log(operation.ExplainInvalidResponse(
                                    request,
                                    updatedState,
                                    response));
                            }
                        });
                    }
                    catch (InvalidSpecException ex)
                    {
                        if (ex.InnerException is StepFunctionApplicationException applicationEx)
                        {
                            Logger.Log($"Step function application exception occurred during validation: {ex}");

                            return (null, false, $"Encountered step function application exception during validation: {ex}", response);
                        }

                        valid = false;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Exception occurred during validation: {ex}");

                        return (null, false, $"Encountered exception during validation: {ex}", response);
                    }

                    var validString = valid ? "valid" : "not valid";
                    Logger.Log($"Response {validString} according to operation.");

                    if (!valid)
                    {
                        var responseMismatchExplanation = ConstructResponseMismatchExplanationText(
                            context,
                            operation,
                            operationCallName,
                            request,
                            stateProfile,
                            response);

                        Logger.Log(responseMismatchExplanation);

                        return (null, false, ConstructAssertionText(responseMismatchExplanation), response);
                    }

                    if (options.ShouldRetry != null)
                    {
                        var retryBehavior = options.ShouldRetry(
                            operation,
                            request,
                            response,
                            numExecutions);

                        if (retryBehavior.ShouldFailTest)
                        {
                            return (null, false, $"Giving up retrying {operationCallName} after retrying {numExecutions} times and failing the test.", response);
                        }

                        if (retryBehavior.ShouldRetry)
                        {
                            Logger.Log($"Retrying {operationCallName} after waiting {retryBehavior.WaitTimeInMilliseconds}ms.");

                            await Task.Delay(retryBehavior.WaitTimeInMilliseconds);

                            numExecutions++;

                            continue;
                        }
                    }

                    return (newStateProfile, true, string.Empty, response);
                }
            }
        }

        private static async Task<(StateProfile, bool, string)> ExecuteSequentialTestCaseInternal(
            TestingContext context,
            IState startingState,
            IList<OperationCall> operationCalls,
            Dictionary<string, object> operationCallRequests,
            Dictionary<string, object> operationCallResponses,
            TestExecutionOptions options)
        {
            return await ExecuteSequentialTestCaseInternal(
                context,
                new StateProfile(startingState),
                operationCalls,
                operationCallRequests,
                operationCallResponses,
                options);
        }

        private static async Task<(StateProfile, bool, string)> ExecuteSequentialTestCaseInternal(
            TestingContext context,
            StateProfile stateProfile,
            IList<OperationCall> operationCalls,
            Dictionary<string, object> operationCallRequests,
            Dictionary<string, object> operationCallResponses,
            TestExecutionOptions options)
        {

            var numExecutions = new Dictionary<OperationCall, int>();

            for (int i = 0; i < operationCalls.Count; i++)
            {
                var operationCall = operationCalls[i];

                if (!numExecutions.ContainsKey(operationCall))
                {
                    numExecutions[operationCall] = 0;
                }

                numExecutions[operationCall]++;

                var input = operationCall.OperationInput;
                var operation = input.Operation;
                var request = operationCall.OperationInput.Request;

                if (input.DerivedFromOperationCalls != null &&
                    input.DerivedFromOperationCalls.Count == 1)
                {
                    var derivedFromOperationName = context.Spec.GetOperationName(
                        input.DerivedFromOperationCalls[0].OperationInput.Operation);

                    var derivation = operation
                        .DerivedFrom
                        .Where(d => d.FromOperations[0] == derivedFromOperationName)
                        .First();

                    var derivedFromOperationCallName = input.DerivedFromOperationCalls[0].Name;

                    var template = request;

                    Dictionary<string, object> requestSet = null;

                    if (operationCallRequests.ContainsKey(derivedFromOperationCallName))
                    {
                        requestSet = derivation.Invoke(
                            new Dictionary<string, (object Request, object Response)>()
                            {
                                [derivedFromOperationName] =
                                    (Request: operationCallRequests[derivedFromOperationCallName],
                                     Response: operationCallResponses[derivedFromOperationCallName])
                            },
                            template);
                    }

                    if (requestSet == null ||
                        !requestSet.ContainsKey(input.DerivationVariant))
                    {
                        using (new Logger(indent: true))
                        {
                            Logger.Log($"SKIPPING execution of {operationCall}");

                            if (requestSet == null)
                            {
                                Logger.Log($"Skipping executing {operationCall} as source operation never executed");
                            }
                            else
                            {
                                Logger.Log($"Skipping executing {operationCall} as no derived request was generated at runtime.");
                            }

                            Logger.Log(string.Empty);
                            continue;
                        }
                    }

                    request = requestSet[input.DerivationVariant];
                }

                var (newStateProfile, valid, message, response) = await ExecuteOperationCallAsync(
                    context,
                    stateProfile,
                    operationCall.Name,
                    operationCall.OperationInput.Operation,
                    request,
                    options);

                if (!valid)
                {
                    return (newStateProfile, valid, message);
                }

                operationCallRequests[operationCall.Name] = request;
                operationCallResponses[operationCall.Name] = response;

                stateProfile = newStateProfile;

                // Check if any step functions require polling (are TerminatingStepFunction)
                // and this input hasn't disabled polling
                if (!operationCall.OperationInput.SkipPolling)
                {
                    var stepFunctionsToPollFor = GetStepFunctionsToPollFor(stateProfile);

                    if (stepFunctionsToPollFor.Count > 0)
                    {
                        var pollingSetup = FetchPollingSetup(operationCall);
                        var pollingOperation = FetchPollingOperation(context, operationCall, request, response);

                        (stateProfile, var success, var failureMessage) = await PollUntilTerminal(
                            context,
                            stateProfile,
                            pollingOperation,
                            pollingSetup,
                            stepFunctionsToPollFor);

                        if (!success)
                        {
                            return (null, false, failureMessage);
                        }
                    }
                }
            }

            return (stateProfile, true, string.Empty);
        }

        private static async Task<(StateProfile, bool, string)> ExecuteConcurrentTestCaseInternal(
            TestingContext context,
            IState startingState,
            ConcurrentTestCase testCase,
            TestExecutionOptions options)
        {
            var operationCallRequests = new Dictionary<string, object>();
            var operationCallResponses = new Dictionary<string, object>();
            var stateProfile = new StateProfile(startingState);

            for (int segmentIndex = 0; segmentIndex < testCase.Segments.Count; segmentIndex++)
            {
                var segment = testCase.Segments[segmentIndex];

                if (segment.IsSequential)
                {
                    Logger.Log($"Executing sequential segment {segmentIndex + 1} of {testCase.Segments.Count}");

                    using (new Logger(indent: true))
                    {
                        var (newStateProfile, success, failureMessage) = await ExecuteSequentialTestCaseInternal(
                            context,
                            stateProfile,
                            segment.OperationCalls,
                            operationCallRequests,
                            operationCallResponses,
                            options);

                        if (!success)
                        {
                            return (null, false, failureMessage);
                        }

                        stateProfile = newStateProfile;
                    }
                }
                else
                {
                    Logger.Log(string.Empty);
                    Logger.Log($"Executing concurrent segment {segmentIndex + 1} of {testCase.Segments.Count} ({segment.OperationCalls.Count} concurrent operations)");
                    Logger.Log(string.Empty);

                    using (new Logger(indent: true))
                    {
                        var (newStateProfile, success, failureMessage) = await ExecuteConcurrentOperationsInternal(
                            context,
                            segment.OperationCalls,
                            stateProfile,
                            operationCallRequests,
                            operationCallResponses,
                            options);

                        if (!success)
                        {
                            return (null, false, failureMessage);
                        }

                        stateProfile = newStateProfile;
                    }
                }
            }

            return (stateProfile, true, string.Empty);
        }

        private static async Task<(StateProfile, bool, string)> ExecuteConcurrentOperationsInternal(
            TestingContext context,
            IList<OperationCall> concurrentOperationCalls,
            StateProfile stateProfile,
            Dictionary<string, object> operationCallRequests,
            Dictionary<string, object> operationCallResponses,
            TestExecutionOptions options)
        {
            List<(Task<object>, OperationCall, object)> concurrentTasks = new List<(Task<object>, OperationCall, object)>();

            foreach (var operationCall in concurrentOperationCalls)
            {
                var input = operationCall.OperationInput;
                var operation = input.Operation;

                var request = operationCall.OperationInput.Request;

                if (input.DerivedFromOperationCalls != null &&
                    input.DerivedFromOperationCalls.Count == 1)
                {
                    var derivedFromOperationName = context.Spec.GetOperationName(
                        input.DerivedFromOperationCalls[0].OperationInput.Operation);

                    var derivation = operation
                        .DerivedFrom
                        .Where(d => d.FromOperations[0] == derivedFromOperationName)
                        .First();

                    var derivedFromOperationCallName = input.DerivedFromOperationCalls[0].Name;

                    var template = request;

                    Dictionary<string, object> requestSet = null;

                    if (operationCallRequests.ContainsKey(derivedFromOperationCallName))
                    {
                        requestSet = derivation.Invoke(
                            new Dictionary<string, (object Request, object Response)>()
                            {
                                [derivedFromOperationName] =
                                    (Request: operationCallRequests[derivedFromOperationCallName],
                                     Response: operationCallResponses[derivedFromOperationCallName])
                            },
                            template);
                    }

                    if (requestSet == null ||
                        !requestSet.ContainsKey(input.DerivationVariant))
                    {
                        using (new Logger(indent: true))
                        {
                            Logger.Log($"SKIPPING execution of {operationCall}");

                            if (requestSet == null)
                            {
                                Logger.Log($"Skipping executing {operationCall} as source operation never executed");
                            }
                            else
                            {
                                Logger.Log($"Skipping executing {operationCall} as no derived request was generated at runtime.");
                            }

                            Logger.Log(string.Empty);
                            continue;
                        }
                    }

                    request = requestSet[input.DerivationVariant];
                }

                var task = Task.Run(() => operation.SafeExecuteAsync(context, request));

                concurrentTasks.Add((task, operationCall, request));
            }

            var numExecutions = new Dictionary<OperationCall, int>();

            while (true)
            {
                using (new Logger(indent: true))
                {
                    Logger.Log("Starting state profile:");
                    Logger.Log(stateProfile.ToString());
                }

                foreach (var (_, operationCall, _) in concurrentTasks)
                {
                    if (!numExecutions.ContainsKey(operationCall))
                    {
                        numExecutions[operationCall] = 0;
                    }

                    numExecutions[operationCall]++;
                }

                bool encounteredException = false;
                try
                {
                    await Task.WhenAll(concurrentTasks.Select(t => t.Item1));
                }
                catch (Exception)
                {
                    encounteredException = true;
                }

                var concurrentSteps = new List<ContractStepFunction>();

                var executionResults = new List<(IOperation operation, object request, object response)>();

                foreach (var (task, operationCall, request) in concurrentTasks)
                {
                    var input = operationCall.OperationInput;
                    var operationCallName = operationCall.Name;

                    using (new Logger(indent: true))
                    {
                        Logger.Log(string.Empty);
                        Logger.Log($"Executed \"{operationCallName}\"");
                        Logger.Log(string.Empty);

                        using (new Logger(indent: true))
                        {
                            Logger.Log($"Request: {context.RequestPrinter(request)}");

                            if (task.IsFaulted)
                            {
                                Logger.Log($"Exception: {task.Exception}");
                            }
                            else
                            {
                                Logger.Log($"Response: {context.ResponsePrinter(task.Result)}");
                            }
                        }
                    }

                    var result = !task.IsFaulted ? task.Result : null;

                    operationCallRequests[operationCallName] = request;
                    operationCallResponses[operationCallName] = result;

                    concurrentSteps.Add(new ContractStepFunction(request, result, input.Operation.Verify));

                    executionResults.Add((input.Operation, request, result));
                }

                if (encounteredException)
                {
                    return (null, false, $"Encountered an exception when running one of the concurrent operations.");
                }

                // Invoke OnStepExecuted hook for concurrent batch (only allocate context if hook is set)
                if (options.HasOnStepExecutedHook)
                {
                    var stepInfo = new StepExecutedInfo(stateProfile, executionResults, context);
                    await options.InvokeOnStepExecutedAsync(stepInfo);
                }

                try
                {
                    stateProfile = SystemChecker.Validate(
                        new IStepFunction[][]
                        {
                            concurrentSteps.ToArray(),
                        },
                        stateProfile);
                }
                catch (InvalidSpecException e)
                {
                    var failureMessage =
                        $"The spec cannot explain the behavior of concurrently invoking the following " +
                        $"operations: {string.Join(", ", concurrentOperationCalls)}";

                    if (e.InnerException != null)
                    {
                        failureMessage += "\nInner Exception: " + e.InnerException.ToString();
                    }

                    return (null, false, failureMessage);
                }

                if (options.ShouldRetry == null)
                {
                    break;
                }

                var newConcurrentTasks = new List<(Task<object>, OperationCall, object)>();
                foreach (var (_, operationCall, request) in concurrentTasks)
                {
                    var operationCallName = operationCall.Name;

                    var operationCallNumExecutions = numExecutions[operationCall];

                    var retryBehavior = options.ShouldRetry(
                        operationCall.OperationInput.Operation,
                        operationCallRequests[operationCallName],
                        operationCallResponses[operationCallName],
                        operationCallNumExecutions);

                    if (retryBehavior.ShouldFailTest)
                    {
                        return (null, false, $"Giving up retrying {operationCall.Name} after retrying {operationCallNumExecutions} times and failing the test.");
                    }

                    if (retryBehavior.ShouldRetry)
                    {
                        Logger.Log($"Retrying {operationCall.Name} after waiting {retryBehavior.WaitTimeInMilliseconds}ms.");

                        var task = Task.Run(async () =>
                        {
                            await Task.Delay(retryBehavior.WaitTimeInMilliseconds);

                            return await operationCall.OperationInput.Operation.SafeExecuteAsync(context, request);
                        });

                        newConcurrentTasks.Add((task, operationCall, request));
                    }
                }

                if (newConcurrentTasks.Count == 0)
                {
                    break;
                }

                concurrentTasks = newConcurrentTasks;
            }

            return (stateProfile, true, string.Empty);
        }

        /// <summary>
        /// Gets the list of TerminatingStepFunction instances that should be polled for.
        /// Only includes step functions that have NOT yet reached their terminal state.
        /// Note: The state profile can have multiple entries with the same state but different
        /// step functions (due to non-determinism). We filter out terminal ones to avoid
        /// spurious polling on subsequent operations.
        /// </summary>
        private static HashSet<TerminatingStepFunction> GetStepFunctionsToPollFor(
            StateProfile stateProfile)
        {
            var result = new HashSet<TerminatingStepFunction>();

            foreach (var (state, stepFunctions) in stateProfile.StatesAndStepFunctions)
            {
                foreach (var sf in stepFunctions)
                {
                    // Only include step functions that haven't reached their terminal state yet.
                    if (sf is TerminatingStepFunction tsf && !tsf.IsTerminalState(state))
                    {
                        result.Add(tsf);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Polls until all specified TerminatingStepFunction instances have reached their terminal state.
        /// Terminal state is determined by each step function's IsTerminalState predicate.
        /// </summary>
        private static async Task<(StateProfile, bool, string)> PollUntilTerminal(
            TestingContext context,
            StateProfile stateProfile,
            OperationInput pollingOperationInput,
            PollingSetup pollingSetup,
            HashSet<TerminatingStepFunction> stepFunctionsToPollFor)
        {
            // Create a minimal options object for polling (no retry, no step executed hook during polling)
            var pollingOptions = new TestExecutionOptions();

            for (int retryCount = 0; retryCount < pollingSetup.MaxRetryCount; retryCount++)
            {
                Logger.Log($"Polling by calling {pollingOperationInput.Name}");

                using (new Logger(indent: true))
                {
                    var (newStateProfile, valid, message, pollingResponse) = await ExecuteOperationCallAsync(
                       context,
                       stateProfile,
                       pollingOperationInput.Name,
                       pollingOperationInput.Operation,
                       pollingOperationInput.Request,
                       pollingOptions);

                    stateProfile = newStateProfile;

                    // Check if all specified TerminatingStepFunctions have reached their terminal state
                    if (AllTerminatingStepFunctionsComplete(stateProfile, stepFunctionsToPollFor))
                    {
                        Logger.Log("All terminating step functions have completed.");
                        return (stateProfile, true, string.Empty);
                    }

                    await Task.Delay(pollingSetup.WaitTimeInMs);

                    LogPendingStepFunctions(stateProfile, stepFunctionsToPollFor);
                }
            }

            // Timeout - liveness failure
            var pollingOperationName = context.Spec.GetOperationName(pollingOperationInput.Operation);
            var failureMessage =
                $"Gave up polling using {pollingOperationName} after retrying " +
                $"{pollingSetup.MaxRetryCount} times with a delay of {pollingSetup.WaitTimeInMs}ms " +
                $"between each retry. Some TerminatingStepFunctions did not reach their terminal state.";

            return (stateProfile, false, failureMessage);
        }

        /// <summary>
        /// Returns true if all specified TerminatingStepFunction instances have reached their terminal state
        /// across all possible states in the profile.
        /// </summary>
        private static bool AllTerminatingStepFunctionsComplete(
            StateProfile stateProfile,
            HashSet<TerminatingStepFunction> stepFunctionsToPollFor)
        {
            foreach (var (state, stepFunctions) in stateProfile.StatesAndStepFunctions)
            {
                foreach (var sf in stepFunctions)
                {
                    if (sf is TerminatingStepFunction tsf && stepFunctionsToPollFor.Contains(tsf))
                    {
                        if (!tsf.IsTerminalState(state))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private static void LogPendingStepFunctions(
            StateProfile stateProfile,
            HashSet<TerminatingStepFunction> stepFunctionsToPollFor)
        {
            var terminatingStepFunctions = stateProfile.StatesAndStepFunctions
                .SelectMany(ssf => ssf.StepFunctions.OfType<TerminatingStepFunction>()
                    .Where(tsf => stepFunctionsToPollFor.Contains(tsf) && !tsf.IsTerminalState(ssf.State))
                    .Select(tsf => (ssf.State, StepFunction: tsf)))
                .ToList();

            if (terminatingStepFunctions.Count == 0)
            {
                return;
            }

            var stateCountStr = terminatingStepFunctions.Count == 1 ? "is one" : $"are {terminatingStepFunctions.Count}";
            Logger.Log($"There {stateCountStr} terminating step function(s) that have not yet reached terminal state.");
            Logger.Log("Continuing to poll. Details:");

            using (new Logger(indent: true))
            {
                foreach (var (state, stepFunction) in terminatingStepFunctions)
                {
                    Logger.Log(string.Empty);
                    Logger.Log($"State: {state}");
                    Logger.Log($"Pending: {stepFunction.GetType().Name}");
                }
                Logger.Log(string.Empty);
            }
        }

        private static PollingSetup FetchPollingSetup(OperationCall operationCall)
        {
            // OperationInput polling overrides Operation polling
            var pollingSetup = operationCall.OperationInput.Polling 
                            ?? operationCall.OperationInput.Operation.Polling;

            if (pollingSetup == null)
            {
                throw new PollingException(
                    $"No polling setup found for operation call {operationCall.Name}. " +
                    "Either configure Polling on the operation or set Polling on the OperationInput.");
            }

            return pollingSetup;
        }

        private static OperationInput FetchPollingOperation(
            TestingContext context,
            OperationCall operationCall,
            object request,
            object response)
        {
            var pollingSetup = FetchPollingSetup(operationCall);
            var spec = context.Spec;

            // Look up the polling operation
            var pollingOp = spec.GetOperation(pollingSetup.Operation);
            if (pollingOp == null)
            {
                throw new PollingException(
                    $"Polling operation '{pollingSetup.Operation}' not found in spec.");
            }

            // Find the derivation on the polling operation that derives from the source operation
            var sourceOperationName = operationCall.OperationInput.Operation.Name;
            var variant = pollingSetup.Variant ?? DerivationLabels.Default;

            RequestDerivation matchingDerivation = null;
            foreach (var derivation in pollingOp.DerivedFrom)
            {
                if (derivation.Sources.Contains(sourceOperationName))
                {
                    matchingDerivation = derivation;
                    break;
                }
            }

            if (matchingDerivation == null)
            {
                throw new PollingException(
                    $"Polling operation '{pollingSetup.Operation}' has no derivation from '{sourceOperationName}'. " +
                    $"Add a derivation using Derive.From<...>(\"{sourceOperationName}\").");
            }

            // Derive the polling request
            var sources = new Dictionary<string, (object Request, object Response)>
            {
                [sourceOperationName] = (request, response)
            };

            var derivedRequests = matchingDerivation.Derive(sources);

            if (!derivedRequests.TryGetValue(variant, out var pollingRequest))
            {
                throw new PollingException(
                    $"Derivation on '{pollingSetup.Operation}' did not produce variant '{variant}'. " +
                    $"Available variants: {string.Join(", ", derivedRequests.Keys)}");
            }

            return pollingOp.With(pollingRequest, $"Poll for {operationCall.Name}");
        }

        private static string ConstructResponseMismatchExplanationText(
            TestingContext context,
            IOperation operation,
            string label,
            object request,
            StateProfile stateProfile,
            object response)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"The observed response did not match what was expected for operation {label}.");
            sb.AppendLine(string.Empty);
            sb.AppendLine($"Request: {context.RequestPrinter(request)}");
            if (stateProfile.IsSingleState())
            {
                sb.Append($"State: {stateProfile.SingleState()}");
            }
            else
            {
                sb.Append($"State: Multiple possible states; see Explanation for details.");
            }
            sb.AppendLine($"Observed response: {context.ResponsePrinter(response)}");
            sb.AppendLine(string.Empty);
            sb.AppendLine($"Explanation: {operation.ExplainInvalidResponse(request, stateProfile, response)}");

            return sb.ToString();
        }

        private static string ConstructAssertionText(
            string lastFailureText = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Encountered failure while execution an operation.");
            sb.AppendLine($"Detailed log lines can be found at {Logger.OutputDirectory}");

            if (lastFailureText != null)
            {
                sb.AppendLine(string.Empty);
                sb.AppendLine("Last failure:");
                sb.AppendLine(lastFailureText);
            }

            return sb.ToString();
        }
    }
}
