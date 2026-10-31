namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Symbolic Brzozowski derivative for <see cref="Ere{TPred}"/>: maps an
    /// ERE to a transition term <c>TTerm⟨A, Ere⟩</c> whose leaves give the
    /// residual ERE for each path-condition class of letters
    /// (Section 7.1 of the POPL'25 paper).
    ///
    /// Derivative rules:
    /// <list type="bullet">
    ///   <item>∂(∅) = ⊥-leaf (= ∅)</item>
    ///   <item>∂(ε) = ⊥-leaf</item>
    ///   <item>∂(p) = ITE(p, leaf(ε), leaf(∅))</item>
    ///   <item>∂(R · S) = (∂(R) ·' S) ∨ (R.Nullable ? ∂(S) : ⊥)
    ///       where ·'S lifts concatenation onto leaves</item>
    ///   <item>∂(R + S) = ∂(R) ∨ ∂(S)</item>
    ///   <item>∂(R &amp; S) = ∂(R) ∧ ∂(S)</item>
    ///   <item>∂(~R) = complement-lift over leaves of ∂(R)</item>
    ///   <item>∂(R*) = mapLeaves(R' → R' · R*)(∂(R))</item>
    ///   <item>∂(R : S) = (OneStep(R), ∂(S)) | (∂(R) : S)  — eq. (32), §7.3 JACM ext.</item>
    /// </list>
    /// </summary>
    public class EreDerivative<TPred, TElem>
    {
        private readonly EreLeafAlgebra<TPred> _leafAlgebra;
        private readonly TransitionTermAlgebra<TPred, TElem, Ere<TPred>> _termAlgebra;
        private readonly IEffectiveBooleanAlgebra<TPred, TElem> _eba;

        // Memoization keyed by Ere.Id (hash-consed unique identifier).
        // _derivCache[id] is the cached derivative of the Ere with that id, or
        // null if not yet computed. Grown by doubling when an Ere with a larger
        // Id is encountered. This avoids recomputing the derivative of any
        // sub-regex that recurs across a derivative tree (extremely common
        // because of right-association in Concat and the way Star, Complement,
        // and the recursive structure of derivative rules re-derive
        // sub-regexes).
        private TransitionTerm<Ere<TPred>>[] _derivCache = new TransitionTerm<Ere<TPred>>[64];

        // Equivalence-by-behavior detection (the user's optimization).
        // Two regexes R and R' with the same nullability AND the same derivative
        // (as a hash-consed TransitionTerm — equal by Id) are language-equivalent
        // by coinduction over the derivative state space, since transition-term
        // leaves are themselves hash-consed Ere terms.
        //
        // We store the FIRST regex Id encountered with each behavior signature
        // (Nullable, Derivative.Id). Subsequent regexes hitting the same
        // signature alias their cached derivative to the representative's, so
        // any future use that picks up the cache hit benefits from sharing.
        // The signature also feeds <see cref="AreEquivalent"/> (a sound
        // approximation of semantic ERE equivalence).
        private readonly Dictionary<(bool nullable, int derivId), int> _repByBehavior
            = new Dictionary<(bool, int), int>();
        private readonly Dictionary<int, int> _canonicalRep
            = new Dictionary<int, int>();

        // Precise-equivalence oracle (lazy: only built when first requested).
        // The checker uses *this* derivative instance, so its caches are shared
        // — no double computation of δ.
        private EreEquivalenceChecker<TPred, TElem> _preciseChecker;
        // Memo of precise (a ≡ b) decisions, keyed (min Id, max Id).
        private readonly Dictionary<(int, int), bool> _preciseEquivCache
            = new Dictionary<(int, int), bool>();

        public EreDerivative(
            IEffectiveBooleanAlgebra<TPred, TElem> eba,
            ConditionRegistry<TPred> registry)
        {
            if (eba == null) throw new ArgumentNullException(nameof(eba));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _eba = eba;
            _leafAlgebra = new EreLeafAlgebra<TPred>();
            _termAlgebra = new TransitionTermAlgebra<TPred, TElem, Ere<TPred>>(
                eba, registry, _leafAlgebra);
        }

        /// <summary>The transition-term algebra for TTerm⟨A, Ere⟩.</summary>
        public TransitionTermAlgebra<TPred, TElem, Ere<TPred>> TermAlgebra => _termAlgebra;

        /// <summary>
        /// <c>OneStep(R)</c>: the predicate over letters <c>a</c> such that the
        /// singleton word <c>a</c> is in <c>L(R)</c>. Used by the fusion derivative
        /// (eq. 32) to detect when <c>R</c> is fully consumed by the shared letter.
        /// </summary>
        public TPred OneStep(Ere<TPred> r)
        {
            switch (r)
            {
                case EreEmpty<TPred> _:   return _eba.Bottom;
                case EreEpsilon<TPred> _: return _eba.Bottom;
                case EreAtom<TPred> atom: return atom.Predicate;
                case EreConcat<TPred> c:
                {
                    // a ∈ L(R·S) iff a ∈ L(R) ∧ ε ∈ L(S)  or  ε ∈ L(R) ∧ a ∈ L(S)
                    TPred lhs = c.Right.Nullable ? OneStep(c.Left) : _eba.Bottom;
                    TPred rhs = c.Left.Nullable ? OneStep(c.Right) : _eba.Bottom;
                    return _eba.Or(lhs, rhs);
                }
                case EreUnion<TPred> u:
                {
                    TPred acc = _eba.Bottom;
                    foreach (var op in u.Operands) acc = _eba.Or(acc, OneStep(op));
                    return acc;
                }
                case EreIntersect<TPred> i:
                {
                    TPred acc = _eba.Top;
                    foreach (var op in i.Operands) acc = _eba.And(acc, OneStep(op));
                    return acc;
                }
                case EreComplement<TPred> cmp:
                    // a ∈ L(~R) iff a ∉ L(R)
                    return _eba.Not(OneStep(cmp.Inner));
                case EreStar<TPred> s:
                    // a ∈ L(R*) iff a ∈ L(R^k) for some k. Only k=1 can match a
                    // length-1 word, so OneStep(R*) = OneStep(R).
                    return OneStep(s.Inner);
                case EreFusion<TPred> f:
                    // a ∈ L(R:S) requires a ∈ L(R) ∧ a ∈ L(S) (both share the only letter).
                    return _eba.And(OneStep(f.Left), OneStep(f.Right));
                case EreProposition<TPred> _:
                case EreExists<TPred> _:
                    // EREQ Phase 2: OneStep returns a TPred, but propositions
                    // constrain letters via a Boolean valuation that is not
                    // expressible in the underlying predicate algebra. Fusion
                    // combined with quantified atoms is out of Phase-2 scope;
                    // gate it with a clear error rather than silently
                    // over-approximating.
                    throw new NotSupportedException(
                        "OneStep over EreProposition/EreExists is not supported "
                        + "(EREQ Phase 2 does not integrate quantified atoms "
                        + "with the fusion derivative).");
                default:
                    throw new ArgumentException($"Unknown ERE: {r.GetType()}");
            }
        }

        /// <summary>Computes ∂(R), memoised by <see cref="Ere{TPred}.Id"/>.</summary>
        public TransitionTerm<Ere<TPred>> Derivative(Ere<TPred> regex)
        {
            if (regex == null) throw new ArgumentNullException(nameof(regex));
            int id = regex.Id;
            if (id >= 0)
            {
                if (id < _derivCache.Length)
                {
                    var hit = _derivCache[id];
                    if (hit != null) return hit;
                }
                else
                {
                    int newLen = _derivCache.Length;
                    while (newLen <= id) newLen *= 2;
                    Array.Resize(ref _derivCache, newLen);
                }
            }

            var d = DerivativeUncached(regex);

            if (id >= 0)
            {
                // Behavior-signature equivalence: if some earlier regex R' was
                // seen with the same (Nullable, Derivative.Id), record the
                // alias. The transition term itself is already shared by
                // hash-consing, so the cached derivative we store under R's Id
                // is reference-equal to R's. The alias is exposed via
                // <see cref="CanonicalRepresentative"/> for downstream
                // canonicalisation passes.
                var sig = (regex.Nullable, d.Id);
                if (_repByBehavior.TryGetValue(sig, out var repId))
                {
                    if (repId != id) _canonicalRep[id] = repId;
                }
                else
                {
                    _repByBehavior[sig] = id;
                }
                _derivCache[id] = d;
            }
            return d;
        }

        /// <summary>
        /// Returns the canonical-representative Ere.Id for <paramref name="ere"/>
        /// according to the (nullable, derivative-Id) equivalence detected so far,
        /// or <paramref name="ere"/>.Id if no equivalent regex has been seen.
        /// Only meaningful after <see cref="Derivative"/> has been called for
        /// both members of an equivalence class.
        /// </summary>
        public int CanonicalRepresentative(Ere<TPred> ere)
        {
            if (ere == null) throw new ArgumentNullException(nameof(ere));
            return FindRep(ere.Id);
        }

        // Walk the _canonicalRep chain to its root, with path-compression on
        // the way back so repeated queries are amortised O(α(n)).
        private int FindRep(int id)
        {
            if (!_canonicalRep.TryGetValue(id, out var parent)) return id;
            int root = FindRep(parent);
            if (root != parent) _canonicalRep[id] = root;
            return root;
        }

        /// <summary>
        /// True when <paramref name="a"/> and <paramref name="b"/> have been
        /// observed to share the same (nullable, derivative-Id) signature, which
        /// — combined with leaf-level Ere hash-consing — implies language
        /// equivalence. Sound but incomplete: returns false for equivalent
        /// regexes whose derivatives have not (yet) been canonicalised to the
        /// same transition term.
        /// </summary>
        public bool AreEquivalent(Ere<TPred> a, Ere<TPred> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (ReferenceEquals(a, b) || a.Id == b.Id) return true;
            // Ensure both are in the cache.
            Derivative(a);
            Derivative(b);
            return FindRep(a.Id) == FindRep(b.Id);
        }

        /// <summary>
        /// Precise (complete) language-equivalence decision via the CAV'26
        /// bisimulation algorithm. Strictly stronger than the
        /// signature-based <see cref="AreEquivalent"/>: returns true for ALL
        /// language-equivalent pairs, not only those whose derivatives have
        /// already been canonicalised to the same transition term.
        ///
        /// <para>Decisions are memoised by (min Id, max Id). On a positive
        /// result, the canonical-rep chain is aliased so downstream
        /// <see cref="CanonicalRepresentative"/> queries return the same rep
        /// for both — giving the rest of the pipeline the benefit of
        /// state-space collapse without re-running the bisim.</para>
        ///
        /// <para>Cost: a bisim run per fresh (a, b) pair on cache miss.
        /// Memoised both ways via the symmetric key. Use this where the
        /// caller cares about completeness (e.g. NBW state minimisation),
        /// and <see cref="AreEquivalent"/> elsewhere.</para>
        /// </summary>
        public bool AreEquivalentPrecise(Ere<TPred> a, Ere<TPred> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (ReferenceEquals(a, b) || a.Id == b.Id) return true;

            // Fast path 1: signature-based check already proves equivalence.
            Derivative(a);
            Derivative(b);
            if (FindRep(a.Id) == FindRep(b.Id)) return true;

            // Cache: symmetric key.
            int aId = a.Id, bId = b.Id;
            var key = aId < bId ? (aId, bId) : (bId, aId);
            if (_preciseEquivCache.TryGetValue(key, out var cached)) return cached;

            // Run the precise checker.
            if (_preciseChecker == null)
                _preciseChecker = new EreEquivalenceChecker<TPred, TElem>(this);
            bool result = _preciseChecker.AreEquivalent(a, b);
            _preciseEquivCache[key] = result;

            // On equivalence: alias canonical reps so future queries (and any
            // downstream consumer of CanonicalRepresentative) benefit.
            if (result) UnionCanonicalRep(a.Id, b.Id);
            return result;
        }

        // Union two ids in the canonical-rep map. Keep the smaller Id as the
        // representative (it predates the larger one by construction of
        // the Ere intern table).
        private void UnionCanonicalRep(int x, int y)
        {
            int rx = FindRep(x), ry = FindRep(y);
            if (rx == ry) return;
            int keep = rx < ry ? rx : ry;
            int drop = rx < ry ? ry : rx;
            _canonicalRep[drop] = keep;
        }

        private TransitionTerm<Ere<TPred>> DerivativeUncached(Ere<TPred> regex)
        {
            switch (regex)
            {
                case EreEmpty<TPred> _:
                case EreEpsilon<TPred> _:
                    return _termAlgebra.Bottom;

                case EreAtom<TPred> atom:
                {
                    int idx = _termAlgebra.Registry.Register(atom.Predicate);
                    return _termAlgebra.MkIte(
                        idx,
                        TransitionTerm<Ere<TPred>>.Leaf(Ere<TPred>.Epsilon()),
                        _termAlgebra.Bottom);
                }

                case EreConcat<TPred> concat:
                {
                    var dR = Derivative(concat.Left);
                    var lifted = _termAlgebra.MapUnary(
                        dR, r => Ere<TPred>.Concat(r, concat.Right));
                    if (concat.Left.Nullable)
                        return _termAlgebra.Or(lifted, Derivative(concat.Right));
                    return lifted;
                }

                case EreUnion<TPred> union:
                {
                    var result = Derivative(union.Operands[0]);
                    for (int i = 1; i < union.Operands.Count; i++)
                        result = _termAlgebra.Or(result, Derivative(union.Operands[i]));
                    return result;
                }

                case EreIntersect<TPred> inter:
                {
                    var result = Derivative(inter.Operands[0]);
                    for (int i = 1; i < inter.Operands.Count; i++)
                        result = _termAlgebra.And(result, Derivative(inter.Operands[i]));
                    return result;
                }

                case EreComplement<TPred> comp:
                {
                    var dR = Derivative(comp.Inner);
                    return _termAlgebra.MapUnary(dR, Ere<TPred>.Complement);
                }

                case EreStar<TPred> star:
                {
                    var dR = Derivative(star.Inner);
                    return _termAlgebra.MapUnary(
                        dR, r => Ere<TPred>.Concat(r, star));
                }

                case EreFusion<TPred> fusion:
                {
                    // δ(R:S) = (OneStep(R), δ(S)) | (δ(R):S)        — eq. (32), §7.3
                    // The guard (α, t) is encoded as the TTerm (α ? ⊤ : ⊥) ∧ t, which
                    // keeps the BDD ordering correct regardless of α's index relative
                    // to t's top condition.
                    var oneStepR = OneStep(fusion.Left);
                    int idx = _termAlgebra.Registry.Register(oneStepR);
                    var guard = _termAlgebra.MkIte(
                        idx, _termAlgebra.Top, _termAlgebra.Bottom);
                    var guarded = _termAlgebra.And(guard, Derivative(fusion.Right));

                    var dRfused = _termAlgebra.MapUnary(
                        Derivative(fusion.Left),
                        r => Ere<TPred>.Fusion(r, fusion.Right));

                    return _termAlgebra.Or(guarded, dRfused);
                }

                case EreXor<TPred> xor:
                {
                    // δ(R₁ ⊕ … ⊕ Rₙ) = δR₁ ⊕ … ⊕ δRₙ — XOR commutes with
                    // derivative because complement does and XOR is built
                    // from complement+union (CAV'26 §4).
                    // For XNOR (Negated): δ(~X) = ~δX → complement leaves.
                    var result = Derivative(xor.Operands[0]);
                    for (int i = 1; i < xor.Operands.Count; i++)
                        result = _termAlgebra.Xor(result, Derivative(xor.Operands[i]));
                    if (xor.Negated)
                        result = _termAlgebra.MapUnary(result, Ere<TPred>.Complement);
                    return result;
                }

                case EreProposition<TPred> prop:
                {
                    // EREQ Phase 2 / paper §7: ∂(p) is a single-letter atom
                    // gated by the proposition's truth value. Polarity=true
                    // accepts when p holds along the letter; polarity=false
                    // accepts when p does not hold. Encoded as an ITE split
                    // on the (negative) proposition index — TransitionTermAlgebra
                    // recognises the negative level and skips path tightening
                    // (D5), so both branches remain reachable.
                    var epsLeaf = TransitionTerm<Ere<TPred>>.Leaf(Ere<TPred>.Epsilon());
                    return prop.Polarity
                        ? _termAlgebra.MkIte(prop.PropositionIndex, epsLeaf, _termAlgebra.Bottom)
                        : _termAlgebra.MkIte(prop.PropositionIndex, _termAlgebra.Bottom, epsLeaf);
                }

                case EreExists<TPred> ex:
                {
                    // EREQ Phase 2 / paper §7 / Rust prototype lib.rs:1199:
                    //   ∂(∃p. R) = ∃p. ∂(R)  lifted onto TTerm leaves.
                    // Bit-elimination optimisation: if the top condition of
                    // ∂(R) is exactly the bound proposition, distribute ∃ over
                    // both branches and union — this strips the prop split out
                    // of the resulting TTerm, since
                    //   ∃p. (p ? T₁ : T₀)  ≡  ∃p.T₁  ∪  ∃p.T₀.
                    var bodyDer = Derivative(ex.Body);
                    int bound = ex.PropositionIndex;
                    if (bodyDer is TransitionTermIte<Ere<TPred>> ite
                        && ite.ConditionIndex == bound)
                    {
                        var hi = _termAlgebra.MapUnary(
                            ite.Hi, r => Ere<TPred>.Exists(bound, r));
                        var lo = _termAlgebra.MapUnary(
                            ite.Lo, r => Ere<TPred>.Exists(bound, r));
                        return _termAlgebra.Or(hi, lo);
                    }
                    return _termAlgebra.MapUnary(
                        bodyDer, r => Ere<TPred>.Exists(bound, r));
                }

                default:
                    throw new ArgumentException($"Unknown ERE: {regex.GetType()}");
            }
        }
    }
}
