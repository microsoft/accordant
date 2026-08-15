namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// A stutter-safe regular pattern over the <em>changing steps</em> of a
    /// behaviour.
    ///
    /// <para>A <see cref="SafeRegex"/> denotes a regular language over the
    /// alphabet of state-<em>changing</em> transitions only. Transitions that
    /// leave the state semantically unchanged (including terminal stutter
    /// self-loops and named no-op model edges) are invisible to it: a pattern
    /// never counts them, never matches them, and never changes its verdict
    /// when they are inserted or removed.</para>
    ///
    /// <para>Before a pattern is used by <see cref="FormulaBuilder{TState}.After"/>
    /// or <see cref="FormulaBuilder{TState}.Whenever"/>, it is compiled to the
    /// inverse image of the erasure homomorphism <c>h</c> that deletes unchanged
    /// steps. For a single step observation <c>A</c> the compiled form is
    /// conceptually</para>
    /// <code>Unchanged* · (Changed ∧ A) · Unchanged*</code>
    /// <para>and the remaining operators are lifted so that the compiled language
    /// is exactly <c>h⁻¹(L)</c> of the visible language <c>L</c>. That
    /// construction is complete for regular languages that are invariant under
    /// insertion and deletion of unchanged steps.</para>
    ///
    /// <para>Patterns observe <em>steps</em>, not actions and not physical
    /// transition positions. A state observation <c>p(s)</c> used in
    /// <see cref="FormulaBuilder{TState}.ChangingStep(Observation)"/> is read at
    /// the <em>source</em> state of the changing step; a transition observation
    /// <c>p(s, s')</c> is read across it. Action and metadata predicates are
    /// deliberately not available, because they are not stable under stuttering.</para>
    ///
    /// <para>Operator conventions:</para>
    /// <list type="bullet">
    ///   <item><c>a.Then(b)</c> — concatenation <c>a · b</c></item>
    ///   <item><c>a | b</c> — union</item>
    ///   <item><c>a &amp; b</c> — intersection</item>
    ///   <item><c>!a</c> — complement, relative to all changing-step words</item>
    ///   <item><c>a.Star()</c>, <c>a.Plus()</c>, <c>a.Optional()</c> — repetition</item>
    /// </list>
    /// <para>Fusion is intentionally absent: it shares one physical transition
    /// between the two operands, which is not stable under inserted unchanged
    /// steps. It remains available on
    /// <see cref="StutterSensitiveFormulaBuilder{TState}"/> through
    /// <see cref="RegexPattern.Fusion"/>.</para>
    /// </summary>
    public sealed class SafeRegex
    {
        private Ere<IStatePredicate> lowered;

        internal SafeRegexNode Node { get; }

        internal SafeRegex(SafeRegexNode node)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
        }

        #region Internal factories

        /// <summary>∅ over changing-step words — matches nothing at all.</summary>
        internal static SafeRegex Never { get; } =
            new SafeRegex(SafeRegexNode.EmptyNode);

        /// <summary>
        /// { ε } over changing-step words — matches exactly the behaviours that
        /// make no progress, i.e. any number of unchanged steps.
        /// </summary>
        internal static SafeRegex NoSteps { get; } =
            new SafeRegex(SafeRegexNode.EpsilonNode);

        /// <summary>One changing step, unconstrained.</summary>
        internal static SafeRegex AnyStep { get; } =
            new SafeRegex(new SafeRegexStep(null, "⟨step⟩"));

        internal static SafeRegex Step(IStatePredicate observation, string display)
            => new SafeRegex(new SafeRegexStep(observation, display));

        #endregion

        #region Combinators

        /// <summary>Concatenation <c>this · other</c> of visible steps.</summary>
        public SafeRegex Then(SafeRegex other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return new SafeRegex(new SafeRegexConcat(Node, other.Node));
        }

        /// <summary>Kleene star <c>this*</c> — zero or more visible repetitions.</summary>
        public SafeRegex Star() => new SafeRegex(new SafeRegexStar(Node));

        /// <summary>Kleene plus <c>this+</c> — one or more visible repetitions.</summary>
        public SafeRegex Plus() => new SafeRegex(new SafeRegexPlus(Node));

        /// <summary>Optional <c>this?</c> — this pattern or no visible steps.</summary>
        public SafeRegex Optional() => new SafeRegex(new SafeRegexOptional(Node));

        /// <summary>Union <c>a + b</c>.</summary>
        public static SafeRegex operator |(SafeRegex a, SafeRegex b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            return new SafeRegex(new SafeRegexUnion(a.Node, b.Node));
        }

        /// <summary>Intersection <c>a ∩ b</c>.</summary>
        public static SafeRegex operator &(SafeRegex a, SafeRegex b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            return new SafeRegex(new SafeRegexIntersect(a.Node, b.Node));
        }

        /// <summary>
        /// Complement <c>~a</c>, taken relative to the set of <em>all</em>
        /// changing-step words. Unchanged steps stay invisible on both sides.
        /// </summary>
        public static SafeRegex operator !(SafeRegex a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            return new SafeRegex(new SafeRegexComplement(a.Node));
        }

        #endregion

        /// <summary>
        /// The compiled inverse image <c>h⁻¹(L)</c> under erasure of unchanged
        /// steps, ready for the RLTL prefix operators.
        /// </summary>
        internal Ere<IStatePredicate> Lower()
            => lowered ?? (lowered = Node.Lower());

        /// <summary>
        /// A rendering in visible changing-step terms. The internal
        /// unchanged-step padding is deliberately not shown.
        /// </summary>
        public override string ToString() => Node.ToString();
    }

    /// <summary>
    /// Expression tree of a <see cref="SafeRegex"/>. Kept separate from
    /// <see cref="RegexPattern"/> so that safe and stutter-sensitive patterns
    /// cannot be mixed accidentally.
    ///
    /// <para>Each node knows how to compile itself to the inverse image under
    /// the erasure homomorphism <c>h : Σ* → C*</c> that deletes unchanged
    /// steps (<c>C</c> = changing steps, <c>U</c> = unchanged steps,
    /// <c>Σ = C ⊎ U</c>). Writing <c>⌈R⌉</c> for the compiled form:</para>
    /// <list type="bullet">
    ///   <item><c>⌈∅⌉ = ∅</c></item>
    ///   <item><c>⌈ε⌉ = U*</c> — no visible step, arbitrarily many unchanged ones</item>
    ///   <item><c>⌈A⌉ = U* · (C ∧ A) · U*</c></item>
    ///   <item><c>⌈R · S⌉ = ⌈R⌉ · ⌈S⌉</c> — every split of a lowered word
    ///     induces a split of its erasure and vice versa</item>
    ///   <item><c>⌈R + S⌉ = ⌈R⌉ + ⌈S⌉</c>, <c>⌈R ∩ S⌉ = ⌈R⌉ ∩ ⌈S⌉</c> —
    ///     <c>h⁻¹</c> is a Boolean-algebra morphism</item>
    ///   <item><c>⌈~R⌉ = ~⌈R⌉</c> — because <c>h</c> is a total function,
    ///     <c>h⁻¹(C* \ L) = Σ* \ h⁻¹(L)</c></item>
    ///   <item><c>⌈R*⌉ = U* + ⌈R⌉*</c> — the extra <c>U*</c> is essential:
    ///     <c>ε ∈ L(R*)</c> always, so every all-unchanged word must match even
    ///     when <c>R</c> itself is not nullable</item>
    ///   <item><c>⌈R+⌉ = ⌈R⌉ · ⌈R*⌉</c>, <c>⌈R?⌉ = ⌈R⌉ + U*</c></item>
    /// </list>
    /// </summary>
    internal abstract class SafeRegexNode
    {
        private Ere<IStatePredicate> lowered;

        internal static readonly SafeRegexNode EmptyNode = new SafeRegexEmpty();
        internal static readonly SafeRegexNode EpsilonNode = new SafeRegexEpsilon();

        internal Ere<IStatePredicate> Lower()
            => this.lowered ?? (this.lowered = this.LowerCore());

        protected abstract Ere<IStatePredicate> LowerCore();
    }

    internal sealed class SafeRegexEmpty : SafeRegexNode
    {
        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Empty();

        public override string ToString() => "∅";
    }

    internal sealed class SafeRegexEpsilon : SafeRegexNode
    {
        protected override Ere<IStatePredicate> LowerCore()
            => StutterAlphabet.UnchangedSteps;

        public override string ToString() => "ε";
    }

    internal sealed class SafeRegexStep : SafeRegexNode
    {
        private readonly IStatePredicate observation;
        private readonly string display;

        internal SafeRegexStep(IStatePredicate observation, string display)
        {
            this.observation = observation;
            this.display = display;
        }

        protected override Ere<IStatePredicate> LowerCore()
        {
            var padding = StutterAlphabet.UnchangedSteps;
            var letter = StutterAlphabet.ChangingStep(this.observation);
            return Ere<IStatePredicate>.Concat(
                padding,
                Ere<IStatePredicate>.Concat(letter, padding));
        }

        public override string ToString() => this.display;
    }

    internal sealed class SafeRegexConcat : SafeRegexNode
    {
        private readonly SafeRegexNode left;
        private readonly SafeRegexNode right;

        internal SafeRegexConcat(SafeRegexNode left, SafeRegexNode right)
        {
            this.left = left;
            this.right = right;
        }

        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Concat(this.left.Lower(), this.right.Lower());

        public override string ToString() => $"({this.left} · {this.right})";
    }

    internal sealed class SafeRegexUnion : SafeRegexNode
    {
        private readonly SafeRegexNode left;
        private readonly SafeRegexNode right;

        internal SafeRegexUnion(SafeRegexNode left, SafeRegexNode right)
        {
            this.left = left;
            this.right = right;
        }

        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Union(this.left.Lower(), this.right.Lower());

        public override string ToString() => $"({this.left} + {this.right})";
    }

    internal sealed class SafeRegexIntersect : SafeRegexNode
    {
        private readonly SafeRegexNode left;
        private readonly SafeRegexNode right;

        internal SafeRegexIntersect(SafeRegexNode left, SafeRegexNode right)
        {
            this.left = left;
            this.right = right;
        }

        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Intersect(this.left.Lower(), this.right.Lower());

        public override string ToString() => $"({this.left} ∩ {this.right})";
    }

    internal sealed class SafeRegexComplement : SafeRegexNode
    {
        private readonly SafeRegexNode inner;

        internal SafeRegexComplement(SafeRegexNode inner)
        {
            this.inner = inner;
        }

        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Complement(this.inner.Lower());

        public override string ToString() => $"~{this.inner}";
    }

    internal sealed class SafeRegexStar : SafeRegexNode
    {
        private readonly SafeRegexNode inner;

        internal SafeRegexStar(SafeRegexNode inner)
        {
            this.inner = inner;
        }

        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Union(
                StutterAlphabet.UnchangedSteps,
                Ere<IStatePredicate>.Star(this.inner.Lower()));

        public override string ToString() => $"{this.inner}*";
    }

    internal sealed class SafeRegexPlus : SafeRegexNode
    {
        private readonly SafeRegexNode inner;

        internal SafeRegexPlus(SafeRegexNode inner)
        {
            this.inner = inner;
        }

        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Concat(
                this.inner.Lower(),
                new SafeRegexStar(this.inner).Lower());

        public override string ToString() => $"{this.inner}+";
    }

    internal sealed class SafeRegexOptional : SafeRegexNode
    {
        private readonly SafeRegexNode inner;

        internal SafeRegexOptional(SafeRegexNode inner)
        {
            this.inner = inner;
        }

        protected override Ere<IStatePredicate> LowerCore()
            => Ere<IStatePredicate>.Union(
                this.inner.Lower(),
                StutterAlphabet.UnchangedSteps);

        public override string ToString() => $"{this.inner}?";
    }
}
