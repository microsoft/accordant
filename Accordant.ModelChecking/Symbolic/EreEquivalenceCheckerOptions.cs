namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// Tunables for <see cref="EreEquivalenceChecker{TPred,TElem}"/>.
    /// <para>
    /// Currently exposes a single switch for the EREQ <em>E-frame UF merge
    /// rule</em> (paper-novel optimization, see plan §"EREQ Phase 6"). The
    /// rule is sound for all inputs; the opt-out exists for differential and
    /// performance comparison.
    /// </para>
    /// </summary>
    public sealed class EreEquivalenceCheckerOptions
    {
        /// <summary>
        /// Default options (all optimizations enabled).
        /// </summary>
        public static EreEquivalenceCheckerOptions Default { get; }
            = new EreEquivalenceCheckerOptions();

        /// <summary>
        /// Enable the EREQ E-frame UF merge rule: when a bisim residual leaf
        /// is XOR-shaped and every operand has the form
        /// <c>∃p.body_i</c> with the same projector <c>p</c>, and every
        /// <c>body_i</c> is already in the same union-find class under the
        /// current candidate relation, discharge the leaf without expanding
        /// its derivative. Sound by monotonicity of <c>∃p</c>:
        /// <c>L(R) = L(S)  ⇒  L(∃p.R) = L(∃p.S)</c>.
        /// <para>Default: <c>true</c>.</para>
        /// <para>
        /// <b>Dormancy note (2026-05-29).</b> Empirically the trigger
        /// pattern almost never arises when checking inputs built via the
        /// public <see cref="Ere{TPred}"/> smart constructors: the
        /// <see cref="Ere{TPred}.Exists(int, Ere{TPred})"/> factory
        /// distributes <c>∃p</c> over Union, Concat, Star, Fusion,
        /// Intersect (partial extract) and Xor, so the <c>∃p</c>
        /// wrapper has typically already been pushed away from the leaf
        /// shape the rule looks for. The
        /// <see cref="EreEquivalenceChecker{TPred,TElem}.EFrameMergeFires"/>
        /// counter is therefore expected to read <c>0</c> on natural
        /// corpora (verified in <c>EreqEFrameBenchmarkTests</c>); the
        /// rule remains in place because it is sound, very low-cost when
        /// it never fires, and provably effective when callers seed the
        /// UF state deliberately via
        /// <see cref="EreEquivalenceChecker{TPred,TElem}.TryEFrameDischarge"/>
        /// or when a future non-canonicalising AST path produces the
        /// trigger shape directly.
        /// </para>
        /// </summary>
        public bool UseEFrameMerge { get; set; } = true;
    }
}
