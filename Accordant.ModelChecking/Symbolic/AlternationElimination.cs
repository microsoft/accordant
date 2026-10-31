namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The Æ alternation elimination algorithm (Section 5 of the paper).
    /// Converts a symbolic alternating Büchi automaton (ABW_A) into a
    /// symbolic nondeterministic Büchi automaton (NBW_A) using the
    /// Miyano-Hayashi breakpoint construction.
    /// 
    /// The NBW states are <see cref="BreakpointState{TState}"/> pairs (S, O)
    /// where S is a macrostate (set of ABW states) and O ⊆ S tracks
    /// acceptance obligations.
    /// </summary>
    public static class AlternationElimination
    {
        /// <summary>
        /// Eliminates alternation from an ABW, producing an equivalent NBW.
        /// Uses the Miyano-Hayashi breakpoint construction for acceptance.
        /// 
        /// The NBW states are <see cref="BreakpointState{TState}"/> pairs (S, O).
        /// States are discovered incrementally (on-the-fly).
        /// </summary>
        public static SymbolicNBW<TPredicate, TElement, BreakpointState<TState>>
            Eliminate<TPredicate, TElement, TState>(
                SymbolicABW<TPredicate, TElement, TState> abw,
                bool eagerAntimirov = false)
        {
            if (abw == null) throw new ArgumentNullException(nameof(abw));

            var stateComparer = abw.DnfAlgebra.StateComparer;
            var termAlgebra = abw.GetTermAlgebra();
            var bpComparerForCollapse = BreakpointState<TState>.GetComparer(stateComparer);
            var bpEqualityForCollapse = BreakpointState<TState>.GetEqualityComparer();

            // Cache for macrostate combined transitions:
            // S → ∧_{q∈S} δ(q), which is TTerm⟨A, Dnf<TState>⟩
            var macroTransitionCache = new Dictionary<StateSet<TState>, TransitionTerm<Dnf<TState>>>();

            // Compute combined transition for a macrostate
            TransitionTerm<Dnf<TState>> GetMacroTransition(StateSet<TState> macrostate)
            {
                if (macroTransitionCache.TryGetValue(macrostate, out var cached))
                    return cached;

                TransitionTerm<Dnf<TState>> combined = termAlgebra.Top;
                foreach (var q in macrostate)
                {
                    var delta_q = abw.GetTransition(q);
                    combined = termAlgebra.And(combined, delta_q);
                }

                macroTransitionCache[macrostate] = combined;
                return combined;
            }

            // Build the NBW transition for a breakpoint state (S, O)
            // Returns transitions in Antimirov form: list of TTerm⟨A, StateSet<BreakpointState>>
            IReadOnlyList<TransitionTerm<StateSet<BreakpointState<TState>>>>
                ComputeBreakpointTransition(BreakpointState<TState> bpState)
            {
                var S = bpState.Macrostate;
                var O = bpState.Obligation;
                var result = new List<TransitionTerm<StateSet<BreakpointState<TState>>>>();
                var bpComparer = BreakpointState<TState>.GetComparer(stateComparer);

                TransitionTerm<Dnf<BreakpointState<TState>>> dnfTerm;
                if (O.IsEmpty)
                {
                    // Breakpoint reached: O was empty, so reset.
                    var delta_S = GetMacroTransition(S);
                    dnfTerm = termAlgebra.MapUnary(delta_S, dnfS =>
                        BuildResetDnf(dnfS, stateComparer, abw.IsAccepting, bpComparer));
                }
                else
                {
                    var sMinusO = S.Except(O);

                    if (sMinusO.IsEmpty)
                    {
                        var delta_O = GetMacroTransition(O);
                        dnfTerm = termAlgebra.MapUnary(delta_O, dnfO =>
                            BuildOOnlyDnf(dnfO, stateComparer, abw.IsAccepting, bpComparer));
                    }
                    else
                    {
                        var delta_SminusO = GetMacroTransition(sMinusO);
                        var delta_O = GetMacroTransition(O);

                        dnfTerm = termAlgebra.ApplyCross(
                            delta_SminusO, delta_O,
                            (dnfSO, dnfO) => BuildCrossDnf(
                                dnfSO, dnfO, stateComparer, abw.IsAccepting, bpComparer),
                            abw.Eba.Top);
                    }
                }

                if (eagerAntimirov)
                {
                    FlattenDnfToAntimirov(dnfTerm, result, bpComparer);
                }
                else
                {
                    result.Add(CollapseDnfToStateSet(
                        dnfTerm, bpComparerForCollapse, bpEqualityForCollapse));
                }

                return result;
            }

            // Initial states: one breakpoint (Sⱼ, ∅) per disjunct Sⱼ of the
            // initial positive Boolean formula φ₀ = ⋁ⱼ Sⱼ ∈ B⁺(Q).
            // Obligation starts empty (first breakpoint).
            var emptyObligation = StateSet<TState>.Empty(stateComparer);
            var initialBPs = new List<BreakpointState<TState>>(abw.InitialState.ClauseCount);
            foreach (var clause in abw.InitialState.Clauses)
                initialBPs.Add(new BreakpointState<TState>(clause, emptyObligation));

            return new SymbolicNBW<TPredicate, TElement, BreakpointState<TState>>(
                abw.Eba,
                abw.Registry,
                initialBPs,
                bp => bp.Obligation.IsEmpty, // Accepting iff obligation is empty (breakpoint)
                ComputeBreakpointTransition,
                BreakpointState<TState>.GetEqualityComparer());
        }

        /// <summary>
        /// Builds successor breakpoint states for the O=∅ (reset) case.
        /// For each clause C in dnfS: S'=C, O'=C\F (reset obligation to all non-accepting).
        /// </summary>
        private static Dnf<BreakpointState<TState>> BuildResetDnf<TState>(
            Dnf<TState> dnfS,
            IComparer<TState> stateComparer,
            Func<TState, bool> isAccepting,
            IComparer<BreakpointState<TState>> bpComparer)
        {
            if (dnfS.IsFalse)
                return new Dnf<BreakpointState<TState>>(
                    Array.Empty<StateSet<BreakpointState<TState>>>());

            var clauses = new List<StateSet<BreakpointState<TState>>>();
            foreach (var clauseS in dnfS.Clauses)
            {
                // O' = S' \ F (all non-accepting states become obligations)
                var nonAccepting = new List<TState>();
                foreach (var q in clauseS)
                    if (!isAccepting(q))
                        nonAccepting.Add(q);
                var oPrime = nonAccepting.Count > 0
                    ? new StateSet<TState>(nonAccepting, stateComparer)
                    : StateSet<TState>.Empty(stateComparer);

                var successor = new BreakpointState<TState>(clauseS, oPrime);
                clauses.Add(StateSet<BreakpointState<TState>>.Singleton(successor, bpComparer));
            }

            return new Dnf<BreakpointState<TState>>(clauses.ToArray());
        }

        /// <summary>
        /// Builds successor breakpoint states when O = S (all states are obligation states).
        /// For each clause C_O in dnfO: S'=C_O, O'=C_O\F.
        /// </summary>
        private static Dnf<BreakpointState<TState>> BuildOOnlyDnf<TState>(
            Dnf<TState> dnfO,
            IComparer<TState> stateComparer,
            Func<TState, bool> isAccepting,
            IComparer<BreakpointState<TState>> bpComparer)
        {
            if (dnfO.IsFalse)
                return new Dnf<BreakpointState<TState>>(
                    Array.Empty<StateSet<BreakpointState<TState>>>());

            var clauses = new List<StateSet<BreakpointState<TState>>>();
            foreach (var clauseO in dnfO.Clauses)
            {
                // S' = C_O, O' = C_O \ F
                var nonAccepting = new List<TState>();
                foreach (var q in clauseO)
                    if (!isAccepting(q))
                        nonAccepting.Add(q);
                var oPrime = nonAccepting.Count > 0
                    ? new StateSet<TState>(nonAccepting, stateComparer)
                    : StateSet<TState>.Empty(stateComparer);

                var successor = new BreakpointState<TState>(clauseO, oPrime);
                clauses.Add(StateSet<BreakpointState<TState>>.Singleton(successor, bpComparer));
            }

            return new Dnf<BreakpointState<TState>>(clauses.ToArray());
        }

        /// <summary>
        /// Builds successor breakpoint states for the general case S\O ≠ ∅, O ≠ ∅.
        /// Cross-products delta_{S\O} with delta_O:
        /// For each (C_{S\O}, C_O): S' = C_{S\O} ∪ C_O, O' = C_O \ F.
        /// This ensures consistency: O-states' choices are determined by their clause.
        /// </summary>
        private static Dnf<BreakpointState<TState>> BuildCrossDnf<TState>(
            Dnf<TState> dnfSO,
            Dnf<TState> dnfO,
            IComparer<TState> stateComparer,
            Func<TState, bool> isAccepting,
            IComparer<BreakpointState<TState>> bpComparer)
        {
            if (dnfSO.IsFalse || dnfO.IsFalse)
                return new Dnf<BreakpointState<TState>>(
                    Array.Empty<StateSet<BreakpointState<TState>>>());

            var clauses = new List<StateSet<BreakpointState<TState>>>();
            foreach (var clauseSO in dnfSO.Clauses)
            {
                foreach (var clauseO in dnfO.Clauses)
                {
                    // S' = C_{S\O} ∪ C_O
                    var sPrime = clauseSO.Union(clauseO);

                    // O' = C_O \ F (obligation successor is just the O-contribution minus accepting)
                    var nonAccepting = new List<TState>();
                    foreach (var q in clauseO)
                        if (!isAccepting(q))
                            nonAccepting.Add(q);
                    var oPrime = nonAccepting.Count > 0
                        ? new StateSet<TState>(nonAccepting, stateComparer)
                        : StateSet<TState>.Empty(stateComparer);

                    var successor = new BreakpointState<TState>(sPrime, oPrime);
                    clauses.Add(StateSet<BreakpointState<TState>>.Singleton(successor, bpComparer));
                }
            }

            return new Dnf<BreakpointState<TState>>(clauses.ToArray());
        }

        /// <summary>
        /// Flattens a transition term with Dnf leaves into Antimirov form.
        /// Each clause in a Dnf leaf becomes a separate transition term disjunct.
        /// </summary>
        private static void FlattenDnfToAntimirov<TState>(
            TransitionTerm<Dnf<TState>> term,
            List<TransitionTerm<StateSet<TState>>> result,
            IComparer<TState> stateComparer)
        {
            if (term is TransitionTermLeaf<Dnf<TState>> leaf)
            {
                foreach (var clause in leaf.Value.Clauses)
                {
                    result.Add(TransitionTerm<StateSet<TState>>.Leaf(clause));
                }
                return;
            }

            var ite = (TransitionTermIte<Dnf<TState>>)term;
            var hiTerms = new List<TransitionTerm<StateSet<TState>>>();
            var loTerms = new List<TransitionTerm<StateSet<TState>>>();
            FlattenDnfToAntimirov(ite.Hi, hiTerms, stateComparer);
            FlattenDnfToAntimirov(ite.Lo, loTerms, stateComparer);

            // Distribute ITE over disjunction: (α ? f₁∨f₂ : g₁∨g₂)
            // = (α ? f₁ : g₁) ∨ (α ? f₁ : g₂) ∨ (α ? f₂ : g₁) ∨ (α ? f₂ : g₂)
            // When one branch is ⊥ (empty), use empty StateSet as dead-end leaf.
            var emptyLeaf = TransitionTerm<StateSet<TState>>.Leaf(
                StateSet<TState>.Empty(stateComparer));

            if (hiTerms.Count == 0 && loTerms.Count == 0)
            {
                // Both branches dead — no transitions
                return;
            }
            else if (loTerms.Count == 0)
            {
                // Under ¬α: dead end (empty successor set)
                foreach (var h in hiTerms)
                    result.Add(TransitionTerm<StateSet<TState>>.Ite(
                        ite.ConditionIndex, h, emptyLeaf));
            }
            else if (hiTerms.Count == 0)
            {
                // Under α: dead end (empty successor set)
                foreach (var l in loTerms)
                    result.Add(TransitionTerm<StateSet<TState>>.Ite(
                        ite.ConditionIndex, emptyLeaf, l));
            }
            else
            {
                foreach (var h in hiTerms)
                {
                    foreach (var l in loTerms)
                    {
                        result.Add(TransitionTerm<StateSet<TState>>.Ite(
                            ite.ConditionIndex, h, l));
                    }
                }
            }
        }

        /// <summary>
        /// DnfLeaves form: collapses a transition term with Dnf leaves into
        /// a single transition term whose leaves carry the *union* of the
        /// DNF clauses as one <see cref="StateSet{TState}"/>. This avoids
        /// the multiplicative blowup of <see cref="FlattenDnfToAntimirov"/>
        /// (which distributes DNF clauses through the BDD structure with a
        /// Cartesian-product factor at every internal node). Semantically
        /// equivalent for NBW consumers that treat outer lists as disjunction
        /// and leaf state-sets as nondeterministic choice over successors.
        /// </summary>
        private static TransitionTerm<StateSet<TState>> CollapseDnfToStateSet<TState>(
            TransitionTerm<Dnf<TState>> term,
            IComparer<TState> stateComparer,
            IEqualityComparer<TState> stateEquality)
        {
            if (term is TransitionTermLeaf<Dnf<TState>> leaf)
            {
                var dnf = leaf.Value;
                if (dnf.IsFalse || dnf.Clauses.Count == 0)
                    return TransitionTerm<StateSet<TState>>.Leaf(
                        StateSet<TState>.Empty(stateComparer));
                if (dnf.Clauses.Count == 1)
                    return TransitionTerm<StateSet<TState>>.Leaf(dnf.Clauses[0]);
                var seen = new HashSet<TState>(stateEquality);
                var union = new List<TState>();
                foreach (var clause in dnf.Clauses)
                    foreach (var s in clause)
                        if (seen.Add(s))
                            union.Add(s);
                return TransitionTerm<StateSet<TState>>.Leaf(
                    new StateSet<TState>(union, stateComparer));
            }
            var ite = (TransitionTermIte<Dnf<TState>>)term;
            return TransitionTerm<StateSet<TState>>.Ite(
                ite.ConditionIndex,
                CollapseDnfToStateSet(ite.Hi, stateComparer, stateEquality),
                CollapseDnfToStateSet(ite.Lo, stateComparer, stateEquality));
        }

        /// <summary>
        /// Explores the NBW produced by alternation elimination,
        /// discovering all reachable states up to a given bound.
        /// Returns the number of states discovered.
        /// </summary>
        /// <param name="nbw">The NBW to explore.</param>
        /// <param name="maxStates">Maximum number of states to discover (0 = unlimited).</param>
        /// <returns>The set of discovered states.</returns>
        public static IReadOnlyCollection<TState> Explore<TPredicate, TElement, TState>(
            SymbolicNBW<TPredicate, TElement, TState> nbw,
            int maxStates = 0)
        {
            var visited = new HashSet<TState>();
            var worklist = new Queue<TState>();

            foreach (var init in nbw.InitialStates)
            {
                if (visited.Add(init))
                    worklist.Enqueue(init);
            }

            while (worklist.Count > 0)
            {
                if (maxStates > 0 && visited.Count >= maxStates)
                    break;

                var state = worklist.Dequeue();
                var transitions = nbw.GetTransition(state);

                foreach (var term in transitions)
                {
                    foreach (var leaf in term.GetDistinctLeaves())
                    {
                        foreach (var s in leaf)
                        {
                            if (visited.Add(s))
                            {
                                worklist.Enqueue(s);
                            }
                        }
                    }
                }
            }

            return visited;
        }
    }

    /// <summary>
    /// A breakpoint state (S, O) for the Miyano-Hayashi construction.
    /// S is the macrostate (set of ABW states) and O ⊆ S is the
    /// obligation set tracking acceptance requirements.
    /// 
    /// The NBW accepts (reaches a breakpoint) when O = ∅.
    /// </summary>
    public sealed class BreakpointState<TState> : IEquatable<BreakpointState<TState>>
    {
        /// <summary>The macrostate: set of ABW states simultaneously active.</summary>
        public StateSet<TState> Macrostate { get; }

        /// <summary>
        /// The obligation set: ABW states in the macrostate that still need
        /// to visit an accepting state before the next breakpoint.
        /// When empty, a breakpoint is reached (NBW accepting state).
        /// </summary>
        public StateSet<TState> Obligation { get; }

        private int? _hash;

        public BreakpointState(StateSet<TState> macrostate, StateSet<TState> obligation)
        {
            Macrostate = macrostate ?? throw new ArgumentNullException(nameof(macrostate));
            Obligation = obligation ?? throw new ArgumentNullException(nameof(obligation));
        }

        public bool Equals(BreakpointState<TState> other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Macrostate.Equals(other.Macrostate) && Obligation.Equals(other.Obligation);
        }

        public override bool Equals(object obj) => Equals(obj as BreakpointState<TState>);

        public override int GetHashCode()
        {
            if (_hash == null)
            {
                unchecked
                {
                    _hash = Macrostate.GetHashCode() * 31 + Obligation.GetHashCode();
                }
            }
            return _hash.Value;
        }

        public override string ToString()
            => $"({Macrostate}, {Obligation})";

        /// <summary>
        /// Gets a comparer for breakpoint states based on the underlying state comparer.
        /// Orders by macrostate first, then by obligation.
        /// </summary>
        public static IComparer<BreakpointState<TState>> GetComparer(
            IComparer<TState> stateComparer)
        {
            return System.Collections.Generic.Comparer<BreakpointState<TState>>.Create(
                (a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return -1;
                    if (b == null) return 1;
                    int cmp = a.Macrostate.CompareTo(b.Macrostate);
                    if (cmp != 0) return cmp;
                    return a.Obligation.CompareTo(b.Obligation);
                });
        }

        /// <summary>
        /// Gets an equality comparer for breakpoint states.
        /// </summary>
        public static IEqualityComparer<BreakpointState<TState>> GetEqualityComparer()
        {
            return EqualityComparer<BreakpointState<TState>>.Default;
        }
    }
}
