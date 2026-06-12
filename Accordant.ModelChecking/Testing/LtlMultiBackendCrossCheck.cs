namespace Microsoft.Accordant.ModelChecking.Testing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// One backend's verdict in a multi-backend differential check.
    /// </summary>
    public sealed class BackendVerdict
    {
        public string BackendName { get; }
        public PropertyCheckingResult Result { get; }
        public bool Skipped { get; }
        public string SkipReason { get; }

        public BackendVerdict(string name, PropertyCheckingResult result)
        {
            BackendName = name;
            Result = result;
        }

        public BackendVerdict(string name, string skipReason)
        {
            BackendName = name;
            Skipped = true;
            SkipReason = skipReason;
        }
    }

    /// <summary>
    /// Aggregate result of running the same LTL property through every
    /// available model-checking backend.
    /// </summary>
    public sealed class MultiBackendCrossCheckResult
    {
        public string Label { get; }
        public IReadOnlyList<BackendVerdict> Verdicts { get; }

        public MultiBackendCrossCheckResult(string label, IReadOnlyList<BackendVerdict> verdicts)
        {
            Label = label;
            Verdicts = verdicts;
        }

        /// <summary>
        /// True iff every non-skipped backend returned the same
        /// <see cref="PropertyCheckingResult.Valid"/>.
        /// </summary>
        public bool Unanimous
        {
            get
            {
                var active = Verdicts.Where(v => !v.Skipped).ToList();
                if (active.Count <= 1) return true;
                var first = active[0].Result.Valid;
                return active.All(v => v.Result.Valid == first);
            }
        }

        /// <summary>
        /// Throws <see cref="MultiBackendDisagreementException"/> when
        /// any pair of active backends disagrees on validity.
        /// </summary>
        public MultiBackendCrossCheckResult ThrowIfDisagree()
        {
            if (!Unanimous) throw new MultiBackendDisagreementException(this);
            return this;
        }
    }

    /// <summary>
    /// Raised when the multi-backend oracle detects a disagreement.
    /// The message lists every backend's verdict and trace so that the
    /// failing run is self-contained for triage.
    /// </summary>
    public sealed class MultiBackendDisagreementException : Exception
    {
        public MultiBackendCrossCheckResult Result { get; }

        public MultiBackendDisagreementException(MultiBackendCrossCheckResult result)
            : base(BuildMessage(result))
        {
            Result = result;
        }

        private static string BuildMessage(MultiBackendCrossCheckResult r)
        {
            var sb = new StringBuilder();
            var label = string.IsNullOrEmpty(r.Label) ? "(unlabeled)" : r.Label;
            sb.AppendLine($"Multi-backend disagreement on '{label}':");
            foreach (var v in r.Verdicts)
            {
                if (v.Skipped)
                    sb.AppendLine($"  {v.BackendName}: SKIPPED ({v.SkipReason})");
                else
                    sb.AppendLine($"  {v.BackendName}: Valid={v.Result.Valid}");
            }
            foreach (var v in r.Verdicts.Where(x => !x.Skipped))
            {
                sb.AppendLine();
                sb.AppendLine($"---- {v.BackendName} trace ----");
                sb.AppendLine(v.Result.GetTraceString());
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Differential model-checking oracle: runs the same LTL property
    /// through every available backend
    /// (<see cref="LtlCheck"/>,
    /// <see cref="SymbolicLtlCheck.Check"/>,
    /// <see cref="SymbolicLtlCheck.CheckNDFS"/>,
    /// <see cref="RltlCheck"/> on the lifted formula) and asserts they
    /// all return the same verdict.
    ///
    /// <para>
    /// Disagreement is by definition a bug in at least one backend. The
    /// oracle is the highest-leverage tool for surfacing such bugs
    /// because the four backends share almost no implementation: the
    /// explicit <see cref="LtlCheck"/> uses on-the-fly product+SCC over
    /// the legacy <see cref="LtlFormula"/>, symbolic-LTL goes
    /// ABW→NBW→product+SCC, NDFS uses nested-DFS on the same NBW, and
    /// RLTL goes through the regex/derivative pipeline via
    /// <see cref="LtlToRltl.Lift"/>.
    /// </para>
    ///
    /// <para>
    /// Backends that don't accept a fairness parameter
    /// (<see cref="SymbolicLtlCheck.CheckNDFS"/>) are skipped — and
    /// recorded as such — whenever a non-trivial fairness is supplied.
    /// Skipped backends do not contribute to the unanimity test.
    /// </para>
    /// </summary>
    public static class LtlMultiBackendCrossCheck
    {
        /// <summary>
        /// Runs every backend and returns their verdicts. Does not
        /// throw; use <see cref="MultiBackendCrossCheckResult.ThrowIfDisagree"/>
        /// or test the <see cref="MultiBackendCrossCheckResult.Unanimous"/>
        /// property directly.
        /// </summary>
        public static MultiBackendCrossCheckResult Run(
            StateGraphNode root,
            LtlFormula ltl,
            Fairness fairness = null,
            string label = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (ltl == null) throw new ArgumentNullException(nameof(ltl));

            var fair = fairness ?? Fairness.None;
            bool fairnessIsTrivial = ReferenceEquals(fair, Fairness.None);

            var verdicts = new List<BackendVerdict>();

            // 1) Explicit LtlCheck on the legacy LtlFormula DSL.
            verdicts.Add(new BackendVerdict(
                "LtlCheck",
                LtlCheck.Check(root, ltl, fair)));

            // 2) Symbolic LTL (Tarjan product+SCC) on the symbolic
            //    Ltl<IStatePredicate> conversion.
            //    Each conversion creates fresh StateProp atoms; that's
            //    intentional — each backend gets an independent atom
            //    namespace and cannot accidentally share state.
            var symLtl1 = LtlFormulaToSymbolic.Convert(ltl);
            verdicts.Add(new BackendVerdict(
                "SymbolicLtlCheck.Check",
                SymbolicLtlCheck.Check(root, symLtl1, maxDepth: 0, fairness: fair)));

            // 3) Symbolic LTL via Nested DFS. Does not accept fairness;
            //    only meaningful when fairness is trivial.
            if (fairnessIsTrivial)
            {
                var symLtl2 = LtlFormulaToSymbolic.Convert(ltl);
                verdicts.Add(new BackendVerdict(
                    "SymbolicLtlCheck.CheckNDFS",
                    SymbolicLtlCheck.CheckNDFS(root, symLtl2, maxDepth: 0)));
            }
            else
            {
                verdicts.Add(new BackendVerdict(
                    "SymbolicLtlCheck.CheckNDFS",
                    "fairness not supported by NDFS"));
            }

            // 4) RLTL via the mechanical Lift, exercising the regex
            //    derivative pipeline.
            var rltl = LtlToRltl.Lift(ltl);
            verdicts.Add(new BackendVerdict(
                "RltlCheck",
                RltlCheck.Check(root, rltl, maxDepth: 0, fairness: fair)));

            return new MultiBackendCrossCheckResult(label, verdicts);
        }
    }

    /// <summary>
    /// Mechanical conversion from the legacy <see cref="LtlFormula"/>
    /// DSL into the symbolic <see cref="Ltl{TPred}"/> representation
    /// over <see cref="IStatePredicate"/>. Mirrors
    /// <see cref="LtlToRltl.Lift"/> but targets the symbolic LTL tree
    /// directly so that the symbolic-LTL backends can be exercised
    /// with the same author-facing formula authors write today.
    /// </summary>
    public static class LtlFormulaToSymbolic
    {
        public static Ltl<IStatePredicate> Convert(LtlFormula phi)
        {
            var alg = LtlAlgebra.Default;
            switch (phi)
            {
                case LtlTrue _:    return alg.True;
                case LtlFalse _:   return alg.False;
                case LtlProp p:    return alg.Atom(
                    new StatePredAtom(new StateProp(p.ToString(), p.Predicate)));
                case LtlNot n:     return alg.Not(Convert(n.Inner));
                case LtlAnd a:
                {
                    Ltl<IStatePredicate> acc = alg.True;
                    foreach (var c in a.Children) acc = alg.And(acc, Convert(c));
                    return acc;
                }
                case LtlOr o:
                {
                    Ltl<IStatePredicate> acc = alg.False;
                    foreach (var c in o.Children) acc = alg.Or(acc, Convert(c));
                    return acc;
                }
                case LtlNext n:    return alg.Next(Convert(n.Inner));
                case LtlUntil u:   return alg.Until(Convert(u.Hold), Convert(u.Goal));
                case LtlRelease r: return alg.Release(Convert(r.Release_), Convert(r.Hold));
                default:
                    throw new NotSupportedException(
                        $"LtlFormulaToSymbolic.Convert: unsupported LTL node {phi.GetType().Name}");
            }
        }
    }
}
