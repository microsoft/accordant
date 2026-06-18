namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// Typed factory for defining state observations and building temporal
    /// formulas over a model state type <typeparamref name="TState"/>.
    /// 
    /// <para>
    /// <typeparamref name="TState"/> is declared once at construction;
    /// all <see cref="Observe"/> calls infer it automatically, avoiding
    /// generic-type repetition at every call site.
    /// </para>
    /// 
    /// <example>
    /// <code>
    /// var p = new Properties&lt;PetersonState&gt;();
    /// var crit0 = p.Observe(s =&gt; s.InCS[0], "Crit0");
    /// var mutex = p.Always(!(crit0 &amp; crit1));
    /// graph.Check(mutex);
    /// </code>
    /// </example>
    /// </summary>
    public sealed class Properties<TState> where TState : State
    {
        #region Observation factory

        /// <summary>
        /// Define an atomic observation (proposition) over the model state.
        /// The returned <see cref="Observation"/> can be used in both temporal
        /// formulas and regex patterns.
        /// </summary>
        /// <param name="predicate">State predicate — evaluated against concrete states
        /// during model checking.</param>
        /// <param name="name">Display name for diagnostics and counterexample traces.</param>
        public Observation Observe(Func<TState, bool> predicate, string name)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (name == null) throw new ArgumentNullException(nameof(name));
            var prop = new StateProp(name, state => predicate((TState)state));
            return new Observation(new StatePredAtom(prop));
        }

        #endregion

        #region Constants

        /// <summary>True constant — satisfied by every infinite word.</summary>
        public TemporalFormula True => new TemporalFormula(Rltl<IStatePredicate>.True());

        /// <summary>False constant — satisfied by no infinite word.</summary>
        public TemporalFormula False => new TemporalFormula(Rltl<IStatePredicate>.False());

        #endregion

        #region Boolean operators

        /// <summary>Negation <c>¬φ</c>.</summary>
        public TemporalFormula Not(TemporalFormula inner)
            => new TemporalFormula(RltlAlgebra.Default.Not(inner.Core));

        /// <summary>Conjunction <c>φ ∧ ψ</c>.</summary>
        public TemporalFormula And(TemporalFormula left, TemporalFormula right)
            => new TemporalFormula(RltlAlgebra.Default.And(left.Core, right.Core));

        /// <summary>Conjunction of multiple formulas.</summary>
        public TemporalFormula And(params TemporalFormula[] formulas)
            => formulas.Aggregate(True, And);

        /// <summary>Disjunction <c>φ ∨ ψ</c>.</summary>
        public TemporalFormula Or(TemporalFormula left, TemporalFormula right)
            => new TemporalFormula(RltlAlgebra.Default.Or(left.Core, right.Core));

        /// <summary>Disjunction of multiple formulas.</summary>
        public TemporalFormula Or(params TemporalFormula[] formulas)
            => formulas.Aggregate(False, Or);

        /// <summary>Implication <c>φ → ψ</c>.</summary>
        public TemporalFormula Implies(TemporalFormula antecedent, TemporalFormula consequent)
            => new TemporalFormula(RltlAlgebra.Default.Implies(antecedent.Core, consequent.Core));

        #endregion

        #region Temporal operators (LTL)

        /// <summary>Next: <c>Xφ</c> — φ holds in the next state.</summary>
        public TemporalFormula Next(TemporalFormula inner)
            => new TemporalFormula(Rltl<IStatePredicate>.Next(inner.Core));

        /// <summary>Until: <c>φ U ψ</c> — φ holds until ψ holds (ψ eventually holds).</summary>
        public TemporalFormula Until(TemporalFormula hold, TemporalFormula goal)
            => new TemporalFormula(Rltl<IStatePredicate>.Until(hold.Core, goal.Core));

        /// <summary>Release: <c>φ R ψ</c> — dual of Until.</summary>
        public TemporalFormula Release(TemporalFormula release, TemporalFormula hold)
            => new TemporalFormula(Rltl<IStatePredicate>.Release(release.Core, hold.Core));

        /// <summary>Eventually: <c>◇φ</c> — φ holds at some future state.</summary>
        public TemporalFormula Eventually(TemporalFormula inner)
            => new TemporalFormula(Rltl<IStatePredicate>.Eventually(inner.Core));

        /// <summary>Always: <c>□φ</c> — φ holds at every future state.</summary>
        public TemporalFormula Always(TemporalFormula inner)
            => new TemporalFormula(Rltl<IStatePredicate>.Globally(inner.Core));

        /// <summary>Infinitely often: <c>□◇φ</c> — φ holds infinitely often.</summary>
        public TemporalFormula InfinitelyOften(TemporalFormula inner) => Always(Eventually(inner));

        /// <summary>Stabilizes: <c>◇□φ</c> — φ eventually holds forever.</summary>
        public TemporalFormula Stabilizes(TemporalFormula inner) => Eventually(Always(inner));

        /// <summary>Leads-to: <c>φ ~> ψ</c> = <c>□(φ → ◇ψ)</c> — whenever φ holds,
        /// ψ eventually follows.</summary>
        public TemporalFormula LeadsTo(TemporalFormula trigger, TemporalFormula response)
            => Always(Implies(trigger, Eventually(response)));

        #endregion

        #region Regex-prefix operators (RLTL)

        /// <summary>
        /// Sequential prefix: <c>R ; φ</c> — there exists a prefix matching R,
        /// after which φ holds.
        /// </summary>
        public TemporalFormula SeqPrefix(RegexPattern r, TemporalFormula phi)
            => new TemporalFormula(Rltl<IStatePredicate>.SeqPrefix(r.Core, phi.Core));

        /// <summary>
        /// Overlapping prefix: <c>R : φ</c> — like SeqPrefix but the last
        /// letter of the match overlaps with the first letter of the suffix.
        /// </summary>
        public TemporalFormula OvlPrefix(RegexPattern r, TemporalFormula phi)
            => new TemporalFormula(Rltl<IStatePredicate>.OvlPrefix(r.Core, phi.Core));

        /// <summary>
        /// Trigger: <c>R ⊳ φ</c> — for every prefix matching R, the suffix
        /// satisfies φ. The universal (safety) dual of SeqPrefix.
        /// </summary>
        public TemporalFormula Trigger(RegexPattern r, TemporalFormula phi)
            => new TemporalFormula(Rltl<IStatePredicate>.Trigger(r.Core, phi.Core));

        /// <summary>
        /// Match: <c>R ⊳⊳ φ</c> — overlapping universal variant of Trigger.
        /// </summary>
        public TemporalFormula Match(RegexPattern r, TemporalFormula phi)
            => new TemporalFormula(Rltl<IStatePredicate>.Match(r.Core, phi.Core));

        #endregion
    }
}
