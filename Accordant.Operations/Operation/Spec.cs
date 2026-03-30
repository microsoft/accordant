// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Static factory for creating specs.
    /// </summary>
    public static class Spec
    {
        /// <summary>
        /// Creates a new spec for the given state type.
        /// </summary>
        /// <typeparam name="TState">The type of state the spec operates on.</typeparam>
        /// <returns>A new <see cref="Spec{TState}"/> instance.</returns>
        public static Spec<TState> For<TState>() where TState : State
        {
            return new Spec<TState>();
        }
    }

    /// <summary>
    /// A Spec contains the list of operations that define the behavior
    /// of a stateful system. Each operation is registered under a unique name.
    /// </summary>
    /// <typeparam name="TState">The type of state the spec operates on.</typeparam>
    public class Spec<TState> : ISpec where TState : State
    {
        private Dictionary<string, IOperation> nameToOperations =
            new Dictionary<string, IOperation>();

        private Dictionary<IOperation, string> operationsToName =
            new Dictionary<IOperation, string>();

        /// <summary>
        /// The operations registered in this spec.
        /// </summary>
        public IEnumerable<IOperation> Operations => nameToOperations.Values;

        /// <summary>
        /// This method registers the given operation under the given name.
        /// </summary>
        public void RegisterOperation(string name, IOperation operation)
        {
            if (nameToOperations.ContainsKey(name))
            {
                throw new SpecException(
                    $"An operation with name {name} has already been registered.");
            }

            if (operationsToName.ContainsKey(operation))
            {
                var existingName = operationsToName[operation];
                throw new SpecException(
                    $"The given operation has already been registered under a different name {existingName}.");
            }

            nameToOperations[name] = operation;
            operationsToName[operation] = name;

            // Set Spec reference on the operation if it's an Operation<,,>
            SetSpecReference(operation);
        }

        /// <summary>
        /// This method returns the operation registered under the given name.
        /// </summary>
        public IOperation GetOperation(string name)
        {
            if (!nameToOperations.ContainsKey(name))
            {
                throw new SpecException(
                    $"No operation with name {name} has been registered.");
            }

            return nameToOperations[name];
        }

        /// <summary>
        /// This method returns the name the given operation was registered under.
        /// </summary>
        public string GetOperationName(IOperation operation)
        {
            if (!operationsToName.ContainsKey(operation))
            {
                throw new SpecException(
                    $"The given operation has not been registered.");
            }

            return operationsToName[operation];
        }

        /// <summary>
        /// The indexer property can be used to register operations under a given
        /// name and retrieve them given the registered name.
        /// </summary>
        public IOperation this[string name]
        {
            get => GetOperation(name);
            set => RegisterOperation(name, value);
        }

        /// <summary>
        /// Registers an <see cref="IOperation"/> with this spec.
        /// </summary>
        /// <param name="operation">The operation to register.</param>
        public void Add(IOperation operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            RegisterOperation(operation.Name, operation);
        }

        #region Allows - Response Validation

        /// <summary>
        /// Validates whether the spec allows the observed response for the given operation,
        /// request, and state. If valid, returns the updated state profile
        /// reflecting any state transitions. If not valid, returns an explanation message.
        /// </summary>
        /// <typeparam name="TRequest">The type of request.</typeparam>
        /// <typeparam name="TResponse">The type of response.</typeparam>
        /// <param name="operation">The operation that was invoked.</param>
        /// <param name="request">The request that was sent.</param>
        /// <param name="response">The observed response from the system.</param>
        /// <param name="state">The state the system was in before the operation.</param>
        /// <returns>A tuple of (isValid, explanationMessage, updatedStateProfile).</returns>
        public (bool IsValid, string Message, StateProfile UpdatedStateProfile) Allows<TRequest, TResponse>(
            Operation<TRequest, TResponse, TState> operation,
            TRequest request,
            object response,
            TState state)
        {
            return Allows(operation, request, response, new StateProfile(state));
        }

        /// <summary>
        /// Validates whether the spec allows the observed response for the given operation,
        /// request, and state profile. If valid, returns the updated state profile
        /// reflecting any state transitions. If not valid, returns an explanation message.
        /// </summary>
        /// <typeparam name="TRequest">The type of request.</typeparam>
        /// <typeparam name="TResponse">The type of response.</typeparam>
        /// <param name="operation">The operation that was invoked.</param>
        /// <param name="request">The request that was sent.</param>
        /// <param name="response">The observed response from the system.</param>
        /// <param name="stateProfile">The state profile representing possible states before the operation.</param>
        /// <returns>A tuple of (isValid, explanationMessage, updatedStateProfile).</returns>
        public (bool IsValid, string Message, StateProfile UpdatedStateProfile) Allows<TRequest, TResponse>(
            Operation<TRequest, TResponse, TState> operation,
            TRequest request,
            object response,
            StateProfile stateProfile)
        {
            bool success;
            StateProfile nextStateProfile;

            // Operation.Verify will re-throw InvalidSpecException when InnerException is
            // StepFunctionApplicationException (spec bug), so we don't need to catch here.
            (success, nextStateProfile) = operation.Verify(request, stateProfile, response);

            var message = string.Empty;

            if (!success)
            {
                // ExplainInvalidResponse calls Apply directly (not through SystemChecker.Validate),
                // so any exceptions from spec bugs will propagate as-is.
                if (stateProfile.StatesAndStepFunctions.Count == 1)
                {
                    message = operation.ExplainInvalidResponse(
                        request,
                        stateProfile.SingleState(),
                        response);
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("The system can be in more than one state, but the observed response couldn't be explained in any state.");
                    sb.AppendLine();

                    foreach (var (state, _) in stateProfile.StatesAndStepFunctions)
                    {
                        sb.AppendLine("The response couldn't be explained for state: " + state);
                        sb.AppendLine(operation.ExplainInvalidResponse(request, state, response));
                        sb.AppendLine();
                    }

                    message = sb.ToString();
                }
            }

            return (success, message, nextStateProfile);
        }

        /// <summary>
        /// Validates whether the spec allows the observed response for the given operation,
        /// request, and state. This non-generic overload accepts object types for request and response,
        /// useful when the caller doesn't know the exact types at compile time.
        /// </summary>
        /// <param name="operation">The operation that was invoked.</param>
        /// <param name="request">The request that was sent.</param>
        /// <param name="response">The observed response from the system.</param>
        /// <param name="state">The state the system was in before the operation.</param>
        /// <returns>A tuple of (isValid, explanationMessage, updatedStateProfile).</returns>
        public (bool IsValid, string Message, StateProfile UpdatedStateProfile) Allows(
            IOperation operation,
            object request,
            object response,
            TState state)
        {
            return Allows(operation, request, response, new StateProfile(state));
        }

        /// <summary>
        /// Validates whether the spec allows the observed response for the given operation,
        /// request, and state profile. This non-generic overload accepts object types for request and response,
        /// useful when the caller doesn't know the exact types at compile time.
        /// </summary>
        /// <param name="operation">The operation that was invoked.</param>
        /// <param name="request">The request that was sent.</param>
        /// <param name="response">The observed response from the system.</param>
        /// <param name="stateProfile">The state profile representing possible states before the operation.</param>
        /// <returns>A tuple of (isValid, explanationMessage, updatedStateProfile).</returns>
        public (bool IsValid, string Message, StateProfile UpdatedStateProfile) Allows(
            IOperation operation,
            object request,
            object response,
            StateProfile stateProfile)
        {
            var contract = (IContract)operation;
            var (success, nextStateProfile) = contract.Verify(request, stateProfile, response);

            var message = string.Empty;

            if (!success)
            {
                if (stateProfile.StatesAndStepFunctions.Count == 1)
                {
                    message = contract.ExplainInvalidResponse(
                        request,
                        stateProfile.SingleState(),
                        response);
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("The system can be in more than one state, but the observed response couldn't be explained in any state.");
                    sb.AppendLine();

                    foreach (var (state, _) in stateProfile.StatesAndStepFunctions)
                    {
                        sb.AppendLine("The response couldn't be explained for state: " + state);
                        sb.AppendLine(contract.ExplainInvalidResponse(request, state, response));
                        sb.AppendLine();
                    }

                    message = sb.ToString();
                }
            }

            return (success, message, nextStateProfile);
        }

        /// <summary>
        /// Validates whether the spec allows the observed responses for a set of operations
        /// that were invoked concurrently. If the responses can be explained by some logical
        /// ordering of the operations, returns the updated state profile. If not, returns
        /// an explanation message.
        /// </summary>
        /// <param name="stateProfile">The state profile representing possible states before the concurrent operations.</param>
        /// <param name="concurrentCalls">The list of concurrent operation calls with their requests and responses.</param>
        /// <returns>A tuple of (isValid, explanationMessage, updatedStateProfile).</returns>
        public (bool IsValid, string Message, StateProfile UpdatedStateProfile) AllowsConcurrent(
            StateProfile stateProfile,
            IList<(IOperation operation, object request, object response)> concurrentCalls)
        {
            var concurrentSteps = new List<ContractStepFunction>();

            foreach (var concurrentCall in concurrentCalls)
            {
                concurrentSteps.Add(new ContractStepFunction(
                    concurrentCall.request,
                    concurrentCall.response,
                    (IContract)concurrentCall.operation));
            }

            try
            {
                stateProfile = SystemChecker.Validate(
                    new IStepFunction[][]
                    {
                        concurrentSteps.ToArray(),
                    },
                    stateProfile);

                return (true, string.Empty, stateProfile);
            }
            catch (InvalidSpecException ex) when (ex.InnerException is StepFunctionApplicationException)
            {
                // The spec itself threw an exception - this is a bug in the spec, not an invalid response.
                // Re-throw so the caller sees it's a spec bug, not a response mismatch.
                throw;
            }
            catch (InvalidSpecException)
            {
                var failureMessage =
                    $"No logical ordering of operations can explain the responses of the concurrent calls.";

                return (false, failureMessage, null);
            }
        }

        #endregion

        /// <summary>
        /// Gets a typed operation by name.
        /// </summary>
        /// <typeparam name="TRequest">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="name">The name of the operation.</param>
        /// <returns>The typed operation.</returns>
        public Operation<TRequest, TResponse, TState> GetOperation<TRequest, TResponse>(string name)
        {
            return (Operation<TRequest, TResponse, TState>)GetOperation(name);
        }

        /// <summary>
        /// Creates and registers an inline operation using a lambda expression.
        /// Returns the spec for fluent chaining.
        /// </summary>
        /// <typeparam name="TRequest">The type of request.</typeparam>
        /// <typeparam name="TResponse">The type of response.</typeparam>
        /// <param name="name">The name of the operation.</param>
        /// <param name="apply">The function that defines the operation's behavior.</param>
        /// <returns>This spec for fluent chaining.</returns>
        public Spec<TState> Operation<TRequest, TResponse>(
            string name,
            Func<TRequest, TState, ExpectedOutcomes> apply)
        {
            var operation = new InlineOperation<TRequest, TResponse, TState>(name, apply);
            Add(operation);
            return this;
        }

        /// <summary>
        /// Configures request derivations for an inline operation.
        /// This allows inline operations to have derivations without needing a class-based definition.
        /// </summary>
        /// <param name="operationName">The name of the operation to configure derivations for.</param>
        /// <param name="derivations">The derivations to set for this operation.</param>
        /// <returns>This spec for fluent chaining.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the operation is not an inline operation (class-based operations should override DerivedFrom instead).
        /// </exception>
        /// <example>
        /// <code>
        /// spec.Operation&lt;(string, string), ApiResult&lt;Todo&gt;&gt;("GetTodo", (req, state) =&gt; { ... });
        /// 
        /// spec.ConfigureDerivations("GetTodo",
        ///     Derive.From&lt;Todo, ApiResult&lt;Todo&gt;, (string, string)&gt;("CreateTodo")
        ///         .When((req, resp) =&gt; resp.IsSuccess)
        ///         .As((req, resp) =&gt; (resp.Data.UserId, resp.Data.TodoId)));
        /// </code>
        /// </example>
        public Spec<TState> ConfigureDerivations(string operationName, params RequestDerivation[] derivations)
        {
            var operation = GetOperation(operationName);
            
            if (operation is IInlineOperation inlineOp)
            {
                inlineOp.SetDerivedFrom(derivations);
            }
            else
            {
                throw new InvalidOperationException(
                    $"ConfigureDerivations can only be used with inline operations. " +
                    $"Operation '{operationName}' is a class-based operation - override DerivedFrom instead.");
            }

            return this;
        }

        /// <summary>
        /// Configures polling for an inline operation.
        /// This allows inline operations to have polling without needing a class-based definition.
        /// </summary>
        /// <param name="operationName">The name of the operation to configure polling for.</param>
        /// <param name="polling">The polling setup for this operation.</param>
        /// <returns>This spec for fluent chaining.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the operation is not an inline operation (class-based operations should override Polling instead).
        /// </exception>
        /// <example>
        /// <code>
        /// spec.Operation&lt;string, ApiResult&lt;Job&gt;&gt;("CreateJob", (req, state) =&gt; { ... });
        /// 
        /// spec.ConfigurePolling("CreateJob", new PollingSetup
        /// {
        ///     Operation = "GetJob",
        ///     WaitTimeInMs = 100,
        ///     MaxRetryCount = 100
        /// });
        /// </code>
        /// </example>
        public Spec<TState> ConfigurePolling(string operationName, PollingSetup polling)
        {
            var operation = GetOperation(operationName);
            
            if (operation is IInlineOperation inlineOp)
            {
                inlineOp.SetPollingSetup(polling);
            }
            else
            {
                throw new InvalidOperationException(
                    $"ConfigurePolling can only be used with inline operations. " +
                    $"Operation '{operationName}' is a class-based operation - override Polling instead.");
            }

            return this;
        }

        #region ExecuteWith

        /// <summary>
        /// Creates an <see cref="ExecuteBuilder{TTarget, TState}"/> for binding execution logic
        /// to operations in this spec.
        /// </summary>
        /// <typeparam name="TTarget">The type of the system under test.</typeparam>
        /// <returns>An <see cref="ExecuteBuilder{TTarget, TState}"/> for fluent binding.</returns>
        public ExecuteBuilder<TTarget, TState> ExecuteWith<TTarget>()
        {
            return new ExecuteBuilder<TTarget, TState>(this);
        }

        #endregion

        #region Printers

        private Func<object, string> _requestPrinter;
        private Func<object, string> _responsePrinter;

        /// <summary>
        /// Configures a custom request printer for logging during test execution.
        /// </summary>
        /// <param name="printer">A function that converts a request to a string for logging.</param>
        /// <returns>This spec for fluent chaining.</returns>
        public Spec<TState> WithRequestPrinter(Func<object, string> printer)
        {
            _requestPrinter = printer ?? throw new ArgumentNullException(nameof(printer));
            return this;
        }

        /// <summary>
        /// Configures a custom response printer for logging during test execution.
        /// </summary>
        /// <param name="printer">A function that converts a response to a string for logging.</param>
        /// <returns>This spec for fluent chaining.</returns>
        public Spec<TState> WithResponsePrinter(Func<object, string> printer)
        {
            _responsePrinter = printer ?? throw new ArgumentNullException(nameof(printer));
            return this;
        }

        /// <summary>
        /// Configures JSON serialization for both request and response logging.
        /// Uses System.Text.Json with default options.
        /// </summary>
        /// <returns>This spec for fluent chaining.</returns>
        public Spec<TState> WithJsonPrinters()
        {
            _requestPrinter = obj => System.Text.Json.JsonSerializer.Serialize(obj);
            _responsePrinter = obj => System.Text.Json.JsonSerializer.Serialize(obj);
            return this;
        }

        #endregion

        #region Test Generation

        /// <summary>
        /// Generates sequential test cases from the given inputs.
        /// </summary>
        /// <param name="initialState">The initial state for test generation.</param>
        /// <param name="inputs">The input set containing operation inputs to use.</param>
        /// <param name="options">Additional test generation options.</param>
        /// <returns>A list of sequential test cases.</returns>
        public IList<SequentialTestCase> GenerateTests(
            TState initialState,
            InputSet inputs,
            TestGenerationOptions options = null)
        {
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));

            var context = CreateTestingContext();

            return TestCaseGenerator.GenerateSequentialTestCases(
                context,
                initialState,
                inputs,
                options ?? new TestGenerationOptions());
        }

        /// <summary>
        /// Generates concurrent test cases from the given inputs.
        /// </summary>
        /// <param name="initialState">The initial state for test generation.</param>
        /// <param name="inputs">The input set containing operation inputs to use.</param>
        /// <param name="options">Additional test generation options.</param>
        /// <returns>A list of concurrent test cases.</returns>
        public IList<ConcurrentTestCase> GenerateConcurrentTests(
            TState initialState,
            InputSet inputs,
            TestGenerationOptions options = null)
        {
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));

            var context = CreateTestingContext();

            return TestCaseGenerator.GenerateConcurrentTestCases(
                context,
                initialState,
                inputs,
                options ?? new TestGenerationOptions());
        }

        /// <summary>
        /// Generates a GraphViz DOT visualization of the state space explored during test generation.
        /// This is useful for understanding how test cases are generated and debugging state transitions.
        /// </summary>
        /// <param name="initialState">The initial state for visualization.</param>
        /// <param name="inputs">The input set containing operation inputs to use.</param>
        /// <param name="generationOptions">Test generation options that control state space exploration.</param>
        /// <param name="visualizationOptions">Options for customizing the visualization output.</param>
        /// <returns>A string containing the GraphViz DOT file content.</returns>
        public string VisualizeStateSpace(
            TState initialState,
            InputSet inputs,
            TestGenerationOptions generationOptions = null,
            VisualizationOptions visualizationOptions = null)
        {
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));

            var context = CreateTestingContext();

            return TestCaseGenerator.VisualizeStateSpace(
                context,
                initialState,
                inputs,
                generationOptions,
                visualizationOptions);
        }

        #endregion

        #region Test Execution

        /// <summary>
        /// Runs the given sequential test cases.
        /// </summary>
        /// <param name="context">The testing context with registered services.</param>
        /// <param name="initialState">The initial state for each test.</param>
        /// <param name="testCases">The test cases to run.</param>
        /// <param name="options">Execution options.</param>
        /// <returns>The execution results.</returns>
        public Task<IList<TestCaseExecutionResult>> RunTests(
            TestingContext context,
            TState initialState,
            IList<SequentialTestCase> testCases,
            TestExecutionOptions options = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));

            return TestCaseExecutor.ExecuteSequentialTestCases(
                context,
                testCases,
                initialState,
                options ?? new TestExecutionOptions());
        }

        /// <summary>
        /// Runs the given concurrent test cases.
        /// </summary>
        /// <param name="context">The testing context with registered services.</param>
        /// <param name="initialState">The initial state for each test.</param>
        /// <param name="testCases">The test cases to run.</param>
        /// <param name="options">Execution options.</param>
        /// <returns>The execution results.</returns>
        public Task<IList<TestCaseExecutionResult>> RunTests(
            TestingContext context,
            TState initialState,
            IList<ConcurrentTestCase> testCases,
            TestExecutionOptions options = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));

            return TestCaseExecutor.ExecuteConcurrentTestCases(
                context,
                testCases,
                initialState,
                options ?? new TestExecutionOptions());
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a new <see cref="TestingContext"/> for this spec.
        /// </summary>
        /// <param name="testDirectoryPath">Optional path for test output.</param>
        /// <returns>A new testing context.</returns>
        TestingContext ISpec.CreateTestingContext(string testDirectoryPath) => CreateTestingContext(testDirectoryPath);

        /// <summary>
        /// Creates a new <see cref="TestingContext"/> for this spec.
        /// </summary>
        /// <param name="testDirectoryPath">Optional path for test output.</param>
        /// <returns>A new testing context.</returns>
        public TestingContext CreateTestingContext(string testDirectoryPath = null)
        {
            var context = new TestingContext(this, testDirectoryPath);

            if (_requestPrinter != null)
            {
                context.RequestPrinter = _requestPrinter;
            }

            if (_responsePrinter != null)
            {
                context.ResponsePrinter = _responsePrinter;
            }

            return context;
        }

        #endregion

        /// <summary>
        /// Automatically registers all public properties that implement <see cref="IOperation"/>.
        /// Call this in the derived Spec constructor after initializing operation properties.
        /// </summary>
        protected void RegisterOperationProperties()
        {
            var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                // Skip indexed properties (like the indexer this[string])
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (typeof(IOperation).IsAssignableFrom(property.PropertyType))
                {
                    var operation = property.GetValue(this) as IOperation;
                    if (operation != null && !nameToOperations.ContainsKey(operation.Name))
                    {
                        Add(operation);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the Spec reference on the operation using reflection.
        /// This handles the generic type correctly.
        /// </summary>
        private void SetSpecReference(IOperation operation)
        {
            var operationType = operation.GetType();
            var baseType = operationType;

            // Walk up the inheritance chain to find Operation<,,>
            while (baseType != null)
            {
                if (baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() == typeof(Operation<,,>))
                {
                    var specProperty = baseType.GetProperty("Spec");
                    if (specProperty != null && specProperty.CanWrite)
                    {
                        specProperty.SetValue(operation, this);
                    }
                    break;
                }
                baseType = baseType.BaseType;
            }
        }
    }
}
