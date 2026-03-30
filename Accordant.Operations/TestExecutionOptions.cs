// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    #region Context Classes

    /// <summary>
    /// Information provided to BeforeAll hooks.
    /// </summary>
    public class BeforeAllInfo
    {
        public BeforeAllInfo(int totalTests, ISpec spec)
        {
            TotalTests = totalTests;
            Spec = spec;
        }

        /// <summary>
        /// Total number of tests to be executed.
        /// </summary>
        public int TotalTests { get; }

        /// <summary>
        /// The spec being tested.
        /// </summary>
        public ISpec Spec { get; }
    }

    /// <summary>
    /// Information provided to AfterAll hooks.
    /// </summary>
    public class AfterAllInfo
    {
        public AfterAllInfo(
            int totalTests,
            int passed,
            int failed,
            int skipped,
            TimeSpan totalDuration,
            IReadOnlyList<TestCaseExecutionResult> results,
            ISpec spec)
        {
            TotalTests = totalTests;
            Passed = passed;
            Failed = failed;
            Skipped = skipped;
            TotalDuration = totalDuration;
            Results = results;
            Spec = spec;
        }

        /// <summary>
        /// Total number of tests.
        /// </summary>
        public int TotalTests { get; }

        /// <summary>
        /// Number of tests that passed.
        /// </summary>
        public int Passed { get; }

        /// <summary>
        /// Number of tests that failed.
        /// </summary>
        public int Failed { get; }

        /// <summary>
        /// Number of tests that were skipped (e.g., due to StopOnFirstFailure).
        /// </summary>
        public int Skipped { get; }

        /// <summary>
        /// Total duration of the test run.
        /// </summary>
        public TimeSpan TotalDuration { get; }

        /// <summary>
        /// Results for each executed test case.
        /// </summary>
        public IReadOnlyList<TestCaseExecutionResult> Results { get; }

        /// <summary>
        /// The spec being tested.
        /// </summary>
        public ISpec Spec { get; }
    }

    /// <summary>
    /// Information provided to BeforeEach hooks.
    /// </summary>
    public class BeforeTestInfo
    {
        public BeforeTestInfo(
            TestCase testCase,
            int testIndex,
            int totalTests,
            State initialState,
            TestingContext context)
        {
            TestCase = testCase;
            TestIndex = testIndex;
            TotalTests = totalTests;
            InitialState = initialState;
            Context = context;
        }

        /// <summary>
        /// The test case about to be executed.
        /// </summary>
        public TestCase TestCase { get; }

        /// <summary>
        /// Zero-based index of this test case.
        /// </summary>
        public int TestIndex { get; }

        /// <summary>
        /// Total number of tests in this run.
        /// </summary>
        public int TotalTests { get; }

        /// <summary>
        /// The initial state for this test.
        /// </summary>
        public State InitialState { get; }

        /// <summary>
        /// The testing context. Use Context.Get&lt;T&gt;() to access registered services.
        /// </summary>
        public TestingContext Context { get; }
    }

    /// <summary>
    /// Information provided to AfterEach hooks.
    /// </summary>
    public class AfterTestInfo
    {
        public AfterTestInfo(
            TestCase testCase,
            int testIndex,
            int totalTests,
            bool success,
            string failureMessage,
            Exception exception,
            StateProfile finalState,
            TimeSpan duration,
            TestingContext context)
        {
            TestCase = testCase;
            TestIndex = testIndex;
            TotalTests = totalTests;
            Success = success;
            FailureMessage = failureMessage;
            Exception = exception;
            FinalState = finalState;
            Duration = duration;
            Context = context;
        }

        /// <summary>
        /// The test case that was executed.
        /// </summary>
        public TestCase TestCase { get; }

        /// <summary>
        /// Zero-based index of this test case.
        /// </summary>
        public int TestIndex { get; }

        /// <summary>
        /// Total number of tests in this run.
        /// </summary>
        public int TotalTests { get; }

        /// <summary>
        /// Whether the test passed.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Failure message if the test failed, null otherwise.
        /// </summary>
        public string FailureMessage { get; }

        /// <summary>
        /// Exception if one was thrown, null otherwise.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// The final state after test execution.
        /// </summary>
        public StateProfile FinalState { get; }

        /// <summary>
        /// Duration of this test case.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// The testing context. Use Context.Get&lt;T&gt;() to access registered services.
        /// </summary>
        public TestingContext Context { get; }
    }

    /// <summary>
    /// Information provided to OnStepExecuted hooks.
    /// Called after each operation or batch of concurrent operations executes.
    /// </summary>
    public class StepExecutedInfo
    {
        public StepExecutedInfo(
            StateProfile stateBefore,
            IReadOnlyList<(IOperation Operation, object Request, object Response)> operations,
            TestingContext context)
        {
            StateBefore = stateBefore;
            Operations = operations;
            Context = context;
        }

        /// <summary>
        /// The state profile before the operation(s) executed.
        /// </summary>
        public StateProfile StateBefore { get; }

        /// <summary>
        /// The operations that were executed (1 for sequential, multiple for concurrent).
        /// </summary>
        public IReadOnlyList<(IOperation Operation, object Request, object Response)> Operations { get; }

        /// <summary>
        /// The testing context. Use Context.Get&lt;T&gt;() to access registered services.
        /// </summary>
        public TestingContext Context { get; }

        /// <summary>
        /// Whether this was a single operation (convenience for sequential tests).
        /// </summary>
        public bool IsSingleOperation => Operations.Count == 1;

        /// <summary>
        /// The operation (convenience for single-operation case, null if multiple).
        /// </summary>
        public IOperation Operation => IsSingleOperation ? Operations[0].Operation : null;

        /// <summary>
        /// The request (convenience for single-operation case, null if multiple).
        /// </summary>
        public object Request => IsSingleOperation ? Operations[0].Request : null;

        /// <summary>
        /// The response (convenience for single-operation case, null if multiple).
        /// </summary>
        public object Response => IsSingleOperation ? Operations[0].Response : null;
    }

    #endregion

    /// <summary>
    /// Options and configurations used during test execution.
    /// </summary>
    public class TestExecutionOptions
    {
        #region Lifecycle Hooks - Sync

        /// <summary>
        /// Called once before any tests run. Use for one-time setup.
        /// </summary>
        public Action<BeforeAllInfo> BeforeAll { get; set; }

        /// <summary>
        /// Called once after all tests complete. Use for one-time cleanup.
        /// </summary>
        public Action<AfterAllInfo> AfterAll { get; set; }

        /// <summary>
        /// Called before each test case, after target/state creation.
        /// </summary>
        public Action<BeforeTestInfo> BeforeEach { get; set; }

        /// <summary>
        /// Called after each test case completes (success or failure).
        /// </summary>
        public Action<AfterTestInfo> AfterEach { get; set; }

        #endregion

        #region Lifecycle Hooks - Async

        /// <summary>
        /// Called once before any tests run (async). Use for one-time setup.
        /// </summary>
        public Func<BeforeAllInfo, Task> BeforeAllAsync { get; set; }

        /// <summary>
        /// Called once after all tests complete (async). Use for one-time cleanup.
        /// </summary>
        public Func<AfterAllInfo, Task> AfterAllAsync { get; set; }

        /// <summary>
        /// Called before each test case (async), after target/state creation.
        /// </summary>
        public Func<BeforeTestInfo, Task> BeforeEachAsync { get; set; }

        /// <summary>
        /// Called after each test case (async), success or failure.
        /// </summary>
        public Func<AfterTestInfo, Task> AfterEachAsync { get; set; }

        #endregion

        #region Per-Step Hook

        /// <summary>
        /// Called after each operation or batch of concurrent operations executes.
        /// Use for detailed request/response logging.
        /// </summary>
        public Action<StepExecutedInfo> OnStepExecuted { get; set; }

        /// <summary>
        /// Called after each operation or batch of concurrent operations executes (async).
        /// </summary>
        public Func<StepExecutedInfo, Task> OnStepExecutedAsync { get; set; }

        #endregion

        #region Execution Control

        /// <summary>
        /// Stop on first failure. Default: true.
        /// </summary>
        public bool StopOnFirstFailure { get; set; } = true;

        /// <summary>
        /// Controls retry behavior for operations.
        /// </summary>
        public ShouldRetryOperationLambda ShouldRetry { get; set; }

        #endregion

        #region Fluent Builder Methods

        /// <summary>
        /// Sets a BeforeEach hook.
        /// Use info.Context to access the TestingContext for service registration.
        /// </summary>
        /// <param name="action">Action to run before each test.</param>
        /// <returns>This instance for chaining.</returns>
        public TestExecutionOptions WithBeforeEach(Action<BeforeTestInfo> action)
        {
            BeforeEach = action;
            return this;
        }

        /// <summary>
        /// Sets an AfterEach hook.
        /// Use info.Context to access the TestingContext.
        /// </summary>
        /// <param name="action">Action to run after each test.</param>
        /// <returns>This instance for chaining.</returns>
        public TestExecutionOptions WithAfterEach(Action<AfterTestInfo> action)
        {
            AfterEach = action;
            return this;
        }

        /// <summary>
        /// Sets an async BeforeEach hook.
        /// Use info.Context to access the TestingContext for service registration.
        /// </summary>
        /// <param name="action">Async action to run before each test.</param>
        /// <returns>This instance for chaining.</returns>
        public TestExecutionOptions WithBeforeEachAsync(Func<BeforeTestInfo, Task> action)
        {
            BeforeEachAsync = action;
            return this;
        }

        /// <summary>
        /// Sets an async AfterEach hook.
        /// Use info.Context to access the TestingContext.
        /// </summary>
        /// <param name="action">Async action to run after each test.</param>
        /// <returns>This instance for chaining.</returns>
        public TestExecutionOptions WithAfterEachAsync(Func<AfterTestInfo, Task> action)
        {
            AfterEachAsync = action;
            return this;
        }

        #endregion

        #region Hook Invocation Helpers

        internal async Task InvokeBeforeAllAsync(BeforeAllInfo info)
        {
            if (BeforeAllAsync != null)
                await BeforeAllAsync(info);
            else if (BeforeAll != null)
                BeforeAll(info);
        }

        internal async Task InvokeAfterAllAsync(AfterAllInfo info)
        {
            if (AfterAllAsync != null)
                await AfterAllAsync(info);
            else if (AfterAll != null)
                AfterAll(info);
        }

        internal async Task InvokeBeforeEachAsync(BeforeTestInfo info)
        {
            if (BeforeEachAsync != null)
                await BeforeEachAsync(info);
            else if (BeforeEach != null)
                BeforeEach(info);
        }

        internal async Task InvokeAfterEachAsync(AfterTestInfo info)
        {
            if (AfterEachAsync != null)
                await AfterEachAsync(info);
            else if (AfterEach != null)
                AfterEach(info);
        }

        internal bool HasOnStepExecutedHook => OnStepExecutedAsync != null || OnStepExecuted != null;

        internal async Task InvokeOnStepExecutedAsync(StepExecutedInfo info)
        {
            if (OnStepExecutedAsync != null)
                await OnStepExecutedAsync(info);
            else if (OnStepExecuted != null)
                OnStepExecuted(info);
        }

        #endregion
    }
}
