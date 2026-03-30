// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// This delegate represents the signature of algorithms used to generate
    /// sequential test cases.
    /// </summary>
    public delegate IList<SequentialTestCase> SequentialTestCaseAlgorithm(
        TestingContext context,
        StateGraphNode rootNode);

    public class SequentialTestCaseAlgorithms
    {
        /// <summary>
        /// This algorithm ensures that each reachable state in the state space graph is
        /// visited at least once. It does not generate all possible operation call
        /// sequences - it only ensures that the set of generated test cases drive the system
        /// to each unique reachable state.
        /// </summary>
        public static IList<SequentialTestCase> StateCoverage(
            TestingContext context,
            StateGraphNode rootNode)
        {
            var testCases = new List<SequentialTestCase>();

            void Recurse(StateGraphNode node, List<string> path, List<OperationCall> operationCalls)
            {
                var stateHash = node.State.GetStateHash();

                if (node.Edges.Count == 0 || path.Contains(stateHash))
                {
                    testCases.Add(new SequentialTestCase()
                    {
                        Description = TestCaseGenerator.ConstructDescriptionForSequentialTestCase(operationCalls),
                        OperationCalls = operationCalls.ToList()
                    });

                    return;
                }

                path.Add(stateHash);

                foreach (var edge in node.Edges)
                {
                    operationCalls.Add((OperationCall)edge.Metadata);
                    Recurse(edge.Target, path, operationCalls);
                    operationCalls.RemoveAt(operationCalls.Count - 1);
                }

                path.RemoveAt(path.Count - 1);
            }

            Recurse(
                rootNode,
                path: new List<string>(),
                operationCalls: new List<OperationCall>());

            return testCases;
        }

        /// <summary>
        /// This algorithm generates all possible edge traversals starting from the given node.
        /// This maximizes coverage of all possible transitions but can result
        /// in a very large number of test cases, therefore the default value of maxSequenceLength is set
        /// to 3. It can be passed as -1 to generate all possible edge sequences (the number of all
        /// possible edge sequences can be exceedingly large however).
        /// </summary>
        public static SequentialTestCaseAlgorithm CreateTransitionCoverage(
            int maxSequenceLength = 3)
        {
            return (context, rootNode) =>
            {
                var testCases = new List<SequentialTestCase>();

                void Recurse(
                    StateGraphNode node,
                    HashSet<string> visitedEdges,
                    List<OperationCall> operationCalls)
                {
                    if (visitedEdges.Count == maxSequenceLength)
                    {
                        testCases.Add(new SequentialTestCase()
                        {
                            Description = TestCaseGenerator.ConstructDescriptionForSequentialTestCase(operationCalls),
                            OperationCalls = operationCalls.ToList()
                        });

                        return;
                    }

                    var hasUnvisitedEdges = false;
                    foreach (var edge in node.Edges)
                    {
                        var operationCall = (OperationCall)edge.Metadata;

                        if (!visitedEdges.Contains(operationCall.Name))
                        {
                            hasUnvisitedEdges = true;
                            visitedEdges.Add(operationCall.Name);
                            operationCalls.Add(operationCall);
                            Recurse(
                                edge.Target,
                                visitedEdges,
                                operationCalls);
                            operationCalls.RemoveAt(operationCalls.Count - 1);
                            visitedEdges.Remove(operationCall.Name);
                        }
                    }

                    if (!hasUnvisitedEdges)
                    {
                        testCases.Add(new SequentialTestCase()
                        {
                            Description = TestCaseGenerator.ConstructDescriptionForSequentialTestCase(operationCalls),
                            OperationCalls = operationCalls.ToList()
                        });
                    }
                }

                Recurse(
                    rootNode,
                    visitedEdges: new HashSet<string>(),
                    operationCalls: new List<OperationCall>());

                return testCases;
            };
        }

        /// <summary>
        /// This algorithm generates test cases by performing random walks through the state graph.
        /// Each walk starts from the root and randomly selects edges until reaching a terminal state
        /// or the maximum walk length. Within a walk, each operation call can only appear once
        /// (edges leading to already-used operation calls are excluded). Duplicate test cases
        /// across walks are eliminated. This is useful for exploring large state spaces
        /// where exhaustive coverage is impractical.
        /// </summary>
        /// <param name="numberOfWalks">The number of random walks to perform.</param>
        /// <param name="maxWalkLength">Maximum steps per walk. -1 for unlimited (walks until no edges).</param>
        /// <param name="seed">Optional random seed for reproducibility. Null uses a random seed.</param>
        public static SequentialTestCaseAlgorithm CreateRandomWalk(
            int numberOfWalks = 100,
            int maxWalkLength = 10,
            int? seed = null)
        {
            return (context, rootNode) =>
            {
                var random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
                var testCases = new List<SequentialTestCase>();
                var seenPaths = new HashSet<string>();

                for (int walk = 0; walk < numberOfWalks; walk++)
                {
                    var operationCalls = new List<OperationCall>();
                    var usedOperationCallNames = new HashSet<string>();
                    var currentNode = rootNode;

                    // Walk randomly, but each operation call can only appear once per walk
                    while (currentNode.Edges.Count > 0 &&
                           (maxWalkLength == -1 || operationCalls.Count < maxWalkLength))
                    {
                        // Filter to edges with operation calls we haven't used yet
                        var availableEdges = currentNode.Edges
                            .Where(e => !usedOperationCallNames.Contains(((OperationCall)e.Metadata).Name))
                            .ToList();

                        if (availableEdges.Count == 0)
                        {
                            // No more edges with unused operation calls - end this walk
                            break;
                        }

                        // Randomly select from available edges
                        var edgeIndex = random.Next(availableEdges.Count);
                        var edge = availableEdges[edgeIndex];
                        var operationCall = (OperationCall)edge.Metadata;

                        operationCalls.Add(operationCall);
                        usedOperationCallNames.Add(operationCall.Name);
                        currentNode = edge.Target;
                    }

                    if (operationCalls.Count > 0)
                    {
                        // Deduplicate identical paths across walks
                        var pathKey = string.Join("|", operationCalls.Select(oc => oc.Name));
                        if (!seenPaths.Contains(pathKey))
                        {
                            seenPaths.Add(pathKey);
                            testCases.Add(new SequentialTestCase()
                            {
                                Description = TestCaseGenerator.ConstructDescriptionForSequentialTestCase(operationCalls),
                                OperationCalls = operationCalls.ToList()
                            });
                        }
                    }
                }

                return testCases;
            };
        }
    }
}
