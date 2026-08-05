namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A symbolic LTL formula in negation normal form (NNF), generic over the predicate type.
    /// Negation is pushed to atoms during construction, so all internal nodes are positive.
    /// 
    /// Node types:
    /// <list type="bullet">
    ///   <item><see cref="LtlTrue{TPred}"/>: ⊤</item>
    ///   <item><see cref="LtlFalse{TPred}"/>: ⊥</item>
    ///   <item><see cref="LtlAtom{TPred}"/>: p or ¬p (predicate with optional negation)</item>
    ///   <item><see cref="LtlAnd{TPred}"/>: φ ∧ ψ (ACI-normalized)</item>
    ///   <item><see cref="LtlOr{TPred}"/>: φ ∨ ψ (ACI-normalized)</item>
    ///   <item><see cref="LtlNext{TPred}"/>: Xφ</item>
    ///   <item><see cref="LtlUntil{TPred}"/>: φ U ψ</item>
    ///   <item><see cref="LtlRelease{TPred}"/>: φ R ψ (dual of Until)</item>
    /// </list>
    /// </summary>
    public abstract class Ltl<TPred> : IEquatable<Ltl<TPred>>, IComparable<Ltl<TPred>>
    {
        private int? _hash;

        internal Ltl() { }

        /// <summary>Structural kind for ordering/comparison.</summary>
        internal abstract int Kind { get; }

        #region Factory Methods

        public static Ltl<TPred> True() => LtlTrue<TPred>.Instance;
        public static Ltl<TPred> False() => LtlFalse<TPred>.Instance;

        /// <summary>
        /// Constructs an atomic LTL formula carrying <paramref name="predicate"/>.
        /// Atoms hold only positive predicates; to negate, use
        /// <see cref="LtlAlgebra{TPred}.Not"/> which pushes the complement
        /// into the EBA via <c>eba.Not</c>.
        /// </summary>
        public static Ltl<TPred> Atom(TPred predicate) => new LtlAtom<TPred>(predicate);

        public static Ltl<TPred> Next(Ltl<TPred> inner)
        {
            if (inner is LtlTrue<TPred>) return LtlTrue<TPred>.Instance;
            if (inner is LtlFalse<TPred>) return LtlFalse<TPred>.Instance;
            return new LtlNext<TPred>(inner);
        }

        public static Ltl<TPred> Until(Ltl<TPred> left, Ltl<TPred> right)
        {
            // φ U ⊤ = ⊤; φ U ⊥ = ⊥ would require infinite left; ⊥ U ψ = ψ
            if (right is LtlTrue<TPred>) return LtlTrue<TPred>.Instance;
            if (left is LtlFalse<TPred>) return right;
            return new LtlUntil<TPred>(left, right);
        }

        public static Ltl<TPred> Release(Ltl<TPred> left, Ltl<TPred> right)
        {
            // φ R ⊥ = ⊥; φ R ⊤ = ⊤; ⊤ R ψ = ψ
            if (right is LtlFalse<TPred>) return LtlFalse<TPred>.Instance;
            if (right is LtlTrue<TPred>) return LtlTrue<TPred>.Instance;
            if (left is LtlTrue<TPred>) return right;
            return new LtlRelease<TPred>(left, right);
        }

        /// <summary>Eventually: Fφ = ⊤ U φ</summary>
        public static Ltl<TPred> Eventually(Ltl<TPred> phi) => Until(True(), phi);

        /// <summary>Globally: Gφ = ⊥ R φ</summary>
        public static Ltl<TPred> Globally(Ltl<TPred> phi) => Release(False(), phi);

        #endregion

        #region Equality and Comparison

        public abstract bool Equals(Ltl<TPred> other);
        public override bool Equals(object obj) => Equals(obj as Ltl<TPred>);

        public override int GetHashCode()
        {
            if (_hash == null)
                _hash = ComputeHashCode();
            return _hash.Value;
        }

        protected abstract int ComputeHashCode();

        public int CompareTo(Ltl<TPred> other)
        {
            if (other == null) return 1;
            if (ReferenceEquals(this, other)) return 0;
            int c = Kind.CompareTo(other.Kind);
            if (c != 0) return c;
            return CompareToSameKind(other);
        }

        protected abstract int CompareToSameKind(Ltl<TPred> other);

        public static bool operator ==(Ltl<TPred> a, Ltl<TPred> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }

        public static bool operator !=(Ltl<TPred> a, Ltl<TPred> b) => !(a == b);

        #endregion
    }

    /// <summary>Comparer for Ltl formulas, used in ACI normalization.</summary>
    internal class LtlComparer<TPred> : IComparer<Ltl<TPred>>
    {
        public static readonly LtlComparer<TPred> Instance = new LtlComparer<TPred>();
        public int Compare(Ltl<TPred> x, Ltl<TPred> y) => x.CompareTo(y);
    }

    #region Node Types

    public sealed class LtlTrue<TPred> : Ltl<TPred>
    {
        public static readonly LtlTrue<TPred> Instance = new LtlTrue<TPred>();
        private LtlTrue() { }
        internal override int Kind => 0;
        public override bool Equals(Ltl<TPred> other) => other is LtlTrue<TPred>;
        protected override int ComputeHashCode() => 0x7F7F7F7F;
        protected override int CompareToSameKind(Ltl<TPred> other) => 0;
        public override string ToString() => "⊤";
    }

    public sealed class LtlFalse<TPred> : Ltl<TPred>
    {
        public static readonly LtlFalse<TPred> Instance = new LtlFalse<TPred>();
        private LtlFalse() { }
        internal override int Kind => 1;
        public override bool Equals(Ltl<TPred> other) => other is LtlFalse<TPred>;
        protected override int ComputeHashCode() => 0x3F3F3F3F;
        protected override int CompareToSameKind(Ltl<TPred> other) => 0;
        public override string ToString() => "⊥";
    }

    public sealed class LtlAtom<TPred> : Ltl<TPred>
    {
        /// <summary>
        /// Construct an atom carrying <paramref name="predicate"/>.
        /// Atoms are <em>positive</em>: negation is folded into the
        /// predicate via the underlying EBA (see <see cref="LtlAlgebra{TPred}.Not"/>).
        /// </summary>
        public LtlAtom(TPred predicate)
        {
            Predicate = predicate;
        }

        public TPred Predicate { get; }

        internal override int Kind => 2;

        public override bool Equals(Ltl<TPred> other)
        {
            if (other is LtlAtom<TPred> atom)
                return EqualityComparer<TPred>.Default.Equals(Predicate, atom.Predicate);
            return false;
        }

        protected override int ComputeHashCode()
            => EqualityComparer<TPred>.Default.GetHashCode(Predicate);

        protected override int CompareToSameKind(Ltl<TPred> other)
        {
            var atom = (LtlAtom<TPred>)other;
            return PredCompare<TPred>.Compare(Predicate, atom.Predicate);
        }

        public override string ToString() => Predicate.ToString();
    }

    public sealed class LtlNext<TPred> : Ltl<TPred>
    {
        public LtlNext(Ltl<TPred> inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Ltl<TPred> Inner { get; }
        internal override int Kind => 3;

        public override bool Equals(Ltl<TPred> other)
            => other is LtlNext<TPred> n && Inner.Equals(n.Inner);

        protected override int ComputeHashCode()
        {
            unchecked { return Inner.GetHashCode() * 31 + 3; }
        }

        protected override int CompareToSameKind(Ltl<TPred> other)
            => Inner.CompareTo(((LtlNext<TPred>)other).Inner);

        public override string ToString() => $"X({Inner})";
    }

    public sealed class LtlUntil<TPred> : Ltl<TPred>
    {
        public LtlUntil(Ltl<TPred> left, Ltl<TPred> right)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public Ltl<TPred> Left { get; }
        public Ltl<TPred> Right { get; }
        internal override int Kind => 4;

        public override bool Equals(Ltl<TPred> other)
            => other is LtlUntil<TPred> u && Left.Equals(u.Left) && Right.Equals(u.Right);

        protected override int ComputeHashCode()
        {
            unchecked { return (Left.GetHashCode() * 31 + Right.GetHashCode()) * 31 + 4; }
        }

        protected override int CompareToSameKind(Ltl<TPred> other)
        {
            var u = (LtlUntil<TPred>)other;
            int c = Left.CompareTo(u.Left);
            return c != 0 ? c : Right.CompareTo(u.Right);
        }

        public override string ToString()
            => Left is LtlTrue<TPred> ? $"F {Right}" : $"({Left} U {Right})";
    }

    public sealed class LtlRelease<TPred> : Ltl<TPred>
    {
        public LtlRelease(Ltl<TPred> left, Ltl<TPred> right)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public Ltl<TPred> Left { get; }
        public Ltl<TPred> Right { get; }
        internal override int Kind => 5;

        public override bool Equals(Ltl<TPred> other)
            => other is LtlRelease<TPred> r && Left.Equals(r.Left) && Right.Equals(r.Right);

        protected override int ComputeHashCode()
        {
            unchecked { return (Left.GetHashCode() * 31 + Right.GetHashCode()) * 31 + 5; }
        }

        protected override int CompareToSameKind(Ltl<TPred> other)
        {
            var r = (LtlRelease<TPred>)other;
            int c = Left.CompareTo(r.Left);
            return c != 0 ? c : Right.CompareTo(r.Right);
        }

        public override string ToString()
            => Left is LtlFalse<TPred> ? $"G {Right}" : $"({Left} R {Right})";
    }

    public sealed class LtlAnd<TPred> : Ltl<TPred>
    {
        internal LtlAnd(Ltl<TPred>[] operands)
        {
            Operands = operands;
        }

        /// <summary>Sorted, deduplicated operands.</summary>
        public IReadOnlyList<Ltl<TPred>> Operands { get; }
        internal override int Kind => 6;

        public override bool Equals(Ltl<TPred> other)
        {
            if (!(other is LtlAnd<TPred> and)) return false;
            if (Operands.Count != and.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
                if (!Operands[i].Equals(and.Operands[i])) return false;
            return true;
        }

        protected override int ComputeHashCode()
        {
            unchecked
            {
                int h = 6;
                foreach (var op in Operands) h = h * 31 + op.GetHashCode();
                return h;
            }
        }

        protected override int CompareToSameKind(Ltl<TPred> other)
        {
            var and = (LtlAnd<TPred>)other;
            int c = Operands.Count.CompareTo(and.Operands.Count);
            if (c != 0) return c;
            for (int i = 0; i < Operands.Count; i++)
            {
                c = Operands[i].CompareTo(and.Operands[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        public override string ToString() => "(" + string.Join(" ∧ ", Operands) + ")";
    }

    public sealed class LtlOr<TPred> : Ltl<TPred>
    {
        internal LtlOr(Ltl<TPred>[] operands)
        {
            Operands = operands;
        }

        /// <summary>Sorted, deduplicated operands.</summary>
        public IReadOnlyList<Ltl<TPred>> Operands { get; }
        internal override int Kind => 7;

        public override bool Equals(Ltl<TPred> other)
        {
            if (!(other is LtlOr<TPred> or)) return false;
            if (Operands.Count != or.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
                if (!Operands[i].Equals(or.Operands[i])) return false;
            return true;
        }

        protected override int ComputeHashCode()
        {
            unchecked
            {
                int h = 7;
                foreach (var op in Operands) h = h * 31 + op.GetHashCode();
                return h;
            }
        }

        protected override int CompareToSameKind(Ltl<TPred> other)
        {
            var or = (LtlOr<TPred>)other;
            int c = Operands.Count.CompareTo(or.Operands.Count);
            if (c != 0) return c;
            for (int i = 0; i < Operands.Count; i++)
            {
                c = Operands[i].CompareTo(or.Operands[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        public override string ToString() => "(" + string.Join(" ∨ ", Operands) + ")";
    }

    #endregion
}
