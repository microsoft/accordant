// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using Microsoft.Accordant;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using NUnit.Framework;

    [TestFixture]
    public class TestExecutorTests
    {
        [Test]
        public async Task SequentialExecutionTests()
        {
            // Create spec and generate tests once
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var sequentialTestCases = spec.GenerateTests(
                initialState,
                inputSet,
                new TestGenerationOptions()
                {
                    MaxDepth = 3
                });

            // All tests should pass with normal behavior.
            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                sequentialTestCases,
                new TestExecutionOptions
                {
                    BeforeEach = _ => context.Register(new SimpleStatefulClass())
                });

            Assert.IsTrue(results.All(r => r.Success));

            // A single test case should pass.
            var context2 = spec.CreateTestingContext();
            var result = await spec.RunTests(
                context2,
                initialState,
                new[]
                {
                    TestCaseGenerator.CreateManualSequentialTestCase(
                        spec.CreateTestingContext(),
                        inputSet,
                        "Add 2", "Count", "Add 3")
                },
                new TestExecutionOptions
                {
                    BeforeEach = _ => context2.Register(new SimpleStatefulClass())
                });

            Assert.IsTrue(result.All(r => r.Success));

            // Most tests should fail with buggy behavior (using factory override).
            var context3 = spec.CreateTestingContext();
            results = await spec.RunTests(
                context3,
                initialState,
                sequentialTestCases,
                new TestExecutionOptions
                {
                    BeforeEach = _ => context3.Register(new SimpleStatefulClass(simulateBuggyBehavior: true))
                });

            Assert.IsTrue(results.Any(r => !r.Success));

            // Single test should fail with buggy behavior.
            var context4 = spec.CreateTestingContext();
            result = await spec.RunTests(
                context4,
                initialState,
                new[]
                {
                    TestCaseGenerator.CreateManualSequentialTestCase(
                        spec.CreateTestingContext(),
                        inputSet,
                        "Add 2", "Add 3")
                },
                new TestExecutionOptions
                {
                    BeforeEach = _ => context4.Register(new SimpleStatefulClass(simulateBuggyBehavior: true))
                });

            Assert.IsTrue(result.Any(r => !r.Success));
        }

        [Test]
        public async Task ConcurrentExecutionTests()
        {
            // Create spec and generate tests once
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var concurrentTestCases = spec.GenerateConcurrentTests(
                initialState,
                inputSet,
                new TestGenerationOptions()
                {
                    MaxDepth = 3
                });

            // All tests should pass with normal behavior.
            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                concurrentTestCases,
                new TestExecutionOptions
                {
                    BeforeEach = _ => context.Register(new SimpleStatefulClass())
                });

            Assert.IsTrue(results.All(r => r.Success));

            // A single test case should pass.
            var context2 = spec.CreateTestingContext();
            var result = await spec.RunTests(
                context2,
                initialState,
                new[]
                {
                    TestCaseGenerator.CreateManualConcurrentTestCase(
                        spec.CreateTestingContext(),
                        inputSet,
                        new string[] { },
                        new string[] { "Add 2", "Count" })
                },
                new TestExecutionOptions
                {
                    BeforeEach = _ => context2.Register(new SimpleStatefulClass())
                });

            Assert.IsTrue(result.All(r => r.Success));

            // Most tests should fail with buggy behavior (using factory override).
            var context3 = spec.CreateTestingContext();
            results = await spec.RunTests(
                context3,
                initialState,
                concurrentTestCases,
                new TestExecutionOptions
                {
                    BeforeEach = _ => context3.Register(new SimpleStatefulClass(simulateBuggyBehavior: true))
                });

            Assert.IsTrue(results.Any(r => !r.Success));

            // Single test should fail with buggy behavior.
            var context4 = spec.CreateTestingContext();
            result = await spec.RunTests(
                context4,
                initialState,
                new[]
                {
                    TestCaseGenerator.CreateManualConcurrentTestCase(
                        spec.CreateTestingContext(),
                        inputSet,
                        new string[] { },
                        new string[] { "Add 2", "Count" })
                },
                new TestExecutionOptions
                {
                    BeforeEach = _ => context4.Register(new SimpleStatefulClass(simulateBuggyBehavior: true))
                });

            Assert.IsTrue(result.Any(r => !r.Success));
        }

        [Test]
        public async Task SequentialExecutionTests_NewApiPattern()
        {
            // Create spec - no ProvideTargetAndInitialState needed
            var spec = new SimpleStatefulClassSpec();

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 3),
                new OperationInput("Count", spec["Count"])
            };

            var initialState = new AtomicState<int>(0);

            var sequentialTestCases = spec.GenerateTests(
                initialState,
                inputSet,
                new TestGenerationOptions { MaxDepth = 3 });

            // Run tests with new API - pass initialState directly, register target in BeforeEach
            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                sequentialTestCases,
                new TestExecutionOptions
                {
                    BeforeEach = _ => context.Register(new SimpleStatefulClass())
                });

            Assert.IsTrue(results.All(r => r.Success));
        }

        [Test]
        public async Task ConcurrentExecutionTests_NewApiPattern()
        {
            // Create spec - no ProvideTargetAndInitialState needed
            var spec = new SimpleStatefulClassSpec();

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 3),
                new OperationInput("Count", spec["Count"])
            };

            var initialState = new AtomicState<int>(0);

            var concurrentTestCases = spec.GenerateConcurrentTests(
                initialState,
                inputSet,
                new TestGenerationOptions { MaxDepth = 3 });

            // Run tests with new API - pass initialState directly, register target in BeforeEach
            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                concurrentTestCases,
                new TestExecutionOptions
                {
                    BeforeEach = _ => context.Register(new SimpleStatefulClass())
                });

            Assert.IsTrue(results.All(r => r.Success));
        }

        [Test]
        public async Task SequentialExecutionTestWithRetries()
        {
            foreach (bool shouldPass in new[] { true, false })
            {
                // Create spec with specific retry configuration
                var spec = new SimpleStatefulClassSpec();
                var initialState = new AtomicState<int>(0);

                var inputSet = new InputSet()
                {
                    new OperationInput("Add 2", spec["Fault Wrapping Add"], 2),
                    new OperationInput("Count", spec["Fault Wrapping Count"])
                };

                var context = spec.CreateTestingContext();
                context.Register(new SimpleStatefulClass(
                    throwExceptionsOnAdd: true,
                    maxAddExceptions: shouldPass ? 2 : 4));
                var result = await spec.RunTests(
                    context,
                    initialState,
                    new[]
                    {
                        TestCaseGenerator.CreateManualSequentialTestCase(
                            spec.CreateTestingContext(),
                            inputSet,
                            "Add 2", "Count")
                    },
                    new TestExecutionOptions()
                    {
                        ShouldRetry = (operation, request, response, numExecutions) =>
                        {
                            // FaultWrappingAddOperation catches exceptions and returns null
                            if (operation != spec["Fault Wrapping Add"] ||
                                response != null)
                            {
                                return new RetryBehavior() { ShouldRetry = false };
                            }

                            return new RetryBehavior()
                            {
                                ShouldRetry = true,
                                ShouldFailTest = numExecutions == 3
                            };
                        }
                    });

                Assert.IsTrue(result.All(r => r.Success) == shouldPass);
            }
        }


        [Test]
        public async Task ConcurrentExecutionTestWithRetries()
        {
            foreach (bool shouldPass in new[] { true, false })
            {
                // Create spec with specific retry configuration
                var spec = new SimpleStatefulClassSpec();
                var initialState = new AtomicState<int>(0);

                var inputSet = new InputSet()
                {
                    new OperationInput("Add 2", spec["Fault Wrapping Add"], 2),
                    new OperationInput("Count", spec["Fault Wrapping Count"])
                };

                var context = spec.CreateTestingContext();
                context.Register(new SimpleStatefulClass(
                    throwExceptionsOnAdd: true,
                    maxAddExceptions: shouldPass ? 2 : 4));
                var result = await spec.RunTests(
                    context,
                    initialState,
                    new[]
                    {
                        TestCaseGenerator.CreateManualConcurrentTestCase(
                            spec.CreateTestingContext(),
                            inputSet,
                            [],
                            ["Add 2", "Count"])
                    },
                    new TestExecutionOptions()
                    {
                        ShouldRetry = (operation, request, response, numExecutions) =>
                        {
                            // FaultWrappingAddOperation catches exceptions and returns null
                            if (operation != spec["Fault Wrapping Add"] ||
                                response != null)
                            {
                                return new RetryBehavior() { ShouldRetry = false };
                            }

                            return new RetryBehavior()
                            {
                                ShouldRetry = true,
                                ShouldFailTest = numExecutions == 3
                            };
                        }
                    });

                Assert.IsTrue(result.All(r => r.Success) == shouldPass);
            }
        }

        [Test]
        public async Task OnStepExecutedHookSequentialTests()
        {
            // Create spec and generate tests once
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var sequentialTestCases = spec.GenerateTests(
                initialState,
                inputSet,
                new TestGenerationOptions()
                {
                    MaxDepth = 3
                });

            int uniqueOperationCalls = sequentialTestCases.Sum(tc => tc.OperationCalls.Count);

            int stepExecutedHookCalledTimes = 0;
            int beforeEachCalledTimes = 0;

            // All tests should pass.
            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                sequentialTestCases,
                new TestExecutionOptions()
                {
                    BeforeEach = (info) =>
                    {
                        beforeEachCalledTimes++;
                        info.Context.Register(new SimpleStatefulClass());
                    },
                    OnStepExecuted = (ctx) =>
                    {
                        stepExecutedHookCalledTimes++;

                        Assert.IsTrue(ctx.IsSingleOperation);
                        Assert.IsTrue(ctx.Operation != null);
                        Assert.IsTrue(ctx.Request != null);
                        Assert.IsTrue(ctx.Response != null);
                    }
                });

            Assert.IsTrue(results.All(r => r.Success));
            Assert.IsTrue(stepExecutedHookCalledTimes == uniqueOperationCalls);
            Assert.IsTrue(beforeEachCalledTimes == results.Count);

            // Run the tests again but this time cause failures using factory override;
            // The test hook should still be called.

            stepExecutedHookCalledTimes = 0;
            beforeEachCalledTimes = 0;

            var context2 = spec.CreateTestingContext();
            results = await spec.RunTests(
                context2,
                initialState,
                sequentialTestCases,
                new TestExecutionOptions()
                {
                    BeforeEach = (info) =>
                    {
                        beforeEachCalledTimes++;
                        info.Context.Register(new SimpleStatefulClass(simulateBuggyBehavior: true));
                    },
                    OnStepExecuted = (ctx) =>
                    {
                        stepExecutedHookCalledTimes++;

                        Assert.IsTrue(ctx.IsSingleOperation);
                        Assert.IsTrue(ctx.Operation != null);
                        Assert.IsTrue(ctx.Request != null);
                        Assert.IsTrue(ctx.Response != null);
                    },
                    StopOnFirstFailure = false
                });

            Assert.IsTrue(stepExecutedHookCalledTimes >= results.Count && stepExecutedHookCalledTimes <= uniqueOperationCalls);
            Assert.IsTrue(beforeEachCalledTimes == results.Count);

            // Run the tests again but this time cause exceptions;
            // With SafeExecuteAsync, exceptions become the response, so the hook is still called
            // The tests will fail validation (int operation returning exception) but hook is invoked

            stepExecutedHookCalledTimes = 0;
            beforeEachCalledTimes = 0;

            var context3 = spec.CreateTestingContext();
            results = await spec.RunTests(
                context3,
                initialState,
                sequentialTestCases,
                new TestExecutionOptions()
                {
                    BeforeEach = (info) =>
                    {
                        beforeEachCalledTimes++;
                        info.Context.Register(new SimpleStatefulClass(
                            throwExceptionsOnAdd: true,
                            throwExceptionsOnGet: true));
                    },
                    OnStepExecuted = (ctx) =>
                    {
                        stepExecutedHookCalledTimes++;
                        // With SafeExecuteAsync, exception becomes the response
                        Assert.IsTrue(ctx.IsSingleOperation);
                        Assert.IsTrue(ctx.Operation != null);
                        Assert.IsTrue(ctx.Request != null);
                        Assert.IsTrue(ctx.Response is Exception);
                    },
                    StopOnFirstFailure = false
                });

            // Hook is called at least once per test case (first operation)
            Assert.IsTrue(stepExecutedHookCalledTimes >= results.Count);
            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(r => !r.Success));
            Assert.IsTrue(beforeEachCalledTimes == results.Count);
        }

        [Test]
        public async Task OnStepExecutedHookConcurrentTests()
        {
            // Create spec and generate tests once
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var concurrentTestCases = spec.GenerateConcurrentTests(
                initialState,
                inputSet,
                new TestGenerationOptions()
                {
                    MaxDepth = 3
                });

            int uniqueOperationCalls = concurrentTestCases.Sum(
                tc => tc.SequentialOperationCalls.Count + (tc.ConcurrentOperationCalls.Count > 0 ? 1 : 0));

            int stepExecutedHookCalledTimes = 0;
            int beforeEachCalledTimes = 0;
            bool someListHadMoreThanOneOperation = false;

            // All tests should pass.
            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                concurrentTestCases,
                new TestExecutionOptions()
                {
                    BeforeEach = (info) =>
                    {
                        beforeEachCalledTimes++;
                        info.Context.Register(new SimpleStatefulClass());
                    },
                    OnStepExecuted = (ctx) =>
                    {
                        stepExecutedHookCalledTimes++;

                        if (!ctx.IsSingleOperation)
                        {
                            someListHadMoreThanOneOperation = true;
                        }

                        Assert.IsTrue(ctx.Operations.Count > 0);
                        Assert.IsTrue(ctx.Operations.All(e =>
                            e.Operation != null &&
                            e.Request != null &&
                            e.Response != null));
                    }
                });

            Assert.IsTrue(results.All(r => r.Success));
            Assert.IsTrue(stepExecutedHookCalledTimes == uniqueOperationCalls);
            Assert.IsTrue(someListHadMoreThanOneOperation);
            Assert.IsTrue(beforeEachCalledTimes == results.Count);

            // Run the tests again but this time cause failures using factory override;
            // The test hook should still be called.

            stepExecutedHookCalledTimes = 0;
            beforeEachCalledTimes = 0;

            var context2 = spec.CreateTestingContext();
            results = await spec.RunTests(
                context2,
                initialState,
                concurrentTestCases,
                new TestExecutionOptions()
                {
                    BeforeEach = (info) =>
                    {
                        beforeEachCalledTimes++;
                        info.Context.Register(new SimpleStatefulClass(simulateBuggyBehavior: true));
                    },
                    OnStepExecuted = (ctx) =>
                    {
                        stepExecutedHookCalledTimes++;

                        Assert.IsTrue(ctx.Operations.Count >= 1);
                        Assert.IsTrue(ctx.Operations.All(e =>
                            e.Operation != null &&
                            e.Request != null &&
                            e.Response != null));
                    },
                    StopOnFirstFailure = false
                });

            Assert.IsTrue(stepExecutedHookCalledTimes >= results.Count && stepExecutedHookCalledTimes <= uniqueOperationCalls);
            Assert.IsTrue(beforeEachCalledTimes == results.Count);

            // Run the tests again but this time cause exceptions;
            // With SafeExecuteAsync, exceptions become the response, so the hook is still called

            stepExecutedHookCalledTimes = 0;
            beforeEachCalledTimes = 0;

            var context3 = spec.CreateTestingContext();
            results = await spec.RunTests(
                context3,
                initialState,
                concurrentTestCases,
                new TestExecutionOptions()
                {
                    BeforeEach = (info) =>
                    {
                        beforeEachCalledTimes++;
                        info.Context.Register(new SimpleStatefulClass(
                            throwExceptionsOnAdd: true,
                            throwExceptionsOnGet: true));
                    },
                    OnStepExecuted = (ctx) =>
                    {
                        stepExecutedHookCalledTimes++;
                        // With SafeExecuteAsync, exception becomes the response
                        Assert.IsTrue(ctx.Operations.Count >= 1);
                        Assert.IsTrue(ctx.Operations.All(e =>
                            e.Operation != null &&
                            e.Request != null &&
                            e.Response is Exception));
                    },
                    StopOnFirstFailure = false
                });

            // Hook is called for concurrent execution blocks
            Assert.IsTrue(stepExecutedHookCalledTimes >= results.Count);
            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(r => !r.Success));
            Assert.IsTrue(beforeEachCalledTimes == results.Count);
        }

        [Test]
        public async Task BeforeAllAndAfterAllHooks()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 2 });

            bool beforeAllCalled = false;
            bool afterAllCalled = false;
            int beforeAllTotalTests = 0;
            int afterAllTotalTests = 0;
            int afterAllPassed = 0;
            int afterAllFailed = 0;
            int afterAllSkipped = 0;
            TimeSpan afterAllDuration = TimeSpan.Zero;

            var context = spec.CreateTestingContext();
            context.Register(new SimpleStatefulClass());
            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions()
                {
                    BeforeAll = ctx =>
                    {
                        beforeAllCalled = true;
                        beforeAllTotalTests = ctx.TotalTests;
                        Assert.IsNotNull(ctx.Spec);
                    },
                    AfterAll = ctx =>
                    {
                        afterAllCalled = true;
                        afterAllTotalTests = ctx.TotalTests;
                        afterAllPassed = ctx.Passed;
                        afterAllFailed = ctx.Failed;
                        afterAllSkipped = ctx.Skipped;
                        afterAllDuration = ctx.TotalDuration;
                        Assert.IsNotNull(ctx.Spec);
                        Assert.IsNotNull(ctx.Results);
                    }
                });

            Assert.IsTrue(beforeAllCalled, "BeforeAll should be called");
            Assert.IsTrue(afterAllCalled, "AfterAll should be called");
            Assert.AreEqual(testCases.Count, beforeAllTotalTests, "BeforeAll should receive correct total tests");
            Assert.AreEqual(testCases.Count, afterAllTotalTests, "AfterAll should receive correct total tests");
            Assert.AreEqual(results.Count(r => r.Success), afterAllPassed, "AfterAll should have correct passed count");
            Assert.AreEqual(results.Count(r => !r.Success), afterAllFailed, "AfterAll should have correct failed count");
            Assert.AreEqual(0, afterAllSkipped, "No tests should be skipped when all pass");
            Assert.IsTrue(afterAllDuration > TimeSpan.Zero, "AfterAll should have non-zero duration");
        }

        [Test]
        public async Task BeforeEachAndAfterEachHooksWithContextVerification()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 2 });

            int beforeEachCalls = 0;
            int afterEachCalls = 0;
            int successfulAfterEach = 0;

            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions()
                {
                    BeforeEach = info =>
                    {
                        beforeEachCalls++;
                        info.Context.Register(new SimpleStatefulClass());
                        Assert.IsNotNull(info.TestCase, "BeforeEach should have TestCase");
                        Assert.IsNotNull(info.InitialState, "BeforeEach should have InitialState");
                        Assert.IsNotNull(info.Context, "BeforeEach should have Context");
                        Assert.IsTrue(info.TestIndex >= 0, "TestIndex should be >= 0");
                        Assert.AreEqual(testCases.Count, info.TotalTests, "TotalTests should match");
                    },
                    AfterEach = info =>
                    {
                        afterEachCalls++;
                        Assert.IsNotNull(info.TestCase, "AfterEach should have TestCase");
                        Assert.IsNotNull(info.Context, "AfterEach should have Context");
                        Assert.IsTrue(info.TestIndex >= 0, "TestIndex should be >= 0");
                        Assert.AreEqual(testCases.Count, info.TotalTests, "TotalTests should match");
                        Assert.IsTrue(info.Duration > TimeSpan.Zero, "Duration should be > 0");
                        
                        if (info.Success)
                        {
                            successfulAfterEach++;
                            Assert.IsTrue(string.IsNullOrEmpty(info.FailureMessage), "FailureMessage should be null or empty on success");
                            Assert.IsNull(info.Exception, "Exception should be null on success");
                            Assert.IsNotNull(info.FinalState, "FinalState should exist on success");
                        }
                    },
                    StopOnFirstFailure = false
                });

            Assert.AreEqual(testCases.Count, beforeEachCalls, "BeforeEach should be called for each test");
            Assert.AreEqual(testCases.Count, afterEachCalls, "AfterEach should be called for each test");
            Assert.AreEqual(results.Count(r => r.Success), successfulAfterEach, "Successful AfterEach count should match");
        }

        [Test]
        public async Task AfterEachCalledOnFailure()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 3)
            };

            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 2 });

            int afterEachCalls = 0;
            int failedAfterEachCalls = 0;

            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions()
                {
                    BeforeEach = _ => context.Register(new SimpleStatefulClass(simulateBuggyBehavior: true)),
                    AfterEach = info =>
                    {
                        afterEachCalls++;
                        if (!info.Success)
                        {
                            failedAfterEachCalls++;
                            Assert.IsNotNull(info.FailureMessage, "FailureMessage should exist on failure");
                        }
                    },
                    StopOnFirstFailure = false
                });

            Assert.AreEqual(testCases.Count, afterEachCalls, "AfterEach should be called for ALL tests including failures");
            Assert.IsTrue(failedAfterEachCalls > 0, "Some tests should have failed");
            Assert.AreEqual(results.Count(r => !r.Success), failedAfterEachCalls, "Failed AfterEach count should match failed results");
        }

        [Test]
        public async Task StopOnFirstFailureWithSkippedCount()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 3)
            };

            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 3 });
            
            // Make sure we have enough test cases
            Assert.IsTrue(testCases.Count > 2, "Need multiple test cases for this test");

            int afterAllSkipped = -1;

            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions()
                {
                    BeforeEach = _ => context.Register(new SimpleStatefulClass(simulateBuggyBehavior: true)),
                    StopOnFirstFailure = true,
                    AfterAll = ctx =>
                    {
                        afterAllSkipped = ctx.Skipped;
                    }
                });

            // With StopOnFirstFailure, we should stop after first failure
            Assert.IsTrue(results.Count < testCases.Count, "Should stop before running all tests");
            Assert.AreEqual(testCases.Count - results.Count, afterAllSkipped, "Skipped count should be total - executed");
        }

        [Test]
        public async Task AsyncHooks()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 2 });

            bool beforeAllAsyncCalled = false;
            bool afterAllAsyncCalled = false;
            bool beforeEachAsyncCalled = false;
            bool afterEachAsyncCalled = false;
            bool onStepExecutedAsyncCalled = false;

            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions()
                {
                    BeforeEach = _ => context.Register(new SimpleStatefulClass()),
                    BeforeAllAsync = async ctx =>
                    {
                        await Task.Delay(1);
                        beforeAllAsyncCalled = true;
                    },
                    AfterAllAsync = async ctx =>
                    {
                        await Task.Delay(1);
                        afterAllAsyncCalled = true;
                    },
                    BeforeEachAsync = async info =>
                    {
                        await Task.Delay(1);
                        beforeEachAsyncCalled = true;
                    },
                    AfterEachAsync = async info =>
                    {
                        await Task.Delay(1);
                        afterEachAsyncCalled = true;
                    },
                    OnStepExecutedAsync = async ctx =>
                    {
                        await Task.Delay(1);
                        onStepExecutedAsyncCalled = true;
                    }
                });

            Assert.IsTrue(beforeAllAsyncCalled, "BeforeAllAsync should be called");
            Assert.IsTrue(afterAllAsyncCalled, "AfterAllAsync should be called");
            Assert.IsTrue(beforeEachAsyncCalled, "BeforeEachAsync should be called");
            Assert.IsTrue(afterEachAsyncCalled, "AfterEachAsync should be called");
            Assert.IsTrue(onStepExecutedAsyncCalled, "OnStepExecutedAsync should be called");
        }

        [Test]
        public async Task InlineHooksViaRunTests()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Count", spec["Count"])
            };

            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 2 });

            bool beforeAllCalled = false;
            bool afterAllCalled = false;
            int beforeEachCalls = 0;
            int afterEachCalls = 0;

            // Test inline hooks syntax
            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions()
                {
                    BeforeAll = ctx => beforeAllCalled = true,
                    AfterAll = ctx => afterAllCalled = true,
                    BeforeEach = info =>
                    {
                        beforeEachCalls++;
                        info.Context.Register(new SimpleStatefulClass());
                    },
                    AfterEach = info => afterEachCalls++
                });

            Assert.IsTrue(beforeAllCalled, "Inline BeforeAll should be called");
            Assert.IsTrue(afterAllCalled, "Inline AfterAll should be called");
            Assert.AreEqual(testCases.Count, beforeEachCalls, "Inline BeforeEach should be called for each test");
            Assert.AreEqual(testCases.Count, afterEachCalls, "Inline AfterEach should be called for each test");
        }

        [Test]
        public void EmptyTestCasesAreFilteredOut()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2)
            };

            // maxDepth: 1 would previously generate 1 test case with 0 operations
            // Now it should be filtered out
            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 1 });

            Console.WriteLine($"maxDepth: 1 => Test cases count: {testCases.Count}");
            foreach (var tc in testCases)
            {
                Console.WriteLine($"  Operations: {tc.OperationCalls.Count} - {tc.Description}");
            }

            // All test cases should have at least one operation
            Assert.IsTrue(testCases.All(tc => tc.OperationCalls.Count > 0), 
                "All generated test cases should have at least one operation");
            
            // With maxDepth: 1, no valid test cases can be generated (need depth 2 for any operations)
            Assert.AreEqual(0, testCases.Count, 
                "maxDepth: 1 should generate 0 test cases since empty ones are filtered out");
        }

        [Test]
        public async Task ContextTargetAccess()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2)
            };

            var testCases = spec.GenerateTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 2 });

            // Find a test case that actually has operations
            var testCaseWithOperations = testCases.FirstOrDefault(tc => tc.OperationCalls.Count > 0);
            Assert.IsNotNull(testCaseWithOperations, "Need at least one test case with operations for this test");
            
            // Run just the test case with operations
            var filteredTestCases = new List<SequentialTestCase> { testCaseWithOperations };

            SimpleStatefulClass targetFromBeforeEach = null;
            SimpleStatefulClass targetFromAfterEach = null;
            bool onStepExecutedCalled = false;

            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                filteredTestCases,
                new TestExecutionOptions()
                {
                    BeforeEach = info =>
                    {
                        info.Context.Register(new SimpleStatefulClass());
                        targetFromBeforeEach = info.Context.Get<SimpleStatefulClass>();
                    },
                    AfterEach = info =>
                    {
                        targetFromAfterEach = info.Context.Get<SimpleStatefulClass>();
                    },
                    OnStepExecuted = ctx =>
                    {
                        onStepExecutedCalled = true;
                        // OnStepExecuted also has Context access
                        Assert.IsNotNull(ctx.Context, "OnStepExecuted should have Context");
                    }
                });

            Assert.IsNotNull(targetFromBeforeEach, "Should be able to access target from BeforeEach");
            Assert.IsNotNull(targetFromAfterEach, "Should be able to access target from AfterEach");
            Assert.IsTrue(onStepExecutedCalled, "OnStepExecuted should be called");
        }

        [Test]
        public async Task ConcurrentTestsWithAllHooks()
        {
            var spec = new SimpleStatefulClassSpec();
            var initialState = new AtomicState<int>(0);

            var inputSet = new InputSet()
            {
                new OperationInput("Add 2", spec["Add"], 2),
                new OperationInput("Add 3", spec["Add"], 3),
                new OperationInput("Count", spec["Count"])
            };

            var testCases = spec.GenerateConcurrentTests(initialState, inputSet, new TestGenerationOptions { MaxDepth = 2 });

            bool beforeAllCalled = false;
            bool afterAllCalled = false;
            int beforeEachCalls = 0;
            int afterEachCalls = 0;
            int onStepExecutedCalls = 0;

            var context = spec.CreateTestingContext();
            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions()
                {
                    BeforeAll = ctx => beforeAllCalled = true,
                    AfterAll = ctx => afterAllCalled = true,
                    BeforeEach = info =>
                    {
                        beforeEachCalls++;
                        info.Context.Register(new SimpleStatefulClass());
                    },
                    AfterEach = info => afterEachCalls++,
                    OnStepExecuted = ctx => onStepExecutedCalls++
                });

            Assert.IsTrue(beforeAllCalled, "BeforeAll should be called for concurrent tests");
            Assert.IsTrue(afterAllCalled, "AfterAll should be called for concurrent tests");
            Assert.AreEqual(testCases.Count, beforeEachCalls, "BeforeEach should be called for each concurrent test");
            Assert.AreEqual(testCases.Count, afterEachCalls, "AfterEach should be called for each concurrent test");
            Assert.IsTrue(onStepExecutedCalls > 0, "OnStepExecuted should be called for concurrent tests");
        }
    }
}
