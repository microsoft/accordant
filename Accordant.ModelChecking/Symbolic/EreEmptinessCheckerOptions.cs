namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// Tunables for <see cref="EreEmptinessChecker{TPred,TElem}"/>.
    /// <para>
    /// Currently exposes a single switch for <em>DNF leaf splitting</em>
    /// during BFS/state-graph exploration (paper-novel optimization, see
    /// plan §"Phase 7"). The strategy is sound for all inputs; the
    /// opt-out exists for differential and performance comparison and to
    /// recover the legacy single-state-per-leaf behaviour.
    /// </para>
    /// </summary>
    public sealed class EreEmptinessCheckerOptions
    {
        /// <summary>
        /// Default options (all optimizations enabled).
        /// </summary>
        public static EreEmptinessCheckerOptions Default { get; }
            = new EreEmptinessCheckerOptions();

        /// <summary>
        /// When <c>true</c>, every transition-term leaf whose residual is a
        /// top-level <see cref="EreUnion{TPred}"/> is split into one
        /// successor per disjunct in the BFS frontier, rather than carried
        /// forward as a single union-state. This converts a latent
        /// subset-construction blow-up (e.g. <c>Σ*·a·Σ^n</c> — the
        /// "distance_n" example) into a polynomial state-count graph: by
        /// hash-cons identity each disjunct that recurs as the same regex
        /// is dedup'd through the BFS <c>parent</c> map, so genuinely
        /// equivalent successor states are visited at most once.
        /// <para>Sound by <c>L(R1 ∪ R2) = L(R1) ∪ L(R2)</c>: emptiness of
        /// the union equals emptiness of every disjunct, so splitting
        /// into separate frontier nodes is a complete refactor of the
        /// exploration. Witness reconstruction is preserved because each
        /// successor edge keeps the same predecessor + guard.</para>
        /// <para>Default: <c>true</c>.</para>
        /// </summary>
        public bool SplitDnfLeaves { get; set; } = true;
    }
}
