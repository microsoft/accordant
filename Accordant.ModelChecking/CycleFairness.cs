namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant.ModelChecking.Symbolic;

internal readonly struct FairnessEdge
{
    public StateGraphNode Source { get; }
    public IStepFunction StepFunction { get; }
    public object Metadata { get; }
    public StateGraphNode Target { get; }

    public bool ChangesState => !StateSemantics.Equal(Source.State, Target.State);

    public bool IsSyntheticStutter =>
        StepFunction == null ||
        StepFunction is StutterAction ||
        StepFunction is Ltl.StutterStep ||
        string.Equals(
            StepFunction.StepFunctionId,
            "<refinement-stutter>",
            StringComparison.Ordinal);

    public TransitionContext Context => TransitionContext.Edge(
        Source.State, StepFunction, Metadata, Target.State, Source);

    public FairnessEdge(
        StateGraphNode source,
        IStepFunction stepFunction,
        object metadata,
        StateGraphNode target)
    {
        Source = source;
        StepFunction = stepFunction;
        Metadata = metadata;
        Target = target;
    }
}

/// <summary>
/// Shared fairness analysis for system and product cycles. Legacy
/// step/relation constraints consume the changing-edge projection;
/// semantic-action constraints consume all selected model edges.
/// </summary>
internal static class CycleFairness
{
    internal sealed class Analysis
    {
        public HashSet<string> Enabled { get; }
        public HashSet<string> ContinuouslyEnabled { get; }
        public HashSet<string> Taken { get; }
        public Dictionary<string, IStepFunction> StepById { get; }
        public IReadOnlyList<IReadOnlyList<FairnessEdge>> EnabledByGroup { get; }
        public IReadOnlyList<FairnessEdge> TakenEdges { get; }
        public IReadOnlyList<IReadOnlyList<FairnessEdge>> AllEnabledByGroup { get; }
        public IReadOnlyList<FairnessEdge> AllTakenEdges { get; }

        public Analysis(
            HashSet<string> enabled,
            HashSet<string> continuouslyEnabled,
            HashSet<string> taken,
            Dictionary<string, IStepFunction> stepById,
            IReadOnlyList<IReadOnlyList<FairnessEdge>> enabledByGroup,
            IReadOnlyList<FairnessEdge> takenEdges,
            IReadOnlyList<IReadOnlyList<FairnessEdge>> allEnabledByGroup,
            IReadOnlyList<FairnessEdge> allTakenEdges)
        {
            Enabled = enabled;
            ContinuouslyEnabled = continuouslyEnabled;
            Taken = taken;
            StepById = stepById;
            EnabledByGroup = enabledByGroup;
            TakenEdges = takenEdges;
            AllEnabledByGroup = allEnabledByGroup;
            AllTakenEdges = allTakenEdges;
        }
    }

    internal sealed class EdgeViolation
    {
        private readonly FairnessKey key;

        internal EdgeViolation(
            Fairness.EdgeConstraint constraint,
            bool hasKey,
            FairnessKey key)
        {
            Constraint = constraint;
            HasKey = hasKey;
            this.key = key;
        }

        internal Fairness.EdgeConstraint Constraint { get; }
        internal bool HasKey { get; }

        internal bool Matches(FairnessEdge edge)
            => Constraint.Matches(edge) &&
               (!HasKey ||
                key.Equals(
                    new FairnessKey(Constraint.KeySelector(edge))));
    }

