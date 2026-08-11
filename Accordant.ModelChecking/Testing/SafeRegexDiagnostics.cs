namespace Microsoft.Accordant.ModelChecking.Testing
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// Diagnostic access to the compiled form of a <see cref="SafeRegex"/>.
    ///
    /// <para>A <see cref="SafeRegex"/> denotes a language over <em>changing</em>
    /// steps. Before use it is compiled to the inverse image of the erasure
    /// homomorphism that deletes unchanged steps. This helper exposes that
    /// compiled extended regular expression so differential tests and oracles
    /// can compare the compiled language with the intended visible language.
    /// It is not needed to write properties.</para>
    /// </summary>
    public static class SafeRegexDiagnostics
    {
        /// <summary>
        /// The compiled expression <c>h⁻¹(L(pattern))</c> over full transition
        /// letters, where <c>h</c> erases unchanged steps.
        /// </summary>
        public static Ere<IStatePredicate> Compile(SafeRegex pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            return pattern.Lower();
        }

        /// <summary>
        /// The single-letter predicate that classifies a transition as
        /// <em>unchanged</em> — the canonical test shared by the stutter-safe
        /// temporal operators, fairness, <c>ENABLED</c>, and
        /// <see cref="SafeRegex"/>.
        /// </summary>
        public static IStatePredicate UnchangedStepPredicate
            => StutterAlphabet.UnchangedPredicate;
    }
}
