// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// This delegate represents the signature of algorithms used to generate
/// concurrent test cases.
/// </summary>
public delegate IList<ConcurrentTestCase> ConcurrentTestCaseAlgorithm(
    TestingContext context,
    StateGraphNode rootNode);

public class ConcurrentTestCaseAlgorithms
{
    /// <summary>
    /// Creates the algorithm to generate concurrent test cases.
    /// </summary>
    /// <param name="maxConcurrencyLevel">
    /// The maximum number of concurrent operations that should be invoked together
    /// in a test. This property can be set to -1 if no bound is required on the max
    /// number of concurrent calls in a test.
    /// </param>
    public static ConcurrentTestCaseAlgorithm CreateDefaultConcurrentTestCaseGenerator(
        int maxConcurrencyLevel = 3)
    {
        return (context, rootNode) =>
        {
            var testCases = new List<ConcurrentTestCase>();

            void Recurse(StateGraphNode node, HashSet<ulong> visitedHashes, List<OperationCall> operationCalls)
            {
                var stateHash = node.State.GetStateHash();

                if (visitedHashes.Contains(stateHash))
                {
                    return;
                }

                visitedHashes.Add(stateHash);

                var sequentialPrefix = operationCalls.ToList();

                foreach (var edge in node.Edges)
                {
                    var operationCall = (OperationCall)edge.Metadata;

                    var sequentialSegments = sequentialPrefix
                        .Select(call => new TestCaseSegment(call))
                        .ToList();

                    var clonedOperationCall = operationCall.Clone();
                    clonedOperationCall.Name = operationCall.Name + " (2)";

                    var concurrentOperationCalls = new List<OperationCall>()
                    {
                        operationCall,
                        clonedOperationCall
                    };

                    var segments = new List<TestCaseSegment>(sequentialSegments)
                    {
                        new TestCaseSegment(concurrentOperationCalls)
                    };

                    // Performing the same operation concurrently
                    testCases.Add(new ConcurrentTestCase()
                    {
                        Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
                        Segments = segments
                    });
                }

                var edgeList = node.Edges;

                if (maxConcurrencyLevel == -1 ||
                    edgeList.Count <= maxConcurrencyLevel)
                {
                    if (edgeList.Any(e => e.Target.State.GetStateHash() != stateHash))
                    {
                        var concurrentOperationList = edgeList.Select(e => (OperationCall)e.Metadata).ToList();

                        var sequentialSegments = sequentialPrefix
                            .Select(call => new TestCaseSegment(call))
                            .ToList();

                        var segments = new List<TestCaseSegment>(sequentialSegments)
                        {
                            new TestCaseSegment(concurrentOperationList)
                        };

                        // Performing all the operations from the current node concurrently
                        testCases.Add(new ConcurrentTestCase()
                        {
                            Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
                            Segments = segments
                        });
                    }
                }
                else
                {
                    var concurrentEdgeList = GetCombinations(
                        edgeList,
                        maxConcurrencyLevel);

                    foreach (var subset in concurrentEdgeList)
                    {
                        if (subset.Any(e => e.Target.State.GetStateHash() != stateHash))
                        {
                            var concurrentOperationList = subset.Select(e => (OperationCall)e.Metadata).ToList();

                            var sequentialSegments = sequentialPrefix
                                .Select(call => new TestCaseSegment(call))
                                .ToList();

                            var segments = new List<TestCaseSegment>(sequentialSegments)
                            {
                                new TestCaseSegment(concurrentOperationList)
                            };

                            // Performing subset of operations from the current node concurrently
                            testCases.Add(new ConcurrentTestCase()
                            {
                                Description = TestCaseGenerator.ConstructDescriptionForConcurrentTestCase(segments),
                                Segments = segments
                            });
                        }
                    }
                }

                foreach (var edge in node.Edges)
                {
                    operationCalls.Add((OperationCall)edge.Metadata);
                    Recurse(edge.Target, visitedHashes, operationCalls);
                    operationCalls.RemoveAt(operationCalls.Count - 1);
                }

                visitedHashes.Remove(stateHash);
            }

            Recurse(
                rootNode,
                visitedHashes: new HashSet<ulong>(),
                operationCalls: new List<OperationCall>());

            return testCases;
        };
    }

    private static List<List<T>> GetCombinations<T>(List<T> list, int length)
    {
        if (length == 1) return list.Select(t => new List<T> { t }).ToList();

        return list.SelectMany((value, index) => GetCombinations(list.Skip(index + 1).ToList(), length - 1)
            .Select(t =>
            {
                List<T> newList = new List<T>(t);
                newList.Insert(0, value);
                return newList;
            })).ToList();
    }
}
