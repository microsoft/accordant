namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A Symbolic Alternating Büchi Word automaton modulo an EBA A.
    /// (Definition 4.1 in the paper: "Symbolic Automata: Omega-Regularity Modulo Theories")
    ///
    /// ABW_A = (Q, φ₀, δ, F) where:
    /// <list type="bullet">
    ///   <item>Q: finite set of states (discovered lazily via transitions)</item>
    ///   <item>φ₀ ∈ B⁺(Q): initial positive Boolean formula over states
    ///         (in DNF; a single atom q is the special case φ₀ = {{q}})</item>
    ///   <item>δ: Q → TTerm⟨A, B⁺(Q)⟩: transition function</item>
    ///   <item>F ⊆ Q: accepting (final) states</item>
    /// </list>
    ///
    /// Transitions are computed lazily via a delegate and cached.
    /// States are discovered incrementally as transitions are computed.
    /// The transition term leaves are <see cref="Dnf{TState}"/> formulas
    /// representing positive Boolean combinations of successor states.
    ///
    /// <para>
    /// Generalising φ₀ to <see cref="Dnf{TState}"/> rather than a single
    /// <typeparamref name="TState"/> makes the natural Boolean closure of ABWs
    /// (union, intersection, lifting δ to B⁺(Q)) immediate, since both
    /// operations only need to combine the initial formulas:
    /// </para>
    /// <list type="bullet">
    ///   <item>L(A) ∪ L(B) ↦ initial = φ₀ᴬ ∨ φ₀ᴮ (disjunction in B⁺(Q))</item>
    ///   <item>L(A) ∩ L(B) ↦ initial = φ₀ᴬ ∧ φ₀ᴮ (conjunction in B⁺(Q))</item>
    ///   <item>δ lifted homomorphically: δ̂(⋁ⱼ ⋀ᵢ qᵢⱼ) = ⋁ⱼ ⋀ᵢ δ(qᵢⱼ)
    ///         using <see cref="TransitionTermAlgebra{TPredicate, TElement, TLeaf}"/>'s
    ///         And/Or over the term structure.</item>
    /// </list>
    /// </summary>
    /// <typeparam name="TPredicate">Predicate type in the condition EBA.</typeparam>
    /// <typeparam name="TElement">Element type in the alphabet universe Σ.</typeparam>
    /// <typeparam name="TState">State type Q.</typeparam>
    public class SymbolicABW<TPredicate, TElement, TState>
    {
        private readonly Func<TState, TransitionTerm<Dnf<TState>>> _delta;
        private readonly Dictionary<TState, TransitionTerm<Dnf<TState>>> _transitionCache;
        private readonly HashSet<TState> _states;
        private readonly IEqualityComparer<TState> _stateEqualityComparer;
        private TransitionTermAlgebra<TPredicate, TElement, Dnf<TState>> _termAlgebra;

        /// <summary>
        /// Creates a symbolic ABW with an initial positive-Boolean formula over states.
        /// </summary>
        /// <param name="eba">The effective Boolean algebra over predicates.</param>
        /// <param name="registry">The condition registry (shared with transition terms).</param>
        /// <param name="dnfAlgebra">The B⁺(Q) leaf algebra for transition term operations.</param>
        /// <param name="initialFormula">The initial formula φ₀ ∈ B⁺(Q).</param>
        /// <param name="isAccepting">Predicate determining if a state is in F.</param>
        /// <param name="delta">
        /// The symbolic transition function: given a state, returns its transition
        /// term TTerm⟨A, B⁺(Q)⟩. Called at most once per state and cached.
        /// </param>
        /// <param name="stateEqualityComparer">
        /// Equality comparer for states. If null, uses default.
        /// </param>
        public SymbolicABW(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            ConditionRegistry<TPredicate> registry,
            DnfAlgebra<TState> dnfAlgebra,
            Dnf<TState> initialFormula,
            Func<TState, bool> isAccepting,
            Func<TState, TransitionTerm<Dnf<TState>>> delta,
            IEqualityComparer<TState> stateEqualityComparer = null)
        {
            Eba = eba ?? throw new ArgumentNullException(nameof(eba));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            DnfAlgebra = dnfAlgebra ?? throw new ArgumentNullException(nameof(dnfAlgebra));
            InitialState = initialFormula ?? throw new ArgumentNullException(nameof(initialFormula));
            IsAccepting = isAccepting ?? throw new ArgumentNullException(nameof(isAccepting));
            _delta = delta ?? throw new ArgumentNullException(nameof(delta));

            _stateEqualityComparer = stateEqualityComparer ?? EqualityComparer<TState>.Default;
            _transitionCache = new Dictionary<TState, TransitionTerm<Dnf<TState>>>(_stateEqualityComparer);
            _states = new HashSet<TState>(_stateEqualityComparer);
            foreach (var s in initialFormula.GetAllStates())
                _states.Add(s);
        }

        /// <summary>
        /// Convenience constructor for the common case where the initial formula
        /// is a single atom <c>{{initialState}}</c>.
        /// </summary>
        public SymbolicABW(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            ConditionRegistry<TPredicate> registry,
            DnfAlgebra<TState> dnfAlgebra,
            TState initialState,
            Func<TState, bool> isAccepting,
            Func<TState, TransitionTerm<Dnf<TState>>> delta,
            IEqualityComparer<TState> stateEqualityComparer = null)
            : this(eba, registry, dnfAlgebra,
                   (dnfAlgebra ?? throw new ArgumentNullException(nameof(dnfAlgebra))).Atom(initialState),
                   isAccepting, delta, stateEqualityComparer)
        {
        }

        /// <summary>The effective Boolean algebra over predicates.</summary>
        public IEffectiveBooleanAlgebra<TPredicate, TElement> Eba { get; }

        /// <summary>The condition registry for transition term conditions.</summary>
        public ConditionRegistry<TPredicate> Registry { get; }

        /// <summary>The B⁺(Q) leaf algebra.</summary>
        public DnfAlgebra<TState> DnfAlgebra { get; }

        /// <summary>The initial state formula φ₀ ∈ B⁺(Q).</summary>
        public Dnf<TState> InitialState { get; }

        /// <summary>Predicate that determines if a state is accepting (in F).</summary>
        public Func<TState, bool> IsAccepting { get; }

        /// <summary>The underlying symbolic transition function δ on atomic states.</summary>
        public Func<TState, TransitionTerm<Dnf<TState>>> Delta => _delta;

        /// <summary>The equality comparer used for states.</summary>
        public IEqualityComparer<TState> StateEqualityComparer => _stateEqualityComparer;

        /// <summary>All discovered states so far.</summary>
        public IReadOnlyCollection<TState> States => _states;

        /// <summary>All cached transitions.</summary>
        public IReadOnlyDictionary<TState, TransitionTerm<Dnf<TState>>> CachedTransitions
            => _transitionCache;

        /// <summary>
        /// Gets the transition term for a state, computing and caching if necessary.
        /// Newly discovered states (from transition leaves) are added to <see cref="States"/>.
        /// </summary>
        public TransitionTerm<Dnf<TState>> GetTransition(TState state)
        {
            if (_transitionCache.TryGetValue(state, out var cached))
                return cached;

            _states.Add(state);
            var transition = _delta(state);
            _transitionCache[state] = transition;

            // Discover successor states from transition leaves
            foreach (var leaf in transition.GetDistinctLeaves())
                foreach (var s in leaf.GetAllStates())
                    _states.Add(s);

            return transition;
        }

        /// <summary>
        /// Lifts the transition function δ homomorphically to any positive
        /// Boolean combination of states:
        /// <c>δ̂(⋁ⱼ ⋀ᵢ qᵢⱼ) = ⋁ⱼ ⋀ᵢ δ(qᵢⱼ)</c>, with the disjunction and
        /// conjunction taken in the transition term algebra
        /// <see cref="TransitionTermAlgebra{TPredicate, TElement, TLeaf}"/>.
        /// Edge cases: ⊤ ↦ leaf(⊤), ⊥ ↦ leaf(⊥).
        /// </summary>
        public TransitionTerm<Dnf<TState>> GetTransition(Dnf<TState> formula)
        {
            if (formula == null) throw new ArgumentNullException(nameof(formula));
            var alg = GetTermAlgebra();
            if (formula.IsFalse) return alg.Leaf(DnfAlgebra.Bottom);
            if (formula.IsTrue) return alg.Leaf(DnfAlgebra.Top);

            TransitionTerm<Dnf<TState>> result = null;
            foreach (var clause in formula.Clauses)
            {
                TransitionTerm<Dnf<TState>> clauseTerm = null;
                foreach (var state in clause)
                {
                    var sigma = GetTransition(state);
                    clauseTerm = clauseTerm == null ? sigma : alg.And(clauseTerm, sigma);
                }
                if (clauseTerm == null)
                    clauseTerm = alg.Leaf(DnfAlgebra.Top); // empty conjunction
                result = result == null ? clauseTerm : alg.Or(result, clauseTerm);
            }
            return result ?? alg.Leaf(DnfAlgebra.Bottom);
        }

        /// <summary>
        /// Gets the transition term algebra for operating on ABW transitions.
        /// Uses the B⁺(Q) leaf algebra for And/Or on transition term leaves.
        /// </summary>
        public TransitionTermAlgebra<TPredicate, TElement, Dnf<TState>> GetTermAlgebra()
        {
            if (_termAlgebra == null)
                _termAlgebra = new TransitionTermAlgebra<TPredicate, TElement, Dnf<TState>>(
                    Eba, Registry, DnfAlgebra);
            return _termAlgebra;
        }

        /// <summary>
        /// Constructs an ABW recognising <c>L(a) ∪ L(b)</c> from two compatible
        /// ABWs <paramref name="a"/> and <paramref name="b"/>:
        /// the new initial formula is <c>φ₀ᴬ ∨ φ₀ᴮ</c>, the transition function
        /// is dispatched via <paramref name="isInA"/>, and F is the union of
        /// the two accepting sets.
        /// </summary>
        /// <param name="a">Left ABW.</param>
        /// <param name="b">Right ABW.</param>
        /// <param name="isInA">
        /// Routing predicate: returns true iff a given state belongs to A's state
        /// space (and thus should be dispatched to A's δ / F). Required because
        /// the two ABWs share the same TState type and the combined automaton
        /// must know which subautomaton owns each state.
        /// </param>
        public static SymbolicABW<TPredicate, TElement, TState> Union(
            SymbolicABW<TPredicate, TElement, TState> a,
            SymbolicABW<TPredicate, TElement, TState> b,
            Func<TState, bool> isInA)
        {
            RequireCompatible(a, b);
            if (isInA == null) throw new ArgumentNullException(nameof(isInA));
            var initial = a.DnfAlgebra.Or(a.InitialState, b.InitialState);
            return new SymbolicABW<TPredicate, TElement, TState>(
                a.Eba, a.Registry, a.DnfAlgebra,
                initial,
                q => isInA(q) ? a.IsAccepting(q) : b.IsAccepting(q),
                q => isInA(q) ? a.Delta(q) : b.Delta(q),
                a.StateEqualityComparer);
        }

        /// <summary>
        /// Constructs an ABW recognising <c>L(a) ∩ L(b)</c> from two compatible
        /// ABWs: the new initial formula is <c>φ₀ᴬ ∧ φ₀ᴮ</c>; transitions and
        /// acceptance are dispatched via <paramref name="isInA"/>. Each branch
        /// of the conjunction is checked independently against its own Büchi
        /// condition (which is sound for alternating Büchi automata).
        /// </summary>
        public static SymbolicABW<TPredicate, TElement, TState> Intersect(
            SymbolicABW<TPredicate, TElement, TState> a,
            SymbolicABW<TPredicate, TElement, TState> b,
            Func<TState, bool> isInA)
        {
            RequireCompatible(a, b);
            if (isInA == null) throw new ArgumentNullException(nameof(isInA));
            var initial = a.DnfAlgebra.And(a.InitialState, b.InitialState);
            return new SymbolicABW<TPredicate, TElement, TState>(
                a.Eba, a.Registry, a.DnfAlgebra,
                initial,
                q => isInA(q) ? a.IsAccepting(q) : b.IsAccepting(q),
                q => isInA(q) ? a.Delta(q) : b.Delta(q),
                a.StateEqualityComparer);
        }

        private static void RequireCompatible(
            SymbolicABW<TPredicate, TElement, TState> a,
            SymbolicABW<TPredicate, TElement, TState> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (!ReferenceEquals(a.Eba, b.Eba))
                throw new ArgumentException("ABWs must share the same EBA.");
            if (!ReferenceEquals(a.Registry, b.Registry))
                throw new ArgumentException("ABWs must share the same ConditionRegistry.");
            if (!ReferenceEquals(a.DnfAlgebra, b.DnfAlgebra))
                throw new ArgumentException("ABWs must share the same DnfAlgebra.");
        }
    }
}
