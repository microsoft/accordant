namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// Extension methods for model checking temporal properties against
    /// explored state graphs.
    /// </summary>
    public static class ModelCheckExtensions
    {
        /// <summary>
        /// Check a temporal formula (RLTL property) against an explored
        /// state graph. Returns a <see cref="PropertyCheckingResult"/>
        /// indicating whether the property holds and, if not, a
        /// counterexample trace.
        /// </summary>
        /// <param name="root">Root of the explored state graph.</param>
        /// <param name="formula">The temporal property that should hold.</param>
        /// <param name="maxDepth">Maximum exploration depth (0 = unlimited).</param>
        /// <param name="fairness">Optional fairness constraint. Defaults to
        ///   <see cref="Fairness.None"/>. Use <see cref="Fairness.WeakFairAll"/>
        ///   for liveness properties that require weak fairness.</param>
        /// <returns>A result indicating validity, with a counterexample trace
        /// on failure.</returns>
        public static PropertyCheckingResult Check(
            this StateGraphNode root,
            TemporalFormula formula,
            int maxDepth = 0,
            Fairness fairness = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (formula == null) throw new ArgumentNullException(nameof(formula));
            return SymbolicRltlCheck.Check(root, formula.Core, maxDepth, fairness);
        }
    }
}
