namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// EBA-aware factory for <see cref="Ltl{TPred}"/> formulas. Pushes all
    /// boolean combinations of atomic predicates into the underlying
    /// <see cref="IPredicateAlgebra{TPred}"/> so they become single
    /// <see cref="LtlAtom{TPred}"/> nodes carrying a fused predicate.
    ///
    /// Consequences:
    /// <list type="bullet">
    ///   <item><c>Not(LtlAtom(p)) = LtlAtom(eba.Not(p))</c> — atoms hold only
    ///   positive predicates; negation flows into the EBA.</item>
    ///   <item><c>And(LtlAtom(p), LtlAtom(q)) = LtlAtom(eba.And(p,q))</c>
    ///   (collapses to <c>False</c> when <c>eba.IsSatisfiable</c> says so;
    ///   in particular <c>p ∧ ¬p ⇒ ⊥</c> via the EBA).</item>
    ///   <item><c>Or(LtlAtom(p), LtlAtom(q)) = LtlAtom(eba.Or(p,q))</c>
    ///   (collapses to <c>True</c> when <c>eba.Not(eba.Or(p,q))</c> is
    ///   unsatisfiable; in particular <c>p ∨ ¬p ⇒ ⊤</c>).</item>
    /// </list>
    ///
    /// All non-boolean structural factories (<see cref="Ltl{TPred}.Next"/>,
    /// <see cref="Ltl{TPred}.Until"/>, <see cref="Ltl{TPred}.Release"/>) are
    /// re-exposed here as instance methods for uniformity.
    /// </summary>
    public sealed class LtlAlgebra<TPred>
    {
        private readonly IPredicateAlgebra<TPred> _eba;

        public LtlAlgebra(IPredicateAlgebra<TPred> eba)
        {
            _eba = eba ?? throw new ArgumentNullException(nameof(eba));
        }

        /// <summary>The underlying predicate algebra.</summary>
        public IPredicateAlgebra<TPred> Eba => _eba;

        public Ltl<TPred> True => LtlTrue<TPred>.Instance;
        public Ltl<TPred> False => LtlFalse<TPred>.Instance;

        /// <summary>
        /// Builds an atom for <paramref name="p"/>, canonicalising
        /// <c>eba.Top</c>/<c>eba.Bottom</c> into <see cref="True"/>/<see cref="False"/>.
        /// </summary>
        public Ltl<TPred> Atom(TPred p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (EqualityComparer<TPred>.Default.Equals(p, _eba.Top)) return True;
            if (EqualityComparer<TPred>.Default.Equals(p, _eba.Bottom)) return False;
            return new LtlAtom<TPred>(p);
        }

        /// <summary>Negative atom: <c>¬p</c> represented as <c>Atom(eba.Not(p))</c>.</summary>
        public Ltl<TPred> NegAtom(TPred p) => Atom(_eba.Not(p));

        public Ltl<TPred> Next(Ltl<TPred> inner) => Ltl<TPred>.Next(inner);
        public Ltl<TPred> Until(Ltl<TPred> l, Ltl<TPred> r) => Ltl<TPred>.Until(l, r);
        public Ltl<TPred> Release(Ltl<TPred> l, Ltl<TPred> r) => Ltl<TPred>.Release(l, r);
        public Ltl<TPred> Eventually(Ltl<TPred> p) => Until(True, p);
        public Ltl<TPred> Globally(Ltl<TPred> p) => Release(False, p);

        /// <summary>NNF negation that pushes complement into atomic predicates via the EBA.</summary>
        public Ltl<TPred> Not(Ltl<TPred> f)
        {
            switch (f)
            {
                case LtlTrue<TPred> _:   return False;
                case LtlFalse<TPred> _:  return True;
                case LtlAtom<TPred> a:   return Atom(_eba.Not(a.Predicate));
                case LtlNext<TPred> n:   return Next(Not(n.Inner));
                case LtlUntil<TPred> u:  return Release(Not(u.Left), Not(u.Right));
                case LtlRelease<TPred> r:return Until(Not(r.Left), Not(r.Right));
                case LtlAnd<TPred> a:    return OrMany(a.Operands.Select(Not));
                case LtlOr<TPred> o:     return AndMany(o.Operands.Select(Not));
                default: throw new ArgumentException($"Unknown formula type: {f.GetType()}");
            }
        }

        public Ltl<TPred> Implies(Ltl<TPred> a, Ltl<TPred> b) => Or(Not(a), b);

        public Ltl<TPred> And(Ltl<TPred> a, Ltl<TPred> b)
        {
            if (a is LtlFalse<TPred> || b is LtlFalse<TPred>) return False;
            if (a is LtlTrue<TPred>) return b;
            if (b is LtlTrue<TPred>) return a;

            var operands = new SortedSet<Ltl<TPred>>(LtlComparer<TPred>.Instance);
            CollectAnd(a, operands);
            CollectAnd(b, operands);
            return FuseAndAtoms(operands);
        }

        public Ltl<TPred> Or(Ltl<TPred> a, Ltl<TPred> b)
        {
            if (a is LtlTrue<TPred> || b is LtlTrue<TPred>) return True;
            if (a is LtlFalse<TPred>) return b;
            if (b is LtlFalse<TPred>) return a;

            var operands = new SortedSet<Ltl<TPred>>(LtlComparer<TPred>.Instance);
            CollectOr(a, operands);
            CollectOr(b, operands);
            return FuseOrAtoms(operands);
        }

        public Ltl<TPred> And(params Ltl<TPred>[] formulas)
            => formulas.Aggregate(True, And);

        public Ltl<TPred> Or(params Ltl<TPred>[] formulas)
            => formulas.Aggregate(False, Or);

        private Ltl<TPred> AndMany(IEnumerable<Ltl<TPred>> ops)
            => ops.Aggregate(True, And);

        private Ltl<TPred> OrMany(IEnumerable<Ltl<TPred>> ops)
            => ops.Aggregate(False, Or);

        private static void CollectAnd(Ltl<TPred> f, SortedSet<Ltl<TPred>> s)
        {
            if (f is LtlAnd<TPred> a) foreach (var op in a.Operands) s.Add(op);
            else s.Add(f);
        }

        private static void CollectOr(Ltl<TPred> f, SortedSet<Ltl<TPred>> s)
        {
            if (f is LtlOr<TPred> o) foreach (var op in o.Operands) s.Add(op);
            else s.Add(f);
        }

        /// <summary>
        /// Fold all <see cref="LtlAtom{TPred}"/> operands of an And into a
        /// single atom via <c>eba.And</c>. If the fused predicate is
        /// unsatisfiable, short-circuits to <see cref="False"/>.
        /// </summary>
        private Ltl<TPred> FuseAndAtoms(SortedSet<Ltl<TPred>> operands)
        {
            TPred fused = default;
            bool hasAtom = false;
            var nonAtoms = new List<Ltl<TPred>>();
            foreach (var op in operands)
            {
                if (op is LtlAtom<TPred> atom)
                {
                    fused = hasAtom ? _eba.And(fused, atom.Predicate) : atom.Predicate;
                    hasAtom = true;
                }
                else
                {
                    nonAtoms.Add(op);
                }
            }

            if (hasAtom)
            {
                if (!_eba.IsSatisfiable(fused)) return False;
                var fusedAtom = Atom(fused);
                if (fusedAtom is LtlFalse<TPred>) return False;
                if (fusedAtom is LtlTrue<TPred>)
                {
                    if (nonAtoms.Count == 0) return True;
                    if (nonAtoms.Count == 1) return nonAtoms[0];
                    return new LtlAnd<TPred>(nonAtoms.ToArray());
                }
                nonAtoms.Add(fusedAtom);
            }

            if (nonAtoms.Count == 0) return True;
            if (nonAtoms.Count == 1) return nonAtoms[0];
            var sorted = new SortedSet<Ltl<TPred>>(nonAtoms, LtlComparer<TPred>.Instance);
            return new LtlAnd<TPred>(sorted.ToArray());
        }

        /// <summary>
        /// Fold all <see cref="LtlAtom{TPred}"/> operands of an Or into a
        /// single atom via <c>eba.Or</c>. If the fused predicate is a
        /// tautology (its complement is unsatisfiable), short-circuits to
        /// <see cref="True"/>.
        /// </summary>
        private Ltl<TPred> FuseOrAtoms(SortedSet<Ltl<TPred>> operands)
        {
            TPred fused = default;
            bool hasAtom = false;
            var nonAtoms = new List<Ltl<TPred>>();
            foreach (var op in operands)
            {
                if (op is LtlAtom<TPred> atom)
                {
                    fused = hasAtom ? _eba.Or(fused, atom.Predicate) : atom.Predicate;
                    hasAtom = true;
                }
                else
                {
                    nonAtoms.Add(op);
                }
            }

            if (hasAtom)
            {
                if (!_eba.IsSatisfiable(_eba.Not(fused))) return True;
                var fusedAtom = Atom(fused);
                if (fusedAtom is LtlTrue<TPred>) return True;
                if (fusedAtom is LtlFalse<TPred>)
                {
                    if (nonAtoms.Count == 0) return False;
                    if (nonAtoms.Count == 1) return nonAtoms[0];
                    return new LtlOr<TPred>(nonAtoms.ToArray());
                }
                nonAtoms.Add(fusedAtom);
            }

            if (nonAtoms.Count == 0) return False;
            if (nonAtoms.Count == 1) return nonAtoms[0];
            var sorted = new SortedSet<Ltl<TPred>>(nonAtoms, LtlComparer<TPred>.Instance);
            return new LtlOr<TPred>(sorted.ToArray());
        }
    }

    /// <summary>
    /// Convenience access to a default <see cref="LtlAlgebra{TPred}"/> for
    /// the common case of <see cref="IStatePredicate"/> predicates over
    /// model-program states.
    /// </summary>
    public static class LtlAlgebra
    {
        /// <summary>
        /// Default LTL algebra over <see cref="IStatePredicate"/>. The
        /// underlying EBA is resolved on each access through
        /// <see cref="StatePropEbaProvider.Default"/> so that backends
        /// registered after type-load (e.g. via module initializers) take
        /// effect.
        /// </summary>
        public static LtlAlgebra<IStatePredicate> Default
            => new LtlAlgebra<IStatePredicate>(StatePropEbaProvider.Default);
    }
}
