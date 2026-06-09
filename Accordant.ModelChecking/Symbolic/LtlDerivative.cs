namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Computes symbolic derivatives of <see cref="Ltl{TPred}"/> formulas and
    /// constructs a <see cref="SymbolicABW{TPredicate, TElement, TState}"/>
    /// whose states are LTL formulas.
    /// 
    /// The symbolic derivative ∂ maps an LTL formula to a transition term
    /// TTerm⟨A, B⁺(Ltl⟨A⟩)⟩:
    /// <list type="bullet">
    ///   <item>∂(⊤) = ⊤ (leaf: Dnf.True)</item>
    ///   <item>∂(⊥) = ⊥ (leaf: Dnf.False)</item>
    ///   <item>∂(p) = ITE(p, ⊤, ⊥)</item>
    ///   <item>∂(¬p) = ITE(p, ⊥, ⊤)</item>
    ///   <item>∂(Xφ) = atom(φ) (next state is φ)</item>
    ///   <item>∂(φ U ψ) = ∂(ψ) ∨ (∂(φ) ∧ atom(φ U ψ))</item>
    ///   <item>∂(φ R ψ) = (∂(ψ) ∧ atom(φ R ψ)) ∨ (∂(φ) ∧ ∂(ψ))</item>
    ///   <item>∂(φ ∧ ψ) = ∂(φ) ∧ ∂(ψ)</item>
    ///   <item>∂(φ ∨ ψ) = ∂(φ) ∨ ∂(ψ)</item>
    /// </list>
    /// 
    /// Accepting states for the ABW: a formula is accepting iff it is NOT
    /// an Until formula (Until formulas represent unfulfilled obligations).
    /// </summary>
    public class LtlDerivative<TPred, TElem>
    {
        private readonly IEffectiveBooleanAlgebra<TPred, TElem> _eba;
        private readonly ConditionRegistry<TPred> _registry;
        private readonly DnfAlgebra<Ltl<TPred>> _dnfAlgebra;
        private readonly TransitionTermAlgebra<TPred, TElem, Dnf<Ltl<TPred>>> _termAlgebra;
        private readonly Dictionary<Ltl<TPred>, TransitionTerm<Dnf<Ltl<TPred>>>> _derivCache;

        public LtlDerivative(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            ConditionRegistry<TPred> registry)
        {
            _eba = eba ?? throw new ArgumentNullException(nameof(eba));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _dnfAlgebra = new DnfAlgebra<Ltl<TPred>>(LtlComparer<TPred>.Instance);
            _termAlgebra = new TransitionTermAlgebra<TPred, TElem, Dnf<Ltl<TPred>>>(
                eba, registry, _dnfAlgebra);
            _derivCache = new Dictionary<Ltl<TPred>, TransitionTerm<Dnf<Ltl<TPred>>>>();
        }

        /// <summary>The EBA over predicates.</summary>
        public IEffectiveBooleanAlgebra<TPred, TElem> Eba => _eba;

        /// <summary>The condition registry.</summary>
        public ConditionRegistry<TPred> Registry => _registry;

        /// <summary>The B⁺(Ltl) leaf algebra.</summary>
        public DnfAlgebra<Ltl<TPred>> DnfAlgebra => _dnfAlgebra;

        /// <summary>The transition term algebra for TTerm⟨A, B⁺(Ltl)⟩.</summary>
        public TransitionTermAlgebra<TPred, TElem, Dnf<Ltl<TPred>>> TermAlgebra => _termAlgebra;

        /// <summary>
        /// Computes the symbolic derivative of an LTL formula.
        /// Returns a transition term TTerm⟨A, B⁺(Ltl⟨A⟩)⟩.
        /// Memoised: identical subformulas share computed transition terms.
        /// </summary>
        public TransitionTerm<Dnf<Ltl<TPred>>> Derivative(Ltl<TPred> formula)
        {
            if (_derivCache.TryGetValue(formula, out var cached))
                return cached;
            var result = DerivativeUncached(formula);
            _derivCache[formula] = result;
            return result;
        }

        /// <summary>Number of distinct subformulas whose derivative is cached.</summary>
        public int DerivativeCacheSize => _derivCache.Count;

        private TransitionTerm<Dnf<Ltl<TPred>>> DerivativeUncached(Ltl<TPred> formula)
        {
            switch (formula)
            {
                case LtlTrue<TPred> _:
                    return _termAlgebra.Top;

                case LtlFalse<TPred> _:
                    return _termAlgebra.Bottom;

                case LtlAtom<TPred> atom:
                {
                    int condIdx = _registry.Register(atom.Predicate);
                    // ∂(p) = ITE(p, ⊤, ⊥). Negation was pushed into the EBA at
                    // formula-construction time, so atoms only carry positive
                    // predicates here.
                    return _termAlgebra.MkIte(condIdx, _termAlgebra.Top, _termAlgebra.Bottom);
                }

                case LtlNext<TPred> next:
                    // ∂(Xφ) = atom(φ)
                    return TransitionTerm<Dnf<Ltl<TPred>>>.Leaf(
                        _dnfAlgebra.Atom(next.Inner));

                case LtlUntil<TPred> until:
                {
                    // ∂(φ U ψ) = ∂(ψ) ∨ (∂(φ) ∧ atom(φ U ψ))
                    var dPhi = Derivative(until.Left);
                    var dPsi = Derivative(until.Right);
                    var selfAtom = TransitionTerm<Dnf<Ltl<TPred>>>.Leaf(
                        _dnfAlgebra.Atom(formula));
                    var cont = _termAlgebra.And(dPhi, selfAtom);
                    return _termAlgebra.Or(dPsi, cont);
                }

                case LtlRelease<TPred> release:
                {
                    // ∂(φ R ψ) = (∂(ψ) ∧ atom(φ R ψ)) ∨ (∂(φ) ∧ ∂(ψ))
                    var dPhi = Derivative(release.Left);
                    var dPsi = Derivative(release.Right);
                    var selfAtom = TransitionTerm<Dnf<Ltl<TPred>>>.Leaf(
                        _dnfAlgebra.Atom(formula));
                    var cont = _termAlgebra.And(dPsi, selfAtom);
                    var done = _termAlgebra.And(dPhi, dPsi);
                    return _termAlgebra.Or(cont, done);
                }

                case LtlAnd<TPred> and:
                {
                    var result = Derivative(and.Operands[0]);
                    for (int i = 1; i < and.Operands.Count; i++)
                        result = _termAlgebra.And(result, Derivative(and.Operands[i]));
                    return result;
                }

                case LtlOr<TPred> or:
                {
                    var result = Derivative(or.Operands[0]);
                    for (int i = 1; i < or.Operands.Count; i++)
                        result = _termAlgebra.Or(result, Derivative(or.Operands[i]));
                    return result;
                }

                default:
                    throw new ArgumentException($"Unknown formula type: {formula.GetType()}");
            }
        }

        /// <summary>
        /// Determines if an LTL formula is an accepting state in the ABW.
        /// A formula is accepting iff it is NOT an Until formula.
        /// (Until represents an unfulfilled obligation that must eventually be satisfied.)
        /// </summary>
        public static bool IsAccepting(Ltl<TPred> formula)
            => !(formula is LtlUntil<TPred>);

        /// <summary>
        /// Constructs a symbolic ABW from an LTL formula.
        /// The initial state is the formula itself, and transitions are
        /// computed lazily via <see cref="Derivative"/>.
        /// </summary>
        public SymbolicABW<TPred, TElem, Ltl<TPred>> ToABW(Ltl<TPred> formula)
        {
            return new SymbolicABW<TPred, TElem, Ltl<TPred>>(
                _eba, _registry, _dnfAlgebra,
                formula,
                IsAccepting,
                Derivative);
        }
    }
}
