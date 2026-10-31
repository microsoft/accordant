namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// The result of checking a property over a state graph.
    /// </summary>
    public class PropertyCheckingResult
    {
        /// <summary>
        /// Creates a successful result (property holds).
        /// </summary>
        public static PropertyCheckingResult Success() => new PropertyCheckingResult { Valid = true };

        /// <summary>
        /// Creates a failure result with a counterexample trace.
        /// </summary>
        public static PropertyCheckingResult Failure(List<TraceItem> trace, StronglyConnectedComponent badCycle = null)
        {
            return new PropertyCheckingResult
            {
                Valid = false,
                Trace = trace,
                BadCycle = badCycle
            };
        }

        /// <summary>
        /// Indicates whether the property holds.
        /// </summary>
        public bool Valid { get; private set; }

        /// <summary>
        /// The counterexample trace if the property doesn't hold.
        /// For liveness properties, this is the path to the bad cycle.
        /// </summary>
        public List<TraceItem> Trace { get; private set; }

        /// <summary>
        /// For liveness failures, the bad cycle (SCC) where the property is violated.
        /// </summary>
        public StronglyConnectedComponent BadCycle { get; private set; }

        /// <summary>
        /// Returns a human-readable representation of the counterexample.
        /// </summary>
        public string GetTraceString()
        {
            if (Valid || Trace == null)
            {
                return "Property holds - no counterexample.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Counterexample trace:");

            bool inCycleSection = false;
            foreach (var item in Trace)
            {
                if (item.IsInCycle && !inCycleSection)
                {
                    sb.AppendLine("--- Cycle begins ---");
                    inCycleSection = true;
                }

                var action = item.StepFunction == null ? "Start" : FormatStep(item.StepFunction);
                sb.AppendLine($"  --{action}--> {item.StateGraphNode.State}{FormatValuation(item.Valuation)}");
            }

            if (inCycleSection)
            {
                sb.AppendLine("--- Cycle repeats ---");
            }

            if (BadCycle != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Bad cycle contains {BadCycle.Nodes.Count} state(s).");
                
                // Show which step functions were enabled but not taken (fairness hint)
                var enabledNotTaken = GetEnabledButNotTakenSteps(BadCycle);
                if (enabledNotTaken.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("Hint: The following actions were enabled but never taken in this cycle:");
                    foreach (var sfId in enabledNotTaken)
                    {
                        sb.AppendLine($"  - {sfId}");
                    }
                    sb.AppendLine("Consider adding fairness constraints: fair: Fairness.WeakFair(...)");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Renders a step function for trace display. Returns the
        /// <see cref="IStepFunction.StepFunctionId"/> when non-empty
        /// (sample step classes typically use the type name or a
        /// disambiguated variant like <c>"PassToken_1"</c>) and falls
        /// back to the runtime type name otherwise.
        /// </summary>
        internal static string FormatStep(IStepFunction sf)
        {
            var id = sf.StepFunctionId;
            if (!string.IsNullOrEmpty(id))
                return id;
            return sf.GetType().Name;
        }

        /// <summary>
        /// Renders a concrete predicate valuation as
        /// <c> [p=true, q=false]</c>. Returns the empty string when no
        /// valuation is attached (explicit-checker traces, or traces over
        /// an NBW with an empty condition registry).
        /// </summary>
        internal static string FormatValuation(IReadOnlyDictionary<IStatePredicate, bool> valuation)
        {
            if (valuation == null || valuation.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            sb.Append("  [");
            bool first = true;
            foreach (var kv in valuation.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal))
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(kv.Key);
                sb.Append('=');
                sb.Append(kv.Value ? "true" : "false");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static List<string> GetEnabledButNotTakenSteps(StronglyConnectedComponent scc)
        {
            // Delegates to the shared CycleFairness helper so the
            // diagnostic stays consistent with the fairness decision
            // procedure. The hint returns human-readable step labels
            // (via FormatStep) rather than raw ids; the id-keyed
            // Enabled / Taken sets are translated through StepById
            // at the end.
            var nodesInSCC = new HashSet<string>(scc.Nodes.Select(n => n.GetNodeFingerprint()));

            IEnumerable<IStepFunction> EnabledAt(StateGraphNode n)
                => n.Edges.Select(e => e.StepFunction);

            IEnumerable<IStepFunction> Taken()
            {
                foreach (var n in scc.Nodes)
                    foreach (var e in n.Edges)
                        if (nodesInSCC.Contains(e.Target.GetNodeFingerprint()))
                            yield return e.StepFunction;
            }

            var analysis = CycleFairness.Compute(scc.Nodes, EnabledAt, Taken());

            var labels = new HashSet<string>();
            foreach (var id in analysis.Enabled)
            {
                if (analysis.Taken.Contains(id)) continue;
                if (analysis.StepById.TryGetValue(id, out var sf))
                    labels.Add(FormatStep(sf));
            }
            return labels.OrderBy(x => x).ToList();
        }
    }
}
