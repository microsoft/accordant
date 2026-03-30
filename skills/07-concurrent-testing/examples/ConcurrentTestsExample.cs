// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Examples;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Example of concurrent test generation and execution.
/// Concurrent tests verify linearizability - that concurrent operations
/// produce results explainable by SOME sequential ordering.
/// </summary>
public class ConcurrentTestsExample
{
    [Test]
    public async Task GenerateAndRunConcurrentTests()
    {
        var spec = new StackSpec();
        var initialState = new StackState<int>();

        // Define inputs - same as sequential tests
        var inputs = new InputSet()
        {
            spec.Push.With(1, "Push 1"),
            spec.Push.With(2, "Push 2"),
            spec.Pop.With("Pop"),
            spec.Count.With("Count")
        };

        // Generate CONCURRENT test cases
        var testCases = spec.GenerateConcurrentTests(
            initialState,
            inputs,
            new TestGenerationOptions
            {
                // Limit state space
                StateConstraint = s => ((StackState<int>)s).Items.Count < 3,
                
                // How many operations to run concurrently
                // 2 = pairs, 3 = triples, etc.
                MaxConcurrencyLevel = 2,
                
                // Limit exploration depth
                MaxDepth = 3
            });

        // Each ConcurrentTestCase has:
        // - SequentialOperationCalls: prefix run sequentially to set up state
        // - ConcurrentOperationCalls: operations run in parallel

        var context = spec.CreateTestingContext();

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                // CRITICAL: Reset system before each test
                BeforeEach = _ => context.Register(new Stack<int>())
            });

        Assert.IsTrue(
            results.All(r => r.Success),
            "Some concurrent test cases failed - linearizability violation detected");

        TestContext.WriteLine($"Generated and ran {testCases.Count} concurrent test cases");
    }

    [Test]
    public async Task ManualConcurrentTest_UsingAllowsConcurrent()
    {
        var spec = new StackSpec();
        var context = spec.CreateTestingContext();
        context.Register(new Stack<int>());

        var stateProfile = new StateProfile(new StackState<int>());

        // First, set up initial state with some items
        spec.Push.Execute(context, 1);
        var (valid1, msg1, profile1) = spec.Allows(spec.Push, 1, Unit.Value, stateProfile);
        Assert.IsTrue(valid1, msg1);
        stateProfile = profile1;

        // Now fire concurrent operations
        // Note: In a real scenario, you'd use Task.Run for true concurrency
        var pushTask = Task.Run(() => spec.Push.Execute(context, 2));
        var countTask = Task.Run(() => spec.Count.Execute(context, Unit.Value));

        await Task.WhenAll(pushTask, countTask);

        // Validate concurrent results using AllowsConcurrent
        // This checks if results are linearizable (explainable by some ordering)
        var (isValid, message, nextProfile) = spec.AllowsConcurrent(
            stateProfile,
            new List<(IOperation, object, object)>
            {
                (spec.Push, 2, Unit.Value),      // Push 2 -> Unit
                (spec.Count, Unit.Value, countTask.Result) // Count -> actual result
            });

        Assert.IsTrue(isValid, $"Linearizability check failed: {message}");
    }
}
