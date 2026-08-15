namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// Outcome of a temporal property check.
    /// </summary>
    public enum PropertyCheckingStatus
    {
        /// <summary>The explored behavior graph satisfies the property.</summary>
        Holds,

        /// <summary>A definitive counterexample was found.</summary>
        Violated,

        /// <summary>
        /// No definitive counterexample was found, but exploration reached a
        /// depth frontier whose unknown continuation could affect the result.
        /// </summary>
        InconclusiveBound
    }

    /// <summary>
    /// The result of checking a property over a state graph.
    /// </summary>
    public sealed class PropertyCheckingResult
    {
        /// <summary>
        /// Creates a successful result (property holds).
        /// </summary>
        public static PropertyCheckingResult Success() =>
            new PropertyCheckingResult(PropertyCheckingStatus.Holds);

        /// <summary>
        /// Creates a failure result with a counterexample trace.
        /// </summary>
        public static PropertyCheckingResult Failure(List<TraceItem> trace, StronglyConnectedComponent badCycle = null)
        {
            return new PropertyCheckingResult(PropertyCheckingStatus.Violated)
            {
                Trace = trace,
                BadCycle = badCycle
            };
        }

        /// <summary>
        /// Creates a result whose verdict is unknown because exploration
        /// reached a depth frontier.
        /// </summary>
        public static PropertyCheckingResult InconclusiveBound() =>
            new PropertyCheckingResult(PropertyCheckingStatus.InconclusiveBound);

        private PropertyCheckingResult(PropertyCheckingStatus status)
        {
            Status = status;
        }

        /// <summary>
        /// The definitive or bounded-inconclusive outcome of the check.
        /// </summary>
        public PropertyCheckingStatus Status { get; }

        /// <summary>
        /// Indicates whether the property holds when the result is conclusive;
        /// otherwise <c>null</c>.
        /// </summary>
        public bool? Valid =>
            Status == PropertyCheckingStatus.Holds ? true :
            Status == PropertyCheckingStatus.Violated ? false :
            (bool?)null;

        /// <summary>
        /// Optional human-readable name of the checked formula.
        /// </summary>
        public string PropertyName { get; private set; }

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
            if (Status == PropertyCheckingStatus.InconclusiveBound)
            {
                return PropertyName == null
                    ? "Property result is inconclusive because exploration reached a depth bound."
                    : $"Property '{PropertyName}' is inconclusive because exploration reached a depth bound.";
            }

            if (Status == PropertyCheckingStatus.Holds)
            {
                return PropertyName == null
                    ? "Property holds - no counterexample."
                    : $"Property '{PropertyName}' holds - no counterexample.";
            }

            if (Trace == null)
            {
                return PropertyName == null
                    ? "Property does not hold - no counterexample trace is available."
                    : $"Property '{PropertyName}' does not hold - no counterexample trace is available.";
            }

            var sb = new StringBuilder();
            if (PropertyName == null)
            {
                sb.AppendLine("Counterexample trace:");
            }
            else
            {
                sb.AppendLine($"Counterexample for property '{PropertyName}':");
            }

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
                    sb.AppendLine("Consider adding fairness constraints: fair: Fairness.Weak(...)");
                }
            }

            return sb.ToString();
        }

        internal PropertyCheckingResult WithPropertyName(string propertyName)
        {
            PropertyName = propertyName;
            return this;
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

            IEnumerable<FairnessEdge> EnabledAt(StateGraphNode n)
                => n.Edges.Select(e =>
                    new FairnessEdge(n, e.StepFunction, e.Metadata, e.Target));

            IEnumerable<FairnessEdge> Taken()
            {
                foreach (var n in scc.Nodes)
                    foreach (var e in n.Edges)
                        if (nodesInSCC.Contains(e.Target.GetNodeFingerprint()))
                            yield return new FairnessEdge(
                                n, e.StepFunction, e.Metadata, e.Target);
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
