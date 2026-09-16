namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A Symbolic Nondeterministic Büchi Word automaton modulo an EBA A.
    /// (Definition 4.2 in the paper: "Symbolic Automata: Omega-Regularity Modulo Theories")
    /// 
    /// NBW_A = (Q, I, δ, F) where:
    /// <list type="bullet">
    ///   <item>Q: finite set of states (discovered lazily)</item>
    ///   <item>I ⊆ Q: initial states</item>
    ///   <item>δ: Q → ⟨TTerm⟨A, P(Q)⟩⟩: transition in Antimirov form</item>
    ///   <item>F ⊆ Q: accepting (final) states</item>
    /// </list>
    /// 
    /// Transitions use Antimirov normal form: each state maps to a list
    /// of transition terms whose leaves are <see cref="StateSet{TState}"/>
    /// (successor sets). The list represents nondeterministic choice
    /// (top-level disjunction that is not propagated into ITEs).
    /// </summary>
    /// <typeparam name="TPredicate">Predicate type in the condition EBA.</typeparam>
    /// <typeparam name="TElement">Element type in the alphabet universe Σ.</typeparam>
    /// <typeparam name="TState">State type Q.</typeparam>
    public class SymbolicNBW<TPredicate, TElement, TState>
    {
        private readonly Func<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>> _delta;
        private readonly Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>> _transitionCache;
        private readonly HashSet<TState> _states;

        /// <summary>
        /// Creates a symbolic NBW with a lazy transition function.
        /// </summary>
        public SymbolicNBW(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            ConditionRegistry<TPredicate> registry,
            IReadOnlyCollection<TState> initialStates,
            Func<TState, bool> isAccepting,
            Func<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>> delta,
            IEqualityComparer<TState> stateEqualityComparer = null)
        {
            Eba = eba ?? throw new ArgumentNullException(nameof(eba));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            IsAccepting = isAccepting ?? throw new ArgumentNullException(nameof(isAccepting));
            _delta = delta ?? throw new ArgumentNullException(nameof(delta));

            var comparer = stateEqualityComparer ?? EqualityComparer<TState>.Default;
            _transitionCache = new Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>>(comparer);
            _states = new HashSet<TState>(comparer);

            InitialStates = new List<TState>(initialStates);
            foreach (var s in initialStates)
                _states.Add(s);
        }

        /// <summary>
        /// Creates a symbolic NBW with eagerly provided transitions.
        /// </summary>
        public SymbolicNBW(
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            ConditionRegistry<TPredicate> registry,
            IReadOnlyCollection<TState> initialStates,
            Func<TState, bool> isAccepting,
            IDictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>> transitions,
            IEqualityComparer<TState> stateEqualityComparer = null)
        {
            Eba = eba ?? throw new ArgumentNullException(nameof(eba));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            IsAccepting = isAccepting ?? throw new ArgumentNullException(nameof(isAccepting));
            _delta = null;

            var comparer = stateEqualityComparer ?? EqualityComparer<TState>.Default;
            _transitionCache = new Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>>(comparer);
            _states = new HashSet<TState>(comparer);

            InitialStates = new List<TState>(initialStates);
            foreach (var s in initialStates)
                _states.Add(s);

            foreach (var kvp in transitions)
            {
                _transitionCache[kvp.Key] = kvp.Value;
                _states.Add(kvp.Key);
                foreach (var term in kvp.Value)
                    foreach (var leaf in term.GetDistinctLeaves())
                        foreach (var s in leaf)
                            _states.Add(s);
            }
        }

        /// <summary>The effective Boolean algebra over predicates.</summary>
        public IEffectiveBooleanAlgebra<TPredicate, TElement> Eba { get; }

        /// <summary>The condition registry.</summary>
        public ConditionRegistry<TPredicate> Registry { get; }

        /// <summary>The initial states I.</summary>
        public IReadOnlyList<TState> InitialStates { get; }

        /// <summary>Predicate that determines if a state is accepting (in F).</summary>
        public Func<TState, bool> IsAccepting { get; }

        /// <summary>All discovered states so far.</summary>
        public IReadOnlyCollection<TState> States => _states;

        /// <summary>All cached transitions.</summary>
        public IReadOnlyDictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>> CachedTransitions
            => _transitionCache;

        /// <summary>
        /// Gets the transitions for a state in Antimirov form,
        /// computing and caching if necessary.
        /// </summary>
        public IReadOnlyList<TransitionTerm<StateSet<TState>>> GetTransition(TState state)
        {
            if (_transitionCache.TryGetValue(state, out var cached))
                return cached;

            if (_delta == null)
                throw new InvalidOperationException(
                    $"No transition function provided and state '{state}' not in cache.");

            _states.Add(state);
            var transition = _delta(state);
            _transitionCache[state] = transition;

            // Discover successor states
            foreach (var term in transition)
                foreach (var leaf in term.GetDistinctLeaves())
                    foreach (var s in leaf)
                        _states.Add(s);

            return transition;
        }
    }
}
