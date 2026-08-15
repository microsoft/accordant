namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// An observation over a model state — an atomic proposition that can be
    /// used in both temporal formulas and regex patterns. Created via
    /// <see cref="FormulaBuilder{TState}.Observe"/>.
    /// </summary>
    public sealed class Observation
    {
        internal Rltl<IStatePredicate> RltlCore { get; }
        internal Ere<IStatePredicate> EreCore { get; }

        /// <summary>
        /// The single-letter predicate form of this observation. Unlike
        /// <see cref="EreCore"/> — whose complement is a whole-language
        /// complement over words — this stays a predicate over one letter, so
        /// it can be conjoined with the changing-step predicate by
        /// <see cref="SafeRegex"/>.
        /// </summary>
        internal IStatePredicate PredicateCore { get; }

        internal Observation(StatePredAtom atom)
        {
            RltlCore = Rltl<IStatePredicate>.Atom(atom);
            EreCore = Ere<IStatePredicate>.Atom(atom);
            PredicateCore = atom;
        }

        /// <summary>Conjunction of two observations (usable in both temporal and regex contexts).</summary>
        public static Observation operator &(Observation a, Observation b)
            => new Observation(
                a.RltlCore, b.RltlCore,
                a.EreCore, b.EreCore,
                a.PredicateCore, b.PredicateCore,
                and: true);

        /// <summary>Disjunction of two observations.</summary>
        public static Observation operator |(Observation a, Observation b)
            => new Observation(
                a.RltlCore, b.RltlCore,
                a.EreCore, b.EreCore,
                a.PredicateCore, b.PredicateCore,
                and: false);

        /// <summary>Negation of an observation.</summary>
        public static Observation operator !(Observation a)
        {
            var rltl = RltlAlgebra.Default.Not(a.RltlCore);
            var ere = Ere<IStatePredicate>.Complement(a.EreCore);
            return new Observation(rltl, ere, new StatePredNot(a.PredicateCore));
        }

        // Internal constructors for compound observations
        private Observation(
            Rltl<IStatePredicate> rltlA, Rltl<IStatePredicate> rltlB,
            Ere<IStatePredicate> ereA, Ere<IStatePredicate> ereB,
            IStatePredicate predA, IStatePredicate predB,
            bool and)
        {
            if (and)
            {
                RltlCore = RltlAlgebra.Default.And(rltlA, rltlB);
                EreCore = Ere<IStatePredicate>.Intersect(ereA, ereB);
                PredicateCore = new StatePredAnd(predA, predB);
            }
            else
            {
                RltlCore = RltlAlgebra.Default.Or(rltlA, rltlB);
                EreCore = Ere<IStatePredicate>.Union(ereA, ereB);
                PredicateCore = new StatePredOr(predA, predB);
            }
        }

        private Observation(
            Rltl<IStatePredicate> rltl,
            Ere<IStatePredicate> ere,
            IStatePredicate predicate)
        {
            RltlCore = rltl;
            EreCore = ere;
            PredicateCore = predicate;
        }

        /// <summary>Implicit conversion to a stutter-safe temporal formula.</summary>
        public static implicit operator StutterSafeFormula(Observation obs)
            => new StutterSafeFormula(obs.RltlCore);

        /// <summary>Implicit conversion to a regex pattern.</summary>
        public static implicit operator RegexPattern(Observation obs)
            => new RegexPattern(obs.EreCore);

        public override string ToString() => RltlCore.ToString();
    }

    /// <summary>
    /// An observation over one concrete transition.
    /// </summary>
    public sealed class TransitionObservation
    {
        internal Rltl<IStatePredicate> RltlCore { get; }
        internal IStatePredicate PredicateCore { get; }

        internal TransitionObservation(StatePredAtom atom)
        {
            RltlCore = Rltl<IStatePredicate>.Atom(atom);
            PredicateCore = atom;
        }

        private TransitionObservation(
            Rltl<IStatePredicate> rltl,
            IStatePredicate predicate)
        {
            RltlCore = rltl;
            PredicateCore = predicate;
        }

        internal static TransitionObservation Combine(
            Rltl<IStatePredicate> rltlA,
            Rltl<IStatePredicate> rltlB,
            IStatePredicate predicateA,
            IStatePredicate predicateB,
            bool and)
            => new TransitionObservation(
                and
                    ? RltlAlgebra.Default.And(rltlA, rltlB)
                    : RltlAlgebra.Default.Or(rltlA, rltlB),
                and
                    ? new StatePredAnd(predicateA, predicateB)
                    : new StatePredOr(predicateA, predicateB));

        public static TransitionObservation operator &(
            TransitionObservation a,
            TransitionObservation b)
            => Combine(
                a.RltlCore,
                b.RltlCore,
                a.PredicateCore,
                b.PredicateCore,
                and: true);

        public static TransitionObservation operator |(
            TransitionObservation a,
            TransitionObservation b)
            => Combine(
                a.RltlCore,
                b.RltlCore,
                a.PredicateCore,
                b.PredicateCore,
                and: false);

        public static TransitionObservation operator !(TransitionObservation a)
            => new TransitionObservation(
                RltlAlgebra.Default.Not(a.RltlCore),
                new StatePredNot(a.PredicateCore));

        public override string ToString() => RltlCore.ToString();
    }

    /// <summary>
    /// A temporal formula (RLTL) over model-program states.
    /// </summary>
    public class TemporalFormula
    {
        internal Rltl<IStatePredicate> Core { get; }

        /// <summary>Optional human-readable name used in diagnostics.</summary>
        public string Name { get; }

        internal TemporalFormula(Rltl<IStatePredicate> core, string name = null)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
            Name = name;
        }

        /// <summary>Return this formula with a diagnostic name.</summary>
        public TemporalFormula Named(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Formula name cannot be empty.", nameof(name));
            return new TemporalFormula(Core, name);
        }

        /// <summary>Conjunction <c>φ ∧ ψ</c>.</summary>
        public static TemporalFormula operator &(TemporalFormula a, TemporalFormula b)
            => new TemporalFormula(RltlAlgebra.Default.And(a.Core, b.Core));

        /// <summary>Disjunction <c>φ ∨ ψ</c>.</summary>
        public static TemporalFormula operator |(TemporalFormula a, TemporalFormula b)
            => new TemporalFormula(RltlAlgebra.Default.Or(a.Core, b.Core));

        /// <summary>Negation <c>¬φ</c>.</summary>
        public static TemporalFormula operator !(TemporalFormula a)
            => new TemporalFormula(RltlAlgebra.Default.Not(a.Core));

        public override string ToString() => Core.ToString();
    }

    /// <summary>
    /// A temporal formula guaranteed to be invariant under finite repetitions
    /// of indistinguishable states.
    /// </summary>
    public sealed class StutterSafeFormula : TemporalFormula
    {
        internal StutterSafeFormula(Rltl<IStatePredicate> core, string name = null)
            : base(core, name)
        {
        }

        /// <summary>
        /// Return this formula with a diagnostic name while preserving its
        /// stutter-invariance guarantee.
        /// </summary>
        public new StutterSafeFormula Named(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Formula name cannot be empty.", nameof(name));
            return new StutterSafeFormula(Core, name);
        }

        public static StutterSafeFormula operator &(
            StutterSafeFormula a,
            StutterSafeFormula b)
            => new StutterSafeFormula(RltlAlgebra.Default.And(a.Core, b.Core));

        public static StutterSafeFormula operator |(
            StutterSafeFormula a,
            StutterSafeFormula b)
            => new StutterSafeFormula(RltlAlgebra.Default.Or(a.Core, b.Core));

        public static StutterSafeFormula operator !(StutterSafeFormula a)
            => new StutterSafeFormula(RltlAlgebra.Default.Not(a.Core));
    }

    /// <summary>
    /// An extended regular expression (ERE) over model-program transition
    /// letters. Used as the regex component of the stutter-<em>sensitive</em>
    /// RLTL prefix operators on
    /// <see cref="StutterSensitiveFormulaBuilder{TState}"/>. Constructed via
    /// <see cref="RegexPattern.Sigma"/>, <see cref="RegexPattern.Star()"/>,
    /// <see cref="Observation"/> implicit conversion, etc.
    ///
    /// <para>Each letter is one physical transition, including transitions that
    /// leave the state unchanged, so a pattern built here can distinguish
    /// stutter-equivalent behaviours. Use <see cref="SafeRegex"/> and
    /// <c>FormulaBuilder&lt;TState&gt;.After</c> /
    /// <c>FormulaBuilder&lt;TState&gt;.Whenever</c> for patterns over changing
    /// steps only.</para>
    /// </summary>
    public sealed class RegexPattern
    {
        internal Ere<IStatePredicate> Core { get; }

        internal RegexPattern(Ere<IStatePredicate> core)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
        }

        #region Constants

        /// <summary>The empty language ∅ — matches no word.</summary>
        public static RegexPattern Empty { get; } = new RegexPattern(Ere<IStatePredicate>.Empty());

        /// <summary>The language { ε } — matches only the empty word.</summary>
        public static RegexPattern Epsilon { get; } = new RegexPattern(Ere<IStatePredicate>.Epsilon());

        /// <summary>
        /// <c>Σ*</c> — the universal language over transition letters; matches
        /// every finite word, including the empty one. (This is <em>not</em> a
        /// single-letter language: use an <see cref="Observation"/> atom such
        /// as <c>Observe(state =&gt; true)</c> for that. <c>p | !p</c> is also
        /// every word, because ERE complement is a whole-language complement.)
        /// </summary>
        public static RegexPattern Sigma { get; } = new RegexPattern(Ere<IStatePredicate>.Sigma());

        #endregion

        #region Combinators

        /// <summary>Concatenation <c>this · other</c>.</summary>
        public RegexPattern Then(RegexPattern other)
            => new RegexPattern(Ere<IStatePredicate>.Concat(Core, other.Core));

        /// <summary>Kleene star <c>this*</c>.</summary>
        public RegexPattern Star()
            => new RegexPattern(Ere<IStatePredicate>.Star(Core));

        /// <summary>Kleene plus <c>this+ = this · this*</c>.</summary>
        public RegexPattern Plus()
            => new RegexPattern(Ere<IStatePredicate>.Plus(Core));

        /// <summary>Optional <c>this? = this + ε</c>.</summary>
        public RegexPattern Optional()
            => new RegexPattern(Ere<IStatePredicate>.Optional(Core));

        /// <summary>
        /// Fusion <c>this : other</c> — the last letter of a <c>this</c>-match
        /// coincides with the first letter of an <c>other</c>-match.
        /// </summary>
        public RegexPattern Fusion(RegexPattern other)
            => new RegexPattern(Ere<IStatePredicate>.Fusion(Core, other.Core));

        #endregion

        #region Operator overloads

        /// <summary>Union <c>a + b</c>.</summary>
        public static RegexPattern operator |(RegexPattern a, RegexPattern b)
            => new RegexPattern(Ere<IStatePredicate>.Union(a.Core, b.Core));

        /// <summary>Intersection <c>a ∩ b</c>.</summary>
        public static RegexPattern operator &(RegexPattern a, RegexPattern b)
            => new RegexPattern(Ere<IStatePredicate>.Intersect(a.Core, b.Core));

        /// <summary>Complement <c>~a</c>.</summary>
        public static RegexPattern operator !(RegexPattern a)
            => new RegexPattern(Ere<IStatePredicate>.Complement(a.Core));

        #endregion

        public override string ToString() => Core.ToString();
    }
}
