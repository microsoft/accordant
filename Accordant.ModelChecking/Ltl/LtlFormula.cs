using Microsoft.Accordant;

namespace Microsoft.Accordant.ModelChecking.Ltl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Helper for hash code computation (netstandard2.0 compatible).
    /// </summary>
    internal static class HashHelper
    {
        public static int Combine(object a, object b)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (a?.GetHashCode() ?? 0);
                hash = hash * 31 + (b?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public static int Combine(object a, object b, object c)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (a?.GetHashCode() ?? 0);
                hash = hash * 31 + (b?.GetHashCode() ?? 0);
                hash = hash * 31 + (c?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// Base class for LTL (Linear Temporal Logic) formulas.
    /// Supports full LTL: propositions, boolean operators, and temporal operators
    /// (Next, Until, Release, and derived operators Eventually, Always).
    /// 
    /// Uses derivative-based semantics for efficient on-the-fly model checking.
    /// </summary>
    public abstract class LtlFormula : IEquatable<LtlFormula>
    {
        // Cached hash code for efficient dictionary/set operations
        private int? _cachedHashCode;

        /// <summary>
        /// Compute the derivative of this formula with respect to a state.
        /// The derivative δ(φ, s) represents "what remains to be satisfied after seeing state s".
        /// </summary>
        public abstract LtlFormula Derivative(IState state);

        /// <summary>
        /// Returns all Until subformulas in this formula (for acceptance condition checking).
        /// </summary>
        public abstract IEnumerable<LtlUntil> GetUntilSubformulas();

        /// <summary>
        /// Check if this formula is syntactically equivalent to True.
        /// </summary>
        public virtual bool IsTrue => false;

        /// <summary>
        /// Check if this formula is syntactically equivalent to False.
        /// </summary>
        public virtual bool IsFalse => false;

        public abstract bool Equals(LtlFormula other);
        public abstract override int GetHashCode();

        public override bool Equals(object obj) => Equals(obj as LtlFormula);

        protected int GetCachedHashCode(Func<int> compute)
        {
            _cachedHashCode ??= compute();
            return _cachedHashCode.Value;
        }

        /// <summary>
        /// Structural kind ordinal used by <see cref="CompareStructural"/> to give
        /// formulas of different syntactic categories a deterministic relative
        /// order. Values are stable within a process run.
        /// </summary>
        internal abstract int Kind { get; }

        /// <summary>
        /// Deterministic total order over <see cref="LtlFormula"/> values.
        /// Used by <see cref="LtlAnd"/> and <see cref="LtlOr"/> to canonicalize
        /// operand order (commutativity) so that ACI-equivalent conjunctions /
        /// disjunctions hash-cons to the same node. Compares by <see cref="Kind"/>
        /// first, then recursively on subformulas. For <see cref="LtlProp"/>
        /// compares by <c>Name</c> (nulls last) then by
        /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(object)"/>
        /// on the predicate delegate to break ties; the latter is stable within
        /// a process run but not across runs.
        /// </summary>
        internal static int CompareStructural(LtlFormula a, LtlFormula b)
        {
            if (ReferenceEquals(a, b)) return 0;
            int kc = a.Kind.CompareTo(b.Kind);
            if (kc != 0) return kc;
            switch (a)
            {
                case LtlTrue _:
                case LtlFalse _:
                    return 0;
                case LtlProp pa:
                {
                    var pb = (LtlProp)b;
                    int nc = string.CompareOrdinal(pa.Name, pb.Name);
                    if (nc != 0) return nc;
                    return System.Runtime.CompilerServices.RuntimeHelpers
                        .GetHashCode(pa.Predicate)
                        .CompareTo(System.Runtime.CompilerServices.RuntimeHelpers
                            .GetHashCode(pb.Predicate));
                }
                case LtlNot na:
                    return CompareStructural(na.Inner, ((LtlNot)b).Inner);
                case LtlNext nx:
                    return CompareStructural(nx.Inner, ((LtlNext)b).Inner);
                case LtlUntil u:
                {
                    var ub = (LtlUntil)b;
                    int c = CompareStructural(u.Hold, ub.Hold);
                    return c != 0 ? c : CompareStructural(u.Goal, ub.Goal);
                }
                case LtlRelease r:
                {
                    var rb = (LtlRelease)b;
                    int c = CompareStructural(r.Release_, rb.Release_);
                    return c != 0 ? c : CompareStructural(r.Hold, rb.Hold);
                }
                case LtlAnd an:
                {
                    var bn = (LtlAnd)b;
                    int lc = an.Children.Count.CompareTo(bn.Children.Count);
                    if (lc != 0) return lc;
                    for (int i = 0; i < an.Children.Count; i++)
                    {
                        int cc = CompareStructural(an.Children[i], bn.Children[i]);
                        if (cc != 0) return cc;
                    }
                    return 0;
                }
                case LtlOr ao:
                {
                    var bo = (LtlOr)b;
                    int lc = ao.Children.Count.CompareTo(bo.Children.Count);
                    if (lc != 0) return lc;
                    for (int i = 0; i < ao.Children.Count; i++)
                    {
                        int cc = CompareStructural(ao.Children[i], bo.Children[i]);
                        if (cc != 0) return cc;
                    }
                    return 0;
                }
                default:
                    throw new InvalidOperationException(
                        $"Unhandled LtlFormula kind: {a.GetType().Name}");
            }
        }

        internal sealed class StructuralComparer : IComparer<LtlFormula>
        {
            public static readonly StructuralComparer Instance = new StructuralComparer();
            public int Compare(LtlFormula x, LtlFormula y) => CompareStructural(x, y);
        }

        /// <summary>
        /// Reference-identity equality comparer for <see cref="LtlProp.Predicate"/>
        /// delegates. Matches <see cref="LtlProp.Equals(LtlFormula)"/>, which
        /// also uses <see cref="object.ReferenceEquals"/>.
        /// </summary>
        internal sealed class PredicateRefComparer : IEqualityComparer<Func<IState, bool>>
        {
            public static readonly PredicateRefComparer Instance = new PredicateRefComparer();
            public bool Equals(Func<IState, bool> x, Func<IState, bool> y) => ReferenceEquals(x, y);
            public int GetHashCode(Func<IState, bool> obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // ============ Static Factory Methods ============

        /// <summary>True constant.</summary>
        public static LtlFormula True { get; } = LtlTrue.Instance;

        /// <summary>False constant.</summary>
        public static LtlFormula False { get; } = LtlFalse.Instance;

        /// <summary>Creates a proposition from a state predicate.</summary>
        public static LtlFormula Prop(Func<IState, bool> predicate, string name = null)
            => new LtlProp(predicate, name);

        /// <summary>Negation: ¬φ</summary>
        public static LtlFormula Not(LtlFormula inner) => LtlNot.Create(inner);

        /// <summary>Conjunction: φ ∧ ψ</summary>
        public static LtlFormula And(LtlFormula left, LtlFormula right) => LtlAnd.Create(left, right);

        /// <summary>Conjunction of multiple formulas.</summary>
        public static LtlFormula And(params LtlFormula[] formulas)
            => formulas.Aggregate(True, (acc, f) => And(acc, f));

        /// <summary>Disjunction: φ ∨ ψ</summary>
        public static LtlFormula Or(LtlFormula left, LtlFormula right) => LtlOr.Create(left, right);

        /// <summary>Disjunction of multiple formulas.</summary>
        public static LtlFormula Or(params LtlFormula[] formulas)
            => formulas.Aggregate(False, (acc, f) => Or(acc, f));

        /// <summary>Implication: φ → ψ (equivalent to ¬φ ∨ ψ)</summary>
        public static LtlFormula Implies(LtlFormula antecedent, LtlFormula consequent)
            => Or(Not(antecedent), consequent);

        /// <summary>Next: Xφ — φ holds in the next state.
        /// On infinite traces (the model's convention; terminal states get a
        /// stutter self-loop) <c>X ⊤ ≡ ⊤</c> and <c>X ⊥ ≡ ⊥</c>, so the
        /// constants are short-circuited at construction.</summary>
        public static LtlFormula Next(LtlFormula inner)
        {
            if (inner.IsTrue) return True;
            if (inner.IsFalse) return False;
            return new LtlNext(inner);
        }

        /// <summary>Until: φ U ψ — φ holds until ψ becomes true (ψ must eventually hold).</summary>
        public static LtlFormula Until(LtlFormula hold, LtlFormula goal) => LtlUntil.Create(hold, goal);

        /// <summary>Release: φ R ψ — ψ holds until and including when φ becomes true (or forever if φ never holds).</summary>
        public static LtlFormula Release(LtlFormula release, LtlFormula hold) => LtlRelease.Create(release, hold);

        /// <summary>Eventually: ◇φ (F φ) — φ holds at some future state. Equivalent to true U φ.</summary>
        public static LtlFormula Eventually(LtlFormula inner) => Until(True, inner);

        /// <summary>Always: □φ (G φ) — φ holds in all future states. Equivalent to false R φ.</summary>
        public static LtlFormula Always(LtlFormula inner) => Release(False, inner);

        /// <summary>Infinitely Often: □◇φ (GF φ) — φ holds infinitely often.</summary>
        public static LtlFormula InfinitelyOften(LtlFormula inner) => Always(Eventually(inner));

        /// <summary>Stabilizes: ◇□φ (FG φ) — φ eventually becomes true and stays true forever.</summary>
        public static LtlFormula Stabilizes(LtlFormula inner) => Eventually(Always(inner));

        /// <summary>Leads-To: φ ~&gt; ψ — whenever φ holds, ψ eventually holds. Equivalent to □(φ → ◇ψ).</summary>
        public static LtlFormula LeadsTo(LtlFormula trigger, LtlFormula response)
            => Always(Implies(trigger, Eventually(response)));

        // ============ Operator Overloads for Fluent API ============

        public static LtlFormula operator &(LtlFormula left, LtlFormula right) => And(left, right);
        public static LtlFormula operator |(LtlFormula left, LtlFormula right) => Or(left, right);
        public static LtlFormula operator !(LtlFormula inner) => Not(inner);
    }

    // ============ Concrete Formula Types ============

    /// <summary>True constant — always satisfied.</summary>
    public sealed class LtlTrue : LtlFormula
    {
        public static LtlTrue Instance { get; } = new LtlTrue();
        private LtlTrue() { }

        public override bool IsTrue => true;
        internal override int Kind => 1;
        public override LtlFormula Derivative(IState state) => this;
        public override IEnumerable<LtlUntil> GetUntilSubformulas() => Enumerable.Empty<LtlUntil>();
        public override bool Equals(LtlFormula other) => other is LtlTrue;
        public override int GetHashCode() => 1;
        public override string ToString() => "true";
    }

    /// <summary>False constant — never satisfied.</summary>
    public sealed class LtlFalse : LtlFormula
    {
        public static LtlFalse Instance { get; } = new LtlFalse();
        private LtlFalse() { }

        public override bool IsFalse => true;
        internal override int Kind => 2;
        public override LtlFormula Derivative(IState state) => this;
        public override IEnumerable<LtlUntil> GetUntilSubformulas() => Enumerable.Empty<LtlUntil>();
        public override bool Equals(LtlFormula other) => other is LtlFalse;
        public override int GetHashCode() => 0;
        public override string ToString() => "false";
    }

    /// <summary>Atomic proposition — evaluates a predicate on the current state.</summary>
    public sealed class LtlProp : LtlFormula
    {
        public Func<IState, bool> Predicate { get; }
        public string Name { get; }

        public LtlProp(Func<IState, bool> predicate, string name = null)
        {
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Name = name;
        }

        internal override int Kind => 3;

        public override LtlFormula Derivative(IState state)
            => Predicate(state) ? True : False;

        public override IEnumerable<LtlUntil> GetUntilSubformulas() => Enumerable.Empty<LtlUntil>();

        public override bool Equals(LtlFormula other)
            => other is LtlProp prop && ReferenceEquals(Predicate, prop.Predicate);

        public override int GetHashCode()
            => GetCachedHashCode(() => Predicate.GetHashCode());

        public override string ToString() => Name ?? "prop";
    }

    /// <summary>
    /// Negation: ¬φ. By construction, <see cref="Inner"/> is always a
    /// <see cref="LtlProp"/> — <see cref="Create(LtlFormula)"/> pushes
    /// negation to atoms via De Morgan and temporal-operator duality
    /// (negation normal form, NNF). This keeps the formula tree in a
    /// canonical shape so syntactically distinct but semantically
    /// equivalent formulas hash-cons to the same node.
    /// </summary>
    public sealed class LtlNot : LtlFormula
    {
        public LtlFormula Inner { get; }

        private LtlNot(LtlFormula inner) => Inner = inner;

        internal override int Kind => 4;

        /// <summary>
        /// Smart constructor that pushes negation to atoms (NNF). Rewrites:
        /// <list type="bullet">
        ///   <item>¬⊤ → ⊥, ¬⊥ → ⊤</item>
        ///   <item>¬¬φ → φ</item>
        ///   <item>¬(φ ∧ ψ) → ¬φ ∨ ¬ψ (De Morgan)</item>
        ///   <item>¬(φ ∨ ψ) → ¬φ ∧ ¬ψ (De Morgan)</item>
        ///   <item>¬Xφ → X¬φ</item>
        ///   <item>¬(φ U ψ) → (¬φ) R (¬ψ) (LTL duality)</item>
        ///   <item>¬(φ R ψ) → (¬φ) U (¬ψ) (LTL duality)</item>
        /// </list>
        /// The only Not nodes that ever survive wrap an <see cref="LtlProp"/>.
        /// </summary>
        public static LtlFormula Create(LtlFormula inner)
        {
            switch (inner)
            {
                case LtlTrue _: return False;
                case LtlFalse _: return True;
                case LtlNot not: return not.Inner;
                case LtlAnd and:
                {
                    LtlFormula acc = False;
                    foreach (var c in and.Children)
                        acc = Or(acc, Create(c));
                    return acc;
                }
                case LtlOr or:
                {
                    LtlFormula acc = True;
                    foreach (var c in or.Children)
                        acc = And(acc, Create(c));
                    return acc;
                }
                case LtlNext nx:
                    return Next(Create(nx.Inner));
                case LtlUntil u:
                    return Release(Create(u.Hold), Create(u.Goal));
                case LtlRelease r:
                    return Until(Create(r.Release_), Create(r.Hold));
                case LtlProp _:
                    return new LtlNot(inner);
                default:
                    throw new InvalidOperationException(
                        $"Unhandled LtlFormula kind in Not: {inner.GetType().Name}");
            }
        }

        // After NNF, Inner is always an LtlProp, whose derivative is True or
        // False, so Not(True) = False / Not(False) = True via Create above.
        public override LtlFormula Derivative(IState state)
            => Not(Inner.Derivative(state));

        public override IEnumerable<LtlUntil> GetUntilSubformulas()
            => Inner.GetUntilSubformulas();

        public override bool Equals(LtlFormula other)
            => other is LtlNot not && Inner.Equals(not.Inner);

        public override int GetHashCode()
            => GetCachedHashCode(() => HashHelper.Combine(typeof(LtlNot), Inner));

        public override string ToString() => $"¬{Inner}";
    }

    /// <summary>Conjunction: φ ∧ ψ - stored in canonical (sorted) form.</summary>
    /// <summary>
    /// Conjunction: φ ∧ ψ — stored in canonical ACI-normal form. Children are
    /// flattened (associativity), deduplicated (idempotency), and sorted by
    /// <see cref="LtlFormula.CompareStructural"/> (commutativity). ⊤ operands
    /// are dropped and ⊥ short-circuits to <see cref="LtlFalse"/>.
    /// </summary>
    public sealed class LtlAnd : LtlFormula
    {
        // Children stored in canonical (structurally-sorted) order
        public IReadOnlyList<LtlFormula> Children { get; }

        private LtlAnd(IReadOnlyList<LtlFormula> children)
        {
            Children = children;
        }

        internal override int Kind => 5;

        public static LtlFormula Create(LtlFormula left, LtlFormula right)
        {
            // Collect all children, flattening nested Ands (associativity)
            var children = new List<LtlFormula>();
            CollectAndChildren(left, children);
            CollectAndChildren(right, children);

            // Track polarity of literal operands (post-NNF, a literal is either
            // an LtlProp or an LtlNot(LtlProp)). If both p and ¬p appear in the
            // same conjunction the whole thing is ⊥ (complementary-literal
            // elimination). Predicate identity is by reference (matches
            // LtlProp.Equals which uses ReferenceEquals on the predicate).
            var positives = new HashSet<Func<IState, bool>>(
                PredicateRefComparer.Instance);
            var negatives = new HashSet<Func<IState, bool>>(
                PredicateRefComparer.Instance);

            // Remove duplicates (idempotency: a ∧ a = a)
            var unique = new HashSet<LtlFormula>();
            var filtered = new List<LtlFormula>();
            foreach (var child in children)
            {
                // Simplification: false ∧ φ = false
                if (child.IsFalse) return False;
                // Simplification: true ∧ φ = φ (skip true)
                if (child.IsTrue) continue;

                // Complementary-literal elimination.
                if (child is LtlProp p)
                {
                    if (negatives.Contains(p.Predicate)) return False;
                    positives.Add(p.Predicate);
                }
                else if (child is LtlNot not && not.Inner is LtlProp np)
                {
                    if (positives.Contains(np.Predicate)) return False;
                    negatives.Add(np.Predicate);
                }

                if (unique.Add(child))
                    filtered.Add(child);
            }

            if (filtered.Count == 0) return True;
            if (filtered.Count == 1) return filtered[0];

            // Deep absorption (context-conditioning).
            //   A ∧ (Y ∨ A)           = A          (Or short-circuits to ⊤)
            //   A ∧ (Y ∨ (A ∧ X))     = A ∧ (Y ∨ X)  (drop A inside the inner And)
            // Rationale: in this conjunction every sibling-conjunct is required,
            // so whenever an Or-child has a disjunct that requires only siblings
            // of the outer And, that disjunct is already implied; whenever an
            // Or-child has an And-disjunct containing a sibling-conjunct, that
            // sibling factor is redundant inside the And. This is the
            // single simplification that prevents the alternating ∧/∨ chain
            // produced by nested Release derivatives from growing without
            // bound (see Depth3 blowup probe).
            var siblingSet = new HashSet<LtlFormula>(filtered);
            bool changedAbs = true;
            while (changedAbs)
            {
                changedAbs = false;
                for (int i = 0; i < filtered.Count; i++)
                {
                    if (!(filtered[i] is LtlOr orChild)) continue;
                    var newDisjuncts = new List<LtlFormula>(orChild.Children.Count);
                    bool orPruned = false;
                    bool orIsTrue = false;
                    foreach (var d in orChild.Children)
                    {
                        if (siblingSet.Contains(d)) { orIsTrue = true; break; }
                        if (d is LtlAnd dAnd)
                        {
                            var kept = new List<LtlFormula>(dAnd.Children.Count);
                            bool prunedInner = false;
                            foreach (var c in dAnd.Children)
                            {
                                if (siblingSet.Contains(c)) prunedInner = true;
                                else kept.Add(c);
                            }
                            if (prunedInner)
                            {
                                orPruned = true;
                                if (kept.Count == 0) { orIsTrue = true; break; }
                                LtlFormula rebuilt = True;
                                foreach (var c in kept) rebuilt = And(rebuilt, c);
                                newDisjuncts.Add(rebuilt);
                            }
                            else newDisjuncts.Add(d);
                        }
                        else newDisjuncts.Add(d);
                    }
                    if (orIsTrue)
                    {
                        siblingSet.Remove(orChild);
                        filtered.RemoveAt(i);
                        i--;
                        changedAbs = true;
                        continue;
                    }
                    if (orPruned)
                    {
                        LtlFormula newOr = False;
                        foreach (var d in newDisjuncts) newOr = Or(newOr, d);
                        siblingSet.Remove(orChild);
                        if (newOr.IsFalse) return False;
                        if (newOr.IsTrue)
                        {
                            filtered.RemoveAt(i);
                            i--;
                        }
                        else
                        {
                            filtered[i] = newOr;
                            siblingSet.Add(newOr);
                        }
                        changedAbs = true;
                    }
                }
            }

            if (filtered.Count == 0) return True;
            if (filtered.Count == 1) return filtered[0];

            // Deterministic structural sort for canonical order (commutativity).
            // Replaces the previous hash-code sort, which was non-canonical under
            // hash collisions.
            filtered.Sort(StructuralComparer.Instance);

            return new LtlAnd(filtered);
        }

        private static void CollectAndChildren(LtlFormula formula, List<LtlFormula> children)
        {
            if (formula is LtlAnd and)
            {
                foreach (var child in and.Children)
                    children.Add(child);
            }
            else
            {
                children.Add(formula);
            }
        }

        public override LtlFormula Derivative(IState state)
        {
            LtlFormula result = True;
            foreach (var child in Children)
                result = And(result, child.Derivative(state));
            return result;
        }

        public override IEnumerable<LtlUntil> GetUntilSubformulas()
            => Children.SelectMany(c => c.GetUntilSubformulas());

        public override bool Equals(LtlFormula other)
        {
            if (!(other is LtlAnd and) || Children.Count != and.Children.Count)
                return false;
            for (int i = 0; i < Children.Count; i++)
                if (!Children[i].Equals(and.Children[i]))
                    return false;
            return true;
        }

        public override int GetHashCode()
            => GetCachedHashCode(() =>
            {
                unchecked
                {
                    int hash = 17 * 31 + typeof(LtlAnd).GetHashCode();
                    foreach (var child in Children)
                        hash = hash * 31 + child.GetHashCode();
                    return hash;
                }
            });

        public override string ToString() => $"({string.Join(" ∧ ", Children)})";
    }

    /// <summary>
    /// Disjunction: φ ∨ ψ — stored in canonical ACI-normal form. Children are
    /// flattened (associativity), deduplicated (idempotency), and sorted by
    /// <see cref="LtlFormula.CompareStructural"/> (commutativity). ⊥ operands
    /// are dropped and ⊤ short-circuits to <see cref="LtlTrue"/>.
    /// </summary>
    public sealed class LtlOr : LtlFormula
    {
        // Children stored in canonical (structurally-sorted) order
        public IReadOnlyList<LtlFormula> Children { get; }

        private LtlOr(IReadOnlyList<LtlFormula> children)
        {
            Children = children;
        }

        internal override int Kind => 6;

        public static LtlFormula Create(LtlFormula left, LtlFormula right)
        {
            // Collect all children, flattening nested Ors (associativity)
            var children = new List<LtlFormula>();
            CollectOrChildren(left, children);
            CollectOrChildren(right, children);

            // Track polarity of literal operands (post-NNF, a literal is either
            // an LtlProp or an LtlNot(LtlProp)). If both p and ¬p appear in the
            // same disjunction the whole thing is ⊤ (complementary-literal
            // elimination).
            var positives = new HashSet<Func<IState, bool>>(
                PredicateRefComparer.Instance);
            var negatives = new HashSet<Func<IState, bool>>(
                PredicateRefComparer.Instance);

            // Remove duplicates (idempotency: a ∨ a = a)
            var unique = new HashSet<LtlFormula>();
            var filtered = new List<LtlFormula>();
            foreach (var child in children)
            {
                // Simplification: true ∨ φ = true
                if (child.IsTrue) return True;
                // Simplification: false ∨ φ = φ (skip false)
                if (child.IsFalse) continue;

                // Complementary-literal elimination.
                if (child is LtlProp p)
                {
                    if (negatives.Contains(p.Predicate)) return True;
                    positives.Add(p.Predicate);
                }
                else if (child is LtlNot not && not.Inner is LtlProp np)
                {
                    if (positives.Contains(np.Predicate)) return True;
                    negatives.Add(np.Predicate);
                }

                if (unique.Add(child))
                    filtered.Add(child);
            }

            if (filtered.Count == 0) return False;
            if (filtered.Count == 1) return filtered[0];

            // Absorption + dual deep-absorption (Or side):
            //   A ∨ (A ∧ X)            = A           (simple absorption)
            //   A ∨ (B ∧ (A ∨ X))      = A ∨ (B ∧ X) (dual conditioning: in
            //     the case where this disjunct matters, sibling A failed,
            //     so the inner Or's A-branch can be pruned)
            //   A ∨ (B ∧ A)            handled by simple absorption above
            // Both are sound boolean equivalences and together with the
            // dual rules in And.Create cap the formula-tree growth produced
            // by alternating ∧/∨ derivative chains.
            {
                var siblingSet = new HashSet<LtlFormula>(filtered);
                bool changedAbs = true;
                while (changedAbs)
                {
                    changedAbs = false;
                    for (int i = 0; i < filtered.Count; i++)
                    {
                        if (!(filtered[i] is LtlAnd andChild)) continue;
                        // Simple absorption: if any conjunct of this And equals
                        // some sibling Or-disjunct, the And is subsumed.
                        bool subsumed = false;
                        foreach (var c in andChild.Children)
                        {
                            if (siblingSet.Contains(c)) { subsumed = true; break; }
                        }
                        if (subsumed)
                        {
                            siblingSet.Remove(andChild);
                            filtered.RemoveAt(i);
                            i--;
                            changedAbs = true;
                            continue;
                        }
                        // Dual deep absorption: for each Or-grandchild of this
                        // And, prune any of its disjuncts that match outer
                        // siblings (since those disjuncts would already be
                        // true at the outer Or — but we're inside an And that
                        // is only relevant when ALL siblings failed, so we
                        // can drop those disjuncts as false).
                        var newConjuncts = new List<LtlFormula>(andChild.Children.Count);
                        bool andPruned = false;
                        bool andIsFalse = false;
                        foreach (var c in andChild.Children)
                        {
                            if (c is LtlOr cOr)
                            {
                                var kept = new List<LtlFormula>(cOr.Children.Count);
                                bool prunedInner = false;
                                foreach (var d in cOr.Children)
                                {
                                    if (siblingSet.Contains(d)) prunedInner = true;
                                    else kept.Add(d);
                                }
                                if (prunedInner)
                                {
                                    andPruned = true;
                                    if (kept.Count == 0) { andIsFalse = true; break; }
                                    LtlFormula rebuilt = False;
                                    foreach (var d in kept) rebuilt = Or(rebuilt, d);
                                    newConjuncts.Add(rebuilt);
                                }
                                else newConjuncts.Add(c);
                            }
                            else newConjuncts.Add(c);
                        }
                        if (andIsFalse)
                        {
                            siblingSet.Remove(andChild);
                            filtered.RemoveAt(i);
                            i--;
                            changedAbs = true;
                            continue;
                        }
                        if (andPruned)
                        {
                            LtlFormula newAnd = True;
                            foreach (var c in newConjuncts) newAnd = And(newAnd, c);
                            siblingSet.Remove(andChild);
                            if (newAnd.IsTrue) return True;
                            if (newAnd.IsFalse)
                            {
                                filtered.RemoveAt(i);
                                i--;
                            }
                            else
                            {
                                filtered[i] = newAnd;
                                siblingSet.Add(newAnd);
                            }
                            changedAbs = true;
                        }
                    }
                }
            }

            if (filtered.Count == 0) return False;
            if (filtered.Count == 1) return filtered[0];

            // Deterministic structural sort for canonical order (commutativity).
            filtered.Sort(StructuralComparer.Instance);

            return new LtlOr(filtered);
        }

        private static void CollectOrChildren(LtlFormula formula, List<LtlFormula> children)
        {
            if (formula is LtlOr or)
            {
                foreach (var child in or.Children)
                    children.Add(child);
            }
            else
            {
                children.Add(formula);
            }
        }

        public override LtlFormula Derivative(IState state)
        {
            LtlFormula result = False;
            foreach (var child in Children)
                result = Or(result, child.Derivative(state));
            return result;
        }

        public override IEnumerable<LtlUntil> GetUntilSubformulas()
            => Children.SelectMany(c => c.GetUntilSubformulas());

        public override bool Equals(LtlFormula other)
        {
            if (!(other is LtlOr or) || Children.Count != or.Children.Count)
                return false;
            for (int i = 0; i < Children.Count; i++)
                if (!Children[i].Equals(or.Children[i]))
                    return false;
            return true;
        }

        public override int GetHashCode()
            => GetCachedHashCode(() =>
            {
                unchecked
                {
                    int hash = 17 * 31 + typeof(LtlOr).GetHashCode();
                    foreach (var child in Children)
                        hash = hash * 31 + child.GetHashCode();
                    return hash;
                }
            });

        public override string ToString() => $"({string.Join(" ∨ ", Children)})";
    }

    /// <summary>Next: Xφ — φ must hold in the next state.</summary>
    public sealed class LtlNext : LtlFormula
    {
        public LtlFormula Inner { get; }

        public LtlNext(LtlFormula inner) => Inner = inner;

        internal override int Kind => 7;

        // δ(Xφ, s) = φ  — after one step, we just need to satisfy φ
        public override LtlFormula Derivative(IState state) => Inner;

        public override IEnumerable<LtlUntil> GetUntilSubformulas()
            => Inner.GetUntilSubformulas();

        public override bool Equals(LtlFormula other)
            => other is LtlNext next && Inner.Equals(next.Inner);

        public override int GetHashCode()
            => GetCachedHashCode(() => HashHelper.Combine(typeof(LtlNext), Inner));

        public override string ToString() => $"X{Inner}";
    }

    /// <summary>
    /// Until: φ U ψ — φ holds until ψ becomes true, and ψ must eventually hold.
    /// This is the key temporal operator for liveness properties.
    /// </summary>
    public sealed class LtlUntil : LtlFormula
    {
        public LtlFormula Hold { get; }  // φ — what must hold until goal
        public LtlFormula Goal { get; }  // ψ — what must eventually be achieved

        private LtlUntil(LtlFormula hold, LtlFormula goal)
        {
            Hold = hold;
            Goal = goal;
        }

        internal override int Kind => 8;

        public static LtlFormula Create(LtlFormula hold, LtlFormula goal)
        {
            // Simplification: φ U true = true, φ U false = false, false U ψ = ψ
            if (goal.IsTrue) return True;
            if (goal.IsFalse) return False;
            if (hold.IsFalse) return goal;
            return new LtlUntil(hold, goal);
        }

        // δ(φ U ψ, s) = δ(ψ, s) ∨ (δ(φ, s) ∧ (φ U ψ))
        // Either ψ holds now, or φ holds now and we continue waiting
        public override LtlFormula Derivative(IState state)
        {
            var goalDeriv = Goal.Derivative(state);
            var holdDeriv = Hold.Derivative(state);
            return Or(goalDeriv, And(holdDeriv, this));
        }

        public override IEnumerable<LtlUntil> GetUntilSubformulas()
        {
            yield return this;
            foreach (var u in Hold.GetUntilSubformulas()) yield return u;
            foreach (var u in Goal.GetUntilSubformulas()) yield return u;
        }

        public override bool Equals(LtlFormula other)
            => other is LtlUntil until && Hold.Equals(until.Hold) && Goal.Equals(until.Goal);

        public override int GetHashCode()
            => GetCachedHashCode(() => HashHelper.Combine(typeof(LtlUntil), Hold, Goal));

        public override string ToString() => $"({Hold} U {Goal})";
    }

    /// <summary>
    /// Release: φ R ψ — ψ must hold until and including when φ becomes true.
    /// If φ never becomes true, ψ must hold forever.
    /// Dual of Until: φ R ψ ≡ ¬(¬φ U ¬ψ)
    /// </summary>
    public sealed class LtlRelease : LtlFormula
    {
        public LtlFormula Release_ { get; }  // φ — releases the obligation
        public LtlFormula Hold { get; }      // ψ — must hold until released

        private LtlRelease(LtlFormula release, LtlFormula hold)
        {
            Release_ = release;
            Hold = hold;
        }

        internal override int Kind => 9;

        public static LtlFormula Create(LtlFormula release, LtlFormula hold)
        {
            // Simplification: φ R true = true, φ R false = false, true R ψ = ψ
            if (hold.IsTrue) return True;
            if (hold.IsFalse) return False;
            if (release.IsTrue) return hold;
            return new LtlRelease(release, hold);
        }

        // δ(φ R ψ, s) = δ(ψ, s) ∧ (δ(φ, s) ∨ (φ R ψ))
        // ψ must hold now, and either φ releases us or we continue
        public override LtlFormula Derivative(IState state)
        {
            var holdDeriv = Hold.Derivative(state);
            var releaseDeriv = Release_.Derivative(state);
            return And(holdDeriv, Or(releaseDeriv, this));
        }

        public override IEnumerable<LtlUntil> GetUntilSubformulas()
        {
            // Release doesn't create Until obligations directly,
            // but we need to track nested Untils
            foreach (var u in Release_.GetUntilSubformulas()) yield return u;
            foreach (var u in Hold.GetUntilSubformulas()) yield return u;
        }

        public override bool Equals(LtlFormula other)
            => other is LtlRelease rel && Release_.Equals(rel.Release_) && Hold.Equals(rel.Hold);

        public override int GetHashCode()
            => GetCachedHashCode(() => HashHelper.Combine(typeof(LtlRelease), Release_, Hold));

        public override string ToString() => $"({Release_} R {Hold})";
    }
}
