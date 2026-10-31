namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// RLTL formula in NNF (Section 7 of the POPL'25 paper; closures from JACM).
    /// Extends pure LTL with two regex-prefix operators, their NNF duals, and
    /// three regex closures:
    /// <list type="bullet">
    ///   <item><see cref="RltlSeqPrefix{TPred}"/>:  R ; φ — ∃k≥0. w[0..k]∈R ∧ w[k..]⊨φ</item>
    ///   <item><see cref="RltlOvlPrefix{TPred}"/>:  R : φ — ∃k≥0. w[0..k+1]∈R ∧ w[k..]⊨φ</item>
    ///   <item><see cref="RltlTrigger{TPred}"/>:    R ⊳ φ — ∀k≥0. w[0..k]∈R → w[k..]⊨φ (= ¬(R; ¬φ))</item>
    ///   <item><see cref="RltlMatch{TPred}"/>:      R ⊳⊳ φ — ∀k≥0. w[0..k+1]∈R → w[k..]⊨φ (= ¬(R: ¬φ))</item>
    ///   <item><see cref="RltlWeakClosure{TPred}"/>:    {R}    — ε⊨R ∨ ∃i: w[..i]⊨R ∨ ∀i: ∂(w[..i],R)≢⊥</item>
    ///   <item><see cref="RltlNegWeakClosure{TPred}"/>: {{R}}̄ — dual of {R}</item>
    ///   <item><see cref="RltlOmegaClosure{TPred}"/>:   {R}ω  — infinite concatenation of R-matches</item>
    /// </list>
    /// All temporal operators of LTL (Until, Release, Next, And, Or, Atom)
    /// are also available with their standard semantics. <see cref="Negate"/>
    /// pushes negation to atoms via De Morgan and the regex-operator duals.
    /// </summary>
    public abstract class Rltl<TPred> : IEquatable<Rltl<TPred>>, IComparable<Rltl<TPred>>
    {
        private int? _hash;
        private int _id = -1;
        internal Rltl() { }
        internal abstract int Kind { get; }

        /// <summary>
        /// Unique non-negative identifier within the
        /// <see cref="DefaultBuilder"/>. Assigned on first interning.
        /// <c>Id 0 == ⊥</c>, <c>Id 1 == ⊤</c>.
        /// </summary>
        public int Id => _id;

        internal bool HasId => _id >= 0;

        internal void AssignId(int id) { _id = id; }

        /// <summary>
        /// Per-<typeparamref name="TPred"/> default term builder. All static
        /// factories on this type route through it so every returned RLTL
        /// formula is canonical (reference-equal to structurally equivalent
        /// terms).
        /// </summary>
        public static RltlBuilder<TPred> DefaultBuilder => BuilderHolder.Instance;

        private static class BuilderHolder
        {
            internal static readonly RltlBuilder<TPred> Instance = new RltlBuilder<TPred>();
        }

        #region Factories

        public static Rltl<TPred> True() => DefaultBuilder.True;
        public static Rltl<TPred> False() => DefaultBuilder.False;

        /// <summary>
        /// Construct a positive atom carrying <paramref name="p"/>. To
        /// negate, use <see cref="RltlAlgebra{TPred}.Not"/> which pushes
        /// complement into the underlying EBA.
        /// </summary>
        public static Rltl<TPred> Atom(TPred p)
            => DefaultBuilder.Intern(new RltlAtom<TPred>(p));

        public static Rltl<TPred> Next(Rltl<TPred> inner)
        {
            if (inner is RltlTrue<TPred>) return True();
            if (inner is RltlFalse<TPred>) return False();
            return DefaultBuilder.Intern(new RltlNext<TPred>(inner));
        }

        public static Rltl<TPred> Until(Rltl<TPred> l, Rltl<TPred> r)
        {
            if (r is RltlTrue<TPred>) return True();
            if (r is RltlFalse<TPred>) return False();      // l U ⊥ = ⊥
            if (l is RltlFalse<TPred>) return r;
            return DefaultBuilder.Intern(new RltlUntil<TPred>(l, r));
        }

        public static Rltl<TPred> Release(Rltl<TPred> l, Rltl<TPred> r)
        {
            if (r is RltlFalse<TPred>) return False();
            if (r is RltlTrue<TPred>) return True();
            if (l is RltlTrue<TPred>) return r;
            return DefaultBuilder.Intern(new RltlRelease<TPred>(l, r));
        }

        public static Rltl<TPred> Eventually(Rltl<TPred> p) => Until(True(), p);
        public static Rltl<TPred> Globally(Rltl<TPred> p) => Release(False(), p);

        /// <summary>R ; φ — sequential regex prefix (∃-quantification).</summary>
        public static Rltl<TPred> SeqPrefix(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return False();
            if (r is EreEpsilon<TPred>) return phi;            // (ε ; φ) = φ
            if (phi is RltlFalse<TPred>) return False();        // R ; ⊥ = ⊥
            if (IsSigmaStar(r)) return Eventually(phi);         // Σ* ; φ ≡ ◇φ
            // Distribute over Union in the regex argument (∃-style):
            //   (R₁ + R₂) ; φ  ≡  (R₁;φ) ∨ (R₂;φ)
            // Exposes per-branch prefix obligations; each disjunct may
            // canonicalise further (e.g. Σ* branch → ◇φ).
            if (r is EreUnion<TPred> u)
            {
                Rltl<TPred> acc = False();
                foreach (var op in u.Operands) acc = Or(acc, SeqPrefix(op, phi));
                return acc;
            }
            return DefaultBuilder.Intern(new RltlSeqPrefix<TPred>(r, phi));
        }

        /// <summary>R : φ — overlapping regex prefix (∃-quantification, ≥1 match).</summary>
        public static Rltl<TPred> OvlPrefix(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return False();
            if (r is EreEpsilon<TPred>) return False();        // : requires positive-length match
            if (phi is RltlFalse<TPred>) return False();
            if (IsSigmaStar(r)) return Eventually(phi);        // Σ* : φ ≡ ◇φ
            // Distribute over Union (∃-style, dual of SeqPrefix above):
            //   (R₁ + R₂) : φ  ≡  (R₁:φ) ∨ (R₂:φ)
            if (r is EreUnion<TPred> u)
            {
                Rltl<TPred> acc = False();
                foreach (var op in u.Operands) acc = Or(acc, OvlPrefix(op, phi));
                return acc;
            }
            return DefaultBuilder.Intern(new RltlOvlPrefix<TPred>(r, phi));
        }

        /// <summary>R ⊳ φ — universal trigger (∀-quantification), dual of ;.</summary>
        public static Rltl<TPred> Trigger(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return True();           // no prefix matches ∅
            if (r is EreEpsilon<TPred>) return phi;            // only k=0 matches: φ at pos 0
            if (phi is RltlTrue<TPred>) return True();
            if (IsSigmaStar(r)) return Globally(phi);          // Σ* ⊳ φ ≡ □φ
            // Distribute over Union (∀-style → conjunction, dual of ∃):
            //   (R₁ + R₂) ⊳ φ  ≡  (R₁⊳φ) ∧ (R₂⊳φ)
            // because ∀k (k matches R₁∪R₂ → φ@k) splits into the conjunction
            // of the per-disjunct universal obligations.
            if (r is EreUnion<TPred> u)
            {
                Rltl<TPred> acc = True();
                foreach (var op in u.Operands) acc = And(acc, Trigger(op, phi));
                return acc;
            }
            return DefaultBuilder.Intern(new RltlTrigger<TPred>(r, phi));
        }

        // -----------------------------------------------------------------
        // "Raw" prefix-operator factories: same unit-law simplifications as
        // the smart constructors above, but they DO NOT distribute Union in
        // the regex argument (Layer A is bypassed). Used by
        // RltlDerivative when constructed with
        // <c>distributePrefixUnion = false</c> — primarily for benchmarks
        // that need to measure state-space size with the distribution rule
        // turned off as a baseline.
        // -----------------------------------------------------------------
        public static Rltl<TPred> SeqPrefixRaw(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return False();
            if (r is EreEpsilon<TPred>) return phi;
            if (phi is RltlFalse<TPred>) return False();
            if (IsSigmaStar(r)) return Eventually(phi);
            return DefaultBuilder.Intern(new RltlSeqPrefix<TPred>(r, phi));
        }

        public static Rltl<TPred> OvlPrefixRaw(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return False();
            if (r is EreEpsilon<TPred>) return False();
            if (phi is RltlFalse<TPred>) return False();
            if (IsSigmaStar(r)) return Eventually(phi);
            return DefaultBuilder.Intern(new RltlOvlPrefix<TPred>(r, phi));
        }

        public static Rltl<TPred> TriggerRaw(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return True();
            if (r is EreEpsilon<TPred>) return phi;
            if (phi is RltlTrue<TPred>) return True();
            if (IsSigmaStar(r)) return Globally(phi);
            return DefaultBuilder.Intern(new RltlTrigger<TPred>(r, phi));
        }

        public static Rltl<TPred> MatchRaw(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return True();
            if (r is EreEpsilon<TPred>) return True();
            if (phi is RltlTrue<TPred>) return True();
            if (IsSigmaStar(r)) return Globally(phi);
            return DefaultBuilder.Intern(new RltlMatch<TPred>(r, phi));
        }

        /// <summary>R ⊳⊳ φ — universal match (∀-quantification, ≥1 length), dual of :.</summary>
        public static Rltl<TPred> Match(Ere<TPred> r, Rltl<TPred> phi)
        {
            if (r is EreEmpty<TPred>) return True();
            if (r is EreEpsilon<TPred>) return True();
            if (phi is RltlTrue<TPred>) return True();
            if (IsSigmaStar(r)) return Globally(phi);          // Σ* ⊳⊳ φ ≡ □φ
            // Distribute over Union (∀-style → conjunction, dual of OvlPrefix):
            //   (R₁ + R₂) ⊳⊳ φ  ≡  (R₁⊳⊳φ) ∧ (R₂⊳⊳φ)
            if (r is EreUnion<TPred> u)
            {
                Rltl<TPred> acc = True();
                foreach (var op in u.Operands) acc = And(acc, Match(op, phi));
                return acc;
            }
            return DefaultBuilder.Intern(new RltlMatch<TPred>(r, phi));
        }

        /// <summary>
        /// Disjunction <c>φ ∨ ψ</c> — minimal static helper used by the
        /// distribution rewrites in <see cref="SeqPrefix"/> and
        /// <see cref="OvlPrefix"/>. Applies the standard ⊥/⊤ unit and
        /// absorption laws and flattens nested <see cref="RltlOr{TPred}"/>
        /// via canonical sorted-set deduplication. Does <em>not</em> perform
        /// the EBA atom-fusion that <see cref="RltlAlgebra{TPred}.Or"/> does;
        /// callers that need full canonicalisation should run the algebra
        /// afterwards.
        /// </summary>
        public static Rltl<TPred> Or(Rltl<TPred> a, Rltl<TPred> b)
        {
            if (a is RltlTrue<TPred> || b is RltlTrue<TPred>) return True();
            if (a is RltlFalse<TPred>) return b;
            if (b is RltlFalse<TPred>) return a;
            var ops = new SortedSet<Rltl<TPred>>(RltlComparer<TPred>.Instance);
            CollectOr(a, ops); CollectOr(b, ops);
            if (ops.Count == 1) return ops.First();
            return DefaultBuilder.Intern(new RltlOr<TPred>(ops.ToArray()));
        }

        /// <summary>
        /// Conjunction <c>φ ∧ ψ</c> — minimal static helper used by the
        /// distribution rewrites in <see cref="Trigger"/> and
        /// <see cref="Match"/>. Same caveats as <see cref="Or"/>: no
        /// EBA atom-fusion is performed here.
        /// </summary>
        public static Rltl<TPred> And(Rltl<TPred> a, Rltl<TPred> b)
        {
            if (a is RltlFalse<TPred> || b is RltlFalse<TPred>) return False();
            if (a is RltlTrue<TPred>) return b;
            if (b is RltlTrue<TPred>) return a;
            var ops = new SortedSet<Rltl<TPred>>(RltlComparer<TPred>.Instance);
            CollectAnd(a, ops); CollectAnd(b, ops);
            if (ops.Count == 1) return ops.First();
            return DefaultBuilder.Intern(new RltlAnd<TPred>(ops.ToArray()));
        }

        private static void CollectOr(Rltl<TPred> f, SortedSet<Rltl<TPred>> s)
        {
            if (f is RltlOr<TPred> o) foreach (var op in o.Operands) s.Add(op);
            else s.Add(f);
        }

        private static void CollectAnd(Rltl<TPred> f, SortedSet<Rltl<TPred>> s)
        {
            if (f is RltlAnd<TPred> a) foreach (var op in a.Operands) s.Add(op);
            else s.Add(f);
        }

        /// <summary>
        /// Weak closure {R} — JACM eq. (2737). This static factory applies
        /// only the cheap *syntactic* rewrites: <c>{⊥} ≡ ⊥</c> and (if
        /// <c>R.Nullable</c>) <c>{R} ≡ ⊤</c>. The full semantic check
        /// <c>FLang(R) = ∅</c> needed for the <c>{R} ≡ ⊥</c> simplification
        /// is performed lazily by <see cref="EreEmptinessChecker{TPred,TElem}"/>
        /// inside <see cref="RltlDerivative{TPred,TElem}"/>.
        /// </summary>
        public static Rltl<TPred> WeakClosure(Ere<TPred> r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r is EreEmpty<TPred>) return False();
            if (r.Nullable) return True();
            return DefaultBuilder.Intern(new RltlWeakClosure<TPred>(r));
        }

        /// <summary>
        /// Negated weak closure {{R}}̄ — JACM eq. (2747). Syntactic rewrites:
        /// <c>{{⊥}}̄ ≡ ⊤</c>, and if <c>R.Nullable</c> then <c>{{R}}̄ ≡ ⊥</c>.
        /// Semantic <c>{{R}}̄ ≡ ⊤</c> when <c>FLang(R) = ∅</c> is applied by
        /// <see cref="EreEmptinessChecker{TPred,TElem}"/> in
        /// <see cref="RltlDerivative{TPred,TElem}"/>.
        /// </summary>
        public static Rltl<TPred> NegWeakClosure(Ere<TPred> r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r is EreEmpty<TPred>) return True();
            if (r.Nullable) return False();
            return DefaultBuilder.Intern(new RltlNegWeakClosure<TPred>(r));
        }

        /// <summary>
        /// ω-closure {R}ω — JACM eq. (2754). Syntactic rewrite: <c>{⊥}ω ≡ ⊥</c>.
        /// No nullable shortcut: ε ∈ L(R) does not make <c>{R}ω</c> trivially
        /// hold. Note: this operator must not occur negatively (RLTL+ disallows
        /// <c>¬{R}ω</c>); <see cref="RltlAlgebra{TPred}.Not"/> throws
        /// <see cref="NotSupportedException"/> on encountering it.
        /// </summary>
        public static Rltl<TPred> OmegaClosure(Ere<TPred> r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r is EreEmpty<TPred>) return False();
            return DefaultBuilder.Intern(new RltlOmegaClosure<TPred>(r));
        }

        /// <summary>True iff <paramref name="r"/> is the universal language Σ* = (~∅)*.</summary>
        private static bool IsSigmaStar(Ere<TPred> r)
            => r is EreStar<TPred> s
               && s.Inner is EreComplement<TPred> c
               && c.Inner is EreEmpty<TPred>;

        #endregion

        #region Equality / Comparison

        public abstract bool Equals(Rltl<TPred> other);
        public override bool Equals(object obj) => Equals(obj as Rltl<TPred>);
        public override int GetHashCode()
        {
            if (_hash == null) _hash = ComputeHashCode();
            return _hash.Value;
        }
        protected abstract int ComputeHashCode();

        public int CompareTo(Rltl<TPred> other)
        {
            if (other == null) return 1;
            if (ReferenceEquals(this, other)) return 0;
            int c = Kind.CompareTo(other.Kind);
            if (c != 0) return c;
            return CompareToSameKind(other);
        }
        protected abstract int CompareToSameKind(Rltl<TPred> other);

        public static bool operator ==(Rltl<TPred> a, Rltl<TPred> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }
        public static bool operator !=(Rltl<TPred> a, Rltl<TPred> b) => !(a == b);

        #endregion
    }

    internal sealed class RltlComparer<TPred> : IComparer<Rltl<TPred>>
    {
        public static readonly RltlComparer<TPred> Instance = new RltlComparer<TPred>();
        public int Compare(Rltl<TPred> x, Rltl<TPred> y) => x.CompareTo(y);
    }

    public sealed class RltlTrue<TPred> : Rltl<TPred>
    {
        public static readonly RltlTrue<TPred> Instance = new RltlTrue<TPred>();
        private RltlTrue() { }
        internal override int Kind => 0;
        public override bool Equals(Rltl<TPred> other) => other is RltlTrue<TPred>;
        protected override int ComputeHashCode() => 0x7F7F7F7F;
        protected override int CompareToSameKind(Rltl<TPred> other) => 0;
        public override string ToString() => "⊤";
    }

    public sealed class RltlFalse<TPred> : Rltl<TPred>
    {
        public static readonly RltlFalse<TPred> Instance = new RltlFalse<TPred>();
        private RltlFalse() { }
        internal override int Kind => 1;
        public override bool Equals(Rltl<TPred> other) => other is RltlFalse<TPred>;
        protected override int ComputeHashCode() => 0x3F3F3F3F;
        protected override int CompareToSameKind(Rltl<TPred> other) => 0;
        public override string ToString() => "⊥";
    }

    public sealed class RltlAtom<TPred> : Rltl<TPred>
    {
        /// <summary>
        /// Construct a positive atom. Negation flows into the predicate
        /// via <see cref="RltlAlgebra{TPred}.Not"/>.
        /// </summary>
        public RltlAtom(TPred p) { Predicate = p; }
        public TPred Predicate { get; }
        internal override int Kind => 2;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlAtom<TPred> a
               && EqualityComparer<TPred>.Default.Equals(Predicate, a.Predicate);
        protected override int ComputeHashCode()
            => EqualityComparer<TPred>.Default.GetHashCode(Predicate);
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var a = (RltlAtom<TPred>)other;
            return PredCompare<TPred>.Compare(Predicate, a.Predicate);
        }
        public override string ToString() => Predicate.ToString();
    }

    public sealed class RltlNext<TPred> : Rltl<TPred>
    {
        public RltlNext(Rltl<TPred> inner) { Inner = inner ?? throw new ArgumentNullException(nameof(inner)); }
        public Rltl<TPred> Inner { get; }
        internal override int Kind => 3;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlNext<TPred> n && Inner.Equals(n.Inner);
        protected override int ComputeHashCode() => unchecked(Inner.GetHashCode() * 13 + 3);
        protected override int CompareToSameKind(Rltl<TPred> other)
            => Inner.CompareTo(((RltlNext<TPred>)other).Inner);
        public override string ToString() => $"X({Inner})";
    }

    public sealed class RltlUntil<TPred> : Rltl<TPred>
    {
        public RltlUntil(Rltl<TPred> l, Rltl<TPred> r) { Left = l; Right = r; }
        public Rltl<TPred> Left { get; }
        public Rltl<TPred> Right { get; }
        internal override int Kind => 4;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlUntil<TPred> u && Left.Equals(u.Left) && Right.Equals(u.Right);
        protected override int ComputeHashCode() => unchecked(Left.GetHashCode() * 17 + Right.GetHashCode() * 19 + 4);
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var u = (RltlUntil<TPred>)other;
            int c = Left.CompareTo(u.Left);
            return c != 0 ? c : Right.CompareTo(u.Right);
        }
        public override string ToString()
            => Left is RltlTrue<TPred> ? $"F {Right}" : $"({Left} U {Right})";
    }

    public sealed class RltlRelease<TPred> : Rltl<TPred>
    {
        public RltlRelease(Rltl<TPred> l, Rltl<TPred> r) { Left = l; Right = r; }
        public Rltl<TPred> Left { get; }
        public Rltl<TPred> Right { get; }
        internal override int Kind => 5;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlRelease<TPred> r && Left.Equals(r.Left) && Right.Equals(r.Right);
        protected override int ComputeHashCode() => unchecked(Left.GetHashCode() * 23 + Right.GetHashCode() * 29 + 5);
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var r = (RltlRelease<TPred>)other;
            int c = Left.CompareTo(r.Left);
            return c != 0 ? c : Right.CompareTo(r.Right);
        }
        public override string ToString()
            => Left is RltlFalse<TPred> ? $"G {Right}" : $"({Left} R {Right})";
    }

    public sealed class RltlAnd<TPred> : Rltl<TPred>
    {
        internal RltlAnd(Rltl<TPred>[] ops) { Operands = ops; }
        public IReadOnlyList<Rltl<TPred>> Operands { get; }
        internal override int Kind => 6;
        public override bool Equals(Rltl<TPred> other)
        {
            if (!(other is RltlAnd<TPred> a)) return false;
            if (Operands.Count != a.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
                if (!Operands[i].Equals(a.Operands[i])) return false;
            return true;
        }
        protected override int ComputeHashCode()
        {
            unchecked { int h = 6; foreach (var o in Operands) h = h * 31 + o.GetHashCode(); return h; }
        }
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var a = (RltlAnd<TPred>)other;
            int c = Operands.Count.CompareTo(a.Operands.Count);
            if (c != 0) return c;
            for (int i = 0; i < Operands.Count; i++)
            {
                c = Operands[i].CompareTo(a.Operands[i]);
                if (c != 0) return c;
            }
            return 0;
        }
        public override string ToString() => "(" + string.Join(" ∧ ", Operands) + ")";
    }

    public sealed class RltlOr<TPred> : Rltl<TPred>
    {
        internal RltlOr(Rltl<TPred>[] ops) { Operands = ops; }
        public IReadOnlyList<Rltl<TPred>> Operands { get; }
        internal override int Kind => 7;
        public override bool Equals(Rltl<TPred> other)
        {
            if (!(other is RltlOr<TPred> a)) return false;
            if (Operands.Count != a.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
                if (!Operands[i].Equals(a.Operands[i])) return false;
            return true;
        }
        protected override int ComputeHashCode()
        {
            unchecked { int h = 7; foreach (var o in Operands) h = h * 31 + o.GetHashCode(); return h; }
        }
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var a = (RltlOr<TPred>)other;
            int c = Operands.Count.CompareTo(a.Operands.Count);
            if (c != 0) return c;
            for (int i = 0; i < Operands.Count; i++)
            {
                c = Operands[i].CompareTo(a.Operands[i]);
                if (c != 0) return c;
            }
            return 0;
        }
        public override string ToString() => "(" + string.Join(" ∨ ", Operands) + ")";
    }

    /// <summary>R ; φ — there exists a prefix matching R after which φ holds.</summary>
    public sealed class RltlSeqPrefix<TPred> : Rltl<TPred>
    {
        public RltlSeqPrefix(Ere<TPred> regex, Rltl<TPred> phi) { Regex = regex; Phi = phi; }
        public Ere<TPred> Regex { get; }
        public Rltl<TPred> Phi { get; }
        internal override int Kind => 8;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlSeqPrefix<TPred> s && Regex.Equals(s.Regex) && Phi.Equals(s.Phi);
        protected override int ComputeHashCode() => unchecked(Regex.GetHashCode() * 41 + Phi.GetHashCode() * 43 + 8);
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var s = (RltlSeqPrefix<TPred>)other;
            int c = Regex.CompareTo(s.Regex);
            return c != 0 ? c : Phi.CompareTo(s.Phi);
        }
        public override string ToString() => $"({Regex} ; {Phi})";
    }

    /// <summary>R : φ — overlapping match (length ≥ 1).</summary>
    public sealed class RltlOvlPrefix<TPred> : Rltl<TPred>
    {
        public RltlOvlPrefix(Ere<TPred> regex, Rltl<TPred> phi) { Regex = regex; Phi = phi; }
        public Ere<TPred> Regex { get; }
        public Rltl<TPred> Phi { get; }
        internal override int Kind => 9;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlOvlPrefix<TPred> s && Regex.Equals(s.Regex) && Phi.Equals(s.Phi);
        protected override int ComputeHashCode() => unchecked(Regex.GetHashCode() * 47 + Phi.GetHashCode() * 53 + 9);
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var s = (RltlOvlPrefix<TPred>)other;
            int c = Regex.CompareTo(s.Regex);
            return c != 0 ? c : Phi.CompareTo(s.Phi);
        }
        public override string ToString() => $"({Regex} : {Phi})";
    }

    /// <summary>R ⊳ φ — universal trigger (= ¬(R ; ¬φ)).</summary>
    public sealed class RltlTrigger<TPred> : Rltl<TPred>
    {
        public RltlTrigger(Ere<TPred> regex, Rltl<TPred> phi) { Regex = regex; Phi = phi; }
        public Ere<TPred> Regex { get; }
        public Rltl<TPred> Phi { get; }
        internal override int Kind => 10;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlTrigger<TPred> s && Regex.Equals(s.Regex) && Phi.Equals(s.Phi);
        protected override int ComputeHashCode() => unchecked(Regex.GetHashCode() * 59 + Phi.GetHashCode() * 61 + 10);
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var s = (RltlTrigger<TPred>)other;
            int c = Regex.CompareTo(s.Regex);
            return c != 0 ? c : Phi.CompareTo(s.Phi);
        }
        public override string ToString() => $"({Regex} ⊳ {Phi})";
    }

    /// <summary>R ⊳⊳ φ — universal overlapping match (= ¬(R : ¬φ)).</summary>
    public sealed class RltlMatch<TPred> : Rltl<TPred>
    {
        public RltlMatch(Ere<TPred> regex, Rltl<TPred> phi) { Regex = regex; Phi = phi; }
        public Ere<TPred> Regex { get; }
        public Rltl<TPred> Phi { get; }
        internal override int Kind => 11;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlMatch<TPred> s && Regex.Equals(s.Regex) && Phi.Equals(s.Phi);
        protected override int ComputeHashCode() => unchecked(Regex.GetHashCode() * 67 + Phi.GetHashCode() * 71 + 11);
        protected override int CompareToSameKind(Rltl<TPred> other)
        {
            var s = (RltlMatch<TPred>)other;
            int c = Regex.CompareTo(s.Regex);
            return c != 0 ? c : Phi.CompareTo(s.Phi);
        }
        public override string ToString() => $"({Regex} ⊳⊳ {Phi})";
    }

    /// <summary>{R} — weak closure (JACM eq. 2737).</summary>
    public sealed class RltlWeakClosure<TPred> : Rltl<TPred>
    {
        public RltlWeakClosure(Ere<TPred> regex) { Regex = regex ?? throw new ArgumentNullException(nameof(regex)); }
        public Ere<TPred> Regex { get; }
        internal override int Kind => 12;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlWeakClosure<TPred> s && Regex.Equals(s.Regex);
        protected override int ComputeHashCode() => unchecked(Regex.GetHashCode() * 73 + 12);
        protected override int CompareToSameKind(Rltl<TPred> other)
            => Regex.CompareTo(((RltlWeakClosure<TPred>)other).Regex);
        public override string ToString() => $"{{{Regex}}}";
    }

    /// <summary>{{R}}̄ — negated weak closure (JACM eq. 2747).</summary>
    public sealed class RltlNegWeakClosure<TPred> : Rltl<TPred>
    {
        public RltlNegWeakClosure(Ere<TPred> regex) { Regex = regex ?? throw new ArgumentNullException(nameof(regex)); }
        public Ere<TPred> Regex { get; }
        internal override int Kind => 13;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlNegWeakClosure<TPred> s && Regex.Equals(s.Regex);
        protected override int ComputeHashCode() => unchecked(Regex.GetHashCode() * 79 + 13);
        protected override int CompareToSameKind(Rltl<TPred> other)
            => Regex.CompareTo(((RltlNegWeakClosure<TPred>)other).Regex);
        public override string ToString() => $"¬{{{Regex}}}";
    }

    /// <summary>{R}ω — ω-closure (JACM eq. 2754). Not closed under negation (RLTL+).</summary>
    public sealed class RltlOmegaClosure<TPred> : Rltl<TPred>
    {
        public RltlOmegaClosure(Ere<TPred> regex) { Regex = regex ?? throw new ArgumentNullException(nameof(regex)); }
        public Ere<TPred> Regex { get; }
        internal override int Kind => 14;
        public override bool Equals(Rltl<TPred> other)
            => other is RltlOmegaClosure<TPred> s && Regex.Equals(s.Regex);
        protected override int ComputeHashCode() => unchecked(Regex.GetHashCode() * 83 + 14);
        protected override int CompareToSameKind(Rltl<TPred> other)
            => Regex.CompareTo(((RltlOmegaClosure<TPred>)other).Regex);
        public override string ToString() => $"{{{Regex}}}ω";
    }
}
