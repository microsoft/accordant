namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;

internal static class FunctionalSafetyRefinement
{
    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAbstract> mapping)
        where TConcrete : IState
        where TAbstract : IState
    {
        var mappedStates = new Dictionary<string, TAbstract>();

        TAbstract Map(StateGraphNode concreteNode)
        {
            if (!(concreteNode.State is TConcrete concreteState))
            {
                throw new InvalidOperationException(
                    $"Concrete graph node state '{concreteNode.State?.GetType().FullName}' " +
                    $"is not a {typeof(TConcrete).FullName}.");
            }

            var fingerprint = concreteNode.GetNodeFingerprint();
            if (!mappedStates.TryGetValue(fingerprint, out var mapped))
            {
                mapped = mapping(concreteState);
                if (ReferenceEquals(mapped, null))
                {
                    throw new InvalidOperationException(
                        $"The refinement mapping returned null for concrete state " +
                        $"'{concreteNode.State}'.");
                }
                mappedStates[fingerprint] = mapped;
            }
            return mapped;
        }

        var mappedRoot = Map(concreteRoot);
        if (!StateSemantics.Equal(mappedRoot, abstractRoot.State))
        {
            return RefinementCheckingResult.Failure(
                RefinementFailureKind.InitialStateMismatch,
                new[]
                {
                    new RefinementTraceItem(
                        concreteRoot,
                        concreteStepFunction: null,
                        concreteEdgeMetadata: null,
                        mappedRoot,
                        Array.Empty<StateGraphNode>())
                });
        }

        var initial = new SearchNode(
            concreteRoot,
            new[] { abstractRoot },
            hasUnknownAbstractAlternative: false,
            parent: null,
            incomingEdge: null,
            mappedRoot);
        var queue = new Queue<SearchNode>();
        queue.Enqueue(initial);

        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            MakeSearchKey(initial.ConcreteNode, initial.AbstractCandidates)
        };
        SearchNode firstUncertain = null;

        void RecordUncertainty(SearchNode node)
        {
            if (firstUncertain == null || node.Depth < firstUncertain.Depth)
            {
                firstUncertain = node;
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var concreteEdges = current.ConcreteNode.Edges;
            if (current.ConcreteNode.IsDepthFrontier)
            {
                RecordUncertainty(current);
            }

            foreach (var concreteEdge in concreteEdges)
            {
                if (!(concreteEdge.Target.State is TConcrete))
                {
                    throw new InvalidOperationException(
                        $"Concrete graph node state " +
                        $"'{concreteEdge.Target.State?.GetType().FullName}' is not a " +
                        $"{typeof(TConcrete).FullName}.");
                }

                var mappedTarget = Map(concreteEdge.Target);
                var nextCandidates = new Dictionary<string, StateGraphNode>(
                    StringComparer.Ordinal);
                var hasUnknownAbstractAlternative =
                    current.HasUnknownAbstractAlternative;

                foreach (var abstractCandidate in current.AbstractCandidates)
                {
                    ValidateAbstractState<TAbstract>(abstractCandidate);

                    if (StateSemantics.Equal(mappedTarget, abstractCandidate.State))
                    {
                        nextCandidates[abstractCandidate.GetNodeFingerprint()] =
                            abstractCandidate;
                    }

                    var abstractEdges = abstractCandidate.Edges;
                    if (abstractCandidate.IsDepthFrontier)
                    {
                        hasUnknownAbstractAlternative = true;
                    }

                    foreach (var abstractEdge in abstractEdges)
                    {
                        ValidateAbstractState<TAbstract>(abstractEdge.Target);
                        if (StateSemantics.Equal(mappedTarget, abstractEdge.Target.State))
                        {
                            nextCandidates[abstractEdge.Target.GetNodeFingerprint()] =
                                abstractEdge.Target;
                        }
                    }
                }

                var orderedCandidates = nextCandidates.Values
                    .OrderBy(candidate => candidate.GetNodeFingerprint(), StringComparer.Ordinal)
                    .ToArray();
                var next = new SearchNode(
                    concreteEdge.Target,
                    orderedCandidates,
                    hasUnknownAbstractAlternative,
                    current,
                    concreteEdge,
                    mappedTarget);

                if (orderedCandidates.Length == 0)
                {
                    if (hasUnknownAbstractAlternative)
                    {
                        RecordUncertainty(next);
                        continue;
                    }

                    return RefinementCheckingResult.Failure(
                        RefinementFailureKind.TransitionMismatch,
                        BuildTrace(next));
                }

                var key = MakeSearchKey(
                    next.ConcreteNode,
                    next.AbstractCandidates,
                    next.HasUnknownAbstractAlternative);
                if (visited.Add(key))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return firstUncertain == null
            ? RefinementCheckingResult.Success()
            : RefinementCheckingResult.Inconclusive(BuildTrace(firstUncertain));
    }

    private static void ValidateAbstractState<TAbstract>(StateGraphNode node)
        where TAbstract : IState
    {
        if (!(node.State is TAbstract))
        {
            throw new InvalidOperationException(
                $"Abstract graph node state '{node.State?.GetType().FullName}' is not a " +
                $"{typeof(TAbstract).FullName}.");
        }
    }

    private static string MakeSearchKey(
        StateGraphNode concreteNode,
        IReadOnlyList<StateGraphNode> abstractCandidates,
        bool hasUnknownAbstractAlternative = false)
    {
        return concreteNode.GetNodeFingerprint() + "|" +
            (hasUnknownAbstractAlternative ? "unknown|" : "known|") +
            string.Join(
                ",",
                abstractCandidates
                    .Select(candidate => candidate.GetNodeFingerprint())
                    .OrderBy(fingerprint => fingerprint, StringComparer.Ordinal));
    }

    private static IReadOnlyList<RefinementTraceItem> BuildTrace(SearchNode end)
    {
        var reversed = new List<RefinementTraceItem>();
        for (var node = end; node != null; node = node.Parent)
        {
            reversed.Add(new RefinementTraceItem(
                node.ConcreteNode,
                node.IncomingEdge?.StepFunction,
                node.IncomingEdge?.Metadata,
                node.MappedAbstractState,
                node.AbstractCandidates));
        }
        reversed.Reverse();
        return reversed;
    }

    private sealed class SearchNode
    {
        public SearchNode(
            StateGraphNode concreteNode,
            IReadOnlyList<StateGraphNode> abstractCandidates,
            bool hasUnknownAbstractAlternative,
            SearchNode parent,
            StateGraphEdge incomingEdge,
            IState mappedAbstractState)
        {
            ConcreteNode = concreteNode;
            AbstractCandidates = abstractCandidates;
            HasUnknownAbstractAlternative = hasUnknownAbstractAlternative;
            Parent = parent;
            IncomingEdge = incomingEdge;
            MappedAbstractState = mappedAbstractState;
            Depth = parent == null ? 0 : parent.Depth + 1;
        }

        public StateGraphNode ConcreteNode { get; }
        public IReadOnlyList<StateGraphNode> AbstractCandidates { get; }
        public bool HasUnknownAbstractAlternative { get; }
        public SearchNode Parent { get; }
        public StateGraphEdge IncomingEdge { get; }
        public IState MappedAbstractState { get; }
        public int Depth { get; }
    }
}
