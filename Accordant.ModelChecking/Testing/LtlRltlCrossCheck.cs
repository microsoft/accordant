using System;
using Microsoft.Accordant.ModelChecking.Ltl;
using Microsoft.Accordant.ModelChecking.Rltl;

namespace Microsoft.Accordant.ModelChecking.Testing
{
    /// <summary>
    /// Result of running an <see cref="LtlCheck"/> and an <see cref="RltlCheck"/>
    /// on the same model under the same fairness and comparing their verdicts.
    /// </summary>
    public sealed class CrossCheckResult
    {
        public PropertyCheckingResult Ltl { get; }
        public PropertyCheckingResult Rltl { get; }
        public bool Agree => Ltl.Valid == Rltl.Valid;
        public string Label { get; }

        public CrossCheckResult(string label, PropertyCheckingResult ltl, PropertyCheckingResult rltl)
        {
            Label = label;
            Ltl = ltl;
            Rltl = rltl;
        }

        /// <summary>
        /// Throws <see cref="LtlRltlDisagreementException"/> if the two
        /// checkers disagree on validity.
        /// </summary>
        public CrossCheckResult ThrowIfDisagree()
        {
            if (!Agree)
                throw new LtlRltlDisagreementException(this);
            return this;
        }
    }

    /// <summary>
    /// Raised by <see cref="LtlRltlCrossCheck"/> when the LTL and RLTL
    /// checkers return different verdicts on the same property. The message
    /// embeds both traces to aid debugging.
    /// </summary>
    public sealed class LtlRltlDisagreementException : Exception
    {
        public CrossCheckResult Result { get; }

        public LtlRltlDisagreementException(CrossCheckResult result)
            : base(BuildMessage(result))
        {
            Result = result;
        }

        private static string BuildMessage(CrossCheckResult r)
        {
            var label = string.IsNullOrEmpty(r.Label) ? "(unlabeled)" : r.Label;
            return $"LTL/RLTL disagreement on '{label}': LTL.Valid={r.Ltl.Valid}, RLTL.Valid={r.Rltl.Valid}.\n" +
                   $"---- LTL trace ----\n{r.Ltl.GetTraceString()}\n" +
                   $"---- RLTL trace ----\n{r.Rltl.GetTraceString()}";
        }
    }

    /// <summary>
    /// Runs an LTL formula and its RLTL counterpart against the same state
    /// graph + fairness combination and reports whether they agree. Intended
    /// for sample test suites where a property is naturally expressible in
    /// both logics.
    ///
    /// <para>
    /// Use <see cref="LtlToRltl.Lift"/> for the common case of mechanically
    /// lifting an LTL formula into an equivalent RLTL formula, or supply
    /// both formulas directly to the <see cref="Run(StateGraphNode, LtlFormula, RltlFormula, Fairness, string)"/>
    /// overload.
    /// </para>
    /// </summary>
    public static class LtlRltlCrossCheck
    {
        /// <summary>
        /// Checks <paramref name="ltl"/> with <see cref="LtlCheck"/> and
        /// <paramref name="rltl"/> with <see cref="RltlCheck"/> on the
        /// same <paramref name="root"/> under <paramref name="fairness"/>.
        /// Returns a <see cref="CrossCheckResult"/> with both verdicts;
        /// does not throw on disagreement.
        /// </summary>
        public static CrossCheckResult Run(
            StateGraphNode root,
            LtlFormula ltl,
            RltlFormula rltl,
            Fairness fairness = null,
            string label = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (ltl == null) throw new ArgumentNullException(nameof(ltl));
            if (rltl == null) throw new ArgumentNullException(nameof(rltl));

            var fair = fairness ?? Fairness.None;
            var ltlResult = LtlCheck.Check(root, ltl, fair);
            var rltlResult = RltlCheck.Check(root, rltl, fairness: fair);
            return new CrossCheckResult(label, ltlResult, rltlResult);
        }

        /// <summary>
        /// Convenience overload: lifts <paramref name="ltl"/> to RLTL via
        /// <see cref="LtlToRltl.Lift"/> and runs the cross-check.
        /// </summary>
        public static CrossCheckResult Run(
            StateGraphNode root,
            LtlFormula ltl,
            Fairness fairness = null,
            string label = null)
        {
            var rltl = LtlToRltl.Lift(ltl);
            return Run(root, ltl, rltl, fairness, label);
        }
    }

    /// <summary>
    /// Mechanical translation from an LTL formula into the structurally
    /// equivalent RLTL formula. The translation is one-to-one on the LTL
    /// fragment (constants, atoms, Boolean ops, Next, Until, Release) and
    /// preserves derived combinators (◇, □, leads-to, …) because both DSLs
    /// expand them to the same Until/Release-based encoding.
    /// </summary>
    public static class LtlToRltl
    {
        public static RltlFormula Lift(LtlFormula phi)
        {
            switch (phi)
            {
                case LtlTrue _:    return RltlFormula.True;
                case LtlFalse _:   return RltlFormula.False;
                case LtlProp p:    return RltlFormula.Prop(p.Predicate, p.ToString());
                case LtlNot n:     return RltlFormula.Not(Lift(n.Inner));
                case LtlAnd a:
                {
                    RltlFormula acc = RltlFormula.True;
                    foreach (var c in a.Children) acc = RltlFormula.And(acc, Lift(c));
                    return acc;
                }
                case LtlOr o:
                {
                    RltlFormula acc = RltlFormula.False;
                    foreach (var c in o.Children) acc = RltlFormula.Or(acc, Lift(c));
                    return acc;
                }
                case LtlNext n:    return RltlFormula.Next(Lift(n.Inner));
                case LtlUntil u:   return RltlFormula.Until(Lift(u.Hold), Lift(u.Goal));
                case LtlRelease r: return RltlFormula.Release(Lift(r.Release_), Lift(r.Hold));
                default:
                    throw new NotSupportedException(
                        $"LtlToRltl.Lift: unsupported LTL node {phi.GetType().Name}");
            }
        }
    }
}
