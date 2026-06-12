namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Incremental (on-the-fly) alternation elimination algorithm.
    /// Produces a lazily-constructed NBW from an ABW, computing transitions
    /// only for states that are actually explored during model checking.
    /// 
    /// Unlike the batch <see cref="AlternationElimination.Eliminate{TPredicate, TElement, TState}"/>,
    /// this version does not eagerly discover all reachable states. Instead,
    /// it wraps the ABW and computes breakpoint transitions on demand when
    /// <see cref="SymbolicNBW{TPredicate, TElement, TState}.GetTransition"/> is called.
    /// 
    /// This follows the design in Section 5.3 of the POPL'25 paper where
    /// the Æ construction is interleaved with the product construction
    /// during model checking.
    /// </summary>
    public class IncrementalAE<TPredicate, TElement, TState>
    {
        private readonly SymbolicABW<TPredicate, TElement, TState> _abw;
        private readonly TransitionTermAlgebra<TPredicate, TElement, Dnf<TState>> _termAlgebra;
        private readonly IComparer<TState> _stateComparer;
        private readonly IComparer<BreakpointState<TState>> _bpComparer;
        private readonly Func<BreakpointState<TState>, BreakpointState<TState>> _bpCanonicalizer;
        private readonly Func<StateSet<TState>, MacroReduction<TState>> _macroReducer;
        private readonly bool _eagerAntimirov;

        // Cache for macrostate combined transitions
        private readonly Dictionary<StateSet<TState>, TransitionTerm<Dnf<TState>>> _macroCache;

        // Cache for computed breakpoint transitions (the NBW uses this)
        private readonly Dictionary<BreakpointState<TState>,
            IReadOnlyList<TransitionTerm<StateSet<BreakpointState<TState>>>>> _transitionCache;

        /// <summary>
        /// Creates an incremental Æ instance for the given ABW.
        /// </summary>
        /// <param name="abw">The alternating Büchi automaton to convert.</param>
        /// <param name="breakpointCanonicalizer">Optional canonicaliser applied
        /// to every freshly-produced <see cref="BreakpointState{TState}"/>
        /// (initial states and successors). Two breakpoint states that map to
        /// the same representative are merged on-the-fly during NBW
        /// construction, implementing the "weak equivalence merging" of the
        /// Æ algorithm (JACM Example 5.1 state-reduction lemma): a freshly
        /// generated <c>(S,O)</c> is replaced by an already-discovered
        /// language-equivalent breakpoint when one exists, otherwise it
        /// becomes the representative of its class. Pass <c>null</c> (default)
        /// to disable merging — the construction is identity-only on
        /// breakpoint states.</param>
        /// <param name="macroReducer">Optional macrostate reducer applied
        /// to every freshly-produced macrostate <c>S</c> (initial states
        /// and successors). Returns the reduced <c>S'</c> together with a
        /// representative map. In <see cref="BuildCrossDnf"/> the
        /// obligation set <c>O'</c> is then derived as
        /// <c>{ rep(q) : q ∈ clauseO, q ∉ F }</c>, so obligations carried
        /// by dropped states are forwarded to their surviving
        /// representatives. Intended driver is the structural
        /// <see cref="RltlMacrostateTransitionMerge{TPred, TElem}"/> rule:
        /// two universal copies with identical transition terms
        /// <c>δ(q₁) = δ(q₂)</c> and matching membership in <c>F</c> are
        /// interchangeable in the breakpoint construction (JACM
        /// Example 5.1 state-reduction lemma). Pass <c>null</c> (default)
        /// to disable.</param>
        /// <param name="eagerAntimirov">When <c>true</c>, each breakpoint
        /// transition is eagerly normalised into Antimirov form via
        /// <c>FlattenDnfToAntimirov</c>: every DNF clause at every BDD leaf is
        /// distributed through the ITE structure, producing a list whose entries
        /// each carry a single <see cref="StateSet{TState}"/> per leaf. This can
        /// blow up multiplicatively in (BDD-depth × clauses/leaf), which is
        /// expensive for highly-conjunctive properties such as
        /// <c>∧ᵢ GF pᵢ</c>. When <c>false</c> (the default), the algorithm
        /// instead returns a single transition term whose leaves carry the
        /// *union* of all DNF clauses as one <see cref="StateSet{TState}"/>.
        /// This is semantically equivalent for NBW emptiness and product
        /// traversal (both treat the outer list as disjunction and the leaf
        /// set as nondeterministic choice over successor breakpoints) and
        /// dramatically cheaper when the DNF is large. The previous default
        /// (true) is kept available for ablation studies; see paper §6.2.</param>
        public IncrementalAE(
            SymbolicABW<TPredicate, TElement, TState> abw,
            Func<BreakpointState<TState>, BreakpointState<TState>> breakpointCanonicalizer = null,
            Func<StateSet<TState>, MacroReduction<TState>> macroReducer = null,
            bool eagerAntimirov = false)
        {
            _abw = abw ?? throw new ArgumentNullException(nameof(abw));
            _stateComparer = abw.DnfAlgebra.StateComparer;
            _termAlgebra = abw.GetTermAlgebra();
            _bpComparer = BreakpointState<TState>.GetComparer(_stateComparer);
            _bpCanonicalizer = breakpointCanonicalizer;
            _macroReducer = macroReducer;
            _eagerAntimirov = eagerAntimirov;

            _macroCache = new Dictionary<StateSet<TState>, TransitionTerm<Dnf<TState>>>();
            _transitionCache = new Dictionary<BreakpointState<TState>,
                IReadOnlyList<TransitionTerm<StateSet<BreakpointState<TState>>>>>(
                BreakpointState<TState>.GetEqualityComparer());

            // Initial states: one breakpoint (Sⱼ, ∅) per disjunct Sⱼ of the
            // initial positive Boolean formula φ₀ = ⋁ⱼ Sⱼ ∈ B⁺(Q).
            var emptyObligation = StateSet<TState>.Empty(_stateComparer);
            var bps = new List<BreakpointState<TState>>(abw.InitialState.ClauseCount);
            foreach (var clause in abw.InitialState.Clauses)
            {
                var reduced = MacroReduce(clause).ReducedS;
                bps.Add(CanonBp(new BreakpointState<TState>(reduced, emptyObligation)));
            }
            InitialStates = bps;
        }

        private BreakpointState<TState> CanonBp(BreakpointState<TState> bp)
            => _bpCanonicalizer == null ? bp : _bpCanonicalizer(bp);

        private MacroReduction<TState> MacroReduce(StateSet<TState> s)
            => _macroReducer == null ? MacroReduction<TState>.Identity(s) : _macroReducer(s);

        /// <summary>
        /// The initial breakpoint states — one per disjunct Sⱼ of φ₀.
        /// </summary>
        public IReadOnlyList<BreakpointState<TState>> InitialStates { get; }

        /// <summary>
        /// The single initial breakpoint state, available only when φ₀ is a
        /// single-disjunct formula (the common case for LTL/RLTL derivation).
        /// Throws <see cref="InvalidOperationException"/> if φ₀ has zero or
        /// multiple disjuncts; use <see cref="InitialStates"/> in that case.
        /// </summary>
        public BreakpointState<TState> InitialState
        {
            get
            {
                if (InitialStates.Count != 1)
                    throw new InvalidOperationException(
                        "InitialState requires a single-disjunct initial formula; " +
                        "use InitialStates for the general case.");
                return InitialStates[0];
            }
        }

        /// <summary>The underlying ABW.</summary>
        public SymbolicABW<TPredicate, TElement, TState> Abw => _abw;

        /// <summary>
        /// Determines if a breakpoint state is accepting.
        /// A breakpoint state is accepting iff O = ∅ (obligation discharged).
        /// </summary>
        public bool IsAccepting(BreakpointState<TState> state)
            => state.Obligation.IsEmpty;

        /// <summary>
        /// Gets the transition for a breakpoint state, computing lazily if needed.
        /// Returns transitions in Antimirov form.
        /// </summary>
        public IReadOnlyList<TransitionTerm<StateSet<BreakpointState<TState>>>>
            GetTransition(BreakpointState<TState> state)
        {
            if (_transitionCache.TryGetValue(state, out var cached))
                return cached;

            var result = ComputeTransition(state);
            _transitionCache[state] = result;
            return result;
        }

        /// <summary>
        /// Constructs the lazy NBW backed by this incremental Æ instance.
        /// The NBW computes transitions on demand via <see cref="GetTransition"/>.
        /// </summary>
        public SymbolicNBW<TPredicate, TElement, BreakpointState<TState>> ToNBW()
        {
            return new SymbolicNBW<TPredicate, TElement, BreakpointState<TState>>(
                _abw.Eba,
                _abw.Registry,
                InitialStates,
                IsAccepting,
                GetTransition,
                BreakpointState<TState>.GetEqualityComparer());
        }

        /// <summary>
        /// Number of breakpoint states whose transitions have been computed so far.
        /// </summary>
        public int ComputedStateCount => _transitionCache.Count;

        /// <summary>
        /// Number of macrostate combined transitions cached.
        /// </summary>
        public int MacroCacheSize => _macroCache.Count;

        #region Private Implementation

        private TransitionTerm<Dnf<TState>> GetMacroTransition(StateSet<TState> macrostate)
        {
            if (_macroCache.TryGetValue(macrostate, out var cached))
                return cached;

            TransitionTerm<Dnf<TState>> combined = _termAlgebra.Top;
            foreach (var q in macrostate)
            {
                var delta_q = _abw.GetTransition(q);
                combined = _termAlgebra.And(combined, delta_q);
            }

            _macroCache[macrostate] = combined;
            return combined;
        }

        private IReadOnlyList<TransitionTerm<StateSet<BreakpointState<TState>>>>
            ComputeTransition(BreakpointState<TState> bpState)
        {
            var S = bpState.Macrostate;
            var O = bpState.Obligation;
            var result = new List<TransitionTerm<StateSet<BreakpointState<TState>>>>();

            TransitionTerm<Dnf<BreakpointState<TState>>> dnfTerm;
            if (O.IsEmpty)
            {
                // Breakpoint: reset. S' = clause from delta_S, O' = S' \ F
                var delta_S = GetMacroTransition(S);
                dnfTerm = _termAlgebra.MapUnary(delta_S, dnfS =>
                    BuildResetDnf(dnfS));
            }
            else
            {
                var sMinusO = S.Except(O);

                if (sMinusO.IsEmpty)
                {
                    // O = S: all obligation states
                    var delta_O = GetMacroTransition(O);
                    dnfTerm = _termAlgebra.MapUnary(delta_O, dnfO =>
                        BuildOOnlyDnf(dnfO));
                }
                else
                {
                    // General: S\O ≠ ∅, O ≠ ∅
                    var delta_SminusO = GetMacroTransition(sMinusO);
                    var delta_O = GetMacroTransition(O);

                    dnfTerm = _termAlgebra.ApplyCross(
                        delta_SminusO, delta_O,
                        (dnfSO, dnfO) => BuildCrossDnf(dnfSO, dnfO),
                        _abw.Eba.Top);
                }
            }

            if (_eagerAntimirov)
            {
                FlattenDnfToAntimirov(dnfTerm, result);
            }
            else
            {
                // DnfLeaves form: collapse the DNF at every leaf into the
                // union of its clauses (a single StateSet of all candidate
                // successor breakpoints). The outer list still represents
                // top-level disjunction; this entry is the *one* term that
                // covers every (cube → successor-set) pair without
                // distributing DNF clauses through the BDD structure.
                result.Add(CollapseDnfToStateSet(dnfTerm));
            }
            return result;
        }

        private TransitionTerm<StateSet<BreakpointState<TState>>>
            CollapseDnfToStateSet(TransitionTerm<Dnf<BreakpointState<TState>>> term)
        {
            if (term is TransitionTermLeaf<Dnf<BreakpointState<TState>>> leaf)
            {
                var dnf = leaf.Value;
                if (dnf.IsFalse || dnf.Clauses.Count == 0)
                    return TransitionTerm<StateSet<BreakpointState<TState>>>.Leaf(
                        StateSet<BreakpointState<TState>>.Empty(_bpComparer));
                if (dnf.Clauses.Count == 1)
                    return TransitionTerm<StateSet<BreakpointState<TState>>>.Leaf(dnf.Clauses[0]);
                var seen = new HashSet<BreakpointState<TState>>(
                    BreakpointState<TState>.GetEqualityComparer());
                var union = new List<BreakpointState<TState>>();
                foreach (var clause in dnf.Clauses)
                    foreach (var bp in clause)
                        if (seen.Add(bp))
                            union.Add(bp);
                return TransitionTerm<StateSet<BreakpointState<TState>>>.Leaf(
                    new StateSet<BreakpointState<TState>>(union, _bpComparer));
            }
            var ite = (TransitionTermIte<Dnf<BreakpointState<TState>>>)term;
            return TransitionTerm<StateSet<BreakpointState<TState>>>.Ite(
                ite.ConditionIndex,
                CollapseDnfToStateSet(ite.Hi),
                CollapseDnfToStateSet(ite.Lo));
        }

        private Dnf<BreakpointState<TState>> BuildResetDnf(Dnf<TState> dnfS)
        {
            if (dnfS.IsFalse)
                return new Dnf<BreakpointState<TState>>(
                    Array.Empty<StateSet<BreakpointState<TState>>>());

            var clauses = new List<StateSet<BreakpointState<TState>>>();
            foreach (var clauseS in dnfS.Clauses)
            {
                var reducedS = MacroReduce(clauseS).ReducedS;
                var nonAccepting = new List<TState>();
                foreach (var q in reducedS)
                    if (!_abw.IsAccepting(q))
                        nonAccepting.Add(q);
                var oPrime = nonAccepting.Count > 0
                    ? new StateSet<TState>(nonAccepting, _stateComparer)
                    : StateSet<TState>.Empty(_stateComparer);

                var successor = CanonBp(new BreakpointState<TState>(reducedS, oPrime));
                clauses.Add(StateSet<BreakpointState<TState>>.Singleton(successor, _bpComparer));
            }
            return new Dnf<BreakpointState<TState>>(clauses.ToArray());
        }

        private Dnf<BreakpointState<TState>> BuildOOnlyDnf(Dnf<TState> dnfO)
        {
            if (dnfO.IsFalse)
                return new Dnf<BreakpointState<TState>>(
                    Array.Empty<StateSet<BreakpointState<TState>>>());

            var clauses = new List<StateSet<BreakpointState<TState>>>();
            foreach (var clauseO in dnfO.Clauses)
            {
                var reducedS = MacroReduce(clauseO).ReducedS;
                var nonAccepting = new List<TState>();
                foreach (var q in reducedS)
                    if (!_abw.IsAccepting(q))
                        nonAccepting.Add(q);
                var oPrime = nonAccepting.Count > 0
                    ? new StateSet<TState>(nonAccepting, _stateComparer)
                    : StateSet<TState>.Empty(_stateComparer);

                var successor = CanonBp(new BreakpointState<TState>(reducedS, oPrime));
                clauses.Add(StateSet<BreakpointState<TState>>.Singleton(successor, _bpComparer));
            }
            return new Dnf<BreakpointState<TState>>(clauses.ToArray());
        }

        private Dnf<BreakpointState<TState>> BuildCrossDnf(
            Dnf<TState> dnfSO, Dnf<TState> dnfO)
        {
            if (dnfSO.IsFalse || dnfO.IsFalse)
                return new Dnf<BreakpointState<TState>>(
                    Array.Empty<StateSet<BreakpointState<TState>>>());

            var clauses = new List<StateSet<BreakpointState<TState>>>();
            foreach (var clauseSO in dnfSO.Clauses)
            {
                foreach (var clauseO in dnfO.Clauses)
                {
                    var sPrime = clauseSO.Union(clauseO);
                    var reduction = MacroReduce(sPrime);
                    var reducedS = reduction.ReducedS;
                    // O' = { rep(q) : q ∈ clauseO, q ∉ F }. Mapping
                    // through the representative function forwards every
                    // dropped obligation onto its surviving rep; the same-
                    // colour invariant of the reducer guarantees that the
                    // rep is itself non-accepting so it correctly stays in
                    // the obligation set. Dedup via HashSet on the BP
                    // comparer is implicit in the StateSet constructor.
                    var oReps = new List<TState>();
                    var seen = new HashSet<TState>(
                        EqualityComparer<TState>.Default);
                    foreach (var q in clauseO)
                    {
                        if (_abw.IsAccepting(q)) continue;
                        var rep = reduction.RepOf(q);
                        if (seen.Add(rep)) oReps.Add(rep);
                    }
                    var oPrime = oReps.Count > 0
                        ? new StateSet<TState>(oReps, _stateComparer)
                        : StateSet<TState>.Empty(_stateComparer);

                    var successor = CanonBp(new BreakpointState<TState>(reducedS, oPrime));
                    clauses.Add(StateSet<BreakpointState<TState>>.Singleton(successor, _bpComparer));
                }
            }
            return new Dnf<BreakpointState<TState>>(clauses.ToArray());
        }

        private void FlattenDnfToAntimirov(
            TransitionTerm<Dnf<BreakpointState<TState>>> term,
            List<TransitionTerm<StateSet<BreakpointState<TState>>>> result)
        {
            if (term is TransitionTermLeaf<Dnf<BreakpointState<TState>>> leaf)
            {
                foreach (var clause in leaf.Value.Clauses)
                    result.Add(TransitionTerm<StateSet<BreakpointState<TState>>>.Leaf(clause));
                return;
            }

            var ite = (TransitionTermIte<Dnf<BreakpointState<TState>>>)term;
            var hiTerms = new List<TransitionTerm<StateSet<BreakpointState<TState>>>>();
            var loTerms = new List<TransitionTerm<StateSet<BreakpointState<TState>>>>();
            FlattenDnfToAntimirov(ite.Hi, hiTerms);
            FlattenDnfToAntimirov(ite.Lo, loTerms);

            // When one branch is ⊥ (empty), use empty StateSet as dead-end leaf.
            var emptyLeaf = TransitionTerm<StateSet<BreakpointState<TState>>>.Leaf(
                StateSet<BreakpointState<TState>>.Empty(_bpComparer));

            if (hiTerms.Count == 0 && loTerms.Count == 0)
            {
                return;
            }
            else if (loTerms.Count == 0)
            {
                foreach (var h in hiTerms)
                    result.Add(TransitionTerm<StateSet<BreakpointState<TState>>>.Ite(
                        ite.ConditionIndex, h, emptyLeaf));
            }
            else if (hiTerms.Count == 0)
            {
                foreach (var l in loTerms)
                    result.Add(TransitionTerm<StateSet<BreakpointState<TState>>>.Ite(
                        ite.ConditionIndex, emptyLeaf, l));
            }
            else
            {
                foreach (var h in hiTerms)
                    foreach (var l in loTerms)
                        result.Add(TransitionTerm<StateSet<BreakpointState<TState>>>.Ite(
                            ite.ConditionIndex, h, l));
            }
        }

        #endregion
    }
}
