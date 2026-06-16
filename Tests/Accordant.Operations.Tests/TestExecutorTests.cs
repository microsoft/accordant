// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests;

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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

        var initialState = new CounterState(0);

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

        var initialState = new CounterState(0);

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
            var initialState = new CounterState(0);

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
            var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
            tc => tc.Segments.Count);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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
        var initialState = new CounterState(0);

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

    /// <summary>
    /// Test that a terminal step function does not cause spurious polling
    /// on subsequent operations that don't have polling configured.
    /// 
    /// Scenario:
    /// 1. StartAsync triggers async work, returns "pending", triggers step function
    /// 2. GetStatus polls and step function fires, transitioning to "success"
    /// 3. Unrelated operation runs (no polling configured)
    /// 4. Should not attempt to poll for Unrelated
    /// </summary>
    [Test]
    public async Task TerminalStepFunction_ShouldNotTriggerPollingOnSubsequentOperations()
    {
        var spec = new AsyncOperationSpec();
        var initialState = new AsyncOperationState { Status = "none" };

        var inputSet = new InputSet()
        {
            new OperationInput("StartAsync", spec["StartAsync"]),
            new OperationInput("Unrelated", spec["Unrelated"]),
        };

        // Create a manual test case: StartAsync -> Unrelated
        var testCase = TestCaseGenerator.CreateManualSequentialTestCase(
            spec.CreateTestingContext(),
            inputSet,
            "StartAsync", "Unrelated");

        var context = spec.CreateTestingContext();

        // Run the test - this should NOT throw a PollingException
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new AsyncWorkTarget())
            });

        // Provide detailed failure information
        var failedResults = results.Where(r => !r.Success).ToList();
        if (failedResults.Any())
        {
            var messages = string.Join("\n", failedResults.Select(r => r.LastFailureMessage));
            Assert.Fail($"Test failed with messages:\n{messages}");
        }

        // The test should pass without throwing PollingException
        Assert.IsTrue(results.All(r => r.Success),
            "Test should pass - Unrelated operation should not trigger polling " +
            "just because a previous step function is still (terminally) in the state profile");
    }

    /// <summary>
    /// Test that TriggersWhen correctly avoids triggering step function when response
    /// indicates immediate completion (like CopyBlob returning "success" immediately).
    /// </summary>
    [Test]
    public async Task TriggersWhen_ImmediateSuccess_NoStepFunctionTriggered()
    {
        var spec = new CopyOperationSpec();
        var initialState = new CopyState { CopyStatus = "none" };

        var inputSet = new InputSet()
        {
            new OperationInput("StartCopy", spec["StartCopy"]),
            new OperationInput("UnrelatedCopy", spec["UnrelatedCopy"]),
        };

        var testCase = TestCaseGenerator.CreateManualSequentialTestCase(
            spec.CreateTestingContext(),
            inputSet,
            "StartCopy", "UnrelatedCopy");

        var context = spec.CreateTestingContext();

        // Use CopyTarget with immediateSuccess=true - StartCopy returns "success" immediately
        // TriggersWhen should NOT trigger the step function
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new CopyTarget(immediateSuccess: true))
            });

        var failedResults = results.Where(r => !r.Success).ToList();
        if (failedResults.Any())
        {
            var messages = string.Join("\n", failedResults.Select(r => r.LastFailureMessage));
            Assert.Fail($"Test failed with messages:\n{messages}");
        }

        Assert.IsTrue(results.All(r => r.Success),
            "Test should pass - TriggersWhen should not trigger SF when response is 'success'");
    }

    /// <summary>
    /// Test that TriggersWhen correctly triggers step function when response
    /// indicates async work is pending (like CopyBlob returning "pending").
    /// </summary>
    [Test]
    public async Task TriggersWhen_PendingResponse_StepFunctionTriggeredAndPolled()
    {
        var spec = new CopyOperationSpec();
        var initialState = new CopyState { CopyStatus = "none" };

        var inputSet = new InputSet()
        {
            new OperationInput("StartCopy", spec["StartCopy"]),
            new OperationInput("UnrelatedCopy", spec["UnrelatedCopy"]),
        };

        var testCase = TestCaseGenerator.CreateManualSequentialTestCase(
            spec.CreateTestingContext(),
            inputSet,
            "StartCopy", "UnrelatedCopy");

        var context = spec.CreateTestingContext();

        // Use CopyTarget with immediateSuccess=false - StartCopy returns "pending"
        // TriggersWhen SHOULD trigger the step function, which will be polled
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new CopyTarget(immediateSuccess: false))
            });

        var failedResults = results.Where(r => !r.Success).ToList();
        if (failedResults.Any())
        {
            var messages = string.Join("\n", failedResults.Select(r => r.LastFailureMessage));
            Assert.Fail($"Test failed with messages:\n{messages}");
        }

        Assert.IsTrue(results.All(r => r.Success),
            "Test should pass - TriggersWhen should trigger SF when response is 'pending', and polling should complete");
    }

    #region Segment-based ConcurrentTestCase Tests

    [Test]
    public async Task ConcurrentTestCase_GeneratedTestCasesHaveSegments()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        var concurrentTestCases = spec.GenerateConcurrentTests(
            initialState,
            inputSet,
            new TestGenerationOptions { MaxDepth = 3 });

        Assert.IsTrue(concurrentTestCases.Count > 0, "Should generate concurrent test cases");

        foreach (var testCase in concurrentTestCases)
        {
            Assert.IsNotNull(testCase.Segments, "Segments should not be null");
            Assert.IsTrue(testCase.Segments.Count > 0, "Should have at least one segment");

            foreach (var segment in testCase.Segments)
            {
                Assert.IsNotNull(segment.OperationCalls, "Segment OperationCalls should not be null");
                Assert.IsTrue(segment.OperationCalls.Count > 0, "Each segment should have at least one operation call");
            }

            // Last segment should be concurrent (multiple ops)
            var lastSegment = testCase.Segments[testCase.Segments.Count - 1];
            Assert.IsTrue(lastSegment.IsConcurrent, "Last segment should be concurrent");

            // All non-last segments should be sequential (single op)
            for (int i = 0; i < testCase.Segments.Count - 1; i++)
            {
                Assert.IsTrue(testCase.Segments[i].IsSequential,
                    $"Segment {i} should be sequential (single op)");
            }
        }
    }

    [Test]
    public async Task ConcurrentTestCase_ManualMultiSegmentTestCase()
    {
        // Create a multi-segment test case manually:
        // Segment 1 (seq): Add 2
        // Segment 2 (concurrent): Add 2 || Count
        // Segment 3 (seq): Count
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new OperationCall("Add 2", inputSet["Add 2"])),
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("Add 2 concurrent", inputSet["Add 2"]),
                new OperationCall("Count concurrent", inputSet["Count"])
            }),
            new TestCaseSegment(new OperationCall("Count final", inputSet["Count"]))
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        Assert.IsTrue(testCase.Segments[0].IsSequential);
        Assert.IsTrue(testCase.Segments[1].IsConcurrent);
        Assert.IsTrue(testCase.Segments[2].IsSequential);

        var context = spec.CreateTestingContext();
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new SimpleStatefulClass())
            });

        Assert.IsTrue(results.All(r => r.Success),
            "Multi-segment concurrent test case should pass");
    }

    [Test]
    public async Task ConcurrentTestCase_ConcurrentOnlySegment()
    {
        // Test case with no sequential prefix — just a concurrent segment
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("Add 2", inputSet["Add 2"]),
                new OperationCall("Count", inputSet["Count"])
            })
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new SimpleStatefulClass())
            });

        Assert.IsTrue(results.All(r => r.Success),
            "Concurrent-only test case should pass");
    }

    [Test]
    public void ConcurrentTestCase_DescriptionFormatting()
    {
        var call1 = new OperationCall("A", null);
        var call2 = new OperationCall("B", null);
        var call3 = new OperationCall("C", null);

        // Sequential only
        var segments1 = new List<TestCaseSegment>
        {
            new TestCaseSegment(call1),
            new TestCaseSegment(call2)
        };
        var desc1 = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments1);
        Assert.AreEqual("A --> B", desc1);

        // Concurrent only
        var segments2 = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall> { call1, call2 })
        };
        var desc2 = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments2);
        Assert.AreEqual("A || B", desc2);

        // Mixed: seq -> concurrent -> seq
        var segments3 = new List<TestCaseSegment>
        {
            new TestCaseSegment(call1),
            new TestCaseSegment(new List<OperationCall> { call2, call3 }),
            new TestCaseSegment(call1)
        };
        var desc3 = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments3);
        Assert.AreEqual("A --> B || C --> A", desc3);
    }

    [Test]
    public async Task ConcurrentTestCase_OnStepExecutedHookCalledPerSegment()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        // Create a test case with 3 segments: seq, concurrent, seq
        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new OperationCall("Add 2", inputSet["Add 2"])),
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("Add 2 again", inputSet["Add 2"]),
                new OperationCall("Count concurrent", inputSet["Count"])
            }),
            new TestCaseSegment(new OperationCall("Count final", inputSet["Count"]))
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        int stepExecutedCount = 0;
        int singleOpCount = 0;
        int multiOpCount = 0;

        var context = spec.CreateTestingContext();
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new SimpleStatefulClass()),
                OnStepExecuted = (ctx) =>
                {
                    stepExecutedCount++;
                    if (ctx.IsSingleOperation) singleOpCount++;
                    else multiOpCount++;
                }
            });

        Assert.IsTrue(results.All(r => r.Success));
        Assert.AreEqual(3, stepExecutedCount, "OnStepExecuted should be called once per segment");
        Assert.AreEqual(2, singleOpCount, "Two sequential segments should trigger single-op hooks");
        Assert.AreEqual(1, multiOpCount, "One concurrent segment should trigger multi-op hook");
    }

    [Test]
    public async Task ConcurrentTestCase_BuggyBehaviorDetected()
    {
        // Verify that a multi-segment test case can detect buggy behavior
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new OperationCall("Add 2", inputSet["Add 2"])),
            new TestCaseSegment(new OperationCall("Count", inputSet["Count"]))
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        // Should fail with buggy behavior
        var context = spec.CreateTestingContext();
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new SimpleStatefulClass(simulateBuggyBehavior: true))
            });

        Assert.IsTrue(results.Any(r => !r.Success),
            "Multi-segment test should detect buggy behavior");
    }

    [Test]
    public void ConcurrentTestCase_SegmentProperties()
    {
        var call1 = new OperationCall("A", null);
        var call2 = new OperationCall("B", null);

        var seqSegment = new TestCaseSegment(call1);
        Assert.IsTrue(seqSegment.IsSequential);
        Assert.IsFalse(seqSegment.IsConcurrent);
        Assert.AreEqual(1, seqSegment.OperationCalls.Count);

        var concSegment = new TestCaseSegment(new List<OperationCall> { call1, call2 });
        Assert.IsFalse(concSegment.IsSequential);
        Assert.IsTrue(concSegment.IsConcurrent);
        Assert.AreEqual(2, concSegment.OperationCalls.Count);
    }

    [Test]
    public void ConcurrentTestCase_SerializationRoundTrip()
    {
        var spec = new SimpleStatefulClassSpec();

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        var initialState = new CounterState(0);

        var concurrentTestCases = spec.GenerateConcurrentTests(
            initialState,
            inputSet,
            new TestGenerationOptions { MaxDepth = 3 });

        var context = spec.CreateTestingContext();

        var serialized = TestCaseGenerator.SerializeConcurrentTestCases(context, concurrentTestCases);
        var deserialized = TestCaseGenerator.DeserializeConcurrentTestCases(context, serialized);

        Assert.AreEqual(concurrentTestCases.Count, deserialized.Count);

        for (int i = 0; i < concurrentTestCases.Count; i++)
        {
            Assert.AreEqual(concurrentTestCases[i].Description, deserialized[i].Description);
            Assert.AreEqual(concurrentTestCases[i].Segments.Count, deserialized[i].Segments.Count);

            for (int j = 0; j < concurrentTestCases[i].Segments.Count; j++)
            {
                Assert.AreEqual(
                    concurrentTestCases[i].Segments[j].OperationCalls.Count,
                    deserialized[i].Segments[j].OperationCalls.Count);
            }
        }
    }

    #endregion

    #region Polling - SkipPolling Step Function Leakage

    [Test]
    public async Task SequentialTestCase_SkipPollingDoesNotLeakStepFunctionsToNextOp()
    {
        // Op A (StartAsync) with SkipPolling=true triggers a step function.
        // Op B (Unrelated) has no polling configured.
        // With the bug: B sees A's non-terminal step function, tries to poll,
        //   and crashes because B has no polling setup.
        // With the fix: B only considers step functions introduced by itself (none),
        //   so no polling is attempted and the test passes.
        var spec = new AsyncOperationSpec();
        var initialState = new AsyncOperationState();

        var startInput = new OperationInput("StartAsync", spec["StartAsync"]);
        startInput.WithoutPolling();

        var inputSet = new InputSet()
        {
            startInput,
            new OperationInput("Unrelated", spec["Unrelated"])
        };

        var testCase = new SequentialTestCase()
        {
            Description = "SkipPolling leakage test",
            OperationCalls = new List<OperationCall>
            {
                new OperationCall("StartAsync", inputSet["StartAsync"]),
                new OperationCall("Unrelated", inputSet["Unrelated"])
            }
        };

        var context = spec.CreateTestingContext();
        context.Register(new AsyncWorkTarget());

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        Assert.IsTrue(results.All(r => r.Success),
            "Unrelated op should not be forced to poll for StartAsync's step function. " +
            $"Failure: {results.FirstOrDefault(r => !r.Success)?.LastFailureMessage}");
    }

    [Test]
    public async Task ConcurrentTestCase_SkipPollingDoesNotLeakToNextSegment()
    {
        // Segment 1 (sequential): StartJobA with SkipPolling=true — triggers step function A
        // Segment 2 (concurrent): StartJobB (has polling) — triggers step function B
        // After segment 2, polling should only target B's step function, not A's.
        var spec = new DualAsyncSpec();
        var initialState = new DualAsyncState();

        var startAInput = new OperationInput("StartJobA", spec["StartJobA"]);
        startAInput.WithoutPolling();

        var inputSet = new InputSet()
        {
            startAInput,
            new OperationInput("StartJobB", spec["StartJobB"])
        };

        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new OperationCall("StartJobA", inputSet["StartJobA"])),
            new TestCaseSegment(new OperationCall("StartJobB", inputSet["StartJobB"]))
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new DualAsyncTarget());

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        Assert.IsTrue(results.All(r => r.Success),
            "Segment 2 should only poll for its own step functions, not segment 1's skipped ones. " +
            $"Failure: {results.FirstOrDefault(r => !r.Success)?.LastFailureMessage}");
    }

    #endregion

    #region Validation Tests

    [Test]
    public void ConcurrentTestCase_NullSegmentsThrows()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var testCase = new ConcurrentTestCase()
        {
            Description = "null segments test",
            Segments = null
        };

        var context = spec.CreateTestingContext();
        context.Register(new SimpleStatefulClass());

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await spec.RunTests(
                context,
                initialState,
                new[] { testCase },
                new TestExecutionOptions()));

        Assert.IsTrue(ex.Message.Contains("no segments"),
            $"Expected 'no segments' in message, got: {ex.Message}");
    }

    [Test]
    public void ConcurrentTestCase_EmptySegmentThrows()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall>()) // empty!
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = "empty segment test",
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new SimpleStatefulClass());

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await spec.RunTests(
                context,
                initialState,
                new[] { testCase },
                new TestExecutionOptions()));

        Assert.IsTrue(ex.Message.Contains("no operation calls"),
            $"Expected 'no operation calls' in message, got: {ex.Message}");
    }

    [Test]
    public void ConcurrentTestCase_DuplicateOpNameAcrossSegmentsThrows()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        // Same name "Add 2" in two different segments
        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new OperationCall("Add 2", inputSet["Add 2"])),
            new TestCaseSegment(new OperationCall("Add 2", inputSet["Add 2"]))
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = "duplicate name test",
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new SimpleStatefulClass());

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await spec.RunTests(
                context,
                initialState,
                new[] { testCase },
                new TestExecutionOptions()));

        Assert.IsTrue(ex.Message.Contains("Duplicate operation call name"),
            $"Expected 'Duplicate operation call name' in message, got: {ex.Message}");
    }

    [Test]
    public void ConcurrentTestCase_SameSegmentDerivedRequestThrows()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        // Create an operation call that derives from another in the same concurrent segment
        var sourceCall = new OperationCall("Source", inputSet["Add 2"]);
        var derivedInput = new OperationInput(
            "Derived",
            spec["Count"],
            Unit.Value,
            new List<OperationCall> { sourceCall },
            null);
        var derivedCall = new OperationCall("Derived", derivedInput);

        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall> { sourceCall, derivedCall })
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = "same-segment derived test",
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new SimpleStatefulClass());

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await spec.RunTests(
                context,
                initialState,
                new[] { testCase },
                new TestExecutionOptions()));

        Assert.IsTrue(ex.Message.Contains("same segment"),
            $"Expected 'same segment' in message, got: {ex.Message}");
    }

    [Test]
    public async Task ConcurrentTestCase_GeneratedTestCasesHaveUniqueNames()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        var concurrentTestCases = spec.GenerateConcurrentTests(
            initialState,
            inputSet,
            new TestGenerationOptions { MaxDepth = 3 });

        // Verify that all generated test cases have unique names within each test case
        foreach (var testCase in concurrentTestCases)
        {
            var allNames = testCase.Segments
                .SelectMany(s => s.OperationCalls)
                .Select(c => c.Name)
                .ToList();

            var uniqueNames = new HashSet<string>(allNames);
            Assert.AreEqual(allNames.Count, uniqueNames.Count,
                $"Test case '{testCase.Description}' has duplicate operation call names: " +
                $"{string.Join(", ", allNames)}");
        }
    }

    [Test]
    public void SequentialTestCase_EmptyOperationCallsThrows()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var testCase = new SequentialTestCase()
        {
            Description = "empty ops test",
            OperationCalls = new List<OperationCall>()
        };

        var context = spec.CreateTestingContext();
        context.Register(new SimpleStatefulClass());

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await spec.RunTests(
                context,
                initialState,
                new[] { testCase },
                new TestExecutionOptions()));

        Assert.IsTrue(ex.Message.Contains("no operation calls"),
            $"Expected 'no operation calls' in message, got: {ex.Message}");
    }

    [Test]
    public void SequentialTestCase_NullOperationCallsThrows()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var testCase = new SequentialTestCase()
        {
            Description = "null ops test",
            OperationCalls = null
        };

        var context = spec.CreateTestingContext();
        context.Register(new SimpleStatefulClass());

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await spec.RunTests(
                context,
                initialState,
                new[] { testCase },
                new TestExecutionOptions()));

        Assert.IsTrue(ex.Message.Contains("no operation calls"),
            $"Expected 'no operation calls' in message, got: {ex.Message}");
    }

    [Test]
    public void SequentialTestCase_DuplicateOpNameThrows()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 2", spec["Add"], 2),
            new OperationInput("Count", spec["Count"])
        };

        var testCase = new SequentialTestCase()
        {
            Description = "duplicate name test",
            OperationCalls = new List<OperationCall>
            {
                new OperationCall("Add 2", inputSet["Add 2"]),
                new OperationCall("Add 2", inputSet["Add 2"])
            }
        };

        var context = spec.CreateTestingContext();
        context.Register(new SimpleStatefulClass());

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await spec.RunTests(
                context,
                initialState,
                new[] { testCase },
                new TestExecutionOptions()));

        Assert.IsTrue(ex.Message.Contains("Duplicate operation call name"),
            $"Expected 'Duplicate operation call name' in message, got: {ex.Message}");
    }

    #endregion

    #region Concurrent Polling Tests

    [Test]
    public async Task ConcurrentTestCase_PollingAfterConcurrentSegment()
    {
        // Use the AsyncOperationSpec which has StartAsync (with polling) and Unrelated (no polling)
        var spec = new AsyncOperationSpec();
        var initialState = new AsyncOperationState();

        var inputSet = new InputSet()
        {
            new OperationInput("StartAsync", spec["StartAsync"]),
            new OperationInput("Unrelated", spec["Unrelated"])
        };

        // Create a concurrent segment with StartAsync + Unrelated
        // StartAsync has polling configured — polling should run after the concurrent segment
        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("StartAsync", inputSet["StartAsync"]),
                new OperationCall("Unrelated", inputSet["Unrelated"])
            })
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new AsyncWorkTarget());

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        Assert.IsTrue(results.All(r => r.Success),
            "Concurrent test with polling should pass — polling should complete the async work. " +
            $"Failure: {results.FirstOrDefault(r => !r.Success)?.LastFailureMessage}");
    }

    [Test]
    public async Task ConcurrentTestCase_PollingSkippedWhenFlagSet()
    {
        // Verify SkipPolling is respected for concurrent segments
        var spec = new AsyncOperationSpec();
        var initialState = new AsyncOperationState();

        var inputWithoutPolling = new OperationInput("StartAsync", spec["StartAsync"]);
        inputWithoutPolling.WithoutPolling();

        var inputSet = new InputSet()
        {
            inputWithoutPolling,
            new OperationInput("Unrelated", spec["Unrelated"])
        };

        // Create test with concurrent segment where polling is skipped
        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("StartAsync", inputSet["StartAsync"]),
                new OperationCall("Unrelated", inputSet["Unrelated"])
            })
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = "skip polling test",
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new AsyncWorkTarget());

        // Should succeed — no polling means we accept the state as is
        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        // The test should complete (not hang or throw polling-related errors)
        Assert.IsTrue(results.Count == 1, "Should have exactly one result");
    }

    [Test]
    public async Task ConcurrentTestCase_MultiSegmentWithPolling()
    {
        // seq(StartAsync) -> polling completes -> seq(GetStatus should show success)
        var spec = new AsyncOperationSpec();
        var initialState = new AsyncOperationState();

        var inputSet = new InputSet()
        {
            new OperationInput("StartAsync", spec["StartAsync"]),
            new OperationInput("Unrelated", spec["Unrelated"])
        };

        // Segment 1: Start async (sequential — polling happens after this)
        // Segment 2: Unrelated (sequential — should work after polling completed)
        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new OperationCall("StartAsync", inputSet["StartAsync"])),
            new TestCaseSegment(new OperationCall("Unrelated", inputSet["Unrelated"]))
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new AsyncWorkTarget());

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        Assert.IsTrue(results.All(r => r.Success),
            "Multi-segment test with polling should pass");
    }

    [Test]
    public async Task ConcurrentTestCase_DualPollingAfterConcurrentSegment()
    {
        // Two independent async ops (StartJobA, StartJobB) in a concurrent segment.
        // Both have polling configured. Polling should complete both jobs.
        var spec = new DualAsyncSpec();
        var initialState = new DualAsyncState();

        var inputSet = new InputSet()
        {
            new OperationInput("StartJobA", spec["StartJobA"]),
            new OperationInput("StartJobB", spec["StartJobB"])
        };

        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("StartJobA", inputSet["StartJobA"]),
                new OperationCall("StartJobB", inputSet["StartJobB"])
            })
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new DualAsyncTarget());

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        Assert.IsTrue(results.All(r => r.Success),
            "Dual concurrent polling should complete both jobs. " +
            $"Failure: {results.FirstOrDefault(r => !r.Success)?.LastFailureMessage}");
    }

    [Test]
    public async Task ConcurrentTestCase_PollingFailureExhaustsRetryCount()
    {
        // Use DualAsyncTarget but set MaxRetryCount=0 so polling immediately fails.
        var spec = new DualAsyncSpec();
        var initialState = new DualAsyncState();

        var startAInput = new OperationInput("StartJobA", spec["StartJobA"]);
        startAInput.WithPolling(new PollingSetup
        {
            Operation = "PollJobA",
            WaitTimeInMs = 1,
            MaxRetryCount = 0
        });

        var startBInput = new OperationInput("StartJobB", spec["StartJobB"]);
        startBInput.WithPolling(new PollingSetup
        {
            Operation = "PollJobB",
            WaitTimeInMs = 1,
            MaxRetryCount = 0
        });

        var inputSet = new InputSet() { startAInput, startBInput };

        // Must be concurrent segment to test PollAfterConcurrentSegment
        var segments = new List<TestCaseSegment>
        {
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("StartJobA", inputSet["StartJobA"]),
                new OperationCall("StartJobB", inputSet["StartJobB"])
            })
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = "polling failure test",
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new DualAsyncTarget());

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        Assert.IsFalse(results[0].Success, "Test should fail when polling exhausts retries");
        Assert.IsTrue(results[0].LastFailureMessage.Contains("Gave up polling"),
            $"Expected 'Gave up polling' in message, got: {results[0].LastFailureMessage}");
    }

    [Test]
    public async Task ConcurrentTestCase_PreExistingStepFunctionsNotPolledAgain()
    {
        // Segment 1 (sequential): StartJobA — triggers step function A, polling completes it
        // Segment 2 (concurrent): StartJobB || PollJobA (as a no-op observation)
        // After segment 2, only JobB's step function should need polling (A already done)
        var spec = new DualAsyncSpec();
        var initialState = new DualAsyncState();

        var inputSet = new InputSet()
        {
            new OperationInput("StartJobA", spec["StartJobA"]),
            new OperationInput("StartJobB", spec["StartJobB"]),
            new OperationInput("PollJobA", spec["PollJobA"])
        };

        var startJobACall = new OperationCall("StartJobA", inputSet["StartJobA"]);

        var segments = new List<TestCaseSegment>
        {
            // Segment 1: sequential StartJobA (polling will complete it)
            new TestCaseSegment(startJobACall),
            // Segment 2: concurrent StartJobB + PollJobA observation
            new TestCaseSegment(new List<OperationCall>
            {
                new OperationCall("StartJobB", inputSet["StartJobB"]),
                new OperationCall("PollJobA", inputSet["PollJobA"])
            })
        };

        var testCase = new ConcurrentTestCase()
        {
            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
            Segments = segments
        };

        var context = spec.CreateTestingContext();
        context.Register(new DualAsyncTarget());

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions());

        Assert.IsTrue(results.All(r => r.Success),
            "Pre-existing step functions from segment 1 should not interfere with segment 2 polling. " +
            $"Failure: {results.FirstOrDefault(r => !r.Success)?.LastFailureMessage}");
    }

    #endregion

    /// <summary>
    /// Test that OnStepExecuted hook is invoked during polling operations.
    /// When an async operation triggers polling, each poll call should fire the hook
    /// so callers have visibility into poll request/response pairs.
    /// </summary>
    [Test]
    public async Task OnStepExecutedHook_CalledDuringPolling()
    {
        var spec = new AsyncOperationSpec();
        var initialState = new AsyncOperationState { Status = "none" };

        var inputSet = new InputSet()
        {
            new OperationInput("StartAsync", spec["StartAsync"]),
        };

        // Create a test case that just calls StartAsync (which triggers polling via GetStatus)
        var testCase = TestCaseGenerator.CreateManualSequentialTestCase(
            spec.CreateTestingContext(),
            inputSet,
            "StartAsync");

        var context = spec.CreateTestingContext();

        var stepExecutedCalls = new List<StepExecutedInfo>();

        var results = await spec.RunTests(
            context,
            initialState,
            new[] { testCase },
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new AsyncWorkTarget()),
                OnStepExecuted = (info) =>
                {
                    stepExecutedCalls.Add(info);
                }
            });

        Assert.IsTrue(results.All(r => r.Success), "Test should pass");

        // We expect at least 2 calls: one for StartAsync itself, and at least one for a poll (GetStatus)
        Assert.IsTrue(stepExecutedCalls.Count >= 2,
            $"Expected at least 2 OnStepExecuted calls (1 for StartAsync + at least 1 poll), " +
            $"but got {stepExecutedCalls.Count}");

        // First call should be StartAsync
        Assert.AreEqual("StartAsync", stepExecutedCalls[0].Operation.Name);

        // Subsequent calls should be GetStatus (polling)
        for (int i = 1; i < stepExecutedCalls.Count; i++)
        {
            Assert.AreEqual("GetStatus", stepExecutedCalls[i].Operation.Name,
                $"Call {i} should be a GetStatus poll operation");
        }
    }
}
