namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// EBA-aware factory for <see cref="Rltl{TPred}"/> formulas — mirrors
    /// <see cref="LtlAlgebra{TPred}"/> for RLTL. Boolean combinations of
    /// atomic predicates flow into the EBA and become single
    /// <see cref="RltlAtom{TPred}"/> nodes; regex-prefix operators
    /// (<see cref="SeqPrefix"/>, <see cref="OvlPrefix"/>,
    /// <see cref="Trigger"/>, <see cref="Match"/>) are delegated to the
    /// existing static smart constructors.
    /// </summary>
    public sealed class RltlAlgebra<TPred>
    {
        private readonly IPredicateAlgebra<TPred> _eba;
        private readonly IEreCanonicalizer<TPred> _ereCanon;

        public RltlAlgebra(IPredicateAlgebra<TPred> eba)
            : this(eba, null)
        {
        }

        /// <summary>
        /// Constructs an RLTL algebra with an optional ERE canonicaliser
        /// (G8-c). When supplied, every embedded regex passed to
        /// <see cref="SeqPrefix"/>, <see cref="OvlPrefix"/>,
        /// <see cref="Trigger"/>, <see cref="Match"/>, <see cref="WeakClosure"/>,
        /// <see cref="NegWeakClosure"/>, or <see cref="OmegaClosure"/> is
        /// replaced by the canonical representative of its language-equivalence
        /// class. Combined with RLTL hash-consing this turns RLTL structural
        /// equality into RLTL equality modulo embedded-ERE equivalence.
        /// </summary>
        public RltlAlgebra(
            IPredicateAlgebra<TPred> eba,
            IEreCanonicalizer<TPred> ereCanonicalizer)
        {
            _eba = eba ?? throw new ArgumentNullException(nameof(eba));
            _ereCanon = ereCanonicalizer;
        }

        public IPredicateAlgebra<TPred> Eba => _eba;

        /// <summary>
        /// The active ERE canonicaliser, or <c>null</c> when none was supplied.
        /// </summary>
        public IEreCanonicalizer<TPred> EreCanonicalizer => _ereCanon;

        private Ere<TPred> Canon(Ere<TPred> r)
            => _ereCanon != null ? _ereCanon.Canonicalize(r) : r;

        public Rltl<TPred> True => RltlTrue<TPred>.Instance;
        public Rltl<TPred> False => RltlFalse<TPred>.Instance;

        public Rltl<TPred> Atom(TPred p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (EqualityComparer<TPred>.Default.Equals(p, _eba.Top)) return True;
            if (EqualityComparer<TPred>.Default.Equals(p, _eba.Bottom)) return False;
            return Rltl<TPred>.Atom(p);
        }

        public Rltl<TPred> NegAtom(TPred p) => Atom(_eba.Not(p));

        public Rltl<TPred> Next(Rltl<TPred> inner) => Rltl<TPred>.Next(inner);
        public Rltl<TPred> Until(Rltl<TPred> l, Rltl<TPred> r) => Rltl<TPred>.Until(l, r);
        public Rltl<TPred> Release(Rltl<TPred> l, Rltl<TPred> r) => Rltl<TPred>.Release(l, r);
        public Rltl<TPred> Eventually(Rltl<TPred> p) => Until(True, p);
        public Rltl<TPred> Globally(Rltl<TPred> p) => Release(False, p);

        public Rltl<TPred> SeqPrefix(Ere<TPred> r, Rltl<TPred> phi) => Rltl<TPred>.SeqPrefix(Canon(r), phi);
        public Rltl<TPred> OvlPrefix(Ere<TPred> r, Rltl<TPred> phi) => Rltl<TPred>.OvlPrefix(Canon(r), phi);
        public Rltl<TPred> Trigger(Ere<TPred> r, Rltl<TPred> phi) => Rltl<TPred>.Trigger(Canon(r), phi);
        public Rltl<TPred> Match(Ere<TPred> r, Rltl<TPred> phi) => Rltl<TPred>.Match(Canon(r), phi);
        public Rltl<TPred> WeakClosure(Ere<TPred> r) => Rltl<TPred>.WeakClosure(Canon(r));
        public Rltl<TPred> NegWeakClosure(Ere<TPred> r) => Rltl<TPred>.NegWeakClosure(Canon(r));
        public Rltl<TPred> OmegaClosure(Ere<TPred> r) => Rltl<TPred>.OmegaClosure(Canon(r));

        public Rltl<TPred> Not(Rltl<TPred> f)
        {
            switch (f)
            {
                case RltlTrue<TPred> _:    return False;
                case RltlFalse<TPred> _:   return True;
                case RltlAtom<TPred> a:    return Atom(_eba.Not(a.Predicate));
                case RltlNext<TPred> n:    return Next(Not(n.Inner));
                case RltlUntil<TPred> u:   return Release(Not(u.Left), Not(u.Right));
                case RltlRelease<TPred> r: return Until(Not(r.Left), Not(r.Right));
                case RltlAnd<TPred> a:     return OrMany(a.Operands.Select(Not));
                case RltlOr<TPred> o:      return AndMany(o.Operands.Select(Not));
                // Regex-prefix duals (Section 7 NNF):
                //   ¬(R ; φ)  = R ⊳  ¬φ
                //   ¬(R : φ)  = R ⊳⊳ ¬φ
                //   ¬(R ⊳ φ)  = R ;  ¬φ
                //   ¬(R ⊳⊳ φ) = R :  ¬φ
                case RltlSeqPrefix<TPred> s: return Trigger(s.Regex, Not(s.Phi));
                case RltlOvlPrefix<TPred> s: return Match(s.Regex, Not(s.Phi));
                case RltlTrigger<TPred> s:   return SeqPrefix(s.Regex, Not(s.Phi));
                case RltlMatch<TPred> s:     return OvlPrefix(s.Regex, Not(s.Phi));
                // Closure duals — JACM Def. RLTLp (line 2779):
                //   ¬{R}    = {{R}}̄
                //   ¬{{R}}̄ = {R}
                // ω-closure is *not* closed under negation in RLTL+ (line 2781).
                case RltlWeakClosure<TPred> w:    return NegWeakClosure(w.Regex);
                case RltlNegWeakClosure<TPred> n: return WeakClosure(n.Regex);
                case RltlOmegaClosure<TPred> _:
                    throw new NotSupportedException(
                        "Negated ω-closure is not in RLTL+. The ω-closure operator "
                        + "must not occur in a negative position.");
                default: throw new ArgumentException($"Unknown RLTL node: {f.GetType()}");
            }
        }

        public Rltl<TPred> Implies(Rltl<TPred> a, Rltl<TPred> b) => Or(Not(a), b);

        public Rltl<TPred> And(Rltl<TPred> a, Rltl<TPred> b)
        {
            if (a is RltlFalse<TPred> || b is RltlFalse<TPred>) return False;
            if (a is RltlTrue<TPred>) return b;
            if (b is RltlTrue<TPred>) return a;
            var ops = new SortedSet<Rltl<TPred>>(RltlComparer<TPred>.Instance);
            CollectAnd(a, ops); CollectAnd(b, ops);
            return FuseAndAtoms(ops);
        }

        public Rltl<TPred> Or(Rltl<TPred> a, Rltl<TPred> b)
        {
            if (a is RltlTrue<TPred> || b is RltlTrue<TPred>) return True;
            if (a is RltlFalse<TPred>) return b;
            if (b is RltlFalse<TPred>) return a;
            var ops = new SortedSet<Rltl<TPred>>(RltlComparer<TPred>.Instance);
            CollectOr(a, ops); CollectOr(b, ops);
            return FuseOrAtoms(ops);
        }

        public Rltl<TPred> And(params Rltl<TPred>[] f) => f.Aggregate(True, And);
        public Rltl<TPred> Or(params Rltl<TPred>[] f) => f.Aggregate(False, Or);
        private Rltl<TPred> AndMany(IEnumerable<Rltl<TPred>> f) => f.Aggregate(True, And);
        private Rltl<TPred> OrMany(IEnumerable<Rltl<TPred>> f) => f.Aggregate(False, Or);

        private static void CollectAnd(Rltl<TPred> f, SortedSet<Rltl<TPred>> s)
        {
            if (f is RltlAnd<TPred> a) foreach (var op in a.Operands) s.Add(op);
            else s.Add(f);
        }

        private static void CollectOr(Rltl<TPred> f, SortedSet<Rltl<TPred>> s)
        {
            if (f is RltlOr<TPred> o) foreach (var op in o.Operands) s.Add(op);
            else s.Add(f);
        }

        private Rltl<TPred> FuseAndAtoms(SortedSet<Rltl<TPred>> ops)
        {
            TPred fused = default;
            bool hasAtom = false;
            var nonAtoms = new List<Rltl<TPred>>();
            foreach (var op in ops)
            {
                if (op is RltlAtom<TPred> atom)
                {
                    fused = hasAtom ? _eba.And(fused, atom.Predicate) : atom.Predicate;
                    hasAtom = true;
                }
                else nonAtoms.Add(op);
            }

            if (hasAtom)
            {
                if (!_eba.IsSatisfiable(fused)) return False;
                var fusedAtom = Atom(fused);
                if (fusedAtom is RltlFalse<TPred>) return False;
                if (fusedAtom is RltlTrue<TPred>)
                {
                    if (nonAtoms.Count == 0) return True;
                    if (nonAtoms.Count == 1) return nonAtoms[0];
                    return new RltlAnd<TPred>(nonAtoms.ToArray());
                }
                nonAtoms.Add(fusedAtom);
            }

            if (nonAtoms.Count == 0) return True;
            if (nonAtoms.Count == 1) return nonAtoms[0];
            var sorted = new SortedSet<Rltl<TPred>>(nonAtoms, RltlComparer<TPred>.Instance);
            return new RltlAnd<TPred>(sorted.ToArray());
        }

        private Rltl<TPred> FuseOrAtoms(SortedSet<Rltl<TPred>> ops)
        {
            TPred fused = default;
            bool hasAtom = false;
            var nonAtoms = new List<Rltl<TPred>>();
            foreach (var op in ops)
            {
                if (op is RltlAtom<TPred> atom)
                {
                    fused = hasAtom ? _eba.Or(fused, atom.Predicate) : atom.Predicate;
                    hasAtom = true;
                }
                else nonAtoms.Add(op);
            }

            if (hasAtom)
            {
                if (!_eba.IsSatisfiable(_eba.Not(fused))) return True;
                var fusedAtom = Atom(fused);
                if (fusedAtom is RltlTrue<TPred>) return True;
                if (fusedAtom is RltlFalse<TPred>)
                {
                    if (nonAtoms.Count == 0) return False;
                    if (nonAtoms.Count == 1) return nonAtoms[0];
                    return new RltlOr<TPred>(nonAtoms.ToArray());
                }
                nonAtoms.Add(fusedAtom);
            }

            if (nonAtoms.Count == 0) return False;
            if (nonAtoms.Count == 1) return nonAtoms[0];
            var sorted = new SortedSet<Rltl<TPred>>(nonAtoms, RltlComparer<TPred>.Instance);
            return new RltlOr<TPred>(sorted.ToArray());
        }
    }

    /// <summary>Default <see cref="RltlAlgebra{TPred}"/> over <see cref="IStatePredicate"/>.</summary>
    public static class RltlAlgebra
    {
        /// <summary>
        /// Default RLTL algebra over <see cref="IStatePredicate"/>. Resolved
        /// on each access through <see cref="StatePropEbaProvider.Default"/>.
        /// </summary>
        public static RltlAlgebra<IStatePredicate> Default
            => new RltlAlgebra<IStatePredicate>(StatePropEbaProvider.Default);
    }
}
