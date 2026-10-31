namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// Syntactic classifier that assigns each RLTL formula a binary
    /// "colour" matching its membership in the ABW's co-Büchi rejecting
    /// set <c>F</c> (see <see cref="RltlDerivative{TPred, TElem}.IsAccepting"/>):
    ///
    /// <list type="bullet">
    ///   <item><b>Guarantee</b> (= rejecting, ∈ F): the formula's head
    ///         imposes an unfulfilled eventuality obligation that the
    ///         breakpoint construction must discharge. Currently:
    ///         <c>U</c> (Until), <c>SeqPrefix</c>, <c>OvlPrefix</c>.</item>
    ///   <item><b>Safety</b> (= accepting, ∉ F): every other formula head
    ///         (atoms, <c>X</c>, <c>R</c>/<c>G</c>, And, Or,
    ///         WeakClosure, NegWeakClosure).</item>
    /// </list>
    ///
    /// <para>
    /// This is the colour used by
    /// <see cref="RltlMacrostateTransitionMerge{TPred,TElem}"/> as the
    /// weak-equivalence guard: states are bucketed by colour so that
    /// universal copies are only merged when they share F-membership.
    /// </para>
    ///
    /// <para>
    /// Pure-syntactic; constant-time per call. Mirrors
    /// <c>RltlDerivative.IsAccepting</c> by head pattern only, except
    /// for <c>WeakClosure</c> and <c>NegWeakClosure</c> which
    /// <see cref="IsRejecting"/> conservatively returns <c>false</c>
    /// (= accepting / safety) to avoid a regex-emptiness call. This
    /// over-classifies them as safety; the soundness consequence is at
    /// worst that fewer drops occur — never an unsound drop.
    /// </para>
    /// </summary>
    public static class RltlColour
    {
        /// <summary>
        /// True iff <paramref name="f"/> is in the ABW's co-Büchi
        /// rejecting set (an unfulfilled eventuality at the head).
        /// </summary>
        public static bool IsRejecting<TPred>(Rltl<TPred> f)
        {
            switch (f)
            {
                case RltlUntil<TPred> _:
                case RltlSeqPrefix<TPred> _:
                case RltlOvlPrefix<TPred> _:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True iff <paramref name="p"/> and <paramref name="q"/> share
        /// the same colour and may therefore participate in a
        /// macrostate-subsumption drop.
        /// </summary>
        public static bool SameColour<TPred>(Rltl<TPred> p, Rltl<TPred> q)
            => IsRejecting(p) == IsRejecting(q);
    }
}
