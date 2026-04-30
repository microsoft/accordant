// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
        using NUnit.Framework;

    /// <summary>
    /// Tests for the different test case generation algorithms:
    /// - StateCoverage (default)
    /// - CreateTransitionCoverage
    /// - CreateRandomWalk
    /// </summary>
    [TestFixture]
    public class TestCaseAlgorithmTests
    {
        #region Test Spec - Simple counter with Add and Reset

        /// <summary>
        /// Simple spec with Add (increments), Reset (sets to 0), and Get operations.
        /// This creates a well-defined state graph for testing algorithms.
        /// </summary>
        public class CounterSpec : Spec<CounterState>
        {
            public Operation<int, int, CounterState> AddOp { get; }
            public Operation<Unit, int, CounterState> ResetOp { get; }
            public Operation<Unit, int, CounterState> GetOp { get; }

            public CounterSpec()
            {
                // Add operation: increments the counter
                Operation<int, int>("Add", (amount, state) =>
                {
                    var newValue = state.Value + amount;
                    return Expect.That<int>(r => r == newValue)
                                 .ThenState<CounterState>(s => s.Value = newValue);
                });
                AddOp = GetOperation<int, int>("Add");

                // Reset operation: sets counter to 0
                Operation<Unit, int>("Reset", (_, state) =>
                {
                    return Expect.That<int>(r => r == 0)
                                 .ThenState<CounterState>(s => s.Value = 0);
                });
                ResetOp = GetOperation<Unit, int>("Reset");

                // Get operation: returns current value (read-only)
                Operation<Unit, int>("Get", (_, state) =>
                {
                    return Expect.That<int>(r => r == state.Value)
                                 .SameState();
                });
                GetOp = GetOperation<Unit, int>("Get");
            }
        }

        #endregion

        #region StateCoverage Tests

        [Test]
        public void StateCoverage_VisitsAllReachableStates()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
                spec.GetOp.With("Get")
            };

            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.StateCoverage,
                MaxDepth = 3
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            // Should have generated test cases
            Assert.That(testCases.Count, Is.GreaterThan(0));

            // Collect all states reached by test cases (by simulating)
            var reachedStates = new HashSet<int>();
            foreach (var testCase in testCases)
            {
                var currentValue = 0;
                foreach (var call in testCase.OperationCalls)
                {
                    if (call.Name.Contains("Add 1"))
                        currentValue += 1;
                    else if (call.Name.Contains("Add 2"))
                        currentValue += 2;
                    // Get doesn't change state
                    
                    reachedStates.Add(currentValue);
                }
            }

            // Should reach states 1, 2, 3 at minimum
            Assert.That(reachedStates, Does.Contain(1));
            Assert.That(reachedStates, Does.Contain(2));
            Assert.That(reachedStates, Does.Contain(3));
        }

        [Test]
        public void StateCoverage_RespectsMaxDepth()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
            };

            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.StateCoverage,
                MaxDepth = 3
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            // No test case should have more operations than MaxDepth
            foreach (var testCase in testCases)
            {
                Assert.That(testCase.OperationCalls.Count, Is.LessThanOrEqualTo(3),
                    $"Test case has {testCase.OperationCalls.Count} operations, expected <= 3");
            }
        }

        [Test]
        public void StateCoverage_RespectsStateConstraint()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
            };

            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.StateCoverage,
                MaxDepth = 3
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            // Simulate each test case and verify state never exceeds constraint
            foreach (var testCase in testCases)
            {
                var currentValue = 0;
                foreach (var call in testCase.OperationCalls)
                {
                    if (call.Name.Contains("Add 1"))
                        currentValue += 1;
                    
                    Assert.That(currentValue, Is.LessThanOrEqualTo(2),
                        "State exceeded constraint");
                }
            }
        }

        #endregion

        #region CreateTransitionCoverage Tests

        [Test]
        public void TransitionCoverage_GeneratesMoreTestCasesThanStateCoverage()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
                spec.GetOp.With("Get")
            };

            var stateOptions = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.StateCoverage,
                MaxDepth = 3
            };

            var transitionOptions = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateTransitionCoverage(maxSequenceLength: 3),
                MaxDepth = 3
            };

            var stateCoverageTests = spec.GenerateTests(initialState, inputs, stateOptions);
            var transitionCoverageTests = spec.GenerateTests(initialState, inputs, transitionOptions);

            // Transition coverage typically generates more test cases
            Assert.That(transitionCoverageTests.Count, Is.GreaterThanOrEqualTo(stateCoverageTests.Count),
                $"Transition coverage ({transitionCoverageTests.Count}) should be >= state coverage ({stateCoverageTests.Count})");
        }

        [Test]
        public void TransitionCoverage_RespectsMaxSequenceLength()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
            };

            int maxSequenceLength = 3;
            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateTransitionCoverage(maxSequenceLength),
                MaxDepth = 10  // Higher than maxSequenceLength to verify algorithm respects its own limit
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            foreach (var testCase in testCases)
            {
                Assert.That(testCase.OperationCalls.Count, Is.LessThanOrEqualTo(maxSequenceLength),
                    $"Test case has {testCase.OperationCalls.Count} operations, expected <= {maxSequenceLength}");
            }
        }

        [Test]
        public void TransitionCoverage_CoversDifferentOperationSequences()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
            };

            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateTransitionCoverage(maxSequenceLength: 2),
                MaxDepth = 3
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            // Extract unique operation sequences
            var sequences = testCases.Select(tc =>
                string.Join(" -> ", tc.OperationCalls.Select(c => c.Name.Contains("Add 1") ? "Add1" : "Add2")))
                .Distinct()
                .ToList();

            // Should have multiple distinct sequences
            Assert.That(sequences.Count, Is.GreaterThan(1),
                $"Expected multiple sequences, got: {string.Join("; ", sequences)}");
        }

        #endregion

        #region CreateRandomWalk Tests

        [Test]
        public void RandomWalk_GeneratesRequestedNumberOfUniqueWalks()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
                spec.GetOp.With("Get")
            };

            int numberOfWalks = 5;
            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: numberOfWalks,
                    maxWalkLength: 3,
                    seed: 12345),
                MaxDepth = 3
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            // Should generate test cases (may be fewer than numberOfWalks due to deduplication)
            Assert.That(testCases.Count, Is.GreaterThan(0));
            Assert.That(testCases.Count, Is.LessThanOrEqualTo(numberOfWalks),
                "Should not exceed requested number of walks");
        }

        [Test]
        public void RandomWalk_RespectsMaxWalkLength()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
            };

            int maxWalkLength = 3;
            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: 5,
                    maxWalkLength: maxWalkLength,
                    seed: 42),
                MaxDepth = 3
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            foreach (var testCase in testCases)
            {
                Assert.That(testCase.OperationCalls.Count, Is.LessThanOrEqualTo(maxWalkLength),
                    $"Test case has {testCase.OperationCalls.Count} operations, expected <= {maxWalkLength}");
            }
        }

        [Test]
        public void RandomWalk_IsReproducibleWithSameSeed()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
            };

            int seed = 99999;
            var options1 = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: 5,
                    maxWalkLength: 3,
                    seed: seed),
                MaxDepth = 3
            };

            var options2 = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: 5,
                    maxWalkLength: 3,
                    seed: seed),
                MaxDepth = 3
            };

            var testCases1 = spec.GenerateTests(initialState, inputs, options1);
            var testCases2 = spec.GenerateTests(initialState, inputs, options2);

            // Same seed should produce same results
            Assert.That(testCases1.Count, Is.EqualTo(testCases2.Count),
                "Same seed should produce same number of test cases");

            for (int i = 0; i < testCases1.Count; i++)
            {
                var ops1 = string.Join(",", testCases1[i].OperationCalls.Select(c => c.Name));
                var ops2 = string.Join(",", testCases2[i].OperationCalls.Select(c => c.Name));
                Assert.That(ops1, Is.EqualTo(ops2),
                    $"Test case {i} differs with same seed");
            }
        }

        [Test]
        public void RandomWalk_ProducesDifferentResultsWithDifferentSeeds()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
                spec.AddOp.With(3, "Add 3"),
            };

            var options1 = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: 5,
                    maxWalkLength: 3,
                    seed: 11111),
                MaxDepth = 3
            };

            var options2 = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: 5,
                    maxWalkLength: 3,
                    seed: 22222),
                MaxDepth = 3
            };

            var testCases1 = spec.GenerateTests(initialState, inputs, options1);
            var testCases2 = spec.GenerateTests(initialState, inputs, options2);

            // Different seeds should (very likely) produce different orderings
            // We check if at least one test case differs
            bool anyDifference = false;
            int minCount = System.Math.Min(testCases1.Count, testCases2.Count);
            for (int i = 0; i < minCount; i++)
            {
                var ops1 = string.Join(",", testCases1[i].OperationCalls.Select(c => c.Name));
                var ops2 = string.Join(",", testCases2[i].OperationCalls.Select(c => c.Name));
                if (ops1 != ops2)
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.That(anyDifference || testCases1.Count != testCases2.Count, Is.True,
                "Different seeds should produce different test cases (with high probability)");
        }

        [Test]
        public void RandomWalk_DeduplicatesIdenticalPaths()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            // Only one input -> all walks will be identical
            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
            };

            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: 10,  // Request many walks
                    maxWalkLength: 3,
                    seed: 42),
                MaxDepth = 3
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            // With only one input, all walks are the same -> should deduplicate to 1
            Assert.That(testCases.Count, Is.EqualTo(1),
                "Should deduplicate identical walks to a single test case");
        }

        [Test]
        public void RandomWalk_CanRevisitStatesWithinSingleWalk()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            // Reset always brings back to state 0
            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.ResetOp.With("Reset"),
            };

            var options = new TestGenerationOptions
            {
                SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(
                    numberOfWalks: 10,
                    maxWalkLength: 3,
                    seed: 12345),
                MaxDepth = 3,
                StateConstraint = s => ((CounterState)s).Value <= 2
            };

            var testCases = spec.GenerateTests(initialState, inputs, options);

            // Look for a test case that has Reset followed by more operations
            // This means it revisited state 0 and continued
            bool foundRevisit = testCases.Any(tc =>
            {
                var calls = tc.OperationCalls;
                for (int i = 0; i < calls.Count - 1; i++)
                {
                    if (calls[i].Name.Contains("Reset"))
                    {
                        // Found Reset, and there are more operations after it
                        return true;
                    }
                }
                return false;
            });

            Assert.That(foundRevisit, Is.True,
                "Random walk should be able to revisit states (Reset -> continue)");
        }

        #endregion

        #region Algorithm Comparison Tests

        [Test]
        public void AllAlgorithms_GenerateValidTestCases()
        {
            var spec = new CounterSpec();
            var initialState = new CounterState { Value = 0 };

            var inputs = new InputSet
            {
                spec.AddOp.With(1, "Add 1"),
                spec.AddOp.With(2, "Add 2"),
                spec.GetOp.With("Get")
            };

            var algorithms = new (string Name, TestGenerationOptions Options)[]
            {
                ("StateCoverage", new TestGenerationOptions
                {
                    SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.StateCoverage,
                    MaxDepth = 3
                }),
                ("TransitionCoverage", new TestGenerationOptions
                {
                    SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateTransitionCoverage(3),
                    MaxDepth = 3
                }),
                ("RandomWalk", new TestGenerationOptions
                {
                    SequentialTestCaseAlgorithm = SequentialTestCaseAlgorithms.CreateRandomWalk(5, 3, 42),
                    MaxDepth = 3
                })
            };

            foreach (var (name, options) in algorithms)
            {
                var testCases = spec.GenerateTests(initialState, inputs, options);

                Assert.That(testCases, Is.Not.Null, $"{name}: Should return non-null");
                Assert.That(testCases.Count, Is.GreaterThan(0), $"{name}: Should generate test cases");

                foreach (var testCase in testCases)
                {
                    Assert.That(testCase.OperationCalls, Is.Not.Null,
                        $"{name}: Test case should have operation calls");
                    Assert.That(testCase.Description, Is.Not.Null.And.Not.Empty,
                        $"{name}: Test case should have description");
                }
            }
        }

        #endregion
    }
}
