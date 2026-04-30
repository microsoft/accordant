// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Example of executing auto-generated tests with lifecycle hooks.
/// Demonstrates BeforeEach, AfterEach, OnStepExecuted hooks.
/// </summary>
public class ExecuteTestsExample
{
    [Test]
    public async Task ExecuteTestsWithLifecycleHooks()
    {
        var spec = new StackSpec();
        var initialState = new StackState<int>();

        // Generate test cases
        var inputs = new InputSet()
        {
            spec.Push.With(1, "Push 1"),
            spec.Push.With(2, "Push 2"),
            spec.Pop.With("Pop"),
            spec.Count.With("Count")
        };

        var testCases = spec.GenerateTests(
            initialState,
            inputs,
            new TestGenerationOptions
            {
                StateConstraint = s => ((StackState<int>)s).Items.Count < 3
            });

        var context = spec.CreateTestingContext();
        var passedCount = 0;
        var failedCount = 0;

        // Configure execution options with hooks
        var options = new TestExecutionOptions
        {
            // Called once before all tests start
            BeforeAll = info =>
            {
                TestContext.WriteLine($"Starting {info.TotalTests} test cases...");
            },

            // Called before each individual test case
            BeforeEach = info =>
            {
                // CRITICAL: Reset the system to initial state
                // Each test must start from a clean slate
                info.Context.Register(new Stack<int>());
                
                TestContext.WriteLine($"\n[Test {info.TestIndex + 1}] Starting: {info.TestCase.Name}");
            },

            // Called after each operation within a test
            OnStepExecuted = info =>
            {
                if (info.IsSingleOperation)
                {
                    TestContext.WriteLine($"  Step: {info.Operation.Name}({info.Request}) -> {info.Response}");
                }
            },

            // Called after each test case completes
            AfterEach = info =>
            {
                if (info.Success)
                {
                    passedCount++;
                    TestContext.WriteLine($"  ✓ PASSED");
                }
                else
                {
                    failedCount++;
                    TestContext.WriteLine($"  ✗ FAILED: {info.FailureMessage}");
                }
            },

            // Called once after all tests complete
            AfterAll = info =>
            {
                TestContext.WriteLine($"\nResults: {info.Passed}/{info.TotalTests} passed, " +
                                      $"{info.Failed} failed, {info.Skipped} skipped");
            },

            // Stop on first failure for easier debugging (default: true)
            StopOnFirstFailure = false
        };

        // Run the tests
        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            options);

        // Assert
        Assert.AreEqual(testCases.Count, passedCount, "All tests should pass");
        Assert.AreEqual(0, failedCount, "No tests should fail");
    }
}
