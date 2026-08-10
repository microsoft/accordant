namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;

internal static class FunctionalTemporalRefinement
{
    internal static RefinementCheckingResult Check<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAbstract> mapping,
        Fairness concreteFairness,
        Fairness abstractFairness)
        where TConcrete : IState
        where TAbstract : IState
        => CheckCore<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            initialAuxiliaryState: null,
            advanceAuxiliaryState: (_, _, _) => null,
            auxiliaryIdentity: _ => string.Empty,
            mapping: (concrete, _) => mapping(
                GetConcrete<TConcrete>(concrete)),
            includeAuxiliaryInTrace: false,
            concreteFairness,
            abstractFairness);

    internal static RefinementCheckingResult CheckAugmented<
        TConcrete,
        TAbstract,
        TAuxiliary>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next,
        Func<TConcrete, TAuxiliary, TAbstract> mapping,
        Fairness concreteFairness,
        Fairness abstractFairness)
        where TConcrete : IState
        where TAbstract : IState
        where TAuxiliary : State
    {
        var runtime = new AugmentationRuntime<TConcrete, TAuxiliary>(
            concreteRoot,
            initial,
            next);
        return CheckCore<TConcrete, TAbstract>(
            concreteRoot,
            abstractRoot,
            runtime.InitialState,
            runtime.Advance,
            runtime.GetIdentity,
            (concrete, auxiliary) =>
            {
                var mapped = mapping(
                    GetConcrete<TConcrete>(concrete),
                    runtime.GetValue(auxiliary));
                runtime.Validate(auxiliary);
                return mapped;
            },
            includeAuxiliaryInTrace: true,
            concreteFairness,
            abstractFairness);
    }

    private static RefinementCheckingResult CheckCore<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        object initialAuxiliaryState,
        Func<object, StateGraphNode, StateGraphEdge, object> advanceAuxiliaryState,
        Func<object, string> auxiliaryIdentity,
        Func<StateGraphNode, object, TAbstract> mapping,
        bool includeAuxiliaryInTrace,
        Fairness concreteFairness,
        Fairness abstractFairness)
        where TConcrete : IState
        where TAbstract : IState
    {
        concreteFairness ??= Fairness.None;
        abstractFairness ??= Fairness.None;

        var mappedStates = new Dictionary<string, TAbstract>(StringComparer.Ordinal);

        TAbstract Map(StateGraphNode concrete, object auxiliaryState)
        {
            GetConcrete<TConcrete>(concrete);
            var key = concrete.GetNodeFingerprint() + "|" +
                auxiliaryIdentity(auxiliaryState);
            if (!mappedStates.TryGetValue(key, out var mapped))
            {
                mapped = mapping(concrete, auxiliaryState);
                if (mapped == null)
                {
                    throw new InvalidOperationException(
                        "The refinement mapping returned null.");
                }
                mappedStates[key] = mapped;
            }
            return mapped;
        }

        ValidateAbstract<TAbstract>(abstractRoot);
        var mappedRoot = Map(concreteRoot, initialAuxiliaryState);
        var initialMismatch =
            !StateSemantics.Equal(mappedRoot, abstractRoot.State);

        var graph = BuildAlignedGraph<TConcrete, TAbstract>(
            concreteRoot,
            initialMismatch ? null : abstractRoot,
            initialAuxiliaryState,
            advanceAuxiliaryState,
            auxiliaryIdentity,
            Map);

        var mismatchCycle = FindConcreteFairCycle(
            graph.Nodes.Where(node =>
                node.AbstractNode == null && !node.HasUnknownAbstractAlternative),
            edge => edge.Target.AbstractNode == null,
            concreteFairness);
        if (mismatchCycle != null)
        {
            return RefinementCheckingResult.Failure(
                initialMismatch
                    ? RefinementFailureKind.InitialStateMismatch
                    : RefinementFailureKind.TransitionMismatch,
                BuildTrace(
                    mismatchCycle,
                    edge => edge.Target.AbstractNode == null,
                    includeAuxiliaryInTrace));
        }

        foreach (var obligation in GetObligations(graph.Nodes, abstractFairness))
        {
            bool NodeAllowed(ProductNode node)
                => node.AbstractNode != null &&
                    !node.HasUnknownAbstractAlternative &&
                    (!obligation.IsWeak || obligation.IsEnabledAt(node.AbstractNode));

            bool EdgeAllowed(ProductEdge edge)
                => NodeAllowed(edge.Target) && !obligation.IsTakenBy(edge);

            foreach (var scc in FindSccs(
                graph.Nodes.Where(NodeAllowed),
                EdgeAllowed))
            {
                if (obligation.IsStrong &&
                    !scc.Any(node => obligation.IsEnabledAt(node.AbstractNode)))
                {
                    continue;
                }

                var fairCycle = FindConcreteFairCycle(
                    scc,
                    EdgeAllowed,
                    concreteFairness,
                    obligation.IsStrong
                        ? node => obligation.IsEnabledAt(node.AbstractNode)
                        : null);
                if (fairCycle != null)
                {
                    return RefinementCheckingResult.Failure(
                        RefinementFailureKind.TemporalFairnessMismatch,
                        BuildTrace(
                            fairCycle,
                            EdgeAllowed,
                            includeAuxiliaryInTrace));
                }
            }
        }

        return graph.ReachedDepthFrontier
            ? RefinementCheckingResult.Inconclusive(
                BuildFrontierTrace(
                    graph.FirstFrontier,
                    includeAuxiliaryInTrace))
            : RefinementCheckingResult.Success();
    }

    private static AlignedGraph BuildAlignedGraph<TConcrete, TAbstract>(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        object initialAuxiliaryState,
        Func<object, StateGraphNode, StateGraphEdge, object> advanceAuxiliaryState,
        Func<object, string> auxiliaryIdentity,
        Func<StateGraphNode, object, TAbstract> map)
        where TConcrete : IState
        where TAbstract : IState
    {
        var nodes = new Dictionary<string, ProductNode>(StringComparer.Ordinal);
        var queue = new Queue<ProductNode>();
        ProductNode firstFrontier = null;

        ProductNode GetOrCreate(
            StateGraphNode concrete,
            StateGraphNode abstraction,
            IState mappedAbstractState,
            object auxiliaryState,
            bool hasUnknown,
            ProductNode parent,
            ProductEdge incoming)
        {
            var key = concrete.GetNodeFingerprint() + "|" +
                auxiliaryIdentity(auxiliaryState) + "|" +
                (abstraction?.GetNodeFingerprint() ?? "<mismatch>") + "|" +
                (hasUnknown ? "unknown" : "known");
            if (!nodes.TryGetValue(key, out var node))
            {
                node = new ProductNode(
                    concrete,
                    abstraction,
                    mappedAbstractState,
                    auxiliaryState,
                    hasUnknown,
                    parent,
                    incoming);
                nodes[key] = node;
                queue.Enqueue(node);
            }
            return node;
        }

        var rootUnknown = abstractRoot?.IsDepthFrontier == true;
        var root = GetOrCreate(
            concreteRoot,
            abstractRoot,
            map(concreteRoot, initialAuxiliaryState),
            initialAuxiliaryState,
            rootUnknown,
            parent: null,
            incoming: null);
        var reachedFrontier = rootUnknown;
        if (rootUnknown)
        {
            firstFrontier = root;
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var materializedConcreteEdges = current.ConcreteNode.Edges;
            if (current.ConcreteNode.IsDepthFrontier)
            {
                reachedFrontier = true;
                firstFrontier ??= current;
                continue;
            }

            var concreteEdges = GetConcreteEdges(
                current.ConcreteNode,
                materializedConcreteEdges);
            foreach (var concreteEdge in concreteEdges)
            {
                ProductNode target;
                StateGraphEdge abstractEdge = null;
                var nextAuxiliaryState =
                    concreteEdge.StepFunction == RefinementStutterStep.Instance
                        ? current.AuxiliaryState
                        : advanceAuxiliaryState(
                            current.AuxiliaryState,
                            current.ConcreteNode,
                            concreteEdge);
                var mappedTarget = map(
                    concreteEdge.Target,
                    nextAuxiliaryState);

                if (current.AbstractNode == null)
                {
                    target = GetOrCreate(
                        concreteEdge.Target,
                        abstraction: null,
                        mappedTarget,
                        nextAuxiliaryState,
                        current.HasUnknownAbstractAlternative,
                        current,
                        incoming: null);
                }
                else
                {
                    ValidateAbstract<TAbstract>(current.AbstractNode);
                    var matches = FindAbstractMatches(
                        current.AbstractNode,
                        mappedTarget);
                    var unknown = current.HasUnknownAbstractAlternative ||
                        current.AbstractNode.IsDepthFrontier;

                    if (current.AbstractNode.IsDepthFrontier)
                    {
                        reachedFrontier = true;
                        firstFrontier ??= current;
                    }

                    if (matches.Count > 1)
                    {
                        throw new AmbiguousTemporalRefinementException(
                            current.ConcreteNode,
                            current.AbstractNode,
                            concreteEdge.StepFunction,
                            matches.Count);
                    }

                    if (matches.Count == 0)
                    {
                        if (unknown)
                        {
                            reachedFrontier = true;
                            firstFrontier ??= current;
                            continue;
                        }

                        target = GetOrCreate(
                            concreteEdge.Target,
                            abstraction: null,
                            mappedTarget,
                            nextAuxiliaryState,
                            hasUnknown: false,
                            current,
                            incoming: null);
                    }
                    else
                    {
                        abstractEdge = matches[0];
                        var abstractTarget = abstractEdge?.Target ??
                            current.AbstractNode;
                        target = GetOrCreate(
                            concreteEdge.Target,
                            abstractTarget,
                            mappedTarget,
                            nextAuxiliaryState,
                            unknown,
                            current,
                            incoming: null);
                    }
                }

                var productEdge = new ProductEdge(
                    current,
                    target,
                    concreteEdge,
                    abstractEdge);
                current.Edges.Add(productEdge);
                if (target.IncomingEdge == null && target != root)
                {
                    target.IncomingEdge = productEdge;
                    target.Parent = current;
                }
            }
        }

        return new AlignedGraph(
            nodes.Values.ToArray(),
            reachedFrontier,
            firstFrontier);
    }

    private static IReadOnlyList<StateGraphEdge> GetConcreteEdges(
        StateGraphNode node,
        IReadOnlyList<StateGraphEdge> materializedEdges)
    {
        if (materializedEdges.Count > 0)
        {
            return materializedEdges;
        }

        return new[]
        {
            new StateGraphEdge
            {
                Target = node,
                StepFunction = RefinementStutterStep.Instance
            }
        };
    }

    private static List<StateGraphEdge> FindAbstractMatches<TAbstract>(
        StateGraphNode abstractNode,
        TAbstract mappedTarget)
        where TAbstract : IState
    {
        var matches = new List<StateGraphEdge>();
        if (StateSemantics.Equal(mappedTarget, abstractNode.State))
        {
            matches.Add(null);
        }

        foreach (var edge in abstractNode.Edges)
        {
            ValidateAbstract<TAbstract>(edge.Target);
            if (StateSemantics.Equal(mappedTarget, edge.Target.State))
            {
                matches.Add(edge);
            }
        }
        return matches;
    }

    private static IReadOnlyList<TemporalFairnessObligation> GetObligations(
        IEnumerable<ProductNode> nodes,
        Fairness fairness)
    {
        var abstractNodes = nodes
            .Where(node => node.AbstractNode != null)
            .Select(node => node.AbstractNode)
            .GroupBy(node => node.GetNodeFingerprint())
            .Select(group => group.First())
            .ToArray();
        var steps = abstractNodes
            .SelectMany(node => node.Edges.Select(edge => (node, edge)))
            .Where(item => !StateSemantics.Equal(
                item.node.State,
                item.edge.Target.State))
            .Select(item => item.edge.StepFunction)
            .GroupBy(step => step.StepFunctionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var obligations = new List<TemporalFairnessObligation>();

        foreach (var step in steps)
        {
            if (fairness.WeakStepPredicate(step))
            {
                obligations.Add(TemporalFairnessObligation.ForStep(
                    isStrong: false,
                    step.StepFunctionId));
            }
            if (fairness.StrongStepPredicate(step))
            {
                obligations.Add(TemporalFairnessObligation.ForStep(
                    isStrong: true,
                    step.StepFunctionId));
            }
        }

        obligations.AddRange(fairness.EdgeConstraints.Select(
            constraint => TemporalFairnessObligation.ForEdge(
                constraint.IsStrong,
                constraint.Matches)));
        return obligations;
    }

    private static IReadOnlyList<ProductNode> FindConcreteFairCycle(
        IEnumerable<ProductNode> nodes,
        Func<ProductEdge, bool> edgeAllowed,
        Fairness fairness,
        Func<ProductNode, bool> recurrentRequirement = null)
    {
        foreach (var scc in FindSccs(nodes, edgeAllowed))
        {
            var fair = FindConcreteFairSubcycle(
                scc,
                edgeAllowed,
                fairness,
                recurrentRequirement);
            if (fair != null)
            {
                return fair;
            }
        }
        return null;
    }

    private static IReadOnlyList<ProductNode> FindConcreteFairSubcycle(
        IReadOnlyList<ProductNode> scc,
        Func<ProductEdge, bool> edgeAllowed,
        Fairness fairness,
        Func<ProductNode, bool> recurrentRequirement)
    {
        if (recurrentRequirement != null && !scc.Any(recurrentRequirement))
        {
            return null;
        }

        var members = new HashSet<ProductNode>(scc);

        IEnumerable<FairnessEdge> EnabledAt(ProductNode node)
            => node.ConcreteNode.Edges.Select(edge => new FairnessEdge(
                node.ConcreteNode,
                edge.StepFunction,
                edge.Metadata,
                edge.Target));

        IEnumerable<FairnessEdge> Taken()
            => scc.SelectMany(node => node.Edges)
                .Where(edge =>
                    edgeAllowed(edge) && members.Contains(edge.Target))
                .Select(edge => new FairnessEdge(
                    edge.Source.ConcreteNode,
                    edge.ConcreteEdge.StepFunction,
                    edge.ConcreteEdge.Metadata,
                    edge.Target.ConcreteNode));

        var analysis = CycleFairness.Compute(scc, EnabledAt, Taken());
        if (CycleFairness.IsFair(analysis, fairness))
        {
            return scc;
        }

        var failedWeak = analysis.Enabled.Any(id =>
            analysis.StepById.TryGetValue(id, out var step) &&
            fairness.WeakStepPredicate(step) &&
            analysis.ContinuouslyEnabled.Contains(id) &&
            !analysis.Taken.Contains(id));
        failedWeak |= fairness.EdgeConstraints
            .Where(constraint => !constraint.IsStrong)
            .Any(constraint =>
                analysis.EnabledByGroup.All(edges =>
                    edges.Any(constraint.Matches)) &&
                !analysis.TakenEdges.Any(constraint.Matches));
        if (failedWeak)
        {
            return null;
        }

        var failedStrongIds = new HashSet<string>(
            analysis.Enabled.Where(id =>
                analysis.StepById.TryGetValue(id, out var step) &&
                fairness.StrongStepPredicate(step) &&
                !analysis.Taken.Contains(id)),
            StringComparer.Ordinal);
        var failedStrongEdges = fairness.EdgeConstraints
            .Where(constraint =>
                constraint.IsStrong &&
                analysis.EnabledByGroup.Any(edges =>
                    edges.Any(constraint.Matches)) &&
                !analysis.TakenEdges.Any(constraint.Matches))
            .ToArray();

        var remaining = scc.Where(node =>
        {
            var enabled = EnabledAt(node).Where(edge => edge.ChangesState).ToArray();
            return !enabled.Any(edge =>
                    failedStrongIds.Contains(edge.StepFunction.StepFunctionId)) &&
                !failedStrongEdges.Any(constraint =>
                    enabled.Any(constraint.Matches));
        }).ToArray();

        foreach (var subScc in FindSccs(remaining, edgeAllowed))
        {
            var fair = FindConcreteFairSubcycle(
                subScc,
                edgeAllowed,
                fairness,
                recurrentRequirement);
            if (fair != null)
            {
                return fair;
            }
        }
        return null;
    }

    private static IReadOnlyList<IReadOnlyList<ProductNode>> FindSccs(
        IEnumerable<ProductNode> candidates,
        Func<ProductEdge, bool> edgeAllowed)
    {
        var nodes = candidates.Distinct().ToArray();
        var allowed = new HashSet<ProductNode>(nodes);
        var index = 0;
        var indexes = new Dictionary<ProductNode, int>();
        var lowLinks = new Dictionary<ProductNode, int>();
        var stack = new Stack<ProductNode>();
        var onStack = new HashSet<ProductNode>();
        var result = new List<IReadOnlyList<ProductNode>>();

        void Connect(ProductNode node)
        {
            indexes[node] = index;
            lowLinks[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var edge in node.Edges.Where(edge =>
                edgeAllowed(edge) && allowed.Contains(edge.Target)))
            {
                var target = edge.Target;
                if (!indexes.ContainsKey(target))
                {
                    Connect(target);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indexes[target]);
                }
            }

            if (lowLinks[node] != indexes[node])
            {
                return;
            }

            var component = new List<ProductNode>();
            ProductNode member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            }
            while (member != node);

            var componentSet = new HashSet<ProductNode>(component);
            var hasCycle = component.Count > 1 ||
                component[0].Edges.Any(edge =>
                    edgeAllowed(edge) &&
                    edge.Target == component[0] &&
                    componentSet.Contains(edge.Target));
            if (hasCycle)
            {
                result.Add(component);
            }
        }

        foreach (var node in nodes)
        {
            if (!indexes.ContainsKey(node))
            {
                Connect(node);
            }
        }
        return result;
    }

    private static IReadOnlyList<RefinementTraceItem> BuildTrace(
        IReadOnlyList<ProductNode> cycle,
        Func<ProductEdge, bool> edgeAllowed,
        bool includeAuxiliaryInTrace)
    {
        var cycleSet = new HashSet<ProductNode>(cycle);
        var entry = cycle[0];
        var prefix = new List<ProductNode>();
        for (var node = entry; node != null; node = node.Parent)
        {
            prefix.Add(node);
        }
        prefix.Reverse();

        var cycleEdges = BuildCoveringClosedWalk(
            entry,
            cycleSet,
            edgeAllowed);
        var result = new List<RefinementTraceItem>();
        foreach (var node in prefix)
        {
            result.Add(ToTraceItem(
                node,
                node == entry,
                node.IncomingEdge,
                includeAuxiliaryInTrace));
        }
        foreach (var edge in cycleEdges)
        {
            result.Add(ToTraceItem(
                edge.Target,
                isInCycle: true,
                edge,
                includeAuxiliaryInTrace));
        }
        return result;
    }

    private static IReadOnlyList<ProductEdge> BuildCoveringClosedWalk(
        ProductNode entry,
        HashSet<ProductNode> cycle,
        Func<ProductEdge, bool> edgeAllowed)
    {
        var internalEdges = cycle
            .SelectMany(node => node.Edges)
            .Where(edge =>
                edgeAllowed(edge) && cycle.Contains(edge.Target))
            .ToArray();
        var result = new List<ProductEdge>();
        var current = entry;

        foreach (var edge in internalEdges)
        {
            result.AddRange(FindPath(
                current,
                edge.Source,
                cycle,
                edgeAllowed));
            result.Add(edge);
            current = edge.Target;
        }
        result.AddRange(FindPath(
            current,
            entry,
            cycle,
            edgeAllowed));
        return result;
    }

    private static IReadOnlyList<ProductEdge> FindPath(
        ProductNode source,
        ProductNode target,
        HashSet<ProductNode> allowed,
        Func<ProductEdge, bool> edgeAllowed)
    {
        if (source == target)
        {
            return Array.Empty<ProductEdge>();
        }

        var queue = new Queue<ProductNode>();
        var parent = new Dictionary<ProductNode, ProductEdge>();
        queue.Enqueue(source);
        parent[source] = null;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in current.Edges.Where(edge =>
                edgeAllowed(edge) && allowed.Contains(edge.Target)))
            {
                if (parent.ContainsKey(edge.Target))
                {
                    continue;
                }

                parent[edge.Target] = edge;
                if (edge.Target == target)
                {
                    var reversed = new List<ProductEdge>();
                    for (var node = target; node != source;)
                    {
                        var incoming = parent[node];
                        reversed.Add(incoming);
                        node = incoming.Source;
                    }
                    reversed.Reverse();
                    return reversed;
                }
                queue.Enqueue(edge.Target);
            }
        }

        throw new InvalidOperationException(
            "The reported SCC is not strongly connected.");
    }

    private static IReadOnlyList<RefinementTraceItem> BuildFrontierTrace(
        ProductNode frontier,
        bool includeAuxiliaryInTrace)
    {
        if (frontier == null)
        {
            return null;
        }

        var reversed = new List<RefinementTraceItem>();
        for (var node = frontier; node != null; node = node.Parent)
        {
            reversed.Add(ToTraceItem(
                node,
                isInCycle: false,
                node.IncomingEdge,
                includeAuxiliaryInTrace));
        }
        reversed.Reverse();
        return reversed;
    }

    private static RefinementTraceItem ToTraceItem(
        ProductNode node,
        bool isInCycle,
        ProductEdge incoming,
        bool includeAuxiliaryInTrace)
        => new RefinementTraceItem(
            node.ConcreteNode,
            incoming?.ConcreteEdge.StepFunction,
            incoming?.ConcreteEdge.Metadata,
            node.MappedAbstractState,
            node.AbstractNode == null
                ? Array.Empty<StateGraphNode>()
                : new[] { node.AbstractNode },
            isInCycle,
            auxiliaryState: includeAuxiliaryInTrace
                ? (State)node.AuxiliaryState
                : null);

    private static TConcrete GetConcrete<TConcrete>(StateGraphNode node)
        where TConcrete : IState
    {
        if (node.State is TConcrete concrete)
        {
            return concrete;
        }

        throw new InvalidOperationException(
            $"Concrete graph node state '{node.State?.GetType().FullName}' " +
            $"is not a {typeof(TConcrete).FullName}.");
    }

    private static void ValidateAbstract<TAbstract>(StateGraphNode node)
        where TAbstract : IState
    {
        if (!(node.State is TAbstract))
        {
            throw new InvalidOperationException(
                $"Abstract graph node state '{node.State?.GetType().FullName}' " +
                $"is not a {typeof(TAbstract).FullName}.");
        }
    }

    private sealed class AlignedGraph
    {
        public AlignedGraph(
            IReadOnlyList<ProductNode> nodes,
            bool reachedDepthFrontier,
            ProductNode firstFrontier)
        {
            Nodes = nodes;
            ReachedDepthFrontier = reachedDepthFrontier;
            FirstFrontier = firstFrontier;
        }

        public IReadOnlyList<ProductNode> Nodes { get; }
        public bool ReachedDepthFrontier { get; }
        public ProductNode FirstFrontier { get; }
    }

    private sealed class ProductNode
    {
        public ProductNode(
            StateGraphNode concreteNode,
            StateGraphNode abstractNode,
            IState mappedAbstractState,
            object auxiliaryState,
            bool hasUnknownAbstractAlternative,
            ProductNode parent,
            ProductEdge incomingEdge)
        {
            ConcreteNode = concreteNode;
            AbstractNode = abstractNode;
            MappedAbstractState = mappedAbstractState;
            AuxiliaryState = auxiliaryState;
            HasUnknownAbstractAlternative = hasUnknownAbstractAlternative;
            Parent = parent;
            IncomingEdge = incomingEdge;
        }

        public StateGraphNode ConcreteNode { get; }
        public StateGraphNode AbstractNode { get; }
        public IState MappedAbstractState { get; }
        public object AuxiliaryState { get; }
        public bool HasUnknownAbstractAlternative { get; }
        public List<ProductEdge> Edges { get; } = new List<ProductEdge>();
        public ProductNode Parent { get; set; }
        public ProductEdge IncomingEdge { get; set; }
    }

    private sealed class ProductEdge
    {
        public ProductEdge(
            ProductNode source,
            ProductNode target,
            StateGraphEdge concreteEdge,
            StateGraphEdge abstractEdge)
        {
            Source = source;
            Target = target;
            ConcreteEdge = concreteEdge;
            AbstractEdge = abstractEdge;
        }

        public ProductNode Source { get; }
        public ProductNode Target { get; }
        public StateGraphEdge ConcreteEdge { get; }
        public StateGraphEdge AbstractEdge { get; }
    }

    private sealed class TemporalFairnessObligation
    {
        private readonly Func<FairnessEdge, bool> matches;

        private TemporalFairnessObligation(
            bool isStrong,
            Func<FairnessEdge, bool> matches)
        {
            IsStrong = isStrong;
            this.matches = matches;
        }

        public bool IsStrong { get; }
        public bool IsWeak => !IsStrong;

        public bool IsEnabledAt(StateGraphNode node)
            => node.Edges
                .Select(edge => new FairnessEdge(
                    node,
                    edge.StepFunction,
                    edge.Metadata,
                    edge.Target))
                .Any(edge => edge.ChangesState && matches(edge));

        public bool IsTakenBy(ProductEdge edge)
        {
            if (edge.AbstractEdge == null)
            {
                return false;
            }
            var fairnessEdge = new FairnessEdge(
                edge.Source.AbstractNode,
                edge.AbstractEdge.StepFunction,
                edge.AbstractEdge.Metadata,
                edge.Target.AbstractNode);
            return fairnessEdge.ChangesState && matches(fairnessEdge);
        }

        public static TemporalFairnessObligation ForStep(
            bool isStrong,
            string stepFunctionId)
            => new TemporalFairnessObligation(
                isStrong,
                edge => edge.StepFunction.StepFunctionId == stepFunctionId);

        public static TemporalFairnessObligation ForEdge(
            bool isStrong,
            Func<FairnessEdge, bool> matches)
            => new TemporalFairnessObligation(isStrong, matches);
    }

    private sealed class RefinementStutterStep : IStepFunction
    {
        public static RefinementStutterStep Instance { get; } =
            new RefinementStutterStep();

        public string StepFunctionId => "<refinement-stutter>";

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            => null;
    }
}
