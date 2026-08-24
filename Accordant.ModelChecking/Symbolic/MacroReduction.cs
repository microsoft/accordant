namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of a macrostate reduction: the reduced macrostate together
    /// with a representative map that tells callers (notably
    /// <see cref="IncrementalAE{TPredicate, TElement, TState}"/>) how to
    /// rewrite an obligation set <c>O ⊆ S</c> into the reduced
    /// <c>O' ⊆ S'</c>.
    /// </summary>
    /// <typeparam name="TState">ABW state type.</typeparam>
    public readonly struct MacroReduction<TState>
    {
        /// <summary>The reduced macrostate.</summary>
        public readonly StateSet<TState> ReducedS;

        /// <summary>
        /// Maps each dropped state to its surviving representative in
        /// <see cref="ReducedS"/>. Survivor states are typically NOT
        /// listed in this map (treated as identity); the
        /// <see cref="RepOf"/> helper handles both cases uniformly.
        /// May be <c>null</c> when no state was dropped.
        /// </summary>
        public readonly IReadOnlyDictionary<TState, TState> RepMap;

        public MacroReduction(StateSet<TState> reducedS,
                              IReadOnlyDictionary<TState, TState> repMap = null)
        {
            ReducedS = reducedS;
            RepMap = repMap;
        }

        /// <summary>Returns the representative of <paramref name="q"/>
        /// in <see cref="ReducedS"/>, or <paramref name="q"/> itself
        /// when it survived.</summary>
        public TState RepOf(TState q)
            => RepMap != null && RepMap.TryGetValue(q, out var r) ? r : q;

        /// <summary>Identity reduction (no states dropped).</summary>
        public static MacroReduction<TState> Identity(StateSet<TState> s)
            => new MacroReduction<TState>(s, null);
    }
}
