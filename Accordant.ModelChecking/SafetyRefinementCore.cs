namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;

internal static class SafetyRefinementCore
{
    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<StateGraphNode, StateGraphNode, bool> correspondence,
        Func<StateGraphNode, IState> abstractView = null)
        where TConcrete : IState
        where TAbstract : IState
        => Check<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            initialAuxiliaryState: null,
            advanceAuxiliaryState: (_, _, _) => null,
            auxiliaryIdentity: _ => string.Empty,
            correspondence: (concrete, _, abstraction) =>
                correspondence(concrete, abstraction),
            abstractView: abstractView == null
                ? null
                : (concrete, _) => abstractView(concrete),
            includeAuxiliaryInTrace: false);

    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        object initialAuxiliaryState,
        Func<object, StateGraphNode, StateGraphEdge, object> advanceAuxiliaryState,
        Func<object, string> auxiliaryIdentity,
        Func<StateGraphNode, object, StateGraphNode, bool> correspondence,
        Func<StateGraphNode, object, IState> abstractView,
        bool includeAuxiliaryInTrace)
        where TConcrete : IState
        where TAbstract : IState
    {
        var correspondenceCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        bool Corresponds(
            StateGraphNode concreteNode,
            object auxiliaryState,
            StateGraphNode abstractNode)
        {
            ValidateConcreteState<TConcrete>(concreteNode);
            ValidateAbstractState<TAbstract>(abstractNode);
            var key = concreteNode.GetNodeFingerprint() + "|" +
                auxiliaryIdentity(auxiliaryState) + "|" +
                abstractNode.GetNodeFingerprint();
            if (!correspondenceCache.TryGetValue(key, out var result))
            {
                result = correspondence(
                    concreteNode,
                    auxiliaryState,
                    abstractNode);
                correspondenceCache[key] = result;
            }
            return result;
        }

        if (!Corresponds(
            concreteRoot,
            initialAuxiliaryState,
            abstractRoot))
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
                            initialAuxiliaryState),
                        Array.Empty<StateGraphNode>(),
                        auxiliaryState: includeAuxiliaryInTrace
                            ? (State)initialAuxiliaryState
                            : null)
                });
        }

        var initial = new SearchNode(
            concreteRoot,
            initialAuxiliaryState,
            new[] { abstractRoot },
            hasUnknownAbstractAlternative: false,
            parent: null,
            incomingEdge: null,
            abstractView?.Invoke(concreteRoot, initialAuxiliaryState));
        var queue = new Queue<SearchNode>();
        queue.Enqueue(initial);

        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            MakeSearchKey(
                initial.ConcreteNode,
                initial.AuxiliaryState,
                auxiliaryIdentity,
                initial.AbstractCandidates)
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
                ValidateConcreteState<TConcrete>(concreteEdge.Target);
                var nextAuxiliaryState = advanceAuxiliaryState(
                    current.AuxiliaryState,
                    current.ConcreteNode,
                    concreteEdge);
                var nextCandidates = new Dictionary<string, StateGraphNode>(
                    StringComparer.Ordinal);
                var hasUnknownAbstractAlternative =
                    current.HasUnknownAbstractAlternative;

                foreach (var abstractCandidate in current.AbstractCandidates)
                {
                    ValidateAbstractState<TAbstract>(abstractCandidate);

                    if (Corresponds(
                        concreteEdge.Target,
                        nextAuxiliaryState,
                        abstractCandidate))
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
                        if (Corresponds(
                            concreteEdge.Target,
                            nextAuxiliaryState,
                            abstractEdge.Target))
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
                    nextAuxiliaryState,
                    orderedCandidates,
                    hasUnknownAbstractAlternative,
                    current,
                    concreteEdge,
                    abstractView?.Invoke(
                        concreteEdge.Target,
                        nextAuxiliaryState));

                if (orderedCandidates.Length == 0)
                {
                    if (hasUnknownAbstractAlternative)
                    {
                        RecordUncertainty(next);
                        continue;
                    }

                    return RefinementCheckingResult.Failure(
                        RefinementFailureKind.TransitionMismatch,
                        BuildTrace(next, includeAuxiliaryInTrace));
                }

                var key = MakeSearchKey(
                    next.ConcreteNode,
                    next.AuxiliaryState,
                    auxiliaryIdentity,
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
            : RefinementCheckingResult.Inconclusive(
                BuildTrace(firstUncertain, includeAuxiliaryInTrace));
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

    private static string MakeSearchKey(
        StateGraphNode concreteNode,
        object auxiliaryState,
        Func<object, string> auxiliaryIdentity,
        IReadOnlyList<StateGraphNode> abstractCandidates,
        bool hasUnknownAbstractAlternative = false)
    {
        return concreteNode.GetNodeFingerprint() + "|" +
            auxiliaryIdentity(auxiliaryState) + "|" +
            (hasUnknownAbstractAlternative ? "unknown|" : "known|") +
            string.Join(
                ",",
                abstractCandidates
                    .Select(candidate => candidate.GetNodeFingerprint())
                    .OrderBy(fingerprint => fingerprint, StringComparer.Ordinal));
    }

    private static IReadOnlyList<RefinementTraceItem> BuildTrace(
        SearchNode end,
        bool includeAuxiliaryInTrace)
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
                auxiliaryState: includeAuxiliaryInTrace
                    ? (State)node.AuxiliaryState
                    : null));
        }
        reversed.Reverse();
        return reversed;
    }

    private sealed class SearchNode
    {
        public SearchNode(
            StateGraphNode concreteNode,
            object auxiliaryState,
            IReadOnlyList<StateGraphNode> abstractCandidates,
            bool hasUnknownAbstractAlternative,
            SearchNode parent,
            StateGraphEdge incomingEdge,
            IState mappedAbstractState)
        {
            ConcreteNode = concreteNode;
            AuxiliaryState = auxiliaryState;
            AbstractCandidates = abstractCandidates;
            HasUnknownAbstractAlternative = hasUnknownAbstractAlternative;
            Parent = parent;
            IncomingEdge = incomingEdge;
            MappedAbstractState = mappedAbstractState;
            Depth = parent == null ? 0 : parent.Depth + 1;
        }

        public StateGraphNode ConcreteNode { get; }
        public object AuxiliaryState { get; }
        public IReadOnlyList<StateGraphNode> AbstractCandidates { get; }
        public bool HasUnknownAbstractAlternative { get; }
        public SearchNode Parent { get; }
        public StateGraphEdge IncomingEdge { get; }
        public IState MappedAbstractState { get; }
        public int Depth { get; }
    }
}
