namespace Microsoft.Accordant.ModelChecking.Bdd
{
    using System.Runtime.CompilerServices;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// Entry point for the BDD backend. Referencing this assembly is
    /// sufficient to register <see cref="BddStatePropEba"/> as the
    /// default propositional EBA used by the symbolic LTL/RLTL pipelines
    /// — a module initializer fires on first load and calls
    /// <see cref="StatePropEbaProvider.SetDefault"/>.
    ///
    /// <para>Callers that want to opt back out can call
    /// <see cref="StatePropEbaProvider.ResetToFallback"/> at any time.</para>
    /// </summary>
    public static class BddBackend
    {
        /// <summary>
        /// Explicit registration entry point. Idempotent. Equivalent to
        /// the module initializer; provided for tests and consumers who
        /// prefer an explicit opt-in.
        /// </summary>
        public static void RegisterAsDefault()
            => StatePropEbaProvider.SetDefault(BddStatePropEba.Instance);

        [ModuleInitializer]
        internal static void AutoRegister()
            => RegisterAsDefault();
    }
}
