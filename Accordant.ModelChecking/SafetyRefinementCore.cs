namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;

internal static class SafetyRefinementCore
{
    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<StateGraphNode, StateGraphNode, bool> matchesAbstract,
        Func<StateGraphNode, IState> abstractView = null,
        TransitionMapping transitionMapping = null)
        where TConcrete : IState
        where TAbstract : IState
        => Check<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            new[] { RefinementProofState.Empty },
            advance: (state, _, _) => new[] { state },
            matchesAbstract: (concrete, _, abstraction) =>
                matchesAbstract(concrete, abstraction),
            abstractView: abstractView == null
                ? (Func<StateGraphNode, RefinementProofState, IState>)null
                : (concrete, _) => abstractView(concrete),
            transitionMapping: transitionMapping);

    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        IReadOnlyList<RefinementProofState> initialProofStates,
        Func<
            RefinementProofState,
            StateGraphNode,
            StateGraphEdge,
            IReadOnlyList<RefinementProofState>> advance,
        Func<StateGraphNode, RefinementProofState, StateGraphNode, bool> matchesAbstract,
        Func<StateGraphNode, RefinementProofState, IState> abstractView,
        TransitionMapping transitionMapping)
        where TConcrete : IState
        where TAbstract : IState
    {
        var matchCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        bool MatchesAbstract(
            StateGraphNode concreteNode,
            RefinementProofState proofState,
            StateGraphNode abstractNode)
        {
            ValidateConcreteState<TConcrete>(concreteNode);
            ValidateAbstractState<TAbstract>(abstractNode);
            var key = concreteNode.GetNodeFingerprint() + "|" +
                proofState.Identity + "|" +
                abstractNode.GetNodeFingerprint();
            if (!matchCache.TryGetValue(key, out var result))
            {
                result = matchesAbstract(
                    concreteNode,
                    proofState,
                    abstractNode);
                matchCache[key] = result;
            }
            return result;
        }

        var queue = new Queue<SearchNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var initialProofState in initialProofStates)
        {
            if (!MatchesAbstract(concreteRoot, initialProofState, abstractRoot))
            {
                return RefinementCheckingResult.Failure(
                    RefinementFailureKind.InitialStateMismatch,
                    new[]
                    {
                        new RefinementTraceItem(
                            concreteRoot,
                            concreteStepFunction: null,
                            concreteEdgeMetadata: null,
                            abstractView?.Invoke(
                                concreteRoot,
                                initialProofState),
                            Array.Empty<StateGraphNode>(),
                            auxiliaryState: initialProofState.AuxiliaryState,
                            witnesses: initialProofState.Witnesses)
                    });
            }

            var initial = new SearchNode(
                concreteRoot,
                initialProofState,
                new[] { abstractRoot },
                hasUnknownAbstractAlternative: false,
                parent: null,
                incomingEdge: null,
                abstractView?.Invoke(concreteRoot, initialProofState));
            if (visited.Add(MakeSearchKey(
                initial.ConcreteNode,
                initial.ProofState,
                initial.AbstractCandidates)))
            {
                queue.Enqueue(initial);
            }
        }

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
                ValidateConcreteState<TConcrete>(concreteEdge.Target);
                var declared = transitionMapping?.Declared(
                    current.ConcreteNode,
                    current.ProofState,
                    concreteEdge);
                var nextProofStates = advance(
                    current.ProofState,
                    current.ConcreteNode,
                    concreteEdge);

                foreach (var nextProofState in nextProofStates)
                {
                    var nextCandidates = new Dictionary<string, StateGraphNode>(
                        StringComparer.Ordinal);
                    var hasUnknownAbstractAlternative =
                        current.HasUnknownAbstractAlternative;

                    foreach (var abstractCandidate in current.AbstractCandidates)
                    {
                        ValidateAbstractState<TAbstract>(abstractCandidate);

                        if (MatchesAbstract(
                            concreteEdge.Target,
                            nextProofState,
                            abstractCandidate) &&
                            TransitionMapping.Admits(
                                declared,
                                abstractCandidate,
                                abstractEdge: null))
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
                            if (MatchesAbstract(
                                concreteEdge.Target,
                                nextProofState,
                                abstractEdge.Target) &&
                                TransitionMapping.Admits(
                                    declared,
                                    abstractCandidate,
                                    abstractEdge))
                            {
                                nextCandidates[abstractEdge.Target.GetNodeFingerprint()] =
                                    abstractEdge.Target;
                            }
                        }
                    }

                    transitionMapping?.Validate(current.ProofState);
                    var orderedCandidates = nextCandidates.Values
                        .OrderBy(
                            candidate => candidate.GetNodeFingerprint(),
                            StringComparer.Ordinal)
                        .ToArray();
                    var next = new SearchNode(
                        concreteEdge.Target,
                        nextProofState,
                        orderedCandidates,
                        hasUnknownAbstractAlternative,
                        current,
                        concreteEdge,
                        abstractView?.Invoke(
                            concreteEdge.Target,
                            nextProofState),
                        declared);

                    if (orderedCandidates.Length == 0)
                    {
                        if (hasUnknownAbstractAlternative)
                        {
                            RecordUncertainty(next);
                            continue;
                        }

                        if (declared != null)
                        {
                            next.StateConsistentResponses = StateConsistentResponses(
                                current.AbstractCandidates,
                                concreteEdge.Target,
                                nextProofState,
                                MatchesAbstract);
                        }

                        return RefinementCheckingResult.Failure(
                            RefinementFailureKind.TransitionMismatch,
                            BuildTrace(next));
                    }

                    var key = MakeSearchKey(
                        next.ConcreteNode,
                        next.ProofState,
                        next.AbstractCandidates,
                        next.HasUnknownAbstractAlternative);
                    if (visited.Add(key))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return firstUncertain == null
            ? RefinementCheckingResult.Success()
            : RefinementCheckingResult.Inconclusive(
                BuildTrace(firstUncertain));
    }

    private static TConcrete ValidateConcreteState<TConcrete>(StateGraphNode node)
        where TConcrete : IState
    {
        if (node.State is TConcrete state)
        {
            return state;
        }

        throw new InvalidOperationException(
            $"Concrete graph node state '{node.State?.GetType().FullName}' is not a " +
            $"{typeof(TConcrete).FullName}.");
    }

    private static TAbstract ValidateAbstractState<TAbstract>(StateGraphNode node)
        where TAbstract : IState
    {
        if (node.State is TAbstract state)
        {
            return state;
        }

        throw new InvalidOperationException(
            $"Abstract graph node state '{node.State?.GetType().FullName}' is not a " +
            $"{typeof(TAbstract).FullName}.");
    }

    private static IReadOnlyList<AbstractTransition> StateConsistentResponses(
        IReadOnlyList<StateGraphNode> abstractCandidates,
        StateGraphNode concreteTarget,
        RefinementProofState proofState,
        Func<StateGraphNode, RefinementProofState, StateGraphNode, bool> matchesAbstract)
    {
        var responses = new List<AbstractTransition>();
        foreach (var candidate in abstractCandidates)
        {
            if (matchesAbstract(concreteTarget, proofState, candidate))
            {
                responses.Add(TransitionMapping.View(candidate, abstractEdge: null));
            }

            foreach (var abstractEdge in candidate.Edges)
            {
                if (matchesAbstract(concreteTarget, proofState, abstractEdge.Target))
                {
                    responses.Add(TransitionMapping.View(candidate, abstractEdge));
                }
            }
        }
        return responses;
    }

    private static string MakeSearchKey(
        StateGraphNode concreteNode,
        RefinementProofState proofState,
        IReadOnlyList<StateGraphNode> abstractCandidates,
        bool hasUnknownAbstractAlternative = false)
    {
        return concreteNode.GetNodeFingerprint() + "|" +
            proofState.Identity + "|" +
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
                node.AbstractCandidates,
                auxiliaryState: node.ProofState.AuxiliaryState,
                witnesses: node.ProofState.Witnesses,
                declaredAbstractResponse: node.DeclaredResponse,
                stateConsistentAbstractTransitions: node.StateConsistentResponses));
        }
        reversed.Reverse();
        return reversed;
    }

    private sealed class SearchNode
    {
        public SearchNode(
            StateGraphNode concreteNode,
            RefinementProofState proofState,
            IReadOnlyList<StateGraphNode> abstractCandidates,
            bool hasUnknownAbstractAlternative,
            SearchNode parent,
            StateGraphEdge incomingEdge,
            IState mappedAbstractState,
            AbstractResponse declaredResponse = null)
        {
            ConcreteNode = concreteNode;
            ProofState = proofState;
            AbstractCandidates = abstractCandidates;
            HasUnknownAbstractAlternative = hasUnknownAbstractAlternative;
            Parent = parent;
            IncomingEdge = incomingEdge;
            MappedAbstractState = mappedAbstractState;
            DeclaredResponse = declaredResponse;
            Depth = parent == null ? 0 : parent.Depth + 1;
        }

        public StateGraphNode ConcreteNode { get; }
        public RefinementProofState ProofState { get; }
        public IReadOnlyList<StateGraphNode> AbstractCandidates { get; }
        public bool HasUnknownAbstractAlternative { get; }
        public SearchNode Parent { get; }
        public StateGraphEdge IncomingEdge { get; }
        public IState MappedAbstractState { get; }
        public AbstractResponse DeclaredResponse { get; }
        public IReadOnlyList<AbstractTransition> StateConsistentResponses { get; set; }
        public int Depth { get; }
    }
}
