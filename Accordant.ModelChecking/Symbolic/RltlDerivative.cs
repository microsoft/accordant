namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;

    /// <summary>
    /// Symbolic derivative for <see cref="Rltl{TPred}"/> formulas. Produces a
    /// transition term TTerm⟨A, B⁺(Rltl⟨A⟩)⟩ — the same shape as the LTL
    /// derivative — so the result plugs directly into
    /// <see cref="SymbolicABW{TPred,TElem,TState}"/> and the existing
    /// alternation-elimination pipeline.
    ///
    /// LTL rules (identical to <see cref="LtlDerivative{TPred,TElem}"/>) plus:
    /// <list type="bullet">
    ///   <item>∂(R ; φ) = lift(R' → R';φ) (∂_ere(R)) ∨ (R.Nullable ? ∂(φ) : ⊥)</item>
    ///   <item>∂(R : φ) = applyCross(∂_ere(R), ∂(φ),
    ///     (R',d_φ) → atom(R':φ) ∨ (R'.Nullable ? d_φ : ⊥))</item>
    ///   <item>∂(R ⊳ φ) = lift(R' → R'⊳φ) (∂_ere(R)) ∧ (R.Nullable ? ∂(φ) : ⊤)</item>
    ///   <item>∂(R ⊳⊳ φ) = applyCross(∂_ere(R), ∂(φ),
    ///     (R',d_φ) → atom(R'⊳⊳φ) ∧ (R'.Nullable ? d_φ : ⊤))</item>
    /// </list>
    /// </summary>
    public class RltlDerivative<TPred, TElem>
    {
        private readonly IEffectiveBooleanAlgebra<TPred, TElem> _eba;
        private readonly ConditionRegistry<TPred> _registry;
        private readonly DnfAlgebra<Rltl<TPred>> _dnfAlgebra;
        private readonly TransitionTermAlgebra<TPred, TElem, Dnf<Rltl<TPred>>> _termAlgebra;
        private readonly EreDerivative<TPred, TElem> _ereDeriv;
        private readonly EreEmptinessChecker<TPred, TElem> _emptiness;
        private readonly IEreCanonicalizer<TPred> _ereCanon;
        private readonly IRltlCanonicalizer<TPred> _rltlCanon;
        private readonly bool _distributePrefixUnion;

        public RltlDerivative(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            ConditionRegistry<TPred> registry)
            : this(eba, registry, null, null, true)
        {
        }

        public RltlDerivative(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            ConditionRegistry<TPred> registry,
            IEreCanonicalizer<TPred> ereCanonicalizer)
            : this(eba, registry, ereCanonicalizer, null, true)
        {
        }

        public RltlDerivative(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            ConditionRegistry<TPred> registry,
            IEreCanonicalizer<TPred> ereCanonicalizer,
            IRltlCanonicalizer<TPred> rltlCanonicalizer)
            : this(eba, registry, ereCanonicalizer, rltlCanonicalizer, true)
        {
        }

        /// <summary>
        /// Full constructor exposing the Layer-A prefix-Union distribution
        /// toggle. When <paramref name="distributePrefixUnion"/> is false,
        /// derivative leaves wrap residual regexes in raw RLTL prefix atoms
        /// (no <c>(R₁+R₂):φ → R₁:φ ∨ R₂:φ</c> distribution). Used by
        /// benchmarks that need the un-distributed baseline as a control
        /// for state-space measurements.
        /// </summary>
        public RltlDerivative(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            ConditionRegistry<TPred> registry,
            IEreCanonicalizer<TPred> ereCanonicalizer,
            IRltlCanonicalizer<TPred> rltlCanonicalizer,
            bool distributePrefixUnion)
        {
            _eba = eba ?? throw new ArgumentNullException(nameof(eba));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _dnfAlgebra = new DnfAlgebra<Rltl<TPred>>(RltlComparer<TPred>.Instance);
            _termAlgebra = new TransitionTermAlgebra<TPred, TElem, Dnf<Rltl<TPred>>>(
                eba, registry, _dnfAlgebra);
            _ereDeriv = new EreDerivative<TPred, TElem>(eba, registry);
            _emptiness = new EreEmptinessChecker<TPred, TElem>(_ereDeriv);
            _ereCanon = ereCanonicalizer;
            _rltlCanon = rltlCanonicalizer;
            _distributePrefixUnion = distributePrefixUnion;
        }

        public IEffectiveBooleanAlgebra<TPred, TElem> Eba => _eba;
        public ConditionRegistry<TPred> Registry => _registry;
        public DnfAlgebra<Rltl<TPred>> DnfAlgebra => _dnfAlgebra;
        public TransitionTermAlgebra<TPred, TElem, Dnf<Rltl<TPred>>> TermAlgebra => _termAlgebra;
        public EreDerivative<TPred, TElem> EreDerivative => _ereDeriv;
        public EreEmptinessChecker<TPred, TElem> Emptiness => _emptiness;
        public IEreCanonicalizer<TPred> EreCanonicalizer => _ereCanon;
        public IRltlCanonicalizer<TPred> RltlCanonicalizer => _rltlCanon;

        private Ere<TPred> Canon(Ere<TPred> r) => _ereCanon == null ? r : _ereCanon.Canonicalize(r);

        /// <summary>Computes ∂(φ) for a RLTL formula.</summary>
        public TransitionTerm<Dnf<Rltl<TPred>>> Derivative(Rltl<TPred> formula)
        {
            switch (formula)
            {
                case RltlTrue<TPred> _: return _termAlgebra.Top;
                case RltlFalse<TPred> _: return _termAlgebra.Bottom;

                case RltlAtom<TPred> atom:
                {
                    int idx = _registry.Register(atom.Predicate);
                    // Atoms only carry positive predicates; negation flowed
                    // into the EBA at formula-construction time.
                    return _termAlgebra.MkIte(idx, _termAlgebra.Top, _termAlgebra.Bottom);
                }

                case RltlNext<TPred> next:
                    return TransitionTerm<Dnf<Rltl<TPred>>>.Leaf(ToDnfAtom(next.Inner));

                case RltlUntil<TPred> until:
                {
                    var dPhi = Derivative(until.Left);
                    var dPsi = Derivative(until.Right);
                    var selfAtom = TransitionTerm<Dnf<Rltl<TPred>>>.Leaf(_dnfAlgebra.Atom(formula));
                    return _termAlgebra.Or(dPsi, _termAlgebra.And(dPhi, selfAtom));
                }

                case RltlRelease<TPred> release:
                {
                    var dPhi = Derivative(release.Left);
                    var dPsi = Derivative(release.Right);
                    var selfAtom = TransitionTerm<Dnf<Rltl<TPred>>>.Leaf(_dnfAlgebra.Atom(formula));
                    return _termAlgebra.Or(_termAlgebra.And(dPsi, selfAtom), _termAlgebra.And(dPhi, dPsi));
                }

                case RltlAnd<TPred> and:
                {
                    var result = Derivative(and.Operands[0]);
                    for (int i = 1; i < and.Operands.Count; i++)
                        result = _termAlgebra.And(result, Derivative(and.Operands[i]));
                    return result;
                }

                case RltlOr<TPred> or:
                {
                    var result = Derivative(or.Operands[0]);
                    for (int i = 1; i < or.Operands.Count; i++)
                        result = _termAlgebra.Or(result, Derivative(or.Operands[i]));
                    return result;
                }

                case RltlSeqPrefix<TPred> seq:
                {
                    var dR = _ereDeriv.Derivative(seq.Regex);
                    var lifted = _ereDeriv.TermAlgebra.MapUnary<Dnf<Rltl<TPred>>>(
                        dR, r => ToDnfAtom(MkSeqPrefix(Canon(r), seq.Phi)));
                    if (seq.Regex.Nullable)
                        return _termAlgebra.Or(lifted, Derivative(seq.Phi));
                    return lifted;
                }

                case RltlOvlPrefix<TPred> ovl:
                {
                    var dR = _ereDeriv.Derivative(ovl.Regex);
                    var dPhi = Derivative(ovl.Phi);
                    return _ereDeriv.TermAlgebra.ApplyCross<Dnf<Rltl<TPred>>, Dnf<Rltl<TPred>>>(
                        dR, dPhi,
                        (rPrime, dF) =>
                        {
                            var atomDnf = ToDnfAtom(MkOvlPrefix(Canon(rPrime), ovl.Phi));
                            if (rPrime.Nullable)
                                return _dnfAlgebra.Or(atomDnf, dF);
                            return atomDnf;
                        },
                        _eba.Top);
                }

                case RltlTrigger<TPred> trig:
                {
                    var dR = _ereDeriv.Derivative(trig.Regex);
                    var lifted = _ereDeriv.TermAlgebra.MapUnary<Dnf<Rltl<TPred>>>(
                        dR, r => ToDnfAtom(MkTrigger(Canon(r), trig.Phi)));
                    if (trig.Regex.Nullable)
                        return _termAlgebra.And(lifted, Derivative(trig.Phi));
                    return lifted;
                }

                case RltlMatch<TPred> mat:
                {
                    var dR = _ereDeriv.Derivative(mat.Regex);
                    var dPhi = Derivative(mat.Phi);
                    return _ereDeriv.TermAlgebra.ApplyCross<Dnf<Rltl<TPred>>, Dnf<Rltl<TPred>>>(
                        dR, dPhi,
                        (rPrime, dF) =>
                        {
                            var atomDnf = ToDnfAtom(MkMatch(Canon(rPrime), mat.Phi));
                            if (rPrime.Nullable)
                                return _dnfAlgebra.And(atomDnf, dF);
                            return atomDnf;
                        },
                        _eba.Top);
                }

                // Closures — JACM eq. (3010)–(3014).
                //
                //   deriv({R})    = ite(Null(R), ⊤, {deriv(R)})
                //   deriv({{R}}̄) = ite(Null(R), ⊥, {{deriv(R)}}̄)
                //   deriv({R}ω)  = deriv(R ; X {R}ω)
                //
                // The Rltl<TPred>.{WeakClosure, NegWeakClosure, OmegaClosure}
                // factories apply syntactic shortcuts (EreEmpty, Nullable); we
                // additionally apply the *semantic* emptiness check via the
                // EreEmptinessChecker so that residuals which simplify to
                // semantically-dead (but not syntactically EreEmpty) regexes
                // become ⊥/⊤ instead of unreachable junk states. This is
                // important for the correctness of IsAccepting (Acc(RLTL+):
                // {R} ∈ Acc iff R alive; {{R}}̄ ∈ Acc iff R dead).

                case RltlWeakClosure<TPred> wcl:
                {
                    if (wcl.Regex.Nullable) return _termAlgebra.Top;
                    var dR = _ereDeriv.Derivative(wcl.Regex);
                    return _ereDeriv.TermAlgebra.MapUnary<Dnf<Rltl<TPred>>>(
                        dR, r => ToDnfAtom(LiftWeakClosure(r)));
                }

                case RltlNegWeakClosure<TPred> nwcl:
                {
                    if (nwcl.Regex.Nullable) return _termAlgebra.Bottom;
                    var dR = _ereDeriv.Derivative(nwcl.Regex);
                    return _ereDeriv.TermAlgebra.MapUnary<Dnf<Rltl<TPred>>>(
                        dR, r => ToDnfAtom(LiftNegWeakClosure(r)));
                }

                case RltlOmegaClosure<TPred> ocl:
                {
                    // deriv({R}ω) = deriv(R ; X{R}ω)
                    return Derivative(
                        Rltl<TPred>.SeqPrefix(ocl.Regex, Rltl<TPred>.Next(ocl)));
                }

                default:
                    throw new ArgumentException($"Unknown RLTL: {formula.GetType()}");
            }
        }

        /// <summary>Build {R'} with semantic dead-check.</summary>
        private Rltl<TPred> LiftWeakClosure(Ere<TPred> r)
        {
            if (_emptiness.IsDead(r)) return Rltl<TPred>.False();
            return Rltl<TPred>.WeakClosure(Canon(r));
        }

        /// <summary>Build {{R'}}̄ with semantic dead-check.</summary>
        private Rltl<TPred> LiftNegWeakClosure(Ere<TPred> r)
        {
            if (_emptiness.IsDead(r)) return Rltl<TPred>.True();
            return Rltl<TPred>.NegWeakClosure(Canon(r));
        }

        // Layer-A-toggle dispatch helpers. When _distributePrefixUnion is true
        // these call the smart constructors that distribute Union; when false
        // they fall back to the raw factories that keep the Union nested.
        private Rltl<TPred> MkSeqPrefix(Ere<TPred> r, Rltl<TPred> phi)
            => _distributePrefixUnion
                ? Rltl<TPred>.SeqPrefix(r, phi)
                : Rltl<TPred>.SeqPrefixRaw(r, phi);

        private Rltl<TPred> MkOvlPrefix(Ere<TPred> r, Rltl<TPred> phi)
            => _distributePrefixUnion
                ? Rltl<TPred>.OvlPrefix(r, phi)
                : Rltl<TPred>.OvlPrefixRaw(r, phi);

        private Rltl<TPred> MkTrigger(Ere<TPred> r, Rltl<TPred> phi)
            => _distributePrefixUnion
                ? Rltl<TPred>.Trigger(r, phi)
                : Rltl<TPred>.TriggerRaw(r, phi);

        private Rltl<TPred> MkMatch(Ere<TPred> r, Rltl<TPred> phi)
            => _distributePrefixUnion
                ? Rltl<TPred>.Match(r, phi)
                : Rltl<TPred>.MatchRaw(r, phi);

        /// <summary>
        /// Converts a Rltl formula into the corresponding Dnf leaf, handling
        /// the structural ⊤/⊥ cases so that the resulting Dnf is canonical.
        /// </summary>
        private Dnf<Rltl<TPred>> ToDnfAtom(Rltl<TPred> f)
        {
            if (f is RltlFalse<TPred>) return _dnfAlgebra.Bottom;
            if (f is RltlTrue<TPred>) return _dnfAlgebra.Top;
            if (_rltlCanon != null)
            {
                var canonF = _rltlCanon.Canonicalize(f);
                if (canonF is RltlFalse<TPred>) return _dnfAlgebra.Bottom;
                if (canonF is RltlTrue<TPred>) return _dnfAlgebra.Top;
                f = canonF;
            }
            return _dnfAlgebra.Atom(f);
        }

        /// <summary>
        /// Accepting condition for the RLTL ABW (JACM Def. M-RLTL+, line 3032):
        /// accepting iff the state carries no liveness obligation.
        /// <list type="bullet">
        ///   <item>⊤, R⊳φ (uimpl), R⊳⊳φ, Release, all Boolean / Next over
        ///   non-liveness — accepting (no live obligation).</item>
        ///   <item>U, R;φ (eimpl), R:φ — non-accepting (have liveness).</item>
        ///   <item>{R} weak closure: accepting iff <c>R</c> is alive (semantic
        ///   check via <see cref="EreEmptinessChecker{TPred,TElem}"/>).
        ///   Dead-R weak closures should have been simplified to ⊥ by the
        ///   derivative pipeline, but we re-check defensively.</item>
        ///   <item>{{R}}̄ neg-weak closure: accepting iff <c>R</c> is dead
        ///   (the obligation has been discharged).</item>
        ///   <item>{R}ω ω-closure: always accepting (its obligation is
        ///   absorbed by the SeqPrefix unrolling in the derivative).</item>
        /// </list>
        /// </summary>
        public bool IsAccepting(Rltl<TPred> f)
        {
            switch (f)
            {
                case RltlUntil<TPred> _:
                case RltlSeqPrefix<TPred> _:
                case RltlOvlPrefix<TPred> _:
                    return false;
                case RltlWeakClosure<TPred> w:
                    return _emptiness.IsAlive(w.Regex);
                case RltlNegWeakClosure<TPred> n:
                    return _emptiness.IsDead(n.Regex);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Constructs a symbolic ABW for an RLTL formula. The resulting
        /// automaton can be passed to <see cref="AlternationElimination"/> or
        /// the incremental <see cref="IncrementalAE{TPred,TElem,TState}"/>
        /// to obtain an NBW for model checking.
        /// </summary>
        public SymbolicABW<TPred, TElem, Rltl<TPred>> ToABW(Rltl<TPred> formula)
        {
            return new SymbolicABW<TPred, TElem, Rltl<TPred>>(
                _eba, _registry, _dnfAlgebra,
                formula,
                IsAccepting,
                Derivative);
        }
    }
}
