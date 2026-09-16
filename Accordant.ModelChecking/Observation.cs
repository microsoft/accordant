namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// An observation over a model state — an atomic proposition that can be
    /// used in both temporal formulas and regex patterns. Created via
    /// <see cref="Properties{TState}.Observe"/>.
    /// </summary>
    public sealed class Observation
    {
        internal Rltl<IStatePredicate> RltlCore { get; }
        internal Ere<IStatePredicate> EreCore { get; }

        internal Observation(StatePredAtom atom)
        {
            RltlCore = Rltl<IStatePredicate>.Atom(atom);
            EreCore = Ere<IStatePredicate>.Atom(atom);
        }

        /// <summary>Conjunction of two observations (usable in both temporal and regex contexts).</summary>
        public static Observation operator &(Observation a, Observation b)
            => new Observation(a.RltlCore, b.RltlCore, a.EreCore, b.EreCore, and: true);

        /// <summary>Disjunction of two observations.</summary>
        public static Observation operator |(Observation a, Observation b)
            => new Observation(a.RltlCore, b.RltlCore, a.EreCore, b.EreCore, and: false);

        /// <summary>Negation of an observation.</summary>
        public static Observation operator !(Observation a)
        {
            var rltl = RltlAlgebra.Default.Not(a.RltlCore);
            var ere = Ere<IStatePredicate>.Complement(a.EreCore);
            return new Observation(rltl, ere);
        }

        // Internal constructors for compound observations
        private Observation(
            Rltl<IStatePredicate> rltlA, Rltl<IStatePredicate> rltlB,
            Ere<IStatePredicate> ereA, Ere<IStatePredicate> ereB,
            bool and)
        {
            if (and)
            {
                RltlCore = RltlAlgebra.Default.And(rltlA, rltlB);
                EreCore = Ere<IStatePredicate>.Intersect(ereA, ereB);
            }
            else
            {
                RltlCore = RltlAlgebra.Default.Or(rltlA, rltlB);
                EreCore = Ere<IStatePredicate>.Union(ereA, ereB);
            }
        }

        private Observation(Rltl<IStatePredicate> rltl, Ere<IStatePredicate> ere)
        {
            RltlCore = rltl;
            EreCore = ere;
        }

        /// <summary>Implicit conversion to a temporal formula.</summary>
        public static implicit operator TemporalFormula(Observation obs)
            => new TemporalFormula(obs.RltlCore);

        /// <summary>Implicit conversion to a regex pattern.</summary>
        public static implicit operator RegexPattern(Observation obs)
            => new RegexPattern(obs.EreCore);

        public override string ToString() => RltlCore.ToString();
    }

    /// <summary>
    /// A temporal formula (RLTL) over model-program states. Constructed via
    /// <see cref="Properties{TState}"/> methods like <see cref="Properties{TState}.Always"/>,
    /// <see cref="Properties{TState}.Eventually"/>, etc.
    /// </summary>
    public sealed class TemporalFormula
    {
        internal Rltl<IStatePredicate> Core { get; }

        internal TemporalFormula(Rltl<IStatePredicate> core)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
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
    /// An extended regular expression (ERE) over model-program states. Used as
    /// the regex component of RLTL prefix operators. Constructed via
    /// <see cref="RegexPattern.Sigma"/>, <see cref="RegexPattern.Star(RegexPattern)"/>,
    /// <see cref="Observation"/> implicit conversion, etc.
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

        /// <summary>Σ — matches any single letter (one state).</summary>
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
