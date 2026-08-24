namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Minimal leaf algebra for StateSet leaves in transition terms.
    /// Used when constructing TransitionTermAlgebra instances for NBW operations.
    /// Bottom = empty set, Top throws (not applicable for NBW transitions).
    /// Or = union, And = intersection (for path merging), Not = not supported.
    /// </summary>
    internal sealed class StateSetLeafAlgebra<TState> : ILeafAlgebra<StateSet<TState>>
    {
        private readonly IComparer<TState> _comparer;
        private readonly StateSet<TState> _bottom;

        public StateSetLeafAlgebra(IComparer<TState> comparer)
        {
            _comparer = comparer;
            _bottom = StateSet<TState>.Empty(comparer);
        }

        public StateSet<TState> Top => throw new NotSupportedException(
            "Top is not defined for StateSet leaf algebra in NBW context.");

        public StateSet<TState> Bottom => _bottom;

        public StateSet<TState> Or(StateSet<TState> a, StateSet<TState> b) => a.Union(b);

        public StateSet<TState> And(StateSet<TState> a, StateSet<TState> b) => a.Intersect(b);

        public StateSet<TState> Not(StateSet<TState> a) => throw new NotSupportedException(
            "Negation is not defined for StateSet leaf algebra.");

        public StateSet<TState> Xor(StateSet<TState> a, StateSet<TState> b) => throw new NotSupportedException(
            "XOR is not defined for StateSet leaf algebra (requires negation).");

        public bool IsBottom(StateSet<TState> a) => a.IsEmpty;

        public bool IsTop(StateSet<TState> a) => false;

        public IEqualityComparer<StateSet<TState>> Comparer => EqualityComparer<StateSet<TState>>.Default;
    }

    /// <summary>
    /// Product (intersection) of two Symbolic NBWs.
    /// 
    /// Given NBW₁ = (Q₁, I₁, δ₁, F₁) and NBW₂ = (Q₂, I₂, δ₂, F₂) over the same EBA,
    /// produces NBW = (Q₁ × Q₂ × {0,1,2}, I₁ × I₂ × {0}, δ, F) where:
    /// 
    /// The flag tracks acceptance progress:
    ///   0 → waiting for F₁ acceptance (transitions to 1 when q₁' ∈ F₁)
    ///   1 → waiting for F₂ acceptance (transitions to 2 when q₂' ∈ F₂)
    ///   2 → accepting state (immediately resets to 0)
    /// 
    /// F = Q₁ × Q₂ × {2} (states with flag=2 are accepting)
    /// 
    /// This is the standard index-based construction for Büchi intersection,
    /// lifted to the symbolic setting where transitions are TTerm⟨A, StateSet⟩.
    /// 
    /// Per the paper (Section 5): the product of two NBWs is a special case
    /// of the Æ algorithm, but this direct construction avoids the overhead
    /// of converting to ABW and applying full Miyano-Hayashi.
    /// </summary>
    public static class NbwProduct
    {
        /// <summary>
        /// Computes the product (intersection) of two symbolic NBWs.
        /// Both NBWs must share the same EBA and ConditionRegistry.
        /// </summary>
        public static SymbolicNBW<TPredicate, TElement, ProductState<TState1, TState2>>
            Intersect<TPredicate, TElement, TState1, TState2>(
                SymbolicNBW<TPredicate, TElement, TState1> nbw1,
                SymbolicNBW<TPredicate, TElement, TState2> nbw2,
                IComparer<TState1> comparer1 = null,
                IComparer<TState2> comparer2 = null)
        {
            comparer1 = comparer1 ?? Comparer<TState1>.Default;
            comparer2 = comparer2 ?? Comparer<TState2>.Default;
            var productComparer = new ProductStateComparer<TState1, TState2>(comparer1, comparer2);
            var productEqComparer = new ProductStateEqualityComparer<TState1, TState2>(
                EqualityComparer<TState1>.Default, EqualityComparer<TState2>.Default);

            // Algebra for the left NBW's transition terms (needed for ApplyCross)
            var algebra1 = new TransitionTermAlgebra<TPredicate, TElement, StateSet<TState1>>(
                nbw1.Eba, nbw1.Registry, new StateSetLeafAlgebra<TState1>(comparer1));

            // Initial states: I₁ × I₂ × {initial flag}
            var initialStates = new List<ProductState<TState1, TState2>>();
            foreach (var q1 in nbw1.InitialStates)
            {
                foreach (var q2 in nbw2.InitialStates)
                {
                    // Start at flag 0; if q1 ∈ F₁ start at 1; if also q2 ∈ F₂ start at 2
                    int flag = 0;
                    if (nbw1.IsAccepting(q1))
                        flag = 1;
                    if (flag == 1 && nbw2.IsAccepting(q2))
                        flag = 2;
                    initialStates.Add(new ProductState<TState1, TState2>(q1, q2, flag));
                }
            }

            // Lazy transition function
            IReadOnlyList<TransitionTerm<StateSet<ProductState<TState1, TState2>>>>
                Delta(ProductState<TState1, TState2> state)
            {
                var trans1 = nbw1.GetTransition(state.State1);
                var trans2 = nbw2.GetTransition(state.State2);
                int currentFlag = state.Flag;

                // Combine all pairs of Antimirov disjuncts from both NBWs
                var result = new List<TransitionTerm<StateSet<ProductState<TState1, TState2>>>>();

                foreach (var t1 in trans1)
                {
                    foreach (var t2 in trans2)
                    {
                        // Use ApplyCross to combine the two transition terms symbolically
                        var combined = algebra1.ApplyCross(
                            t1, t2,
                            (ss1, ss2) => CombineStateSets(
                                ss1, ss2, currentFlag,
                                nbw1.IsAccepting, nbw2.IsAccepting,
                                productComparer),
                            nbw1.Eba.Top);

                        // Skip if the result is bottom (empty state set)
                        if (!IsBottom(combined, productComparer))
                            result.Add(combined);
                    }
                }

                return result;
            }

            // Accepting: flag == 2
            bool IsAccepting(ProductState<TState1, TState2> state) => state.Flag == 2;

            return new SymbolicNBW<TPredicate, TElement, ProductState<TState1, TState2>>(
                nbw1.Eba,
                nbw1.Registry,
                initialStates,
                IsAccepting,
                Delta,
                productEqComparer);
        }

        /// <summary>
        /// Combines two state sets from NBW₁ and NBW₂ into a product state set,
        /// advancing the acceptance flag according to the breakpoint rule.
        /// </summary>
        private static StateSet<ProductState<TState1, TState2>>
            CombineStateSets<TState1, TState2>(
                StateSet<TState1> ss1,
                StateSet<TState2> ss2,
                int currentFlag,
                Func<TState1, bool> isAccepting1,
                Func<TState2, bool> isAccepting2,
                IComparer<ProductState<TState1, TState2>> comparer)
        {
            if (ss1.IsEmpty || ss2.IsEmpty)
                return StateSet<ProductState<TState1, TState2>>.Empty(comparer);

            var pairs = new List<ProductState<TState1, TState2>>();
            foreach (var q1 in ss1)
            {
                foreach (var q2 in ss2)
                {
                    int nextFlag = AdvanceFlag(currentFlag, q1, q2, isAccepting1, isAccepting2);
                    pairs.Add(new ProductState<TState1, TState2>(q1, q2, nextFlag));
                }
            }

            return new StateSet<ProductState<TState1, TState2>>(pairs, comparer);
        }

        /// <summary>
        /// Advances the acceptance flag based on successor states.
        /// Flag 0: waiting for F₁ → if q1 ∈ F₁, advance to 1
        /// Flag 1: waiting for F₂ → if q2 ∈ F₂, advance to 2
        /// Flag 2: already accepting → reset to 0 (and re-check)
        /// </summary>
        private static int AdvanceFlag<TState1, TState2>(
            int currentFlag,
            TState1 q1, TState2 q2,
            Func<TState1, bool> isAccepting1,
            Func<TState2, bool> isAccepting2)
        {
            int flag = currentFlag;

            // From flag 2, reset to 0
            if (flag == 2) flag = 0;

            // Try to advance through phases in one step
            if (flag == 0 && isAccepting1(q1)) flag = 1;
            if (flag == 1 && isAccepting2(q2)) flag = 2;

            return flag;
        }

        private static bool IsBottom<TState>(
            TransitionTerm<StateSet<TState>> term,
            IComparer<TState> comparer)
        {
            if (term is TransitionTermLeaf<StateSet<TState>> leaf)
                return leaf.Value.IsEmpty;
            return false;
        }
    }

    /// <summary>
    /// Product state: (q₁, q₂, flag) where flag ∈ {0, 1, 2}.
    /// </summary>
    public sealed class ProductState<TState1, TState2> : IEquatable<ProductState<TState1, TState2>>
    {
        public TState1 State1 { get; }
        public TState2 State2 { get; }
        public int Flag { get; }

        public ProductState(TState1 state1, TState2 state2, int flag)
        {
            State1 = state1;
            State2 = state2;
            Flag = flag;
        }

        public bool Equals(ProductState<TState1, TState2> other)
        {
            if (other == null) return false;
            return EqualityComparer<TState1>.Default.Equals(State1, other.State1)
                && EqualityComparer<TState2>.Default.Equals(State2, other.State2)
                && Flag == other.Flag;
        }

        public override bool Equals(object obj) => Equals(obj as ProductState<TState1, TState2>);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = EqualityComparer<TState1>.Default.GetHashCode(State1) * 31;
                hash = (hash + EqualityComparer<TState2>.Default.GetHashCode(State2)) * 31;
                return hash + Flag;
            }
        }

        public override string ToString() => $"({State1}, {State2}, {Flag})";
    }

    internal sealed class ProductStateComparer<TState1, TState2>
        : IComparer<ProductState<TState1, TState2>>
    {
        private readonly IComparer<TState1> _cmp1;
        private readonly IComparer<TState2> _cmp2;

        public ProductStateComparer(IComparer<TState1> cmp1, IComparer<TState2> cmp2)
        {
            _cmp1 = cmp1;
            _cmp2 = cmp2;
        }

        public int Compare(ProductState<TState1, TState2> x, ProductState<TState1, TState2> y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int c = _cmp1.Compare(x.State1, y.State1);
            if (c != 0) return c;
            c = _cmp2.Compare(x.State2, y.State2);
            if (c != 0) return c;
            return x.Flag.CompareTo(y.Flag);
        }
    }

    internal sealed class ProductStateEqualityComparer<TState1, TState2>
        : IEqualityComparer<ProductState<TState1, TState2>>
    {
        private readonly IEqualityComparer<TState1> _eq1;
        private readonly IEqualityComparer<TState2> _eq2;

        public ProductStateEqualityComparer(
            IEqualityComparer<TState1> eq1, IEqualityComparer<TState2> eq2)
        {
            _eq1 = eq1;
            _eq2 = eq2;
        }

        public bool Equals(ProductState<TState1, TState2> x, ProductState<TState1, TState2> y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return _eq1.Equals(x.State1, y.State1)
                && _eq2.Equals(x.State2, y.State2)
                && x.Flag == y.Flag;
        }

        public int GetHashCode(ProductState<TState1, TState2> obj)
        {
            if (obj == null) return 0;
            return obj.GetHashCode();
        }
    }
}
