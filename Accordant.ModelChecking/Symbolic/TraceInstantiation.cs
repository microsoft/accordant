namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System.Collections.Generic;

    /// <summary>
    /// Post-processes a counterexample trace produced by the symbolic LTL/RLTL
    /// backends so that each <see cref="TraceItem"/> carries a concrete
    /// valuation of every <see cref="IStatePredicate"/> registered with the
    /// NBW's <see cref="ConditionRegistry{TPredicate}"/>.
    ///
    /// <para>The valuation is computed by re-evaluating each registered
    /// predicate against the trace item's concrete <see cref="State"/>. Because
    /// every transition in the symbolic NBW only branches on registered
    /// predicates, the valuations attached here are exactly the assignments
    /// that selected the leaves taken along the lasso — i.e. a concrete
    /// witness path through the property's symbolic transition relation.</para>
    ///
    /// <para>The original step-function and cycle-flag annotations are
    /// preserved verbatim; only the predicate valuation field is filled in.
    /// Compound derived predicates (<see cref="StatePredAnd"/>,
    /// <see cref="StatePredOr"/>, <see cref="StatePredNot"/>) introduced by
    /// the RLTL → NBW translation are evaluated and emitted alongside the
    /// underlying atoms; this keeps the witness self-contained for downstream
    /// pretty-printing without forcing the consumer to recompute conjunctions
    /// of atoms.</para>
    /// </summary>
    internal static class TraceInstantiation
    {
        /// <summary>
        /// Returns a new list of trace items, structurally identical to
        /// <paramref name="trace"/> but with each item augmented by a
        /// concrete <see cref="TraceItem.Valuation"/> derived from
        /// <paramref name="registry"/>.
        /// </summary>
        public static List<TraceItem> AttachValuations(
            List<TraceItem> trace,
            ConditionRegistry<IStatePredicate> registry)
        {
            if (trace == null) return null;
            if (registry == null || registry.Count == 0) return trace;

            var preds = registry.Predicates;
            var result = new List<TraceItem>(trace.Count);
            foreach (var item in trace)
            {
                var state = item.StateGraphNode?.State;
                IReadOnlyDictionary<IStatePredicate, bool> valuation;
                if (state == null)
                {
                    valuation = null;
                }
                else
                {
                    var map = new Dictionary<IStatePredicate, bool>(preds.Count);
                    foreach (var p in preds)
                    {
                        // Skip the trivial constants; they don't carry useful
                        // information for a counterexample reader.
                        if (p is StatePredTrue || p is StatePredFalse) continue;
                        map[p] = p.Eval(state);
                    }
                    valuation = map;
                }

                result.Add(new TraceItem(
                    item.StepFunction,
                    item.StateGraphNode,
                    item.IsInCycle,
                    valuation));
            }
            return result;
        }
    }
}
