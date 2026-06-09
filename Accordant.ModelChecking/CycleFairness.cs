namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Single source of truth for the enabled / continuously-enabled /
    /// taken-in-cycle computation underlying every fairness decision in
    /// the model checker.
    ///
    /// <para>
    /// Four call sites previously duplicated this logic, each with
    /// slightly different group/edge iteration patterns:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="Fairness.IsFairCycle"/> — system SCC for the
    ///   explicit non-product fairness path.</item>
    ///   <item><see cref="Ltl.LtlCheck.IsFairCycle"/> — explicit LTL
    ///   product SCC projected to its unique system nodes; "taken" comes
    ///   from product edges that stay inside the product SCC.</item>
    ///   <item><c>SccProductCheck.IsFairProductCycle</c> — symbolic
    ///   product SCC, the symbolic analogue of the explicit-LTL
    ///   projection.</item>
    ///   <item><see cref="PropertyCheckingResult"/>'s
    ///   <c>GetEnabledButNotTakenSteps</c> — diagnostic hint emitting
    ///   labels for steps that were enabled but never fired inside the
    ///   bad cycle.</item>
    /// </list>
    /// <para>
    /// Each call site differs only in how it enumerates (a) the
    /// "groups" (one per unique system node visited by the cycle),
    /// (b) the step functions enabled at each group's system state, and
    /// (c) the step functions actually fired on edges that stay inside
    /// the cycle. This helper factors that variation behind
    /// <see cref="Compute{TGroup}"/> and returns a single
    /// <see cref="Analysis"/> from which fairness verdicts and the
    /// diagnostic hint are derived.
    /// </para>
    /// </summary>
    internal static class CycleFairness
    {
        /// <summary>
        /// Aggregated enabled / continuouslyEnabled / taken sets for a
        /// cycle, plus a representative <see cref="IStepFunction"/> per
        /// id so that fairness predicates can be evaluated.
        /// </summary>
        internal sealed class Analysis
        {
            public HashSet<string> Enabled { get; }
            public HashSet<string> ContinuouslyEnabled { get; }
            public HashSet<string> Taken { get; }
            public Dictionary<string, IStepFunction> StepById { get; }

            public Analysis(
                HashSet<string> enabled,
                HashSet<string> continuouslyEnabled,
                HashSet<string> taken,
                Dictionary<string, IStepFunction> stepById)
            {
                Enabled = enabled;
                ContinuouslyEnabled = continuouslyEnabled;
                Taken = taken;
                StepById = stepById;
            }
        }

        /// <summary>
        /// Build the per-cycle <see cref="Analysis"/>.
        /// <paramref name="enabledAt"/> is called once per group;
        /// continuouslyEnabled is computed as the intersection of
        /// per-group enabled sets. <paramref name="taken"/> is the
        /// global enumeration of step functions actually fired by edges
        /// that stay inside the cycle — callers decide whether that's
        /// system-edge intra-SCC firing (system-SCC case) or product-
        /// edge intra-product-SCC firing (product-SCC case).
        /// </summary>
        internal static Analysis Compute<TGroup>(
            IEnumerable<TGroup> groups,
            Func<TGroup, IEnumerable<IStepFunction>> enabledAt,
            IEnumerable<IStepFunction> taken)
        {
            var stepById = new Dictionary<string, IStepFunction>();

            // Per-group enabled sets. Materialize so we can intersect.
            var perGroup = new List<HashSet<string>>();
            foreach (var g in groups)
            {
                var local = new HashSet<string>();
                foreach (var sf in enabledAt(g))
                {
                    if (sf == null) continue;
                    local.Add(sf.StepFunctionId);
                    if (!stepById.ContainsKey(sf.StepFunctionId))
                        stepById[sf.StepFunctionId] = sf;
                }
                perGroup.Add(local);
            }

            var enabled = new HashSet<string>();
            foreach (var s in perGroup) enabled.UnionWith(s);

            var continuouslyEnabled = new HashSet<string>();
            if (perGroup.Count > 0)
            {
                foreach (var id in enabled)
                {
                    bool atAll = true;
                    for (int i = 0; i < perGroup.Count; i++)
                    {
                        if (!perGroup[i].Contains(id)) { atAll = false; break; }
                    }
                    if (atAll) continuouslyEnabled.Add(id);
                }
            }

            var takenSet = new HashSet<string>();
            foreach (var sf in taken)
            {
                if (sf == null) continue;
                takenSet.Add(sf.StepFunctionId);
                if (!stepById.ContainsKey(sf.StepFunctionId))
                    stepById[sf.StepFunctionId] = sf;
            }

            return new Analysis(enabled, continuouslyEnabled, takenSet, stepById);
        }

        /// <summary>
        /// Apply <paramref name="fairness"/> to the analysis. Returns
        /// <c>true</c> iff the cycle is fair: every weakly-fair step
        /// that is continuously enabled is taken, and every strongly-
        /// fair step that is enabled at all is taken.
        /// </summary>
        internal static bool IsFair(Analysis a, Fairness fairness)
        {
            if (fairness == null) return true;
            foreach (var id in a.Enabled)
            {
                if (!a.StepById.TryGetValue(id, out var rep)) continue;

                if (fairness.WeakFairPredicate(rep) && a.ContinuouslyEnabled.Contains(id))
                {
                    if (!a.Taken.Contains(id)) return false;
                }
                if (fairness.StrongFairPredicate(rep))
                {
                    if (!a.Taken.Contains(id)) return false;
                }
            }
            return true;
        }
    }
}