    internal static Analysis Compute<TGroup>(
        IEnumerable<TGroup> groups,
        Func<TGroup, IEnumerable<FairnessEdge>> enabledAt,
        IEnumerable<FairnessEdge> taken)
    {
        var stepById = new Dictionary<string, IStepFunction>();
        var perGroup = new List<IReadOnlyList<FairnessEdge>>();
        var allPerGroup = new List<IReadOnlyList<FairnessEdge>>();
        var perGroupIds = new List<HashSet<string>>();

        foreach (var group in groups)
        {
            var allEdges = enabledAt(group)
                .Where(edge => !edge.IsSyntheticStutter)
                .ToList();
            allPerGroup.Add(allEdges);

            var edges = allEdges.Where(edge => edge.ChangesState).ToList();
            perGroup.Add(edges);

            var ids = new HashSet<string>();
            foreach (var edge in edges)
            {
                ids.Add(edge.StepFunction.StepFunctionId);
                if (!stepById.ContainsKey(edge.StepFunction.StepFunctionId))
                    stepById[edge.StepFunction.StepFunctionId] = edge.StepFunction;
            }
            perGroupIds.Add(ids);
        }

        var enabled = new HashSet<string>();
        foreach (var ids in perGroupIds)
            enabled.UnionWith(ids);

        var continuouslyEnabled = new HashSet<string>(enabled);
        foreach (var ids in perGroupIds)
            continuouslyEnabled.IntersectWith(ids);
        if (perGroupIds.Count == 0)
            continuouslyEnabled.Clear();

        var allTakenEdges = taken
            .Where(edge => !edge.IsSyntheticStutter)
            .ToList();
        var takenEdges = allTakenEdges
            .Where(edge => edge.ChangesState)
            .ToList();
        var takenIds = new HashSet<string>();
        foreach (var edge in takenEdges)
        {
            takenIds.Add(edge.StepFunction.StepFunctionId);
            if (!stepById.ContainsKey(edge.StepFunction.StepFunctionId))
                stepById[edge.StepFunction.StepFunctionId] = edge.StepFunction;
        }

        return new Analysis(
            enabled,
            continuouslyEnabled,
            takenIds,
            stepById,
            perGroup,
            takenEdges,
            allPerGroup,
            allTakenEdges);
    }

    internal static bool IsFair(Analysis analysis, Fairness fairness)
    {
        if (fairness == null)
            return true;

        foreach (var id in analysis.Enabled)
        {
            if (!analysis.StepById.TryGetValue(id, out var step))
                continue;

            if (fairness.WeakStepPredicate(step)
                && analysis.ContinuouslyEnabled.Contains(id)
                && !analysis.Taken.Contains(id))
                return false;

            if (fairness.StrongStepPredicate(step)
                && !analysis.Taken.Contains(id))
                return false;
        }

        if (GetEdgeViolations(analysis, fairness, isStrong: false).Count > 0 ||
            GetEdgeViolations(analysis, fairness, isStrong: true).Count > 0)
            return false;

        return true;
    }

    internal static IReadOnlyList<EdgeViolation> GetEdgeViolations(
        Analysis analysis,
        Fairness fairness,
        bool isStrong)
    {
        var violations = new List<EdgeViolation>();

        foreach (var constraint in fairness.EdgeConstraints
            .Where(item => item.IsStrong == isStrong))
        {
            var enabledByGroup = constraint.IncludesStateNeutral
                ? analysis.AllEnabledByGroup
                : analysis.EnabledByGroup;
            var takenEdges = constraint.IncludesStateNeutral
                ? analysis.AllTakenEdges
                : analysis.TakenEdges;

            if (constraint.KeySelector == null)
            {
                var enabled = IsEnabled(
                    enabledByGroup,
                    constraint.IsStrong,
                    constraint.Matches);
                if (enabled && !takenEdges.Any(constraint.Matches))
                {
                    violations.Add(new EdgeViolation(
                        constraint,
                        hasKey: false,
                        default));
                }
                continue;
            }

            var keys = new HashSet<FairnessKey>();
            foreach (var edges in enabledByGroup)
            {
                foreach (var edge in edges)
                {
                    if (constraint.Matches(edge))
                    {
                        keys.Add(new FairnessKey(
                            constraint.KeySelector(edge)));
                    }
                }
            }

            foreach (var key in keys)
            {
                var obligation = new EdgeViolation(
                    constraint,
                    hasKey: true,
                    key);
                var enabled = IsEnabled(
                    enabledByGroup,
                    constraint.IsStrong,
                    obligation.Matches);
                if (enabled && !takenEdges.Any(obligation.Matches))
                    violations.Add(obligation);
            }
        }

        return violations;
    }

    private static bool IsEnabled(
        IReadOnlyList<IReadOnlyList<FairnessEdge>> enabledByGroup,
        bool isStrong,
        Func<FairnessEdge, bool> matches)
        => isStrong
            ? enabledByGroup.Any(edges => edges.Any(matches))
            : enabledByGroup.Count > 0 &&
              enabledByGroup.All(edges => edges.Any(matches));

    internal readonly struct FairnessKey : IEquatable<FairnessKey>
    {
        private readonly object value;

        internal FairnessKey(object value)
        {
            this.value = value;
        }

        public bool Equals(FairnessKey other)
            => object.Equals(value, other.value);

        public override bool Equals(object obj)
            => obj is FairnessKey other && Equals(other);

        public override int GetHashCode()
            => value?.GetHashCode() ?? 0;
    }
}
