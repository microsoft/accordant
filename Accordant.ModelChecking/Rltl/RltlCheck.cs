namespace Microsoft.Accordant.ModelChecking.Rltl
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// Top-level entry point for RLTL model checking. Thin wrapper that
    /// unwraps <see cref="RltlFormula"/> and delegates to
    /// <see cref="SymbolicRltlCheck"/>.
    /// </summary>
    public static class RltlCheck
    {
        /// <summary>
        /// Check an <see cref="RltlFormula"/> against a model program's
        /// state graph.
        /// </summary>
        /// <param name="root">Root of the state graph (model program).</param>
        /// <param name="formula">The RLTL property that should hold.</param>
        /// <param name="maxDepth">Maximum exploration depth (0 = unlimited).</param>
        /// <param name="fairness">Optional fairness constraint. Defaults to
        ///   <see cref="Fairness.None"/>. Use <see cref="Fairness.WeakFairAll"/>
        ///   for parity with the legacy <c>LtlCheck</c> default semantics.</param>
        public static PropertyCheckingResult Check(
            StateGraphNode root,
            RltlFormula formula,
            int maxDepth = 0,
            Fairness fairness = null)
        {
            if (formula == null) throw new ArgumentNullException(nameof(formula));
            return SymbolicRltlCheck.Check(root, formula.Core, maxDepth, fairness);
        }
    }
}
