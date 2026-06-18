namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Extended regular expression (ERE) over a predicate algebra A,
    /// generic in the predicate type <typeparamref name="TPred"/>.
    /// EREs are the regex component of RLTL formulas (Section 7 of the
    /// POPL'25 paper).
    ///
    /// Forms:
    /// <list type="bullet">
    ///   <item><see cref="EreEmpty{TPred}"/>:      ∅       (empty language)</item>
    ///   <item><see cref="EreEpsilon{TPred}"/>:    ε       (language containing only the empty word)</item>
    ///   <item><see cref="EreAtom{TPred}"/>:       p       (single letter satisfying predicate p)</item>
    ///   <item><see cref="EreConcat{TPred}"/>:     R · S   (concatenation)</item>
    ///   <item><see cref="EreUnion{TPred}"/>:      R + S   (ACI-normalized)</item>
    ///   <item><see cref="EreIntersect{TPred}"/>:  R &amp; S   (ACI-normalized)</item>
    ///   <item><see cref="EreComplement{TPred}"/>: ~R      (Σ* \ L(R))</item>
    ///   <item><see cref="EreStar{TPred}"/>:       R*      (Kleene star)</item>
    ///   <item><see cref="EreXor{TPred}"/>:        R ⊕ S   (symmetric difference, AC + self-inverse)</item>
    /// </list>
    ///
    /// Factory methods apply the standard simplifications so structural
    /// equality coincides with semantic equivalence on the algebraic core.
    /// </summary>
    public abstract class Ere<TPred> : IEquatable<Ere<TPred>>, IComparable<Ere<TPred>>
    {
        private int? _hash;
        private int _id = -1;
        internal Ere() { }

        /// <summary>
        /// Unique non-negative identifier within the
        /// <see cref="DefaultBuilder"/>. Assigned by
        /// <see cref="EreBuilder{TPred}.Intern"/> on first canonicalisation.
        /// <c>Id 0 == Bottom (∅)</c>, <c>Id 1 == Epsilon (ε)</c>.
        /// </summary>
        public int Id => _id;

        /// <summary>True once this term has been interned and assigned an Id.</summary>
        internal bool HasId => _id >= 0;

        internal void AssignId(int id) { _id = id; }

        /// <summary>
        /// Per-<typeparamref name="TPred"/> default term builder. All static
        /// factories on this type route through it so every returned ERE is
        /// canonical (reference-equal to any structurally equivalent term).
        /// </summary>
        public static EreBuilder<TPred> DefaultBuilder => BuilderHolder.Instance;

        private static class BuilderHolder
        {
            internal static readonly EreBuilder<TPred> Instance = new EreBuilder<TPred>();
        }

        /// <summary>Structural kind index for total ordering.</summary>
        internal abstract int Kind { get; }

        /// <summary>True iff the language contains the empty word.</summary>
        public abstract bool Nullable { get; }

        /// <summary>
        /// True iff this term contains an <see cref="EreComplement{TPred}"/>
        /// node anywhere in its syntax tree. Propagated eagerly at
        /// construction; constant-time after caching. Mirrors the Rust
        /// EREQ <c>CONTAINS_COMPL</c> meta flag (lib.rs:524–558). Used by
        /// <see cref="IsDefinitelyAlive"/> and to gate the more expensive
        /// rewrite/simplification rules that only matter in the presence
        /// of complement.
        /// </summary>
        public virtual bool ContainsCompl => false;

        /// <summary>
        /// True iff this term contains an <see cref="EreIntersect{TPred}"/>
        /// node anywhere in its syntax tree. Propagated eagerly at
        /// construction. Mirrors the Rust EREQ <c>CONTAINS_INTER</c> meta
        /// flag. Combined with <see cref="ContainsCompl"/> to identify the
        /// "standard fragment" (regexes built only from atoms, ε, ∅,
        /// concat, union, and star), which is the alive-by-construction
        /// fragment.
        /// </summary>
        public virtual bool ContainsInter => false;

        /// <summary>
        /// True iff this term contains an <see cref="EreExists{TPred}"/>
        /// node anywhere in its syntax tree. Distinct from
        /// <see cref="FreeProps"/>, which tracks proposition occurrences
        /// (free or bound). Mirrors the Rust EREQ <c>CONTAINS_EXISTS</c>
        /// meta flag.
        /// </summary>
        public virtual bool ContainsExists => false;

        /// <summary>
        /// Structural size of this term — a coarse complexity proxy.
        /// Leaves cost <c>1</c>; compound nodes cost <c>Σ children.Cost + 1</c>.
        /// Mirrors the Rust EREQ <c>Metadata.cost</c> field (lib.rs:573–578)
        /// and is intended to gate expensive simplification rules (subsumption,
        /// brute-force equivalence) that the Rust runs only when
        /// <c>cost &lt; threshold</c>.
        /// </summary>
        public abstract int Cost { get; }

        /// <summary>
        /// Conservative lower bound on the length of any word in this
        /// language. Mirrors Rust EREQ <c>get_min_max_len</c> (lib.rs:999–1038).
        /// </summary>
        public virtual int MinLen => 0;

        /// <summary>
        /// Conservative upper bound on the length of any word in this
        /// language. <see cref="int.MaxValue"/> represents <c>+∞</c>.
        /// Mirrors Rust EREQ <c>get_min_max_len</c>.
        /// </summary>
        public virtual int MaxLen => int.MaxValue;

        /// <summary>Saturating addition; <c>+∞ + k = +∞</c>.</summary>
        internal static int SatAdd(int a, int b)
        {
            if (a == int.MaxValue || b == int.MaxValue) return int.MaxValue;
            long sum = (long)a + b;
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>Saturating subtraction (floored at 0).</summary>
        internal static int SatSub(int a, int b)
        {
            if (a == int.MaxValue) return int.MaxValue;
            long d = (long)a - b;
            return d < 0 ? 0 : (int)d;
        }

        /// <summary>
        /// Conservative "this regex denotes a non-empty language" check.
        /// Returns <c>true</c> exactly when this term lies in the standard
        /// fragment (no complement, no intersection) and is not the literal
        /// empty language. For such terms aliveness is guaranteed by
        /// construction. Returning <c>false</c> means "unknown" — the term
        /// may still be alive, but settling the question requires the more
        /// expensive emptiness check. Useful as a fast-path in weak closure
        /// and similar contexts where the unknown branch is costly.
        /// </summary>
        public bool IsDefinitelyAlive
            => !(this is EreEmpty<TPred>) && !ContainsCompl && !ContainsInter;

        /// <summary>
        /// Bitset of free propositions referenced anywhere inside this
        /// term. Bit <c>i</c> corresponds to proposition with registry
        /// index <c>-(i+1)</c>. EREQ Phase-1 D2: capped at 64 distinct
        /// propositions; out-of-range usage throws at the construction
        /// site. The default value is <c>0UL</c> (no free propositions)
        /// — composite subclasses override to OR over their children;
        /// <c>EreExists</c> (Phase 2) clears the bit for its bound
        /// proposition.
        /// </summary>
        public virtual ulong FreeProps => 0UL;

        /// <summary>
        /// Returns the bit corresponding to proposition index
        /// <paramref name="propIdx"/> (which must be strictly negative
        /// and within <see cref="ConditionRegistry{TPred}.MaxPropositions"/>).
        /// </summary>
        public static ulong BitForProp(int propIdx)
        {
            if (propIdx >= 0)
                throw new ArgumentOutOfRangeException(nameof(propIdx),
                    "Proposition indices must be strictly negative.");
            int slot = -propIdx - 1;
            if (slot >= 64)
                throw new ArgumentOutOfRangeException(nameof(propIdx),
                    $"Proposition index {propIdx} exceeds the 64-proposition cap.");
            return 1UL << slot;
        }

        #region Factories

        public static Ere<TPred> Empty() => DefaultBuilder.Bottom;
        public static Ere<TPred> Epsilon() => DefaultBuilder.Epsilon;
        public static Ere<TPred> Atom(TPred predicate)
            => DefaultBuilder.Intern(new EreAtom<TPred>(predicate));

        /// <summary>
        /// Single-letter atom constrained by a proposition variable
        /// (EREQ Phase 2). <paramref name="propIdx"/> must be a
        /// strictly-negative index obtained from
        /// <see cref="ConditionRegistry{TPredicate}.RegisterProposition(string)"/>.
        /// <c>L(p) = { letter w | proposition p holds at w }</c>.
        /// <paramref name="polarity"/>=<c>false</c> denotes ¬p.
        /// </summary>
        public static Ere<TPred> PropositionAtom(int propIdx, bool polarity = true)
            => DefaultBuilder.Intern(new EreProposition<TPred>(propIdx, polarity));

        /// <summary>
        /// Existential projection over a proposition (EREQ Phase 2).
        /// <c>L(∃p. R) = { w | ∃ assignment of p along w that puts w in L(R) }</c>.
        /// <para>
        /// Eager rewrite cascade (paper §3.4 / Rust prototype
        /// <c>mk_exists</c>; Phase-0 decision D3):
        /// <list type="bullet">
        /// <item><c>∃p. ∅ = ∅</c>, <c>∃p. ε = ε</c>, <c>∃p. Σ* = Σ*</c></item>
        /// <item><c>p ∉ Free(R)  ⇒  ∃p. R = R</c> (pass-through, D2)</item>
        /// <item><c>∃p. (R · S) = (∃p. R) · (∃p. S)</c></item>
        /// <item><c>∃p. (R + S) = (∃p. R) + (∃p. S)</c> — mandatory (D3)</item>
        /// <item><c>∃p. (R*)    = (∃p. R)*</c></item>
        /// <item><c>∃p. (R : S) = (∃p. R) : (∃p. S)</c></item>
        /// <item><c>∃p. (A ∩ B) = A ∩ ∃p. B</c> if <c>p ∉ Free(A)</c>
        ///   (leveraged O(1) rewrite using FreeProps; partitions
        ///   conjuncts).</item>
        /// <item><c>∃p. (R ⊕ S) = ∃p.((R ∧ ¬S) + (¬R ∧ S))</c> — XOR
        ///   expanded, then the above rules apply.</item>
        /// </list>
        /// Cases without a structural rule fall through to a residual
        /// <see cref="EreExists{TPred}"/> node (e.g. <c>∃p</c> over
        /// <see cref="EreProposition{TPred}"/>, <see cref="EreComplement{TPred}"/>,
        /// or a nested <see cref="EreExists{TPred}"/>): the derivative
        /// algorithm (<c>ereq-p2-derivative</c>) handles those at run
        /// time.
        /// </para>
        /// </summary>
        public static Ere<TPred> Exists(int propIdx, Ere<TPred> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (propIdx >= 0)
                throw new ArgumentOutOfRangeException(nameof(propIdx),
                    "Proposition indices must be strictly negative.");
            var bit = BitForProp(propIdx); // also validates the 64-cap
            if ((body.FreeProps & bit) == 0UL) return body; // p not free → pass through

            // Structural distributions (paper §3.4). All recursive calls
            // re-enter this factory so the FreeProps pass-through fires
            // wherever propIdx fails to reach.
            switch (body)
            {
                case EreEmpty<TPred>:                       // ∃p. ∅ = ∅
                case EreEpsilon<TPred>:                     // ∃p. ε = ε
                    return body;

                case EreConcat<TPred> cc:                   // ∃p. (R·S) = ∃p.R · ∃p.S
                    return Concat(Exists(propIdx, cc.Left), Exists(propIdx, cc.Right));

                case EreUnion<TPred> uu:                    // ∃p. (R+S) = ∃p.R + ∃p.S — D3
                {
                    Ere<TPred> acc = Empty();
                    foreach (var op in uu.Operands)
                        acc = Union(acc, Exists(propIdx, op));
                    return acc;
                }

                case EreStar<TPred> st:                     // ∃p. R* = (∃p.R)*
                    return Star(Exists(propIdx, st.Inner));

                case EreFusion<TPred> fu:                   // ∃p. (R:S) = ∃p.R : ∃p.S
                    return Fusion(Exists(propIdx, fu.Left), Exists(propIdx, fu.Right));

                case EreIntersect<TPred> ii:                // ∃p. (A ∩ B) = A ∩ ∃p.B if p ∉ Free(A)
                {
                    Ere<TPred> extracted = Sigma(); // intersection identity
                    Ere<TPred> kept = Sigma();
                    foreach (var op in ii.Operands)
                    {
                        if ((op.FreeProps & bit) == 0UL)
                            extracted = Intersect(extracted, op);
                        else
                            kept = Intersect(kept, op);
                    }
                    if (!IsSigma(extracted))
                    {
                        // recurse on the kept block: still contains p,
                        // but may admit further internal simplification
                        // through factory-canonicalised forms.
                        var inner = IsSigma(kept)
                            ? kept
                            : DefaultBuilder.Intern(new EreExists<TPred>(propIdx, kept));
                        return Intersect(extracted, inner);
                    }
                    // Nothing to extract — fall through to residual node.
                    break;
                }

                case EreXor<TPred> xx:                      // expand XOR then re-enter
                {
                    Ere<TPred> expanded = ExpandXor(xx);
                    return Exists(propIdx, expanded);
                }
            }

            // Σ* (= ~∅) pass-through: handled implicitly because FreeProps(Σ*)=0.
            return DefaultBuilder.Intern(new EreExists<TPred>(propIdx, body));
        }

        // ExpandXor: (R ⊕ S ⊕ T …) [⊙ if Negated]
        //   binary  → (R ∧ ¬S) + (¬R ∧ S), and ⊙ adds outer complement
        //   n-ary   → fold left as (((R₁ ⊕ R₂) ⊕ R₃) ⊕ …) and expand each step
        private static Ere<TPred> ExpandXor(EreXor<TPred> x)
        {
            var ops = x.Operands;
            Ere<TPred> acc = ops[0];
            for (int i = 1; i < ops.Count; i++)
            {
                var r = acc;
                var s = ops[i];
                acc = Union(
                    Intersect(r, Complement(s)),
                    Intersect(Complement(r), s));
            }
            return x.Negated ? Complement(acc) : acc;
        }

        /// <summary>Σ* — the universal language (= ~∅).</summary>
        public static Ere<TPred> Sigma() => Complement(Empty());

        public static Ere<TPred> Concat(Ere<TPred> a, Ere<TPred> b)
        {
            if (a is EreEmpty<TPred> || b is EreEmpty<TPred>) return Empty();
            if (a is EreEpsilon<TPred>) return b;
            if (b is EreEpsilon<TPred>) return a;
            // Distribute Concat over Union on the LEFT only (right-propagating):
            //   (R₁ + R₂) · S = R₁·S + R₂·S
            // Exposes top-level DNF — feeds the Phase 7 driver splitting in
            // EreEmptinessChecker and the ∃-over-+ rule in Exists. Hash-cons
            // identity in the resulting Union dedups equivalent disjuncts.
            // Right-side Union is *not* distributed to avoid the quadratic
            // blow-up of (a+b)·(c+d) = ac+ad+bc+bd.
            if (a is EreUnion<TPred> ua)
            {
                Ere<TPred> acc = Empty();
                foreach (var op in ua.Operands) acc = Union(acc, Concat(op, b));
                return acc;
            }
            // Right-associate: (a·b)·c → a·(b·c) for canonical form.
            if (a is EreConcat<TPred> ac)
                return Concat(ac.Left, Concat(ac.Right, b));
            // R* · R* ≡ R*  (with same R). Handle both b = Star(R) and
            // b = Concat(Star(R), rest) — the latter arises after right-association.
            if (a is EreStar<TPred> sa)
            {
                if (b is EreStar<TPred> sb && sa.Inner.Equals(sb.Inner)) return a;
                if (b is EreConcat<TPred> bc
                    && bc.Left is EreStar<TPred> sb2
                    && sa.Inner.Equals(sb2.Inner))
                    return Concat(a, bc.Right);
            }
            return DefaultBuilder.Intern(new EreConcat<TPred>(a, b));
        }

        public static Ere<TPred> Union(Ere<TPred> a, Ere<TPred> b)
        {
            if (a is EreEmpty<TPred>) return b;
            if (b is EreEmpty<TPred>) return a;
            if (IsSigma(a) || IsSigma(b)) return Sigma();

            var ops = new SortedSet<Ere<TPred>>(EreComparer<TPred>.Instance);
            CollectUnion(a, ops);
            CollectUnion(b, ops);

            // Complementary-language elimination: R + ~R ≡ Σ*.
            foreach (var op in ops)
            {
                if (op is EreComplement<TPred> c && ops.Contains(c.Inner)) return Sigma();
            }

            // R + R* ≡ R*: for every Star(R) operand, drop other operands that are
            // already in L(R*) — namely R itself, ε (since R* is nullable), and R·R*.
            var starInners = ops.OfType<EreStar<TPred>>().Select(s => s.Inner).ToList();
            if (starInners.Count > 0)
            {
                ops.RemoveWhere(x =>
                {
                    if (x is EreStar<TPred>) return false;
                    if (x is EreEpsilon<TPred>) return true; // ε ⊆ R* for any R
                    foreach (var inner in starInners)
                    {
                        if (x.Equals(inner)) return true;                          // R ⊆ R*
                        if (x is EreConcat<TPred> cc                                // R·R* ⊆ R*
                            && cc.Left.Equals(inner)
                            && cc.Right is EreStar<TPred> rs
                            && rs.Inner.Equals(inner)) return true;
                    }
                    return false;
                });
            }

            // R⁺ = R · R*  collapse rules under Union (Rust SU at lib.rs:3549–3559):
            //   ε  | R⁺  →  R*
            //   R* | R⁺  →  R*   (same R)
            // Detected by walking ops for EreConcat(L, EreStar(L')) with
            // ReferenceEquals(L, L') (interned canonical form), then checking
            // for ε or Star(L) sibling. The replacement (Star(body)) re-enters
            // hash-consing via Star() so we keep canonical reference identity.
            var plusEntries = new List<(Ere<TPred> entry, Ere<TPred> body)>();
            foreach (var op in ops)
            {
                if (op is EreConcat<TPred> pc
                    && pc.Right is EreStar<TPred> prs
                    && ReferenceEquals(pc.Left, prs.Inner))
                {
                    plusEntries.Add((op, pc.Left));
                }
            }
            if (plusEntries.Count > 0)
            {
                bool hasEps = false;
                foreach (var op in ops) { if (op is EreEpsilon<TPred>) { hasEps = true; break; } }
                foreach (var (entry, body) in plusEntries)
                {
                    var starOfBody = Star(body); // interned canonical
                    bool hasStar = ops.Contains(starOfBody);
                    if (hasEps || hasStar)
                    {
                        ops.Remove(entry);
                        if (!hasStar) ops.Add(starOfBody);
                        if (hasEps && !ReferenceEquals(starOfBody, Epsilon()))
                        {
                            // ε is now subsumed by Star(body) — drop it.
                            ops.RemoveWhere(o => o is EreEpsilon<TPred>);
                            hasEps = false; // only drop once
                        }
                    }
                }
            }


            // Σ*-prefix absorption:  Σ* · T  absorbs any other operand whose
            // syntactic right-suffix chain reaches  Σ* · T.  Soundness: any
            // word in  L(R · Σ* · T)  decomposes as  r · w · t  with
            // r ∈ L(R), w ∈ Σ*, t ∈ L(T); reassociate as  (r · w) · t  to
            // see it lies in  Σ* · L(T) = L(Σ* · T).  Hash-consing makes
            // the right-chain walk an O(depth) reference-equality check.
            // This is the rule that collapses regex-concat fairness
            // derivatives such as  Σ*·p₁·Σ*·…·Σ*·pₙ | Σ*·pₖ·…·Σ*·pₙ
            // (the union of "progress levels" generated by symbolic
            // differentiation) down to the single deepest progress level.
            var sigmaStarPrefixed = ops
                .Where(o => o is EreConcat<TPred> c && IsSigmaStar(c.Left))
                .ToList();
            if (sigmaStarPrefixed.Count > 0)
            {
                ops.RemoveWhere(small =>
                {
                    foreach (var big in sigmaStarPrefixed)
                    {
                        if (ReferenceEquals(small, big)) continue;
                        if (HasRightSuffix(small, big)) return true;
                    }
                    return false;
                });
            }

            // P2.3 Contains-pattern merge (Rust SU-5 at lib.rs:2081–2086):
            //   Σ*·R·Σ* + Σ*·S·Σ*  ≡  Σ*·(R+S)·Σ*
            // Right-associated parse: Concat(Σ*, Concat(body, Σ*)). Bodies
            // are merged under recursive Union (which may further simplify),
            // then re-wrapped in a single contains-pattern. Runs before
            // head-factoring so the tightest containing form wins.
            List<(Ere<TPred> entry, Ere<TPred> body)> containsBodies = null;
            foreach (var op in ops)
            {
                if (op is EreConcat<TPred> outer
                    && IsSigmaStar(outer.Left)
                    && outer.Right is EreConcat<TPred> inner
                    && IsSigmaStar(inner.Right))
                {
                    if (containsBodies == null)
                        containsBodies = new List<(Ere<TPred>, Ere<TPred>)>();
                    containsBodies.Add((op, inner.Left));
                }
            }
            if (containsBodies != null && containsBodies.Count >= 2)
            {
                Ere<TPred> mergedBody = Empty();
                foreach (var (_, body) in containsBodies)
                    mergedBody = Union(mergedBody, body);
                foreach (var (entry, _) in containsBodies) ops.Remove(entry);
                var sStar = Star(Sigma());
                ops.Add(Concat(sStar, Concat(mergedBody, sStar)));
            }

            // P3.3 predicate-star union (Rust SU-4, lib.rs:2057–2061):
            //   [p]*  |  [q]*   ≡   (p ⊔ q)*
            // Requires the predicate algebra plumbed via P1; silently
            // skipped when no algebra has been registered.
            if (DefaultBuilder.Algebra is IPredicateAlgebra<TPred> palg)
            {
                var predStars = ops
                    .OfType<EreStar<TPred>>()
                    .Where(s => s.Inner is EreAtom<TPred>)
                    .ToList();
                if (predStars.Count >= 2)
                {
                    TPred merged = ((EreAtom<TPred>)predStars[0].Inner).Predicate;
                    for (int i = 1; i < predStars.Count; i++)
                        merged = palg.Or(merged, ((EreAtom<TPred>)predStars[i].Inner).Predicate);
                    foreach (var s in predStars) ops.Remove(s);
                    ops.Add(Star(Atom(merged)));
                }
            }

            // P2.5b Σ*-tail structural subsumption (Rust lib.rs:2104-2114):
            //   Σ*·t1 + Σ*·t2  ≡  Σ*·t1   when t2 = …·t1 structurally.
            // If t2 has t1 as a structural concat-suffix then any word in
            // Σ*·t2 also ends in t1, so Σ*·t2 ⊆ Σ*·t1; drop the longer-tailed
            // operand. Runs before head/tail factoring so that the simpler
            // surviving shape participates in factoring.
            {
                var sigmaStarOps = ops
                    .OfType<EreConcat<TPred>>()
                    .Where(c => IsSigmaStar(c.Left))
                    .ToList();
                var toDrop = new List<EreConcat<TPred>>();
                for (int i = 0; i < sigmaStarOps.Count; i++)
                {
                    var ci = sigmaStarOps[i];
                    if (toDrop.Contains(ci)) continue;
                    for (int j = 0; j < sigmaStarOps.Count; j++)
                    {
                        if (i == j) continue;
                        var cj = sigmaStarOps[j];
                        if (toDrop.Contains(cj)) continue;
                        // ci.Right is suffix of cj.Right ⇒ drop cj.
                        if (!ReferenceEquals(ci.Right, cj.Right)
                            && HasConcatTail(cj.Right, ci.Right))
                            toDrop.Add(cj);
                    }
                }
                foreach (var d in toDrop) ops.Remove(d);
            }

            // P2.1 Head/tail factoring (Rust SU-7 at lib.rs:2118–2121):
            //   H·T₁ + H·T₂ + … + H·Tₙ  ≡  H·(T₁+T₂+…+Tₙ)
            // when n ≥ 2 EreConcat operands share the same Left child by
            // hash-cons reference. Particularly valuable on derivative
            // classes generated by the same head predicate. Concat's own
            // left-distribution would re-distribute if H were itself a
            // Union, but Concat factory eagerly distributes left-Union
            // away, so H is never an EreUnion here.
            Dictionary<Ere<TPred>, List<EreConcat<TPred>>> byHead = null;
            foreach (var op in ops)
            {
                if (op is EreConcat<TPred> cc)
                {
                    if (byHead == null)
                        byHead = new Dictionary<Ere<TPred>, List<EreConcat<TPred>>>();
                    if (!byHead.TryGetValue(cc.Left, out var list))
                        byHead[cc.Left] = list = new List<EreConcat<TPred>>();
                    list.Add(cc);
                }
            }
            if (byHead != null)
            {
                foreach (var kv in byHead)
                {
                    if (kv.Value.Count < 2) continue;
                    var head = kv.Key;
                    Ere<TPred> mergedTail = Empty();
                    foreach (var c in kv.Value)
                        mergedTail = Union(mergedTail, c.Right);
                    foreach (var c in kv.Value) ops.Remove(c);
                    ops.Add(Concat(head, mergedTail));
                }
            }

            // P2.5a tail-factoring (R₁·T + R₂·T → (R₁+R₂)·T), the dual of
            // P2.1 head-factoring, is intentionally NOT applied: the Concat
            // factory eagerly distributes left-Union (Concat(Union, T) ⇒
            // Union(Concat·,Concat·)), which would immediately undo any
            // tail-factoring and produce infinite Union↔Concat recursion.
            // The Σ*-tail subsumption above (P2.5b) captures the most
            // impactful subset of the same idea without that conflict.

            if (ops.Count == 1) return ops.First();
            return DefaultBuilder.Intern(new EreUnion<TPred>(ops.ToArray()));
        }

        /// <summary>
        /// True iff <paramref name="suffix"/> is a structural right-spine
        /// suffix of <paramref name="whole"/> in right-associated concat
        /// form. Used by the Σ*-tail subsumption rewrite (P2.5b) to detect
        /// when one ‘ends-with’ pattern subsumes another. O(spine length)
        /// thanks to hash-consing reference identity on subterms.
        /// </summary>
        private static bool HasConcatTail(Ere<TPred> whole, Ere<TPred> suffix)
        {
            var cur = whole;
            while (true)
            {
                if (ReferenceEquals(cur, suffix)) return true;
                if (cur is EreConcat<TPred> c) { cur = c.Right; continue; }
                return false;
            }
        }

        public static Ere<TPred> Intersect(Ere<TPred> a, Ere<TPred> b)
        {
            if (a is EreEmpty<TPred> || b is EreEmpty<TPred>) return Empty();
            if (IsSigma(a)) return b;
            if (IsSigma(b)) return a;

            // R ∩ ε  =  ε if R nullable, ∅ otherwise.
            if (a is EreEpsilon<TPred>) return b.Nullable ? Epsilon() : Empty();
            if (b is EreEpsilon<TPred>) return a.Nullable ? Epsilon() : Empty();

            var ops = new SortedSet<Ere<TPred>>(EreComparer<TPred>.Instance);
            CollectIntersect(a, ops);
            CollectIntersect(b, ops);

            // Complementary-language elimination: R ∩ ~R ≡ ∅.
            foreach (var op in ops)
            {
                if (op is EreComplement<TPred> c && ops.Contains(c.Inner)) return Empty();
            }

            // R ∩ R* ≡ R: when both appear, the star is redundant (R ⊆ R*).
            // Dual of the union star absorption.
            var starsToRemove = new List<EreStar<TPred>>();
            foreach (var s in ops.OfType<EreStar<TPred>>())
            {
                if (ops.Contains(s.Inner)) starsToRemove.Add(s);
            }
            foreach (var s in starsToRemove) ops.Remove(s);

            // P2.2: length-bound disjointness check. If the operands' length
            // intervals do not overlap then no word can lie in the intersection.
            // Mirrors Rust EREQ get_min_max_len + Intersect-unsat pruning
            // (lib.rs:999–1038).
            {
                int lo = 0, hi = int.MaxValue;
                foreach (var op in ops)
                {
                    if (op.MinLen > lo) lo = op.MinLen;
                    if (op.MaxLen < hi) hi = op.MaxLen;
                }
                if (lo > hi) return Empty();
            }

            // P3.2 predicate-star × concat distribution (Rust I-P4, lib.rs:2604–2612):
            //   [p]* ∩ R·S  ≡  ([p]* ∩ R) · ([p]* ∩ S)
            // Sound because a word w = u·v lies in L([p]*) iff every
            // character of u and v satisfies p iff u, v ∈ L([p]*). The
            // distributed form lets later derivative passes prune each
            // factor independently. The predicate-star operand is left
            // in place — it is redundant w.r.t. the new concat but may
            // still be needed for any further intersection operands.
            {
                EreStar<TPred> predStar = null;
                foreach (var op in ops)
                {
                    if (op is EreStar<TPred> s && s.Inner is EreAtom<TPred>)
                    {
                        predStar = s;
                        break;
                    }
                }
                if (predStar != null)
                {
                    var toDistribute = ops.OfType<EreConcat<TPred>>().ToList();
                    if (toDistribute.Count > 0)
                    {
                        foreach (var cc in toDistribute)
                        {
                            ops.Remove(cc);
                            var left  = Intersect(predStar, cc.Left);
                            var right = Intersect(predStar, cc.Right);
                            var distributed = Concat(left, right);
                            if (distributed is EreEmpty<TPred>) return Empty();
                            CollectIntersect(distributed, ops);
                        }
                        // Each distributed factor already conjoins with
                        // predStar, so the outer copy is redundant.
                        ops.Remove(predStar);
                    }
                }
            }

            if (ops.Count == 1) return ops.First();
            return DefaultBuilder.Intern(new EreIntersect<TPred>(ops.ToArray()));
        }

        public static Ere<TPred> Complement(Ere<TPred> a)
        {
            if (a is EreComplement<TPred> c) return c.Inner; // ~~R = R
            // ~(R ⊕ S) absorbed by flipping the XOR node's Negated flag.
            if (a is EreXor<TPred> x)
                return DefaultBuilder.Intern(
                    new EreXor<TPred>(x.Operands.ToArray(), !x.Negated));
            if (a is EreEmpty<TPred>)
                return DefaultBuilder.Intern(new EreComplement<TPred>(a)); // ~∅ = Σ*
            // De Morgan: push complement inward over union/intersect so that
            // complements appear only on Empty / Epsilon / Atom / Star / Concat
            // / Fusion (the "atomic" forms wrt boolean connectives). This is the
            // ERE analogue of LTL NNF: it canonicalises complement nodes and
            // exposes the underlying operands to further rewrites in the
            // resulting Union/Intersect.
            if (a is EreUnion<TPred> u)
            {
                // ~(R + S + …) = ~R ∩ ~S ∩ …
                Ere<TPred> acc = Sigma();
                foreach (var op in u.Operands) acc = Intersect(acc, Complement(op));
                return acc;
            }
            if (a is EreIntersect<TPred> i)
            {
                // ~(R ∩ S ∩ …) = ~R + ~S + …
                Ere<TPred> acc = Empty();
                foreach (var op in i.Operands) acc = Union(acc, Complement(op));
                return acc;
            }
            // P2.4: complement push-through on canonical predicate shapes.
            // Mirrors Rust EREQ rules COMPL-12 and COMPL-13 and requires a
            // predicate algebra to negate atom predicates; silently skipped
            // when no algebra has been registered with the default builder.
            if (DefaultBuilder.Algebra is IPredicateAlgebra<TPred> alg
                && a is EreConcat<TPred> cn)
            {
                // ~([p] · Σ*)  =  ε  |  [¬p] · Σ*
                if (cn.Left is EreAtom<TPred> at1 && IsSigmaStar(cn.Right))
                {
                    var negAt = Atom(alg.Not(at1.Predicate));
                    return Union(Epsilon(), Concat(negAt, cn.Right));
                }
                // ~(Σ* · [p] · Σ*)  =  [¬p]*
                if (IsSigmaStar(cn.Left)
                    && cn.Right is EreConcat<TPred> inner
                    && inner.Left is EreAtom<TPred> at2
                    && IsSigmaStar(inner.Right))
                {
                    var negAt = Atom(alg.Not(at2.Predicate));
                    return Star(negAt);
                }
            }
            return DefaultBuilder.Intern(new EreComplement<TPred>(a));
        }

        public static Ere<TPred> Star(Ere<TPred> a)
        {
            if (a is EreEmpty<TPred>) return Epsilon();    // ∅* = ε
            if (a is EreEpsilon<TPred>) return Epsilon();  // ε* = ε
            if (a is EreStar<TPred>) return a;             // (R*)* = R*
            // (R + ε)* ≡ R*: ε contributes nothing under star.
            if (a is EreUnion<TPred> u && u.Operands.Any(o => o is EreEpsilon<TPred>))
            {
                Ere<TPred> rest = Empty();
                foreach (var op in u.Operands)
                    if (!(op is EreEpsilon<TPred>)) rest = Union(rest, op);
                return Star(rest);
            }
            return DefaultBuilder.Intern(new EreStar<TPred>(a));
        }

        /// <summary>
        /// Fusion <c>R : S</c> (Section 7.3 of the JACM extension).
        /// <para>
        /// <c>L(R : S) = { v ∈ Σ∞ | ∃ i &lt; |v| : v[..i] ∈ L(R) ∧ v[i..] ∈ L(S) }</c>
        /// </para>
        /// where <c>v[..i]</c> includes position <c>i</c> (length <c>i+1</c>, hence
        /// nonempty) and <c>v[i..]</c> starts at position <c>i</c>: the last letter of
        /// the regex match coincides with the first letter of the suffix match.
        /// </summary>
        public static Ere<TPred> Fusion(Ere<TPred> a, Ere<TPred> b)
        {
            // Fusion requires both sides to contribute at least one letter at the
            // shared position; ∅ or ε on either side yields ∅.
            if (a is EreEmpty<TPred>   || b is EreEmpty<TPred>)   return Empty();
            if (a is EreEpsilon<TPred> || b is EreEpsilon<TPred>) return Empty();
            // Distribute Fusion over Union on the LEFT only (right-propagating):
            //   (R₁ + R₂) : S = (R₁:S) + (R₂:S)
            // Same DNF-exposure rationale as the Concat-over-Union rule above;
            // right-side Union is not distributed to avoid quadratic blow-up.
            if (a is EreUnion<TPred> ua)
            {
                Ere<TPred> acc = Empty();
                foreach (var op in ua.Operands) acc = Union(acc, Fusion(op, b));
                return acc;
            }
            return DefaultBuilder.Intern(new EreFusion<TPred>(a, b));
        }

        /// <summary>R⁺ = R · R*</summary>
        public static Ere<TPred> Plus(Ere<TPred> a) => Concat(a, Star(a));

        /// <summary>R? = R + ε</summary>
        public static Ere<TPred> Optional(Ere<TPred> a) => Union(a, Epsilon());

        /// <summary>
        /// Symmetric difference (XOR) <c>R ⊕ S</c> and its negation
        /// (XNOR) <c>R ⊙ S</c>, with
        /// <c>L(R ⊕ S) = L(R) △ L(S)</c>.
        ///
        /// <para>Canonicalisation (paper §6 "Implementation"):</para>
        /// <list type="bullet">
        ///   <item>Associative, commutative, self-inverse: nested XORs are
        ///   flattened, operands sorted, identical pairs cancel.</item>
        ///   <item><c>R ⊕ R   ≡ ⊥</c></item>
        ///   <item><c>R ⊕ ⊥   ≡ R</c></item>
        ///   <item><c>~R ⊕ ~S ≡ R ⊕ S</c></item>
        ///   <item><c>R ⊕ ~S  ≡ ~(R ⊕ S) = R ⊙ S</c>
        ///         (complement is absorbed into the XOR node as
        ///         <see cref="EreXor{TPred}.Negated"/> = <c>true</c>;
        ///         no <see cref="EreComplement{TPred}"/> wrapper is needed).</item>
        ///   <item><c>Σ* ⊕ R  ≡ ~R</c>  (falls out of the lift)</item>
        /// </list>
        ///
        /// <para>Nullable iff <c>Nullable(R) ≠ Nullable(S)</c>
        /// (XNOR inverts).</para>
        ///
        /// <para>Used as the primitive operator of the bisimulation-based
        /// equivalence algorithm: <c>Eq(p,q) ⇔ L(p ⊕ q) = ∅</c>
        /// (see <c>EreEquivalenceChecker</c>).</para>
        /// </summary>
        public static Ere<TPred> Xor(Ere<TPred> a, Ere<TPred> b)
        {
            // Collect operands modulo complement parity.
            var ops = new List<Ere<TPred>>();
            bool negated = false;
            CollectXor(a, ops, ref negated);
            CollectXor(b, ops, ref negated);

            // Sort by structural order and pair-cancel duplicates.
            ops.Sort(EreComparer<TPred>.Instance);
            var canon = new List<Ere<TPred>>(ops.Count);
            for (int i = 0; i < ops.Count;)
            {
                if (i + 1 < ops.Count && ops[i].Equals(ops[i + 1]))
                {
                    // r ⊕ r ≡ ⊥: drop the pair (no parity change).
                    i += 2;
                }
                else
                {
                    canon.Add(ops[i]);
                    i++;
                }
            }

            if (canon.Count == 0)
            {
                // 0 operands → identity ⊥; XNOR form gives ~⊥ = Σ*.
                return negated ? Sigma() : Empty();
            }
            if (canon.Count == 1)
            {
                // Single operand → r or ~r (resolved via Complement factory).
                return negated ? Complement(canon[0]) : canon[0];
            }
            return DefaultBuilder.Intern(new EreXor<TPred>(canon.ToArray(), negated));
        }

        /// <summary>XNOR: <c>R ⊙ S ≡ ~(R ⊕ S)</c>.</summary>
        public static Ere<TPred> Xnor(Ere<TPred> a, Ere<TPred> b) => Complement(Xor(a, b));

        private static void CollectXor(Ere<TPred> e, List<Ere<TPred>> ops, ref bool negated)
        {
            // Strip ⊥ (identity for ⊕).
            if (e is EreEmpty<TPred>) return;

            // Lift complement out: ~r contributes r and flips the outer
            // parity. Handles Σ* (= ~∅) for free (it contributes nothing
            // but toggles parity).
            if (e is EreComplement<TPred> c)
            {
                negated = !negated;
                CollectXor(c.Inner, ops, ref negated);
                return;
            }

            // Flatten nested XOR; the inner node's own Negated flag merges
            // into the running parity.
            if (e is EreXor<TPred> x)
            {
                if (x.Negated) negated = !negated;
                foreach (var op in x.Operands) CollectXor(op, ops, ref negated);
                return;
            }

            ops.Add(e);
        }

        private static bool IsSigma(Ere<TPred> e)
            => e is EreComplement<TPred> c && c.Inner is EreEmpty<TPred>;

        private static bool IsSigmaStar(Ere<TPred> e)
            => e is EreStar<TPred> s && IsSigma(s.Inner);

        // Walk the right-spine of a (right-associated) concat chain looking
        // for a syntactic match of <paramref name="tail"/>. Relies on
        // hash-cons reference equality of canonical terms.
        private static bool HasRightSuffix(Ere<TPred> e, Ere<TPred> tail)
        {
            while (true)
            {
                if (ReferenceEquals(e, tail)) return true;
                if (e is EreConcat<TPred> c) { e = c.Right; continue; }
                return false;
            }
        }

        private static void CollectUnion(Ere<TPred> e, SortedSet<Ere<TPred>> ops)
        {
            if (e is EreUnion<TPred> u)
                foreach (var op in u.Operands) ops.Add(op);
            else
                ops.Add(e);
        }

        private static void CollectIntersect(Ere<TPred> e, SortedSet<Ere<TPred>> ops)
        {
            if (e is EreIntersect<TPred> i)
                foreach (var op in i.Operands) ops.Add(op);
            else
                ops.Add(e);
        }

        #endregion

        #region Equality / Comparison

        public abstract bool Equals(Ere<TPred> other);
        public override bool Equals(object obj) => Equals(obj as Ere<TPred>);

        public override int GetHashCode()
        {
            if (_hash == null) _hash = ComputeHashCode();
            return _hash.Value;
        }

        protected abstract int ComputeHashCode();

        public int CompareTo(Ere<TPred> other)
        {
            if (other == null) return 1;
            if (ReferenceEquals(this, other)) return 0;
            int c = Kind.CompareTo(other.Kind);
            if (c != 0) return c;
            return CompareToSameKind(other);
        }

        protected abstract int CompareToSameKind(Ere<TPred> other);

        public static bool operator ==(Ere<TPred> a, Ere<TPred> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }
        public static bool operator !=(Ere<TPred> a, Ere<TPred> b) => !(a == b);

        #endregion
    }

    internal sealed class EreComparer<TPred> : IComparer<Ere<TPred>>
    {
        public static readonly EreComparer<TPred> Instance = new EreComparer<TPred>();
        public int Compare(Ere<TPred> x, Ere<TPred> y) => x.CompareTo(y);
    }

    public sealed class EreEmpty<TPred> : Ere<TPred>
    {
        public static readonly EreEmpty<TPred> Instance = new EreEmpty<TPred>();
        private EreEmpty() { }
        internal override int Kind => 0;
        public override bool Nullable => false;
        public override int Cost => 1;
        public override bool Equals(Ere<TPred> other) => other is EreEmpty<TPred>;
        protected override int ComputeHashCode() => 0x11111111;
        protected override int CompareToSameKind(Ere<TPred> other) => 0;
        public override string ToString() => "∅";
    }

    public sealed class EreEpsilon<TPred> : Ere<TPred>
    {
        public static readonly EreEpsilon<TPred> Instance = new EreEpsilon<TPred>();
        private EreEpsilon() { }
        internal override int Kind => 1;
        public override bool Nullable => true;
        public override int Cost => 1;
        public override int MinLen => 0;
        public override int MaxLen => 0;
        public override bool Equals(Ere<TPred> other) => other is EreEpsilon<TPred>;
        protected override int ComputeHashCode() => 0x22222222;
        protected override int CompareToSameKind(Ere<TPred> other) => 0;
        public override string ToString() => "ε";
    }

    public sealed class EreAtom<TPred> : Ere<TPred>
    {
        public EreAtom(TPred predicate) { Predicate = predicate; }
        public TPred Predicate { get; }
        internal override int Kind => 2;
        public override bool Nullable => false;
        public override int Cost => 1;
        public override int MinLen => 1;
        public override int MaxLen => 1;
        public override bool Equals(Ere<TPred> other)
            => other is EreAtom<TPred> a
               && EqualityComparer<TPred>.Default.Equals(Predicate, a.Predicate);
        protected override int ComputeHashCode()
            => unchecked(EqualityComparer<TPred>.Default.GetHashCode(Predicate) * (int)0x9E3779B1);
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var a = (EreAtom<TPred>)other;
            return PredCompare<TPred>.Compare(Predicate, a.Predicate);
        }
        public override string ToString() => Predicate.ToString();
    }

    public sealed class EreConcat<TPred> : Ere<TPred>
    {
        public EreConcat(Ere<TPred> left, Ere<TPred> right)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
            _containsCompl = left.ContainsCompl || right.ContainsCompl;
            _containsInter = left.ContainsInter || right.ContainsInter;
            _containsExists = left.ContainsExists || right.ContainsExists;
            _cost = left.Cost + right.Cost + 1;
        }
        private readonly bool _containsCompl;
        private readonly bool _containsInter;
        private readonly bool _containsExists;
        private readonly int _cost;
        public override bool ContainsCompl => _containsCompl;
        public override bool ContainsInter => _containsInter;
        public override bool ContainsExists => _containsExists;
        public override int Cost => _cost;
        public Ere<TPred> Left { get; }
        public Ere<TPred> Right { get; }
        internal override int Kind => 3;
        public override bool Nullable => Left.Nullable && Right.Nullable;
        public override int MinLen => SatAdd(Left.MinLen, Right.MinLen);
        public override int MaxLen => SatAdd(Left.MaxLen, Right.MaxLen);
        public override ulong FreeProps => Left.FreeProps | Right.FreeProps;
        public override bool Equals(Ere<TPred> other)
            => other is EreConcat<TPred> c && Left.Equals(c.Left) && Right.Equals(c.Right);
        protected override int ComputeHashCode()
            => unchecked(Left.GetHashCode() * 31 + Right.GetHashCode() + 3);
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var c = (EreConcat<TPred>)other;
            int r = Left.CompareTo(c.Left);
            return r != 0 ? r : Right.CompareTo(c.Right);
        }
        public override string ToString() => $"({Left}·{Right})";
    }

    public sealed class EreUnion<TPred> : Ere<TPred>
    {
        internal EreUnion(Ere<TPred>[] operands)
        {
            Operands = operands;
            bool cc = false, ci = false, ce = false;
            int cost = 1;
            foreach (var op in operands)
            {
                cc |= op.ContainsCompl;
                ci |= op.ContainsInter;
                ce |= op.ContainsExists;
                cost += op.Cost;
            }
            _containsCompl = cc;
            _containsInter = ci;
            _containsExists = ce;
            _cost = cost;
        }
        private readonly bool _containsCompl;
        private readonly bool _containsInter;
        private readonly bool _containsExists;
        private readonly int _cost;
        public override bool ContainsCompl => _containsCompl;
        public override bool ContainsInter => _containsInter;
        public override bool ContainsExists => _containsExists;
        public override int Cost => _cost;
        public IReadOnlyList<Ere<TPred>> Operands { get; }
        internal override int Kind => 4;
        public override bool Nullable => Operands.Any(o => o.Nullable);
        public override int MinLen
        {
            get
            {
                int m = int.MaxValue;
                foreach (var o in Operands) if (o.MinLen < m) m = o.MinLen;
                return m;
            }
        }
        public override int MaxLen
        {
            get
            {
                int m = 0;
                foreach (var o in Operands) if (o.MaxLen > m) m = o.MaxLen;
                return m;
            }
        }
        public override ulong FreeProps
        {
            get { ulong b = 0UL; foreach (var o in Operands) b |= o.FreeProps; return b; }
        }
        public override bool Equals(Ere<TPred> other)
        {
            if (!(other is EreUnion<TPred> u)) return false;
            if (Operands.Count != u.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
                if (!Operands[i].Equals(u.Operands[i])) return false;
            return true;
        }
        protected override int ComputeHashCode()
        {
            unchecked
            {
                int h = 4;
                foreach (var op in Operands) h = h * 31 + op.GetHashCode();
                return h;
            }
        }
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var u = (EreUnion<TPred>)other;
            int c = Operands.Count.CompareTo(u.Operands.Count);
            if (c != 0) return c;
            for (int i = 0; i < Operands.Count; i++)
            {
                c = Operands[i].CompareTo(u.Operands[i]);
                if (c != 0) return c;
            }
            return 0;
        }
        public override string ToString() => "(" + string.Join("+", Operands) + ")";
    }

    public sealed class EreIntersect<TPred> : Ere<TPred>
    {
        internal EreIntersect(Ere<TPred>[] operands)
        {
            Operands = operands;
            bool cc = false, ce = false;
            int cost = 1;
            foreach (var op in operands)
            {
                cc |= op.ContainsCompl;
                ce |= op.ContainsExists;
                cost += op.Cost;
            }
            _containsCompl = cc;
            _containsExists = ce;
            _cost = cost;
        }
        private readonly bool _containsCompl;
        private readonly bool _containsExists;
        private readonly int _cost;
        public override bool ContainsCompl => _containsCompl;
        public override bool ContainsInter => true;
        public override bool ContainsExists => _containsExists;
        public override int Cost => _cost;
        public IReadOnlyList<Ere<TPred>> Operands { get; }
        internal override int Kind => 5;
        public override bool Nullable => Operands.All(o => o.Nullable);
        public override int MinLen
        {
            get
            {
                int m = 0;
                foreach (var o in Operands) if (o.MinLen > m) m = o.MinLen;
                return m;
            }
        }
        public override int MaxLen
        {
            get
            {
                int m = int.MaxValue;
                foreach (var o in Operands) if (o.MaxLen < m) m = o.MaxLen;
                return m;
            }
        }
        public override ulong FreeProps
        {
            get { ulong b = 0UL; foreach (var o in Operands) b |= o.FreeProps; return b; }
        }
        public override bool Equals(Ere<TPred> other)
        {
            if (!(other is EreIntersect<TPred> i)) return false;
            if (Operands.Count != i.Operands.Count) return false;
            for (int k = 0; k < Operands.Count; k++)
                if (!Operands[k].Equals(i.Operands[k])) return false;
            return true;
        }
        protected override int ComputeHashCode()
        {
            unchecked
            {
                int h = 5;
                foreach (var op in Operands) h = h * 31 + op.GetHashCode();
                return h;
            }
        }
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var i = (EreIntersect<TPred>)other;
            int c = Operands.Count.CompareTo(i.Operands.Count);
            if (c != 0) return c;
            for (int k = 0; k < Operands.Count; k++)
            {
                c = Operands[k].CompareTo(i.Operands[k]);
                if (c != 0) return c;
            }
            return 0;
        }
        public override string ToString() => "(" + string.Join("&", Operands) + ")";
    }

    public sealed class EreComplement<TPred> : Ere<TPred>
    {
        public EreComplement(Ere<TPred> inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }
        public Ere<TPred> Inner { get; }
        internal override int Kind => 6;
        public override bool Nullable => !Inner.Nullable;
        public override ulong FreeProps => Inner.FreeProps;
        public override bool ContainsCompl => true;
        public override bool ContainsInter => Inner.ContainsInter;
        public override bool ContainsExists => Inner.ContainsExists;
        public override int Cost => Inner.Cost + 1;
        // ~R: word lengths complement L(R). 0 ∈ L(~R) iff !Inner.Nullable.
        // Otherwise we have no tight upper bound; conservative bounds.
        public override int MinLen => Inner.Nullable ? 1 : 0;
        public override int MaxLen => int.MaxValue;
        public override bool Equals(Ere<TPred> other)
            => other is EreComplement<TPred> c && Inner.Equals(c.Inner);
        protected override int ComputeHashCode() => unchecked(Inner.GetHashCode() * 7 + 6);
        protected override int CompareToSameKind(Ere<TPred> other)
            => Inner.CompareTo(((EreComplement<TPred>)other).Inner);
        public override string ToString()
            => Inner is EreEmpty<TPred> ? "Σ*" : $"~{Inner}";
    }

    public sealed class EreStar<TPred> : Ere<TPred>
    {
        public EreStar(Ere<TPred> inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }
        public Ere<TPred> Inner { get; }
        internal override int Kind => 7;
        public override bool Nullable => true;
        public override ulong FreeProps => Inner.FreeProps;
        public override bool ContainsCompl => Inner.ContainsCompl;
        public override bool ContainsInter => Inner.ContainsInter;
        public override bool ContainsExists => Inner.ContainsExists;
        public override int Cost => Inner.Cost + 1;
        public override int MinLen => 0;
        public override int MaxLen => int.MaxValue;
        public override bool Equals(Ere<TPred> other)
            => other is EreStar<TPred> s && Inner.Equals(s.Inner);
        protected override int ComputeHashCode() => unchecked(Inner.GetHashCode() * 11 + 7);
        protected override int CompareToSameKind(Ere<TPred> other)
            => Inner.CompareTo(((EreStar<TPred>)other).Inner);
        public override string ToString() => $"({Inner})*";
    }

    /// <summary>
    /// Fusion <c>R : S</c> — the prefix and the suffix share their boundary letter.
    /// <c>L(R : S) = { v | ∃ i &lt; |v| : v[..i] ∈ L(R) ∧ v[i..] ∈ L(S) }</c>
    /// (Section 7.3 of the JACM extension; <c>nullable(R:S) = false</c>).
    /// </summary>
    public sealed class EreFusion<TPred> : Ere<TPred>
    {
        public EreFusion(Ere<TPred> left, Ere<TPred> right)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
            _containsCompl = left.ContainsCompl || right.ContainsCompl;
            _containsInter = left.ContainsInter || right.ContainsInter;
            _containsExists = left.ContainsExists || right.ContainsExists;
            _cost = left.Cost + right.Cost + 1;
        }
        private readonly bool _containsCompl;
        private readonly bool _containsInter;
        private readonly bool _containsExists;
        private readonly int _cost;
        public override bool ContainsCompl => _containsCompl;
        public override bool ContainsInter => _containsInter;
        public override bool ContainsExists => _containsExists;
        public override int Cost => _cost;
        public Ere<TPred> Left { get; }
        public Ere<TPred> Right { get; }
        internal override int Kind => 8;
        public override bool Nullable => false;
        // Fusion: prefix and suffix share their boundary letter, so the
        // length is len(L)+len(R)-1 with both ≥1.
        public override int MinLen => SatSub(SatAdd(Left.MinLen, Right.MinLen), 1);
        public override int MaxLen => SatSub(SatAdd(Left.MaxLen, Right.MaxLen), 1);
        public override ulong FreeProps => Left.FreeProps | Right.FreeProps;
        public override bool Equals(Ere<TPred> other)
            => other is EreFusion<TPred> f && Left.Equals(f.Left) && Right.Equals(f.Right);
        protected override int ComputeHashCode()
            => unchecked(Left.GetHashCode() * 37 + Right.GetHashCode() + 8);
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var f = (EreFusion<TPred>)other;
            int c = Left.CompareTo(f.Left);
            return c != 0 ? c : Right.CompareTo(f.Right);
        }
        public override string ToString() => $"({Left}:{Right})";
    }

    /// <summary>
    /// Symmetric difference (XOR / XNOR) — primitive ERE operator (CAV'26).
    /// <para>If <see cref="Negated"/> is <c>false</c>, denotes
    /// <c>R₁ ⊕ R₂ ⊕ … ⊕ Rₙ</c> (XOR);
    /// if <c>true</c>, denotes <c>~(R₁ ⊕ … ⊕ Rₙ)</c> (XNOR for n = 2).</para>
    /// <para><c>L(R₁ ⊕ … ⊕ Rₙ) = { v | parity of {i : v ∈ L(Rᵢ)} is odd }</c>.</para>
    /// <para>Operands are stored sorted by structural order and are
    /// guaranteed (via the <see cref="Ere{TPred}.Xor"/> factory) to
    /// satisfy: no duplicates, no inner complement, no nested XOR,
    /// count ≥ 2. The <see cref="Negated"/> flag absorbs any outer
    /// complement, so an <c>EreXor</c> is never wrapped in
    /// <see cref="EreComplement{TPred}"/>.</para>
    /// </summary>
    public sealed class EreXor<TPred> : Ere<TPred>
    {
        internal EreXor(Ere<TPred>[] operands, bool negated)
        {
            Operands = operands;
            Negated = negated;
            bool cc = false, ci = false, ce = false;
            int cost = 1;
            foreach (var op in operands)
            {
                cc |= op.ContainsCompl;
                ci |= op.ContainsInter;
                ce |= op.ContainsExists;
                cost += op.Cost;
            }
            _containsCompl = cc;
            _containsInter = ci;
            _containsExists = ce;
            _cost = cost;
        }
        private readonly bool _containsCompl;
        private readonly bool _containsInter;
        private readonly bool _containsExists;
        private readonly int _cost;
        public override bool ContainsCompl => _containsCompl;
        public override bool ContainsInter => _containsInter;
        public override bool ContainsExists => _containsExists;
        public override int Cost => _cost;
        public IReadOnlyList<Ere<TPred>> Operands { get; }

        /// <summary>True ⇔ this node denotes the XNOR (negated parity).</summary>
        public bool Negated { get; }

        internal override int Kind => 9;
        public override ulong FreeProps
        {
            get { ulong b = 0UL; foreach (var o in Operands) b |= o.FreeProps; return b; }
        }
        public override bool Nullable
        {
            get
            {
                bool n = Negated;
                foreach (var op in Operands) n ^= op.Nullable;
                return n;
            }
        }
        public override bool Equals(Ere<TPred> other)
        {
            if (!(other is EreXor<TPred> x)) return false;
            if (Negated != x.Negated) return false;
            if (Operands.Count != x.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
                if (!Operands[i].Equals(x.Operands[i])) return false;
            return true;
        }
        protected override int ComputeHashCode()
        {
            unchecked
            {
                int h = Negated ? 0x5A5A5A5A : 9;
                foreach (var op in Operands) h = h * 31 + op.GetHashCode();
                return h;
            }
        }
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var x = (EreXor<TPred>)other;
            // XOR < XNOR (Negated=false < Negated=true).
            int c = Negated.CompareTo(x.Negated);
            if (c != 0) return c;
            c = Operands.Count.CompareTo(x.Operands.Count);
            if (c != 0) return c;
            for (int i = 0; i < Operands.Count; i++)
            {
                c = Operands[i].CompareTo(x.Operands[i]);
                if (c != 0) return c;
            }
            return 0;
        }
        public override string ToString()
        {
            string sep = Negated ? "⊙" : "⊕";
            return "(" + string.Join(sep, Operands) + ")";
        }
    }

    /// <summary>
    /// Single-letter atom constrained by a proposition variable
    /// (EREQ Phase 2). Indexed by a strictly-negative
    /// <see cref="PropositionIndex"/> from the
    /// <see cref="ConditionRegistry{TPredicate}"/>. When
    /// <see cref="Polarity"/> is <c>false</c> the atom denotes ¬p.
    /// </summary>
    public sealed class EreProposition<TPred> : Ere<TPred>
    {
        public EreProposition(int propositionIndex, bool polarity)
        {
            if (propositionIndex >= 0)
                throw new ArgumentOutOfRangeException(nameof(propositionIndex),
                    "Proposition indices must be strictly negative.");
            PropositionIndex = propositionIndex;
            Polarity = polarity;
        }
        public int PropositionIndex { get; }
        public bool Polarity { get; }
        internal override int Kind => 10;
        public override bool Nullable => false;
        public override int Cost => 1;
        public override int MinLen => 1;
        public override int MaxLen => 1;
        public override ulong FreeProps => BitForProp(PropositionIndex);
        public override bool Equals(Ere<TPred> other)
            => other is EreProposition<TPred> p
               && p.PropositionIndex == PropositionIndex
               && p.Polarity == Polarity;
        protected override int ComputeHashCode()
            => unchecked((int)((long)PropositionIndex * 0x27d4eb2dL) ^ (Polarity ? 1 : 0));
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var p = (EreProposition<TPred>)other;
            int c = PropositionIndex.CompareTo(p.PropositionIndex);
            return c != 0 ? c : Polarity.CompareTo(p.Polarity);
        }
        public override string ToString()
            => (Polarity ? "p" : "¬p") + (-PropositionIndex - 1);
    }

    /// <summary>
    /// Existential projection over a proposition (EREQ Phase 2):
    /// <c>∃p. R</c>. <see cref="PropositionIndex"/> is the bound
    /// proposition's negative registry index;
    /// <see cref="Body"/> is the regex under projection.
    /// </summary>
    public sealed class EreExists<TPred> : Ere<TPred>
    {
        public EreExists(int propositionIndex, Ere<TPred> body)
        {
            if (propositionIndex >= 0)
                throw new ArgumentOutOfRangeException(nameof(propositionIndex),
                    "Proposition indices must be strictly negative.");
            Body = body ?? throw new ArgumentNullException(nameof(body));
            PropositionIndex = propositionIndex;
        }
        public int PropositionIndex { get; }
        public Ere<TPred> Body { get; }
        internal override int Kind => 11;
        // ∃p.R contains ε iff R does — projection does not affect the
        // empty-word membership.
        public override bool Nullable => Body.Nullable;
        public override bool ContainsCompl => Body.ContainsCompl;
        public override bool ContainsInter => Body.ContainsInter;
        public override bool ContainsExists => true;
        public override int Cost => Body.Cost + 1;
        public override int MinLen => Body.MinLen;
        public override int MaxLen => Body.MaxLen;
        public override ulong FreeProps
            => Body.FreeProps & ~BitForProp(PropositionIndex);
        public override bool Equals(Ere<TPred> other)
            => other is EreExists<TPred> e
               && e.PropositionIndex == PropositionIndex
               && Body.Equals(e.Body);
        protected override int ComputeHashCode()
            => unchecked((int)((long)PropositionIndex * 0x9E3779B1L) ^ (Body.GetHashCode() * 11));
        protected override int CompareToSameKind(Ere<TPred> other)
        {
            var e = (EreExists<TPred>)other;
            int c = PropositionIndex.CompareTo(e.PropositionIndex);
            return c != 0 ? c : Body.CompareTo(e.Body);
        }
        public override string ToString()
            => $"(∃p{-PropositionIndex - 1}.{Body})";
    }

    /// <summary>
    /// Leaf Boolean algebra over <see cref="Ere{TPred}"/>: ∨ = union, ∧ = intersection,
    /// ¬ = complement, ⊥ = ∅, ⊤ = Σ*. Used as the leaf algebra for ERE
    /// transition terms (TTerm⟨A, Ere⟩).
    /// </summary>
    public sealed class EreLeafAlgebra<TPred> : ILeafAlgebra<Ere<TPred>>
    {
        public Ere<TPred> Top => Ere<TPred>.Sigma();
        public Ere<TPred> Bottom => Ere<TPred>.Empty();
        public bool IsTop(Ere<TPred> a)
            => a is EreComplement<TPred> c && c.Inner is EreEmpty<TPred>;
        public bool IsBottom(Ere<TPred> a) => a is EreEmpty<TPred>;
        public Ere<TPred> Or(Ere<TPred> a, Ere<TPred> b) => Ere<TPred>.Union(a, b);
        public Ere<TPred> And(Ere<TPred> a, Ere<TPred> b) => Ere<TPred>.Intersect(a, b);
        public Ere<TPred> Not(Ere<TPred> a) => Ere<TPred>.Complement(a);
        public Ere<TPred> Xor(Ere<TPred> a, Ere<TPred> b) => Ere<TPred>.Xor(a, b);
        public IEqualityComparer<Ere<TPred>> Comparer => EqualityComparer<Ere<TPred>>.Default;
    }
}
