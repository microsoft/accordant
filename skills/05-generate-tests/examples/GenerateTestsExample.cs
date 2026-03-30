// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Examples;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Example of auto-generating sequential test cases.
/// Accordant explores the state space to generate comprehensive tests.
/// </summary>
public class GenerateTestsExample
{
    [Test]
    public async Task GenerateAndRunSequentialTests()
    {
        var spec = new StackSpec();

        // 1. Define the input set - concrete requests for each operation
        var inputs = new InputSet()
        {
            spec.Push.With(1, "Push 1"),      // Push with value 1
            spec.Push.With(2, "Push 2"),      // Push with value 2  
            spec.Pop.With("Pop"),             // Pop (Unit request)
            spec.Peek.With("Peek"),           // Peek
            spec.IsEmpty.With("IsEmpty"),     // IsEmpty
            spec.Count.With("Count")          // Count
        };

        // 2. Configure test generation options
        var testGenerationOptions = new TestGenerationOptions
        {
            // Limit state space exploration to prevent explosion
            StateConstraint = state =>
            {
                var stackState = (StackState<int>)state;

                // Only explore states with < 3 items
                // and no duplicate values
                return stackState.Items.Count < 3 &&
                       stackState.Items.Distinct().Count() == stackState.Items.Count;
            },
            
            // MaxDepth controls exploration depth (default: 5)
            MaxDepth = 5
        };

        // 3. Generate test cases
        var initialState = new StackState<int>();
        var testCases = spec.GenerateTests(
            initialState,
            inputs,
            testGenerationOptions);

        // 4. Create context and run tests
        var context = spec.CreateTestingContext();

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                // Reset the system before each test case
                BeforeEach = _ => context.Register(new Stack<int>())
            });

        // 5. Assert all tests passed
        Assert.IsTrue(
            results.All(r => r.Success),
            $"Some test cases failed: {string.Join(", ", results.Where(r => !r.Success).Select(r => r.LastFailureMessage))}");
        
        // Log test count
        TestContext.WriteLine($"Generated and ran {testCases.Count} test cases");
    }
}
