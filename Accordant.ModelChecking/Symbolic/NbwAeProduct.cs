namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Discriminated-union state type used by the Æ-based NBW product
    /// (Section 5.3 of the JACM paper, "Symbolic Automata: Omega-Regularity
    /// Modulo Theories"). A state is either tagged Left (from N₁) or
    /// Right (from N₂); the two underlying state spaces remain disjoint.
    /// </summary>
    public sealed class EitherState<TLeft, TRight> : IEquatable<EitherState<TLeft, TRight>>
    {
        public bool IsLeft { get; }
        public TLeft Left { get; }
        public TRight Right { get; }

        private EitherState(bool isLeft, TLeft left, TRight right)
        {
            IsLeft = isLeft; Left = left; Right = right;
        }

        public static EitherState<TLeft, TRight> FromLeft(TLeft l)
            => new EitherState<TLeft, TRight>(true, l, default);
        public static EitherState<TLeft, TRight> FromRight(TRight r)
            => new EitherState<TLeft, TRight>(false, default, r);

        public bool Equals(EitherState<TLeft, TRight> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (IsLeft != other.IsLeft) return false;
            return IsLeft
                ? EqualityComparer<TLeft>.Default.Equals(Left, other.Left)
                : EqualityComparer<TRight>.Default.Equals(Right, other.Right);
        }

        public override bool Equals(object obj) => Equals(obj as EitherState<TLeft, TRight>);

        public override int GetHashCode()
        {
            unchecked
            {
                return IsLeft
                    ? EqualityComparer<TLeft>.Default.GetHashCode(Left) * 31 + 1
                    : EqualityComparer<TRight>.Default.GetHashCode(Right) * 31 + 2;
            }
        }

        public override string ToString() => IsLeft ? $"L({Left})" : $"R({Right})";

        public static IComparer<EitherState<TLeft, TRight>> GetComparer(
            IComparer<TLeft> leftCmp, IComparer<TRight> rightCmp)
            => new Comparer(leftCmp, rightCmp);

        private sealed class Comparer : IComparer<EitherState<TLeft, TRight>>
        {
            private readonly IComparer<TLeft> _l;
            private readonly IComparer<TRight> _r;
            public Comparer(IComparer<TLeft> l, IComparer<TRight> r) { _l = l; _r = r; }
            public int Compare(EitherState<TLeft, TRight> x, EitherState<TLeft, TRight> y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                if (x.IsLeft && !y.IsLeft) return -1;
                if (!x.IsLeft && y.IsLeft) return 1;
                return x.IsLeft ? _l.Compare(x.Left, y.Left) : _r.Compare(x.Right, y.Right);
            }
        }
    }

    /// <summary>
    /// NBW product modulo theories via alternation elimination
    /// (Section 5.3 of the JACM paper "Symbolic Automata: Omega-Regularity
    /// Modulo Theories"):
    /// <code>
    ///   N₁ × N₂ ≝ AElim(N₁ ∧ N₂)
    /// </code>
    /// where N₁ ∧ N₂ is the alternating Büchi automaton whose state
    /// space is the disjoint union <c>Q₁ ⊎ Q₂</c>, initial formula
    /// φ₀ = φ₀^N₁ ∧ φ₀^N₂ (conjunction in B⁺(Q)), transition function
    /// δ dispatches by tag, and accepting set F = F₁ ∪ F₂.
    ///
    /// <para>By Corollary 5.x in the paper, the resulting NBW has at most
    /// <c>4·|Q₁|·|Q₂|</c> breakpoint states, in only four shapes:
    /// <c>⟨{q₁,q₂},∅⟩</c>, <c>⟨∅,{q₁,q₂}⟩</c>, <c>⟨{q₁},{q₂}⟩</c>,
    /// <c>⟨{q₂},{q₁}⟩</c>. The alternation product on transition terms
    /// runs in <c>O(|N₁|·|N₂|)</c> SAT calls (vs the
    /// <c>O(2^{|N₁|+|N₂|})</c> bound required by classical NBW
    /// intersection if minterm-based bitblasting is needed for a common
    /// finite alphabet).</para>
    ///
    /// <para>Unlike <see cref="NbwProduct"/> (which uses the classical
    /// Büchi flag-trick over <c>Q₁ × Q₂ × {0,1,2}</c>), this construction
    /// stays in the symbolic world end-to-end: it neither materialises a
    /// minterm alphabet nor explodes the state space combinatorially.</para>
    /// </summary>
    public static class NbwAeProduct
    {
        /// <summary>
        /// Lifts an NBW into an ABW with the same state type. Each NBW
        /// transition list <c>[tt₁, …, ttₙ]</c> becomes the disjunction
        /// <c>tt₁ ∨ … ∨ ttₙ</c> as a single ABW transition term whose
        /// leaves are <see cref="Dnf{TState}"/> disjunctions of singleton
        /// clauses: a leaf <c>StateSet {r₁,…,rₖ}</c> becomes
        /// <c>{{r₁},…,{rₖ}}</c>. The initial formula is the disjunction
        /// of singleton clauses for each initial state.
        /// </summary>
        public static SymbolicABW<TPred, TElem, TState> NbwToAbw<TPred, TElem, TState>(
            SymbolicNBW<TPred, TElem, TState> nbw,
            IComparer<TState> stateOrd,
            IEqualityComparer<TState> stateEq = null)
        {
            if (nbw == null) throw new ArgumentNullException(nameof(nbw));
            if (stateOrd == null) throw new ArgumentNullException(nameof(stateOrd));

            var dnfAlg = new DnfAlgebra<TState>(stateOrd);
            var ssAlg = new StateSetLeafAlgebra<TState>(stateOrd);
            var srcTermAlg = new TransitionTermAlgebra<TPred, TElem, StateSet<TState>>(
                nbw.Eba, nbw.Registry, ssAlg);
            var dstTermAlg = new TransitionTermAlgebra<TPred, TElem, Dnf<TState>>(
                nbw.Eba, nbw.Registry, dnfAlg);

            Dnf<TState> LeafToDnf(StateSet<TState> set)
            {
                if (set.IsEmpty) return dnfAlg.Bottom;
                var clauses = new List<StateSet<TState>>(set.Count);
                foreach (var q in set)
                    clauses.Add(StateSet<TState>.Singleton(q, stateOrd));
                return dnfAlg.FromClauses(clauses);
            }

            // Initial: φ₀ = ⋁ s∈I {{s}}
            Dnf<TState> initial = nbw.InitialStates.Count == 0
                ? dnfAlg.Bottom
                : dnfAlg.FromClauses(nbw.InitialStates
                    .Select(s => StateSet<TState>.Singleton(s, stateOrd)));

            TransitionTerm<Dnf<TState>> Delta(TState s)
            {
                var list = nbw.GetTransition(s);
                var acc = dstTermAlg.Bottom;
                foreach (var tt in list)
                {
                    var mapped = srcTermAlg.MapUnary<Dnf<TState>>(tt, LeafToDnf);
                    acc = dstTermAlg.Or(acc, mapped);
                }
                return acc;
            }

            return new SymbolicABW<TPred, TElem, TState>(
                nbw.Eba, nbw.Registry, dnfAlg, initial, nbw.IsAccepting, Delta, stateEq);
        }

        /// <summary>
        /// Conjoins two compatible ABWs (sharing the same EBA / condition
        /// registry) into a single ABW over <see cref="EitherState{L,R}"/>.
        /// Initial formula is the B⁺(Q) conjunction
        /// <c>φ₀^A ∧ φ₀^B</c> (distributed into DNF).
        /// Transitions dispatch by tag; F = F₁ ∪ F₂.
        /// </summary>
        public static SymbolicABW<TPred, TElem, EitherState<L, R>> ConjoinAbw<TPred, TElem, L, R>(
            SymbolicABW<TPred, TElem, L> a,
            SymbolicABW<TPred, TElem, R> b,
            IComparer<L> ordL,
            IComparer<R> ordR)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (!ReferenceEquals(a.Registry, b.Registry))
                throw new ArgumentException(
                    "Conjoined ABWs must share the same ConditionRegistry.");

            var eitherOrd = EitherState<L, R>.GetComparer(ordL, ordR);
            var eitherEq = EqualityComparer<EitherState<L, R>>.Default;
            var dnfAlg = new DnfAlgebra<EitherState<L, R>>(eitherOrd);
            var dstTermAlg = new TransitionTermAlgebra<TPred, TElem, Dnf<EitherState<L, R>>>(
                a.Eba, a.Registry, dnfAlg);

            // Helpers: map a Dnf<L> / Dnf<R> leaf into the tagged DnfAlgebra.
            Dnf<EitherState<L, R>> EmbedL(Dnf<L> d)
            {
                if (d.IsFalse) return dnfAlg.Bottom;
                if (d.IsTrue) return dnfAlg.Top;
                var clauses = new List<StateSet<EitherState<L, R>>>(d.Clauses.Count);
                foreach (var clause in d.Clauses)
                {
                    var lifted = new List<EitherState<L, R>>(clause.Count);
                    foreach (var s in clause) lifted.Add(EitherState<L, R>.FromLeft(s));
                    lifted.Sort(eitherOrd);
                    clauses.Add(new StateSet<EitherState<L, R>>(lifted, eitherOrd));
                }
                return dnfAlg.FromClauses(clauses);
            }
            Dnf<EitherState<L, R>> EmbedR(Dnf<R> d)
            {
                if (d.IsFalse) return dnfAlg.Bottom;
                if (d.IsTrue) return dnfAlg.Top;
                var clauses = new List<StateSet<EitherState<L, R>>>(d.Clauses.Count);
                foreach (var clause in d.Clauses)
                {
                    var lifted = new List<EitherState<L, R>>(clause.Count);
                    foreach (var s in clause) lifted.Add(EitherState<L, R>.FromRight(s));
                    lifted.Sort(eitherOrd);
                    clauses.Add(new StateSet<EitherState<L, R>>(lifted, eitherOrd));
                }
                return dnfAlg.FromClauses(clauses);
            }

            // Source-side term algebras for cross-type MapUnary.
            var srcAlgL = new TransitionTermAlgebra<TPred, TElem, Dnf<L>>(
                a.Eba, a.Registry, a.DnfAlgebra);
            var srcAlgR = new TransitionTermAlgebra<TPred, TElem, Dnf<R>>(
                b.Eba, b.Registry, b.DnfAlgebra);

            // Initial: φ₀^A ∧ φ₀^B (distributed into DNF over EitherState).
            Dnf<EitherState<L, R>> initial = dnfAlg.And(
                EmbedL(a.InitialState), EmbedR(b.InitialState));

            TransitionTerm<Dnf<EitherState<L, R>>> Delta(EitherState<L, R> s)
            {
                if (s.IsLeft)
                {
                    var srcTt = a.GetTransition(s.Left);
                    return srcAlgL.MapUnary<Dnf<EitherState<L, R>>>(srcTt, EmbedL);
                }
                else
                {
                    var srcTt = b.GetTransition(s.Right);
                    return srcAlgR.MapUnary<Dnf<EitherState<L, R>>>(srcTt, EmbedR);
                }
            }

            bool IsAccepting(EitherState<L, R> s)
                => s.IsLeft ? a.IsAccepting(s.Left) : b.IsAccepting(s.Right);

            return new SymbolicABW<TPred, TElem, EitherState<L, R>>(
                a.Eba, a.Registry, dnfAlg, initial, IsAccepting, Delta, eitherEq);
        }

        /// <summary>
        /// Æ-based product of two compatible symbolic NBWs:
        /// <c>N₁ × N₂ = AElim(NbwToAbw(N₁) ∧ NbwToAbw(N₂))</c>.
        /// Returns a lazy NBW over <see cref="BreakpointState{TState}"/>
        /// of <see cref="EitherState{L,R}"/>. The construction is purely
        /// on-demand: only breakpoints visited by the caller (e.g.,
        /// <see cref="NestedDfsCheck"/> / <see cref="SccProductCheck"/>)
        /// are expanded.
        /// </summary>
        public static SymbolicNBW<TPred, TElem, BreakpointState<EitherState<L, R>>>
            Product<TPred, TElem, L, R>(
                SymbolicNBW<TPred, TElem, L> n1,
                SymbolicNBW<TPred, TElem, R> n2,
                IComparer<L> ordL,
                IComparer<R> ordR,
                IEqualityComparer<L> eqL = null,
                IEqualityComparer<R> eqR = null)
        {
            var abw1 = NbwToAbw(n1, ordL, eqL);
            var abw2 = NbwToAbw(n2, ordR, eqR);
            var abwConj = ConjoinAbw(abw1, abw2, ordL, ordR);
            var ae = new IncrementalAE<TPred, TElem, EitherState<L, R>>(abwConj);
            return ae.ToNBW();
        }
    }
}
