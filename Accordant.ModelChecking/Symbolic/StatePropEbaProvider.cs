namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Threading;

    /// <summary>
    /// Process-wide registration seam for the default
    /// <see cref="IEffectiveBooleanAlgebra{IStatePredicate, State}"/>
    /// used by the symbolic LTL/RLTL pipelines.
    ///
    /// <para>
    /// The fallback default is the brute-force-propositional
    /// <see cref="StatePropEba.Instance"/>. More capable backends (for
    /// example the BDD-based one in <c>BraggerSpecs.ModelChecking.Bdd</c>,
    /// or the SMT-based one in <c>BraggerSpecs.ModelChecking.Z3</c>) can
    /// self-register at assembly load via a module initializer by calling
    /// <see cref="SetDefault"/>.
    /// </para>
    ///
    /// <para>
    /// The provider exposes the algebra through both the basic
    /// <see cref="IEffectiveBooleanAlgebra{IStatePredicate, State}"/>
    /// interface and the structural
    /// <see cref="IPredicateAlgebra{IStatePredicate}"/> interface — call
    /// sites in this assembly use the same instance under both views, so
    /// upgrading the backend upgrades both at once.
    /// </para>
    /// </summary>
    public static class StatePropEbaProvider
    {
        private static IEffectiveBooleanAlgebra<IStatePredicate, State> _default
            = StatePropEba.Instance;

        /// <summary>
        /// The currently registered default EBA over
        /// <see cref="IStatePredicate"/>.
        /// </summary>
        public static IEffectiveBooleanAlgebra<IStatePredicate, State> Default
            => Volatile.Read(ref _default);

        /// <summary>
        /// Registers <paramref name="eba"/> as the process-wide default.
        /// Intended to be called once, early (e.g. from a module
        /// initializer in an opt-in backend assembly). Later registrations
        /// silently win.
        /// </summary>
        public static void SetDefault(IEffectiveBooleanAlgebra<IStatePredicate, State> eba)
        {
            if (eba == null) throw new ArgumentNullException(nameof(eba));
            Volatile.Write(ref _default, eba);
        }

        /// <summary>
        /// Restores the fallback <see cref="StatePropEba.Instance"/> as the
        /// default. Primarily intended for tests that want to pin the toy
        /// backend regardless of what other assemblies have registered.
        /// </summary>
        public static void ResetToFallback()
            => Volatile.Write(ref _default, StatePropEba.Instance);
    }
}
