using Microsoft.Accordant;

namespace Microsoft.Accordant.ModelChecking.Rltl
{
    using System;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// User-facing RLTL (Regular LTL) formula over model-program states. A
    /// thin facade over <see cref="Rltl{TPred}"/> with
    /// <see cref="IStatePredicate"/> as the predicate type.
    ///
    /// RLTL extends LTL with four regex-prefix operators (Section 7 of the
    /// POPL'25 paper):
    /// <list type="bullet">
    ///   <item><see cref="SeqPrefix"/>: <c>R ; φ</c> — ∃k≥0. w[0..k] ∈ L(R) ∧ w[k..] ⊨ φ</item>
    ///   <item><see cref="OvlPrefix"/>: <c>R : φ</c> — overlapping variant: w[0..k+1] ∈ L(R) ∧ w[k..] ⊨ φ</item>
    ///   <item><see cref="Trigger"/>:   <c>R ⊳ φ</c> — ∀k≥0. w[0..k] ∈ L(R) → w[k..] ⊨ φ</item>
    ///   <item><see cref="Match"/>:     <c>R ⊳⊳ φ</c> — overlapping universal variant</item>
    /// </list>
    /// All pure-LTL operators are also available with the standard semantics
    /// (<see cref="Always"/>, <see cref="Eventually"/>, <see cref="Until"/>, …).
    /// </summary>
    public sealed class RltlFormula
    {
        internal Rltl<IStatePredicate> Core { get; }

        internal RltlFormula(Rltl<IStatePredicate> core)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
        }

        #region Constants and atoms

        /// <summary>True constant — satisfied by every word.</summary>
        public static RltlFormula True { get; } = new RltlFormula(Rltl<IStatePredicate>.True());

        /// <summary>False constant — satisfied by no word.</summary>
        public static RltlFormula False { get; } = new RltlFormula(Rltl<IStatePredicate>.False());

        /// <summary>Atomic proposition over a state predicate.</summary>
        public static RltlFormula Prop(Func<IState, bool> predicate, string name = null)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            var atom = new StatePredAtom(new StateProp(name ?? "prop", predicate));
            return new RltlFormula(Rltl<IStatePredicate>.Atom(atom));
        }

        #endregion

        #region Boolean operators

        /// <summary>Negation <c>¬φ</c>. Internally pushed to NNF via the EBA.</summary>
        public static RltlFormula Not(RltlFormula inner)
            => new RltlFormula(RltlAlgebra.Default.Not(inner.Core));

        /// <summary>Conjunction <c>φ ∧ ψ</c>.</summary>
        public static RltlFormula And(RltlFormula left, RltlFormula right)
            => new RltlFormula(RltlAlgebra.Default.And(left.Core, right.Core));

        /// <summary>Conjunction of multiple formulas.</summary>
        public static RltlFormula And(params RltlFormula[] formulas)
            => formulas.Aggregate(True, And);

        /// <summary>Disjunction <c>φ ∨ ψ</c>.</summary>
        public static RltlFormula Or(RltlFormula left, RltlFormula right)
            => new RltlFormula(RltlAlgebra.Default.Or(left.Core, right.Core));

        /// <summary>Disjunction of multiple formulas.</summary>
        public static RltlFormula Or(params RltlFormula[] formulas)
            => formulas.Aggregate(False, Or);

        /// <summary>Implication <c>φ → ψ</c>.</summary>
        public static RltlFormula Implies(RltlFormula antecedent, RltlFormula consequent)
            => new RltlFormula(RltlAlgebra.Default.Implies(antecedent.Core, consequent.Core));

        #endregion

        #region Temporal operators (pure LTL)

        /// <summary>Next: <c>Xφ</c>.</summary>
        public static RltlFormula Next(RltlFormula inner)
            => new RltlFormula(Rltl<IStatePredicate>.Next(inner.Core));

        /// <summary>Until: <c>φ U ψ</c>.</summary>
        public static RltlFormula Until(RltlFormula hold, RltlFormula goal)
            => new RltlFormula(Rltl<IStatePredicate>.Until(hold.Core, goal.Core));

        /// <summary>Release: <c>φ R ψ</c>.</summary>
        public static RltlFormula Release(RltlFormula release, RltlFormula hold)
            => new RltlFormula(Rltl<IStatePredicate>.Release(release.Core, hold.Core));

        /// <summary>Eventually: <c>◇φ</c> (= <c>true U φ</c>).</summary>
        public static RltlFormula Eventually(RltlFormula inner)
            => new RltlFormula(Rltl<IStatePredicate>.Eventually(inner.Core));

        /// <summary>Always: <c>□φ</c> (= <c>false R φ</c>).</summary>
        public static RltlFormula Always(RltlFormula inner)
            => new RltlFormula(Rltl<IStatePredicate>.Globally(inner.Core));

        /// <summary>Infinitely often: <c>□◇φ</c>.</summary>
        public static RltlFormula InfinitelyOften(RltlFormula inner) => Always(Eventually(inner));

        /// <summary>Stabilizes: <c>◇□φ</c>.</summary>
        public static RltlFormula Stabilizes(RltlFormula inner) => Eventually(Always(inner));

        /// <summary>Leads-to: <c>φ ~&gt; ψ</c> = <c>□(φ → ◇ψ)</c>.</summary>
        public static RltlFormula LeadsTo(RltlFormula trigger, RltlFormula response)
            => Always(Implies(trigger, Eventually(response)));

        #endregion

        #region Regex-prefix operators (RLTL-specific)

        /// <summary>
        /// Sequential prefix: <c>R ; φ</c> — there exists k≥0 such that the
        /// prefix <c>w[0..k]</c> is in <c>L(R)</c> and the suffix
        /// <c>w[k..]</c> satisfies <c>φ</c>.
        /// </summary>
        public static RltlFormula SeqPrefix(Regex r, RltlFormula phi)
            => new RltlFormula(Rltl<IStatePredicate>.SeqPrefix(r.Core, phi.Core));

        /// <summary>
        /// Overlapping prefix: <c>R : φ</c> — like <see cref="SeqPrefix"/>
        /// but the match consumes one extra letter (<c>w[0..k+1] ∈ L(R)</c>).
        /// </summary>
        public static RltlFormula OvlPrefix(Regex r, RltlFormula phi)
            => new RltlFormula(Rltl<IStatePredicate>.OvlPrefix(r.Core, phi.Core));

        /// <summary>
        /// Trigger: <c>R ⊳ φ</c> — for every k≥0 with <c>w[0..k] ∈ L(R)</c>,
        /// the suffix <c>w[k..]</c> satisfies <c>φ</c>. The universal
        /// (safety) dual of <see cref="SeqPrefix"/>.
        /// </summary>
        public static RltlFormula Trigger(Regex r, RltlFormula phi)
            => new RltlFormula(Rltl<IStatePredicate>.Trigger(r.Core, phi.Core));

        /// <summary>
        /// Overlapping trigger / "match": <c>R ⊳⊳ φ</c> — universal dual of
        /// <see cref="OvlPrefix"/>.
        /// </summary>
        public static RltlFormula Match(Regex r, RltlFormula phi)
            => new RltlFormula(Rltl<IStatePredicate>.Match(r.Core, phi.Core));

        #endregion

        #region Operator overloads

        public static RltlFormula operator &(RltlFormula left, RltlFormula right) => And(left, right);
        public static RltlFormula operator |(RltlFormula left, RltlFormula right) => Or(left, right);
        public static RltlFormula operator !(RltlFormula inner) => Not(inner);

        #endregion

        public override string ToString() => Core.ToString();
    }
}
