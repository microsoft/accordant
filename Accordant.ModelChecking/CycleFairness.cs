namespace Microsoft.Accordant.ModelChecking
{
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

        public TransitionContext Context => TransitionContext.Edge(
            Source.State, StepFunction, Metadata, Target.State);

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
    /// Shared changing-edge fairness analysis for system and product cycles.
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

            public Analysis(
                HashSet<string> enabled,
                HashSet<string> continuouslyEnabled,
                HashSet<string> taken,
                Dictionary<string, IStepFunction> stepById,
                IReadOnlyList<IReadOnlyList<FairnessEdge>> enabledByGroup,
                IReadOnlyList<FairnessEdge> takenEdges)
            {
                Enabled = enabled;
                ContinuouslyEnabled = continuouslyEnabled;
                Taken = taken;
                StepById = stepById;
                EnabledByGroup = enabledByGroup;
                TakenEdges = takenEdges;
            }
        }

        internal static Analysis Compute<TGroup>(
            IEnumerable<TGroup> groups,
            Func<TGroup, IEnumerable<FairnessEdge>> enabledAt,
            IEnumerable<FairnessEdge> taken)
        {
            var stepById = new Dictionary<string, IStepFunction>();
            var perGroup = new List<IReadOnlyList<FairnessEdge>>();
            var perGroupIds = new List<HashSet<string>>();

            foreach (var group in groups)
            {
                var edges = enabledAt(group).Where(edge => edge.ChangesState).ToList();
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

            var takenEdges = taken.Where(edge => edge.ChangesState).ToList();
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
                takenEdges);
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

            foreach (var constraint in fairness.EdgeConstraints)
            {
                var enabled = constraint.IsStrong
                    ? analysis.EnabledByGroup.Any(
                        edges => edges.Any(constraint.Matches))
                    : analysis.EnabledByGroup.All(
                        edges => edges.Any(constraint.Matches));
                var taken = analysis.TakenEdges.Any(constraint.Matches);
                if (enabled && !taken)
                    return false;
            }

            return true;
        }
    }
}
