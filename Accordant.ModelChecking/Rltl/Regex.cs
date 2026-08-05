using Microsoft.Accordant;

namespace Microsoft.Accordant.ModelChecking.Rltl
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// User-facing extended-regular-expression (ERE) over model-program states.
    /// A thin facade over <see cref="Ere{TPred}"/> with
    /// <see cref="IStatePredicate"/> as the predicate type, providing friendly
    /// factories and operator overloads.
    ///
    /// Used as the regex component of <see cref="RltlFormula"/> prefix
    /// operators (<see cref="RltlFormula.SeqPrefix"/>,
    /// <see cref="RltlFormula.OvlPrefix"/>, <see cref="RltlFormula.Trigger"/>,
    /// <see cref="RltlFormula.Match"/>).
    ///
    /// Operator conventions:
    /// <list type="bullet">
    ///   <item><c>a | b</c>   — union (a + b)</item>
    ///   <item><c>a &amp; b</c>   — intersection (a ∩ b)</item>
    ///   <item><c>!a</c>      — complement (~a)</item>
    ///   <item><c>a.Then(b)</c> — concatenation (a · b)</item>
    /// </list>
    /// Use <see cref="Star"/>/<see cref="Plus"/>/<see cref="Optional"/> for
    /// repetition.
    /// </summary>
    public sealed class Regex
    {
        internal Ere<IStatePredicate> Core { get; }

        internal Regex(Ere<IStatePredicate> core)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
        }

        #region Factories

        /// <summary>The empty language ∅ — matches no word.</summary>
        public static Regex Empty { get; } = new Regex(Ere<IStatePredicate>.Empty());

        /// <summary>The language { ε } — matches only the empty word.</summary>
        public static Regex Epsilon { get; } = new Regex(Ere<IStatePredicate>.Epsilon());

        /// <summary>Σ* — the universal language, matches every finite word.</summary>
        public static Regex Sigma { get; } = new Regex(Ere<IStatePredicate>.Sigma());

        /// <summary>
        /// Single-letter language: matches one state satisfying
        /// <paramref name="predicate"/>.
        /// </summary>
        public static Regex Prop(Func<IState, bool> predicate, string name = null)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            var atom = new StatePredAtom(new StateProp(name ?? "prop", predicate));
            return new Regex(Ere<IStatePredicate>.Atom(atom));
        }

        /// <summary>Concatenation <c>a · b</c>.</summary>
        public Regex Then(Regex other) => Concat(this, other);

        /// <summary>Concatenation <c>a · b</c>.</summary>
        public static Regex Concat(Regex a, Regex b)
            => new Regex(Ere<IStatePredicate>.Concat(a.Core, b.Core));

        /// <summary>Union <c>a + b</c>.</summary>
        public static Regex Union(Regex a, Regex b)
            => new Regex(Ere<IStatePredicate>.Union(a.Core, b.Core));

        /// <summary>Intersection <c>a ∩ b</c>.</summary>
        public static Regex Intersect(Regex a, Regex b)
            => new Regex(Ere<IStatePredicate>.Intersect(a.Core, b.Core));

        /// <summary>Complement <c>~a</c>.</summary>
        public static Regex Complement(Regex a)
            => new Regex(Ere<IStatePredicate>.Complement(a.Core));

        /// <summary>Kleene star <c>a*</c>.</summary>
        public static Regex Star(Regex a)
            => new Regex(Ere<IStatePredicate>.Star(a.Core));

        /// <summary>
        /// Fusion <c>a : b</c> (Section 7.3, JACM extension): the last letter of an
        /// <c>a</c>-match coincides with the first letter of a <c>b</c>-match.
        /// <c>L(a : b) = { v | ∃ i &lt; |v| : v[..i] ∈ L(a) ∧ v[i..] ∈ L(b) }</c>.
        /// </summary>
        public static Regex Fusion(Regex a, Regex b)
            => new Regex(Ere<IStatePredicate>.Fusion(a.Core, b.Core));

        /// <summary>Kleene plus <c>a+ = a · a*</c>.</summary>
        public static Regex Plus(Regex a)
            => new Regex(Ere<IStatePredicate>.Plus(a.Core));

        /// <summary>Optional <c>a? = a + ε</c>.</summary>
        public static Regex Optional(Regex a)
            => new Regex(Ere<IStatePredicate>.Optional(a.Core));

        #endregion

        #region Operator overloads

        public static Regex operator |(Regex a, Regex b) => Union(a, b);
        public static Regex operator &(Regex a, Regex b) => Intersect(a, b);
        public static Regex operator !(Regex a) => Complement(a);

        #endregion

        public override string ToString() => Core.ToString();
    }
}
